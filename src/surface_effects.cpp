#include "promzona/world.hpp"

namespace pz
{
void World::splash(Vec2 point, double strength)
{
    if ((point - position()).length2() > 200 * 200 ||
        terrain.bedHeight(point.x, point.z) >= CityLayout::waterLevel)
        return;
    strength = clamp(strength, .15, 3.);
    if (ripples.size() == 96)
        ripples.pop_front();
    ripples.push_back({point, time, strength});
    sounds.push_back({"splash", point, clamp(strength * .35, .12, .9)});

    // A cosmetic burst must not advance the traffic or combat random stream.
    Random spray(uint32_t(std::lround(time * 120)) ^ uint32_t(std::lround(point.x * 32)) ^
                 (uint32_t(std::lround(point.z * 32)) << 16));
    int drops = std::clamp(int(strength * 22), 5, 65);
    for (int i = 0; i < drops && particles.size() < 5000; ++i)
    {
        auto outward = forward(spray.next() * 2 * pi);
        float speed = float((.45 + spray.next()) * std::sqrt(strength));
        float up = float((1.6 + 1.6 * spray.next()) * std::sqrt(strength));
        particles.push_back({{float(point.x), float(CityLayout::waterLevel + .035), float(point.z)},
                             {float(outward.x) * speed, up, float(outward.z) * speed},
                             {.62f, .83f, .86f},
                             up / 3.f,
                             float(.025 + .04 * strength)});
    }
}

void World::generatePuddles()
{
    puddles.clear();
    puddleCells.assign(nodes * nodes, -1);
    for (int j = 0; j < nodes - 1; ++j)
        for (int i = 0; i < nodes - 1; ++i)
        {
            if ((i * 7 + j * 11) % 13 || !layout.road(i, j, 0))
                continue;
            Vec2 centre{layout.coordinate(i) + 1.8, layout.coordinate(j) + spacing * .5};
            if (!layout.urbanSite(centre, RoadPuddle::radiusX) || levelAt(centre, RoadPuddle::radiusX))
                continue;
            RoadPuddle puddle{centre};
            constexpr double x = RoadPuddle::radiusX, z = RoadPuddle::radiusZ;
            constexpr Vec2 offsets[]{{-x, -z}, {-x, z}, {x, z}, {x, -z}};
            for (size_t n = 0; n < puddle.corners.size(); ++n)
            {
                Vec2 p = centre + offsets[n];
                puddle.corners[n] = {float(p.x), float(terrain.height(p) + .024), float(p.z)};
            }
            puddleCells[j * nodes + i] = int(puddles.size());
            puddles.push_back(puddle);
        }
}

// Port of CityRoadDetail.Holes: one xorshift sequence per block, seeded by the city seed
// and the block, so the holes are a property of the city and need no place in a save.
std::vector<Pothole> blockPotholes(std::uint32_t seed, int i, int j, bool roadNorth, bool roadEast)
{
    std::vector<Pothole> holes;
    std::uint32_t state =
        (seed * 16777619u + std::uint32_t(i) * 73856093u + std::uint32_t(j) * 19349663u) | 1u;
    auto next = [&]
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state + .5) / 4294967296.;
    };
    for (int axis = 0; axis < 2; ++axis)
    {
        if (!(axis == 0 ? roadNorth : roadEast))
            continue;
        const int count = next() < .42 ? 0 : next() < .68 ? 1 : 2;
        double last = -10;
        for (int k = 0; k < count; ++k)
        {
            const double along = 12 + next() * 13;
            if (std::abs(along - last) < 3.5)
                continue;
            last = along;
            const double across = -3.5 + next() * 7;
            const double size = .48 + next() * .65;
            const int style = std::min(2, int(next() * 3));
            holes.push_back({axis == 0 ? Vec2{across, along} : Vec2{along, across}, size, style});
        }
    }
    return holes;
}

void World::generatePotholes()
{
    potholes.clear();
    const int blocks = nodes - 1;
    potholeFirst.assign(size_t(blocks) * blocks + 1, 0);
    for (int j = 0; j < blocks; ++j)
        for (int i = 0; i < blocks; ++i)
        {
            potholeFirst[size_t(j) * blocks + i] = std::uint32_t(potholes.size());
            const Vec2 node{layout.coordinate(i), layout.coordinate(j)};
            for (auto hole : blockPotholes(seed, i, j, layout.road(i, j, 0), layout.road(i, j, 1)))
            {
                hole.p += node;
                // Bridge decks, ramps and tunnel mouths have their own surface.
                if (layout.urbanSite(hole.p, hole.size) && !levelAt(hole.p, hole.size + 1) &&
                    !openCut(hole.p, hole.size))
                    potholes.push_back(hole);
            }
        }
    potholeFirst.back() = std::uint32_t(potholes.size());
}

