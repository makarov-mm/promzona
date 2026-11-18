#include "promzona/core.hpp"
#include <stdexcept>

namespace Promzona
{
    Path::Path(Vec2 a, Vec2 b) : Path(a, a + (b - a) / 3, a + (b - a) * (2. / 3), b) {}

    Path::Path(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        points_.resize(65);
        arc_.resize(65);
        for (int i = 0; i < 65; i++)
        {
            double u = i / 64., v = 1 - u;
            points_[i] = a * (v * v * v) + b * (3 * v * v * u) + c * (3 * v * u * u) + d * (u * u * u);
            if (i)
                arc_[i] = arc_[i - 1] + (points_[i] - points_[i - 1]).length();
        }
    }

    Path::Path(std::vector<Vec2> p) : points_(std::move(p))
    {
        if (points_.size() < 2)
            throw std::invalid_argument("Path requires two points");
        for (auto v : points_)
            if (!std::isfinite(v.x) || !std::isfinite(v.z))
                throw std::invalid_argument("Non-finite path");
        arc_.resize(points_.size());
        for (size_t i = 1; i < points_.size(); i++)
            arc_[i] = arc_[i - 1] + (points_[i] - points_[i - 1]).length();
    }

    std::pair<Vec2, double> Path::sample(double distance) const
    {
        distance = clamp(distance, 0., length());
        auto at =
            clamp<size_t>(std::ranges::lower_bound(arc_, distance) - arc_.begin(), 1, points_.size() - 1);
        double f = (distance - arc_[at - 1]) / std::max(1e-12, arc_[at] - arc_[at - 1]);
        auto d = points_[at] - points_[at - 1];
        return {points_[at - 1] + d * f, std::atan2(d.x, d.z)};
    }

    double Path::closest_distance(Vec2 p) const
    {
        double best = INFINITY, along = 0;
        for (size_t i = 1; i < points_.size(); i++)
        {
            auto a = points_[i - 1], d = points_[i] - a;
            double t = clamp(dot(p - a, d) / std::max(1e-12, dot(d, d)), 0., 1.);
            double score = (p - a - d * t).length2();
            if (score < best)
            {
                best = score;
                along = arc_[i - 1] + (arc_[i] - arc_[i - 1]) * t;
            }
        }
        return along;
    }

    Path Path::turnaround(Vec2 centre, Vec2 f, double lane)
    {
        auto r = perpendicular(f);
        std::vector<Vec2> points;
        auto curve = [&](Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            Path p(a, b, c, d);
            points.insert(points.end(), p.points_.begin() + (points.empty() ? 0 : 1), p.points_.end());
        };
        curve(centre - f * stopLine + r * lane, centre - f * 8 + r * lane, centre - f * 5 + r * 5,
              centre - f * 3 + r * 5);
        for (int i = 1; i <= 128; i++)
        {
            double t = i * pi / 128;
            points.push_back(centre - f * 3 + r * (5 * std::cos(t)) + f * (5 * std::sin(t)));
        }
        curve(centre - f * 3 - r * 5, centre - f * 5 - r * 5, centre - f * 6.5 - r * lane,
              centre - f * 9 - r * lane);
        return Path(std::move(points));
    }

    static constexpr std::array weaponData{
        Weapon{WeaponId::pistol, 1, "PISTOL", 30, .34, 45, .16, .60, 1, .34, .85, .04, "shot"},
        Weapon{WeaponId::automatic, 2, "AUTOMATIC", 30, .12, 60, .12, .38, 3, .12, 1.15, .028, "automatic"},
        Weapon{WeaponId::bazooka, 4, "BAZOOKA", 20, 1.15, 110, 2.8, 1, 0, 0, 0, 0, "rocket"},
        Weapon{WeaponId::flamethrower, 3, "FLAMETHROWER", 120, .1, 8, .1, .1, 0, 0, 0, 0, "fire"}};

    std::span<const Weapon> weapons()
    {
        return weaponData;
    }

    const Weapon *weapon(WeaponId id)
    {
        for (auto &w : weaponData)
            if (w.id == id)
                return &w;
        return nullptr;
    }

    int Inventory::ammo(WeaponId id) const
    {
        return weapon(id) ? ammo_[std::to_underlying(id)] : 0;
    }

