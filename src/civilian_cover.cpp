#include "promzona/world.hpp"

namespace Promzona
{
    bool World::cover_allows_aim(Person &p, std::span<const Car *const> traffic, Vec2 &direction, double &speed) const
    {
        CivilianCover &cover = p.cover;
        const Body &target = controlledCar >= 0 ? cars[controlledCar].body : player;

        if (p.body.roadLevel || target.roadLevel || (target.p - p.body.p).length2() > 24 * 24 ||
            p.alertUntil <= time || p.burnUntil > time || p.dead || health <= 0 ||
            (p.ownedScooter >= 0 && scooters[p.ownedScooter].rider >= 0))
        {
            cover = {};
            return true;
        }

        if (!cover.searchAt)
            cover.searchAt = time + .75 + p.body.id % 5 * .07;

        if (cover.found && ((target.p - cover.threat).length2() > 9 || (cover.vehicle >= 0 && (static_cast<size_t>(cover.vehicle) >= cars.size() || cars[cover.vehicle].body.v.length2() > .49 || cars[cover.vehicle].fireTime > 0 || (cars[cover.vehicle].body.p - cover.vehicleAnchor).length2() > .25))))
        {
            cover.found = false;
            cover.searchAt = time;
        }

        if (time >= cover.searchAt)
        {
            cover = {};
            cover.searchAt = time + 1.7 + p.body.id % 5 * .13;
            double best = 12;

            // All candidate walks and sight lines lie inside the local contact neighbourhood.
            auto blocked = [&](Vec2 from, Vec2 to, double y1, double y2, double clearance, bool lineOfFire)
            {
                Vec2 delta = to - from;
                double length = std::max(.001, delta.length());
                Vec2 ray = delta / length;
                double slope = (y2 - y1) / length;

                auto crosses = [&](const Body &body)
                {
                    Body box = body;
                    box.halfW += clearance;
                    box.halfH += clearance;
                    return ray_prism(from, ray, y1, slope, box, length) <= length;
                };

                for (const Car *car : traffic)
                    if ((!lineOfFire || &car->body != &target) && crosses(car->body))
                        return true;

                for (size_t index : nearbyStaticBodies)
                    if (crosses(staticBodies[index]))
                        return true;

                for (int i = 1, steps = static_cast<int>(std::ceil(length / .5)); i <= steps; ++i)
                {
                    double t = static_cast<double>(i) / steps;
                    auto point = from + delta * t;

                    if (scenerySolid(point, y1 + (y2 - y1) * t, clearance) || (!lineOfFire && openCut(point, clearance)))
                        return true;
                }

                return false;
            };

            auto consider = [&](const Vec2& hide, const Vec2& peek, const int vehicle)
            {
                const double distance = (hide - p.body.p).length();

                if (distance >= best || !layout.urbanSite(hide, .35) || !layout.urbanSite(peek, .35))
                    return;

                double hideY = terrain.height(hide) + .85, peekY = terrain.height(peek) + .85;
                if (blocked(p.body.p, hide, p.body.height, hideY, .33, false) ||
                    blocked(hide, peek, hideY, peekY, .33, false) ||
                    !blocked(hide, target.p, hideY + .42, target.height, 0, true) ||
                    blocked(peek, target.p, peekY + .42, target.height, 0, true))
                    return;

                best = distance;
                cover.hide = hide;
                cover.peek = peek;
                cover.threat = target.p;
                cover.vehicle = vehicle;

                if (vehicle >= 0)
                    cover.vehicleAnchor = cars[vehicle].body.p;

                cover.found = true;
            };

            for (const Car *car : traffic)
            {
                const auto &b = car->body;
                if (&b == &target || car->falling || car->fireTime > 0 || b.roadLevel || b.v.length2() > .49 ||
                    (b.p - p.body.p).length2() > 100 || std::abs(b.height - p.body.height) > 1.5)
                    continue;
                auto along = forward(car->heading()), right = perpendicular(along);
                int side = dot(target.p - b.p, right) > 0 ? -1 : 1;
                auto hide = b.p + right * (side * (b.halfW + .8));
                for (int end : {-1, 1})
                    consider(hide, hide + along * (end * (b.halfH + 1.1)), int(car - cars.data()));
            }
            for (size_t i : nearbyStaticBodies)
            {
                if (i >= buildings.size())
                    continue; // First static bodies are the building footprints.
                const auto &b = buildings[i];
                if (std::abs(b.p.x - p.body.p.x) > b.half.x + 12 || std::abs(b.p.z - p.body.p.z) > b.half.z + 12)
                    continue;
                for (int x : {-1, 1})
                    for (int z : {-1, 1})
                    {
                        Vec2 corner = b.p + Vec2{x * (b.half.x + .85), z * (b.half.z + .85)};
                        consider(corner - Vec2{x * 2., 0}, corner, -1);
                        consider(corner - Vec2{0, z * 2.}, corner, -1);
                    }
            }
            if (cover.found)
                cover.searchAt = time + 12.;
        }
        if (!cover.found)
            return true;
        auto delta = (cover.peeking ? cover.peek : cover.hide) - p.body.p;
        if (delta.length2() > .4 * .4)
        {
            direction = delta.unit();
            speed = std::min(2.8, delta.length() * 3.);
            cover.holdUntil = 0;
            return false;
        }
        speed = 0;

        if (!cover.holdUntil)
            cover.holdUntil = time + (cover.peeking ? 1.8 : 1.1);

        if (time < cover.holdUntil)
            return cover.peeking;

        cover.peeking = !cover.peeking;
        cover.holdUntil = 0;
        return false;
    }
}