// The managed rule: above 4 m/s a hole under the car kicks it up and pitches the nose;
// half a second passes before the next jolt or impact damage.
void World::potholeJolt(Car &car)
{
    const double speed = std::abs(dot(car.body.v, forward(car.heading())));
    if (car.destroyed || car.falling || car.body.roadLevel || !car.grounded() || speed <= 4 ||
        car.impactCooldown > 0 || potholeFirst.empty())
        return;
    const int blocks = nodes - 1;
    const int i = int(std::floor((car.body.p.x - layout.coordinate(0)) / spacing)),
              j = int(std::floor((car.body.p.z - layout.coordinate(0)) / spacing));
    for (int dj = -1; dj <= 1; ++dj)
        for (int di = -1; di <= 1; ++di)
        {
            const int x = i + di, z = j + dj;
            if (x < 0 || z < 0 || x >= blocks || z >= blocks)
                continue;
            const size_t block = size_t(z) * blocks + x;
            for (auto n = potholeFirst[block]; n < potholeFirst[block + 1]; ++n)
                if ((car.body.p - potholes[n].p).length() < potholes[n].size * .65)
                {
                    car.verticalVelocity = std::min(.8, speed * .035);
                    car.pitchRate += .18;
                    car.impactCooldown = .5;
                    ++potholeJolts;
                    return;
                }
        }
}

bool World::inRoadPuddle(Vec2 wheel) const
{
    // Each patch is inside one grid square. Sample the same ellipse drawn on the asphalt.
    int i = int(std::floor((wheel.x - layout.coordinate(0)) / spacing));
    int j = int(std::floor((wheel.z - layout.coordinate(0)) / spacing));
    if (i < 0 || j < 0 || i >= nodes || j >= nodes || puddleCells.empty())
        return false;
    int index = puddleCells[j * nodes + i];
    if (index < 0)
        return false;
    Vec2 d = wheel - puddles[index].centre;
    double x = d.x / RoadPuddle::radiusX, z = d.z / RoadPuddle::radiusZ;
    return x * x + z * z < .86;
}

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
            contact.active = contact.skidding = contact.inPuddle = false;
            continue;
        }

        auto forwardDir = forward(car.heading()), right = Vec2{forwardDir.z, -forwardDir.x};
        auto rear = body.p - forwardDir * (.5 * car.wheelbase);
        bool sand =
            !body.roadLevel && !layout.bridge(rear) && layout.sandyGround(rear, layout.roadDistance(rear));
        bool skid = body.v.length2() > 20. && (car.braking || std::abs(dot(body.v, right)) > 2.);
        bool wet = rain > .45 && (body.roadLevel || layout.roadDistance(rear) < layout.roadHalf());
        if (wet)
            if (auto level = levelAt(rear))
                wet = level->tunnel ? (body.roadLevel != level->id || !level->roof(rear.x))
                                    : (body.roadLevel == level->id ||
                                       level->floor(terrain, rear) < terrain.height(rear) + .4);
        Vec2 wheels[]{rear - right * (.83 * body.halfW), rear + right * (.83 * body.halfW)};
        bool inPuddle = false;
        if (!body.roadLevel)
            for (Vec2 wheel : wheels)
                if (inRoadPuddle(wheel) || inRoadPuddle(wheel + forwardDir * car.wheelbase))
                {
                    inPuddle = true;
                    contact.wetUntil = time + 8.;
                    break;
                }
        if (inPuddle && !contact.inPuddle && time >= contact.nextSplash && body.v.length2() > 9.)
        {
            sounds.push_back({"splash", rear, std::min(.55, body.v.length() * .035)});
            contact.nextSplash = time + .8;
        }
        bool squeal = skid && !sand && !wet && !inPuddle && time >= contact.wetUntil;
        if (squeal && !contact.skidding && time >= contact.nextSqueal)
        {
            sounds.push_back({"brake", rear, .4});
            contact.nextSqueal = time + 1.;
        }
        contact.skidding = squeal;
        contact.inPuddle = inPuddle;
        wet = wet || time < contact.wetUntil;
        if (!sand && !skid && !wet)
        {
            contact.active = false;
            continue;
        }

        MarkKind kind = sand ? MarkKind::sand : skid ? MarkKind::rubber : MarkKind::wet;
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
        contact.wheels[0] = wheels[0];
        contact.wheels[1] = wheels[1];
        contact.kind = kind;
        contact.level = body.roadLevel;
        contact.active = true;
    }
}
} // namespace pz