    const Weapon *Inventory::active() const
    {
        if (selected_ && (*selected_ == WeaponId::none || ammo(*selected_) > 0))
            return weapon(*selected_);
        const Weapon *best = nullptr;
        for (auto &w : weapons())
            if (ammo(w.id) > 0 && (!best || w.power > best->power))
                best = &w;
        return best;
    }

    bool Inventory::give(WeaponId id, std::optional<int> amount)
    {
        auto w = weapon(id);
        if (!w)
            return false;
        int n = amount.value_or(w->pickup_ammo);
        if (n <= 0 || n > ammo_limit || ammo(id) >= ammo_limit)
            return false;
        auto old = active();
        if (selected_ && w->power > (old ? old->power : 0))
            selected_.reset();
        auto &a = ammo_[std::to_underlying(id)];
        a = std::min(ammo_limit, a + n);
        owned_[std::to_underlying(id)] = true;
        return true;
    }

    bool Inventory::select(WeaponId id)
    {
        if (id == WeaponId::none || ammo(id) > 0)
        {
            selected_ = id;
            return true;
        }
        return false;
    }

    bool Inventory::consume(WeaponId id)
    {
        if (ammo(id) <= 0)
            return false;
        if (--ammo_[std::to_underlying(id)] == 0 && selected_ == id)
            selected_.reset();
        return true;
    }

    void Inventory::cycle(int dir)
    {
        if (!dir)
            return;
        auto a = active();
        int start = a ? std::to_underlying(a->id) : 0;
        for (int i = 1; i <= 5; i++)
            if (select(static_cast<WeaponId>((start + 5 + i * (dir > 0 ? 1 : -1)) % 5)))
                break;
    }

    void Inventory::restore(const std::array<int, 5> &values, const std::array<bool, 5> &owned)
    {
        if (values[0] != 0 || owned[0])
            throw std::invalid_argument("Ammo for fists");
        for (auto v : values)
            if (v < 0 || v > ammo_limit)
                throw std::invalid_argument("Invalid ammo");
        for (size_t i = 1; i < 5; i++)
            if (values[i] > 0 && !owned[i])
                throw std::invalid_argument("Ammo without weapon");
        ammo_ = values;
        owned_ = owned;
        selected_.reset();
    }

    Input Input::sanitized() const
    {
        return {
            std::isfinite(throttle) ? clamp(throttle, -1., 1.) : 0,
            std::isfinite(steering) ? clamp(steering, -1., 1.) : 0, brake, handbrake};
    }

    void Car::begin_righting()
    {
        recovering = true;
        righting_roll = std::remainder(roll, 2 * pi);
        righting_pitch = std::remainder(pitch, 2 * pi);
        righting_time = overturned_time = 0;
        body.v = {};
        body.w = roll_rate = pitch_rate = verticalVelocity = 0;
        body.wake();
    }

    void Car::apply_impact_rotation(Vec2 change)
    {
        if (recovering || (!destroyed && (kind == 9 || kind == 12)))
            return;
        const auto f = forward(heading());
        const double side = dot(change, Vec2{f.z, -f.x});
        const double cap = destroyed ? 3. : 4.;
        roll_rate += clamp(side * (destroyed ? .16 : .20), -cap, cap);
        pitch_rate += clamp(dot(change, f) * (destroyed ? .09 : .11), -2., 2.);
    }

