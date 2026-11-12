#include "promzona/world.hpp"

namespace pz
{
namespace
{
struct RoadPose
{
    Vec2 at;
    double heading, along, distance2;
};

// Separating axes of the two oriented footprints, or the closest point to a circle.
bool obstructs(const Body &vehicle, const Body &other)
{
    if (std::abs(vehicle.height - other.height) > .5 * (vehicle.verticalExtent + other.verticalExtent))
        return false;
    const Vec2 delta = other.p - vehicle.p;
    const double bound =
        vehicle.halfW + vehicle.halfH + (other.circle ? other.radius : other.halfH + other.halfW);
    if (delta.length2() > bound * bound)
        return false;
    if (other.circle)
    {
        const Vec2 local = rotate(delta, -vehicle.angle);
        const Vec2 outside{std::max(0., std::abs(local.x) - vehicle.halfW),
                           std::max(0., std::abs(local.z) - vehicle.halfH)};
        return outside.length2() <= other.radius * other.radius;
    }
    const Vec2 a = forward(-vehicle.angle), b = forward(-other.angle);
    for (Vec2 axis : {a, perpendicular(a), b, perpendicular(b)})
    {
        double extent = vehicle.halfH * std::abs(dot(a, axis)) + vehicle.halfW * std::abs(cross(a, axis)) +
                        other.halfH * std::abs(dot(b, axis)) + other.halfW * std::abs(cross(b, axis));
        if (std::abs(dot(delta, axis)) > extent)
            return false;
    }
    return true;
}
} // namespace

bool World::safeRecoveryPose(const Car &car, Vec2 at, double heading) const
{
    if (!layout.urbanSite(at, car.body.halfW + 1.) || levelAt(at, car.body.halfH + 1.))
        return false;
    Body footprint = car.body;
    footprint.p = at;
    footprint.angle = -heading;
    footprint.halfW += .4;
    footprint.halfH += .4;
    footprint.height = terrain.height(at) + .8;
    double low = INFINITY, high = -INFINITY;
    for (int side = -1; side <= 1; ++side)
        for (int end = -1; end <= 1; ++end)
        {
            Vec2 p = at + rotate({side * footprint.halfW, end * footprint.halfH}, footprint.angle);
            const double h = terrain.height(p);
            if (layout.roadDistance(p) > layout.roadHalf() || h < CityLayout::waterLevel + .05 ||
                insideBuilding(p, h + .8, .3))
                return false;
            low = std::min(low, h);
            high = std::max(high, h);
        }
    if (high - low > 1.1)
        return false;
    auto blocked = [&](const Body &body) { return obstructs(footprint, body); };
    return std::ranges::none_of(staticBodies, blocked) &&
           std::ranges::none_of(cars, [&](const Car &c) { return &c != &car && blocked(c.body); }) &&
           std::ranges::none_of(people, [&](const Person &p) { return p.riding < 0 && blocked(p.body); }) &&
           std::ranges::none_of(scooters, [&](const Scooter &s) { return blocked(s.body); });
}

bool World::recover()
{
    if (controlledCar < 0 || transitPassenger() || drone.active || time < recoveryReadyAt)
        return false;
    auto &c = cars[controlledCar];
    if (c.destroyed || c.falling || c.recovering || c.body.v.length2() > 1.5 * 1.5 ||
        (c.kind == 12 && c.lift > .4))
        return false;

    std::vector<RoadPose> options;
    auto add = [&](Vec2 at, double heading, double along)
    { options.push_back({at, heading, along, (at - c.body.p).length2()}); };
    if (c.line >= 0)
    {
        const auto &line = lines[c.line];
        for (double s = 0; s < line.track.length(); s += 6.)
        {
            auto [p, heading] = line.pose(s);
            add(p, heading, s);
        }
    }
    else
    {
        int node = nearestNode(c.body.p), x = node % nodes, z = node / nodes;
        for (int j = std::max(1, z - 5); j <= std::min(nodes - 2, z + 5); ++j)
            for (int i = std::max(1, x - 5); i <= std::min(nodes - 2, x + 5); ++i)
                for (int d = 0; d < 4; ++d)
                {
                    if (!layout.road(i, j, d))
                        continue;
                    const Vec2 f = CityLayout::direction(d);
                    add(nodePoint(j * nodes + i) + f * (spacing * .5) + perpendicular(f) * CityLayout::lane,
                        std::atan2(f.x, f.z), 0);
                }
    }
    std::stable_sort(options.begin(), options.end(),
                     [](const RoadPose &a, const RoadPose &b) { return a.distance2 < b.distance2; });
    auto found = std::ranges::find_if(options, [&](const RoadPose &pose)
                                      { return safeRecoveryPose(c, pose.at, pose.heading); });
    if (found == options.end())
        return false;
    c.body.p = found->at;
    c.body.angle = -found->heading;
    c.body.roadLevel = 0;
    c.body.height = terrain.height(c.body.p) + .8;
    c.steering = c.lift = c.verticalVelocity = 0;
    if (c.line >= 0)
    {
        c.along = found->along;
        c.nextStop = lines[c.line].nextStopAfter(c.along);
        c.dwell = c.checkTrafficAt = 0;
    }
    c.beginRighting();
    recoveryReadyAt = time + 8;
    staticQueryCount = size_t(-1);
    physics.clear();
    indexTraffic();
    if (size_t(controlledCar) < tireContacts.size())
        tireContacts[controlledCar] = {};
    for (auto &passenger : people)
        if (passenger.riding == controlledCar)
            seatPassenger(passenger);
    return true;
}

bool World::safeExitPoint(const Car &car, Vec2 at) const
{
    const double floor = surface(at, car.body.roadLevel);
    const double from = surface(car.body.p, car.body.roadLevel);
    if (floor <= CityLayout::waterLevel + .1 || std::abs(floor - from) > 3. ||
        selectLevel(at, car.body.roadLevel, from) != car.body.roadLevel)
        return false;
    if (car.body.roadLevel)
    {
        const auto *level = levelById(car.body.roadLevel);
        if (!level || !level->contains(at, -.45))
            return false;
    }
    return vehicleSpace(at, floor + .85, &car, true);
}

std::optional<Vec2> World::playerVehicleExit(const Car &car) const
{
    if (auto door = vehicleExit(car))
        return door;
    if (car.destroyed || car.falling || car.body.v.length2() > 1.5 * 1.5)
        return {};
    for (int radius = 2; radius <= 10; ++radius)
        for (int i = 0; i < 24; ++i)
        {
            Vec2 at = car.body.p + forward(i * 2 * pi / 24.) * radius;
            const Vec2 local = rotate(at - car.body.p, -car.body.angle);
            if (std::abs(local.x) < car.body.halfW + .6 && std::abs(local.z) < car.body.halfH + .6)
                continue;
            if (!safeExitPoint(car, at))
                continue;
            // A clear point on the far side of a solid wall is not a usable exit.
            const Vec2 offset = at - car.body.p;
            const int steps = int(std::ceil(radius / .35));
            bool reachable = true;
            for (int step = 1; step < steps && reachable; ++step)
                reachable = safeExitPoint(car, car.body.p + offset * (double(step) / steps));
            if (reachable)
                return at;
        }
    return {};
}
} // namespace pz
