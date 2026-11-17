#include "promzona/world.hpp"
#include <algorithm>

namespace Promzona
{
    void World::refresh_drone_obstacles(Vec2 centre)
    {
        drone.obstacles.clear();
        drone.obstacleFocus = centre;

        for (size_t i = 0; i < staticBodies.size(); ++i)
        {
            const auto &body = staticBodies[i];
            const double reach = std::hypot(body.halfW, body.halfH) + body.radius + 70;

            if ((body.p - centre).length2() < reach * reach)
                drone.obstacles.push_back(i);
        }
    }

    bool World::droneClear(Vec2 point, double height, double radius) const
    {
        if (std::max(std::abs(point.x), std::abs(point.z)) > landEdge || scenerySolid(point, height, radius))
            return false;

        for (size_t i : drone.obstacles)
        {
            const auto &b = staticBodies[i];
            if (std::abs(b.height - height) > b.verticalExtent * .5 + radius)
                continue;
            auto d = point - b.p;
            if (b.circle)
            {
                if (d.length2() < (b.radius + radius) * (b.radius + radius))
                    return false;
            }
            else
            {
                auto local = rotate(d, -b.angle);
                if (std::abs(local.x) < b.halfW + radius && std::abs(local.z) < b.halfH + radius)
                    return false;
            }
        }
        return true;
    }

    void World::toggle_drone(double heading)
    {
        if (drone.active)
        {
            stop_drone(L"Управление возвращено оператору", 2);
            return;
        }
        if (controlledCar >= 0 || playerScooter >= 0 || health <= 0)
            return;
        if (time < drone.readyAt)
        {
            notify(L"Дрон готовится к вылету");
            return;
        }
        std::vector<size_t> candidates;
        for (size_t i = 0; i < buildings.size(); ++i)
        {
            const auto &b = buildings[i];
            double d = (b.p - player.p).length2();
            if (b.height >= 6 && b.height < 25 && d > 22 * 22 && d < 135 * 135)
                candidates.push_back(i);
        }
        if (candidates.size() < 3)
        {
            notify(L"Для разведки нужны три здания поблизости");
            return;
        }
        std::ranges::partial_sort(candidates, candidates.begin() + 3, {},
                                  [&](size_t i) { return (buildings[i].p - player.p).length2(); });
        drone = {};
        refresh_drone_obstacles(player.p);
        drone.altitude = ground() + 2.4;
        for (int n = 0; n < 16; ++n)
        {
            auto p = player.p + forward(n * 2 * pi / 16) * .8;
            if (!droneClear(p, drone.altitude, .35))
                continue;
            drone.active = true;
            drone.position = p;
            drone.heading = heading;
            drone.pitch = -.3;
            drone.battery = 100;
            drone.health = 1;
            std::copy_n(candidates.begin(), 3, drone.targets.begin());
            return;
        }
        notify(L"Для взлёта нужно свободное место");
    }

    void World::stop_drone(const wchar_t *message, double delay)
    {
        drone.active = false;
        drone.obstacles.clear();
        drone.readyAt = time + delay;
        notify(message);
    }

    void World::step_drone(const PlayerInput &input, double dt)
    {
        if (!drone.active)
            return;
        if (health <= 0 || controlledCar >= 0)
        {
            stop_drone(L"Связь с оператором потеряна", 20);
            return;
        }
        drone.battery = std::max(0., drone.battery - dt);
        if (drone.battery <= 0)
        {
            stop_drone(L"Аккумулятор разряжен", 20);
            return;
        }
        drone.heading = input.lookHeading;
        drone.pitch = input.lookPitch;
        const auto desired = input.walking / std::max(1., input.walking.length());
        drone.velocity += (desired * 11 - drone.velocity) * (1 - std::exp(-4 * dt));
        if ((drone.position - drone.obstacleFocus).length2() > 12 * 12)
            refresh_drone_obstacles(drone.position);
        auto next = drone.position + drone.velocity * dt;
        double altitude = std::min(ground() + 65, drone.altitude + clamp(input.collective, -1., 1.) * 5 * dt);
        if ((next - player.p).length2() > 170 * 170)
        {
            drone.velocity = {};
            next = drone.position;
        }
        if (droneClear(next, altitude, .35))
        {
            drone.position = next;
            drone.altitude = altitude;
        }
        else
        {
            // Keep tangential movement when a floor, ceiling or facade blocks just one axis.
            if (droneClear(drone.position, altitude, .35))
                drone.altitude = altitude;
            double impact = 0;
            if (droneClear({next.x, drone.position.z}, drone.altitude, .35))
                drone.position.x = next.x;
            else
            {
                impact = std::abs(drone.velocity.x);
                drone.velocity.x = 0;
            }
            if (droneClear({drone.position.x, next.z}, drone.altitude, .35))
                drone.position.z = next.z;
            else
            {
                impact = std::hypot(impact, drone.velocity.z);
                drone.velocity.z = 0;
            }
            drone.health -= std::max(0., impact - 2.) * .025;
        }
        // Police act at their physical location even when the operator's camera is elsewhere.
        if (wantedLevel() && time >= drone.nextShot)
        {
            drone.nextShot = time + .8;
            for (const auto &c : cars)
            {
                if (!c.responding || c.destroyed || c.falling ||
                    (c.body.p - drone.position).length2() > 55 * 55 ||
                    std::abs(c.body.height - drone.altitude) > 32)
                    continue;
                if (!blastVisible(c.body.p, c.body.height + 1.5, drone.position, drone.altitude))
                    continue;
                sounds.push_back({"shot", c.body.p, .5});
                drone.health -= .12;
                break;
            }
        }

        if (drone.health <= 0)
        {
            stop_drone(L"Дрон потерян", 20);
            return;
        }

        const auto &roof = buildings[drone.targets[drone.scanned]];
        const auto delta = roof.p - drone.position;
        double roofY = terrain.height(roof.p) + roof.height;
        double dy = roofY + .7 - drone.altitude;
        double dist = std::hypot(delta.length(), dy);
        double alignment = dot(forward(drone.heading), delta) * std::cos(drone.pitch) + dy * std::sin(drone.pitch);

        auto clear_sight = [&]
        {
            const int steps = std::max(1, static_cast<int>(std::ceil(dist / .3)));
            for (int i = 1; i < steps; ++i)
            {
                double t = static_cast<double>(i) / steps;
                if (!droneClear(drone.position + delta * t, drone.altitude + dy * t, .04))
                    return false;
            }
            return true;
        };

        bool scanning =input.shooting && drone.altitude > roofY + 1 && dist < 45 && alignment > dist * .90 && clear_sight();
        drone.scanTime = scanning ? drone.scanTime + dt : std::max(0., drone.scanTime - dt);

        if (drone.scanTime >= 2)
        {
            drone.scanTime = 0;
            sounds.push_back({drone.scanned == 2 ? "cash" : "energy", player.p, .5, true});
            if (++drone.scanned == 3)
            {
                money = std::min(999999999, money + 200); // the cap every other reward keeps
                stop_drone(L"Разведка завершена   +$200", 60);
            }
        }
    }
}