    void Car::settle_attitude(double dt, bool player_driving)
    {
        if (recovering)
        {
            righting_time += dt;
            const double t = clamp(righting_time / 2.2, 0., 1.);
            const double u = clamp((t - .18) / .64, 0., 1.);
            const double remaining = 1. - u * u * (3. - 2. * u);
            roll = righting_roll * remaining;
            pitch = righting_pitch * remaining;
            lift = t < 1. ? 1.5 * std::sin(pi * t) : 0.;
            if (t >= 1.)
            {
                recovering = false;
                righting_time = 0;
            }
            return;
        }
        if (roll == 0 && pitch == 0 && roll_rate == 0 && pitch_rate == 0)
            return;
        const bool supported = lift <= .04;
        if (supported)
        {
            roll_rate -= (destroyed ? 9. : 12.) * std::sin(2. * roll) * dt;
            pitch_rate -= (destroyed ? 12. * std::sin(2. * pitch) : 18. * std::sin(pitch)) * dt;
        }
        roll_rate *= std::exp(-dt * (supported ? (destroyed ? 2.8 : 2.1) : .16));
        pitch_rate *= std::exp(-dt * (supported ? (destroyed ? 2.8 : 2.2) : .18));
        roll = std::remainder(roll + roll_rate * dt, 2 * pi);
        pitch = std::remainder(pitch + pitch_rate * dt, 2 * pi);
        if (std::abs(roll) + std::abs(roll_rate) < .0001)
            roll = roll_rate = 0;
        if (std::abs(pitch) + std::abs(pitch_rate) < .0001)
            pitch = pitch_rate = 0;
        const bool stuck = player_driving && !destroyed && !falling && fireTime <= 0 && lift < .12 &&
                           body.v.length2() < .7 * .7 && std::cos(roll) * std::cos(pitch) < -.45;
        overturned_time = stuck ? overturned_time + dt : 0.;
        if (overturned_time > 4.)
            begin_righting();
    }

    void Car::destroy()
    {
        destroyed = true;
        personal = false;
        health = 0;
        body.mass(mass);
        body.wake();
        recovering = false;
        wreckSupported = false;
    }

    void step_wreck(Car &car, double dt)
    {
        auto &b = car.body;
        bool supported = car.wreckSupported || (car.lift <= .03 && car.verticalVelocity <= 0);
        if (supported)
        {
            if (car.terrainActive && car.lift <= .03)
            {
                double h = -b.angle, f = -9.81 * std::sin(car.terrainPitch), s = 9.81 * std::sin(car.terrainRoll);
                b.v += Vec2{f * std::sin(h) + s * std::cos(h), f * std::cos(h) - s * std::sin(h)} * dt;
            }
            b.v *= std::max(0., 1 - 3.4 * dt / std::max(1e-9, b.v.length()));
        }
        b.v *= std::exp(-dt * (supported ? .16 : .12));
        b.w *= std::exp(-dt * (supported ? 2.4 : .25));
        if (b.v.length2() > .0001 || std::abs(b.w) > .02)
            b.wake();
    }

