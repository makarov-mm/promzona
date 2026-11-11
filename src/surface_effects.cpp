#include "promzona/world.hpp"

namespace pz
{
double World::precipitationSurface(Vec2 p) const
{
    double floor = terrain.height(p);
    if (auto level = levelAt(p))
    {
        if (level->open(p))
            floor = level->floor(terrain, p);
        else if (!level->tunnel)
            floor = std::max(floor, level->floor(terrain, p));
    }
    return highestRoof(p, std::max(CityLayout::waterLevel, floor), 0.);
}

void World::addMark(SurfaceMark mark)
{
    constexpr size_t capacity = 1800;
    if (surfaceMarks.size() >= capacity)
        surfaceMarks.pop_front();
    auto mid = (mark.from + mark.to) * .5;
    bool blood = mark.kind == MarkKind::blood;
    auto along = blood ? Vec2{0, .51} : (mark.to - mark.from) * .5;
    auto across = perpendicular(along.unit()) * (blood ? .42 : .085);
    auto corner = [&](Vec2 p) { return Vec3{float(p.x), float(surface(p, mark.level) + .045), float(p.z)}; };
    mark.corners = {corner(mid - across - along), corner(mid + across - along), corner(mid + across + along),
                    corner(mid - across + along)};
    surfaceMarks.push_back(mark);
}

void World::updateTireMarks(Vec2 focus, double dt)
{
    // Expiration order differs for wet asphalt, sand and blood. Cull once per second.
    if (int(time) != int(time - dt) && !surfaceMarks.empty())
        std::erase_if(surfaceMarks, [&](const auto &m) { return time >= m.born + m.duration; });

    tireContacts.resize(cars.size());
    for (size_t i = 0; i < cars.size(); ++i)
    {
        auto &contact = tireContacts[i];
        const auto &car = cars[i];
        const auto &body = car.body;
        if (car.kind == 9 || car.kind == 12 || car.falling || !car.grounded() ||
            (body.p - focus).length2() > 85 * 85 || body.v.length2() < 2.)
        {
            contact.active = false;
            continue;
        }

        auto forwardDir = forward(car.heading()), right = Vec2{forwardDir.z, -forwardDir.x};
        auto rear = body.p - forwardDir * (.5 * car.wheelbase);
        bool sand = !body.roadLevel && !layout.bridge(rear) &&
                    (layout.riverDistance(rear.x) < layout.bankHalf() ||
                     std::max(std::abs(rear.x), std::abs(rear.z)) > layout.coordinate(nodes - 1) + 12.);
        bool skid = body.v.length2() > 20. && (car.braking || std::abs(dot(body.v, right)) > 2.);
        bool wet = rain > .45 && (body.roadLevel || layout.roadDistance(rear) < layout.roadHalf());
        if (wet)
            if (auto level = levelAt(rear))
                wet = level->tunnel ? (body.roadLevel != level->id || !level->roof(rear.x))
                                    : (body.roadLevel == level->id ||
                                       level->floor(terrain, rear) < terrain.height(rear) + .4);
        if (!sand && !skid && !wet)
        {
            contact.active = false;
            continue;
        }

        MarkKind kind = sand ? MarkKind::sand : skid ? MarkKind::rubber : MarkKind::wet;
        Vec2 wheels[]{rear - right * (.83 * body.halfW), rear + right * (.83 * body.halfW)};
        if (contact.active && contact.kind == kind && contact.level == body.roadLevel)
        {
            double distance = (wheels[0] - contact.wheels[0]).length2();
            if (distance < .16)
                continue;
            // Recovery, rail transfers or teleporting must never connect different streets.
            if (distance < 9.)
                for (int wheel = 0; wheel < 2; ++wheel)
                    addMark({contact.wheels[wheel], wheels[wheel], time,
                             kind == MarkKind::wet    ? 6.
                             : kind == MarkKind::sand ? 24.
                                                      : 40.,
                             body.roadLevel, kind});
        }
        contact = {{wheels[0], wheels[1]}, body.roadLevel, kind, true};
    }
}
} // namespace pz