    void step_vehicle(Car &car, Input raw, double dt, double traction)
    {
        if (!(dt > 0 && std::isfinite(dt)))
            throw std::invalid_argument("Invalid physics timestep");
        auto input = raw.sanitized();
        auto &b = car.body;
        if (car.falling)
            return;
        if (car.destroyed)
        {
            step_wreck(car, dt);
            return;
        }
        if (car.recovering)
        {
            b.v = {};
            b.w = 0;
            return;
        }
        if (!car.grounded())
        {
            b.v *= std::exp(-.12 * dt);
            b.w *= std::exp(-.5 * dt);
            return;
        }
        double heading = -b.angle, s = std::sin(heading), c = std::cos(heading);
        // Taken before the slope pulls: on a steep street one step of that pull is more than a
        // standing car may have, and the car slid sideways down the camber for good.
        const bool standing = b.v.length2() < .0144 && std::abs(b.w) < .05;
        if (car.terrainActive)
            b.v -= car.terrainGradient * (9.81 / (1 + car.terrainGradient.length2()) * dt);
        double speed = dot(b.v, {s, c}), steerLimit = .68 / (1 + std::pow(std::abs(speed) / 22, 2));
        car.steering += clamp(input.steering * steerLimit - car.steering, -1.8 * dt, 1.8 * dt);
        bool braking = input.brake || input.throttle * speed < -.35;
        car.braking = braking || input.handbrake;
        // Static friction: a braked car at rest does not creep across a slope, and nobody who
        // leans on it pushes it away.
        if (car.braking && standing)
        {
            b.v = {};
            b.w = 0;
            return;
        }
        double engine = 8200;
        switch (car.kind)
        {
        case 11:
            engine = 185000;
            break;
        case 10:
        {
            // The trolley's electric drive has a low-speed torque reserve for hill starts.
            // At higher speeds motor power, then tire adhesion, limit the uphill effort.
            const double grade = car.terrainActive ? dot(car.terrainGradient, {s, c}) : 0.;
            const double uphill = input.throttle > 0 ? std::max(0., grade) : 0.;
            const double available = clamp(240000. / std::max(1., std::abs(speed)), 26000., 65000.);
            engine = std::min(available, 26000. + car.mass * 9.81 * uphill);
            break;
        }
        case 8:
        case 6:
        case 4:
            engine = 14000;
            break;
        case 7:
        case 5:
            engine = 24000;
            break;
        case 1:
        case 3:
            engine = 10500;
            break;
        case 2:
            engine = 18000;
            break;
        }
        // A lorry and a bus have a low gear for hills, as the trolley has its reserve of torque.
        // Without it the pull of a street that rises one in four was more than their engines
        // gave: near the player they stood at the foot of it, and the traffic behind them.
        if (car.mass >= 6000 && car.kind != 10 && car.kind != 11 && car.kind != 12)
        {
            const double grade = car.terrainActive ? dot(car.terrainGradient, {s, c}) : 0.;
            const double uphill = std::max(0., input.throttle >= 0 ? grade : -grade);
            const double available = clamp(engine * 9 / std::max(1., std::abs(speed)), engine, engine * 2.5);
            engine = std::min(available, engine + car.mass * 9.81 * uphill);
        }
        double maximum = car.kind == 11 ? 14 : (car.kind == 2 || car.kind == 5 || car.kind == 10 ? 19 : 27),
               pedal = braking || input.handbrake ? 0 : input.throttle;
        double force = pedal * engine * clamp(1 - std::abs(speed) / (pedal < 0 ? 7 : maximum), 0., 1.),
               drag = .018 * car.mass * 9.81 + .43 * speed * speed;
        double rolling = std::copysign(std::min(std::abs(speed) * car.mass / dt, drag), speed),
               longitudinal = (force - rolling) * dt * .5;
        double rearSlip = input.handbrake ? clamp((std::abs(speed) - 5) / 10, 0., 1.) *
                                                std::min(1., std::abs(car.steering) / steerLimit)
                                          : 0;
        double frontShare = input.handbrake ? .58 : .5,
               surfaceGrip =
                   .88 * 9.81 * std::cos(car.terrainPitch) * std::cos(car.terrainRoll) * clamp(traction, .5, 1.);
        b.wake();
        for (int axle = -1; axle <= 1; axle += 2)
        {
            double rx = s * car.wheelbase * .5 * axle, ry = c * car.wheelbase * .5 * axle,
                   wheel = heading + (axle > 0 ? car.steering : 0), fx = std::sin(wheel), fy = std::cos(wheel),
                   ly = -fx;
            double vx = b.v.x - b.w * ry, vy = b.v.z + b.w * rx, cr = rx * ly - ry * fy,
                   effective = b.invMass + cr * cr * b.invI, lateralRate = axle < 0 ? 18 - 8 * rearSlip : 18;
            double lateral = -(vx * fy + vy * ly) * (1 - std::exp(-dt * lateralRate)) / effective,
                   load = car.mass * (axle > 0 ? frontShare : 1 - frontShare), drive = longitudinal;
            if (braking || input.handbrake)
            {
                double wheelSpeed = vx * fx + vy * fy, lever = rx * fy - ry * fx,
                       stop = -wheelSpeed / (b.invMass + lever * lever * b.invI),
                       limit = load * (input.handbrake ? 9.5 : 7.8) * dt;
                drive = clamp(stop, -limit, limit);
            }
            double grip = load * surfaceGrip * dt * (axle < 0 ? 1 - .12 * rearSlip : 1),
                   impulse = std::hypot(drive, lateral);
            if (impulse > grip)
            {
                drive *= grip / impulse;
                lateral *= grip / impulse;
            }
            b.impulse({fx * drive + fy * lateral, fy * drive + ly * lateral}, {rx, ry});
        }
        if (input.handbrake)
        {
            double limit = surfaceGrip / std::max(5., std::abs(speed)),
                   target = clamp(-speed * std::tan(car.steering) / car.wheelbase, -limit, limit),
                   correction = (target - b.w) * (1 - std::exp(-7 * dt)), maxC = surfaceGrip * dt / car.wheelbase;
            b.w += clamp(correction, -maxC, maxC);
        }
        b.w *= std::exp(-.12 * dt);
    }
}
