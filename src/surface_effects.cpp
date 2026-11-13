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

    // Drops rise from the surface and fall back through it to the bed (CityTraffic.Splash).
    emit(Vec3{float(point.x), float(CityLayout::waterLevel + .08), float(point.z)},
         terrain.bedHeight(point.x, point.z), ParticleKind::water, std::clamp(int(strength * 22), 5, 65),
         std::min(7., 1 + strength * 2));
}

// Port of CityTraffic.Noise: xorshift, apart from the sequence the city is driven by.
double World::effectNoise()
{
    effectSeed ^= effectSeed << 13;
    effectSeed ^= effectSeed >> 17;
    effectSeed ^= effectSeed << 5;
    return (effectSeed + .5) / 4294967296.;
}

// Port of CityTraffic.Emit: a burst that spreads in a cone about the vertical.
void World::emit(Vec3 at, double floor, ParticleKind kind, int count, double speed)
{
    using enum ParticleKind;
    for (int i = 0; i < count && particles.size() < maxParticles; ++i)
    {
        const double angle = effectNoise() * 2 * pi, spread = effectNoise();
        Particle p;
        p.kind = kind;
        p.p = at;
        p.floor = float(floor + .03);
        const double rise = speed * (.3 + effectNoise());
        p.v = {float(std::cos(angle) * speed * spread), float(rise), float(std::sin(angle) * speed * spread)};
        p.span = p.life = float(kind == fire     ? .5 + effectNoise() * .5
                                : kind == smoke  ? 2 + effectNoise() * 2
                                : kind == debris ? .5 + effectNoise()
                                : kind == water  ? 1.5
                                                 : .3 + effectNoise() * .6);
        p.size = float(kind == fire || kind == smoke ? .25 + effectNoise() * .4
                       : kind == debris              ? .025
                                                     : .035 + effectNoise() * .06);
        particles.push_back(p);
    }
}

void World::emit(Vec2 at, double above, int level, ParticleKind kind, int count, double speed)
{
    const double floor = surface(at, level);
    emit(Vec3{float(at.x), float(floor + above), float(at.z)}, floor, kind, count, speed);
}

double World::floorBelow(Vec2 at, double y) const
{
    double floor = terrain.height(at);
    if (const auto *level = levelAt(at))
    {
        const double deck = level->floor(terrain, at);
        // In a tunnel the ground is overhead; a deck counts for whatever is on or above it.
        if (level->tunnel ? y < floor - .5 : y >= deck - .3)
            floor = deck;
    }
    return floor;
}

// Port of the particle loop of CityTraffic.AfterContacts.
void World::stepParticles(double dt)
{
    const float step = float(dt);
    for (auto &p : particles)
    {
        p.life -= step;
        if (p.kind == ParticleKind::plain)
        {
            p.v.y -= step * 6;
            p.p = p.p + p.v * step;
            continue;
        }
        p.p = p.p + p.v * step;
        if (p.kind == ParticleKind::flame)
        {
            p.v.y += 1.4f * step;
            p.size += .55f * step;
        }
        else if (p.kind == ParticleKind::smoke)
        {
            p.v.y += .5f * step;
            p.size += .22f * step;
            p.v.x += .4f * step;
        }
        else
            p.v.y -= 9.81f * step;
        if (p.p.y < p.floor)
        {
            p.p.y = p.floor;
            p.v.y *= -.22f;
            p.v.x *= .8f;
            p.v.z *= .8f;
        }
    }
    std::erase_if(particles, [](const Particle &p) { return p.life <= 0; });
    if (particles.size() > maxParticles)
        particles.erase(particles.begin(),
                        particles.begin() + static_cast<std::ptrdiff_t>(particles.size() - maxParticles));
    std::erase_if(scuffs, [&](const Scuff &s) { return time - s.born >= s.duration(); });
}

// Port of the marks of CityTraffic.AfterContacts: a collision darkens the flanks of the
// car and the nearest wall within reach of it.
void World::addScuffs(const Car &car)
{
    if (scuffs.size() >= 96)
        scuffs.erase(scuffs.begin());
    scuffs.push_back({false, car.id, {}, false, time});
    if (roofCells.empty())
        return;
    const Vec2 at = car.body.p;
    for (auto index : roofCells[nearestNode(at)])
    {
        const auto &b = buildings[index];
        const Vec2 nearest{clamp(at.x, b.p.x - b.half.x, b.p.x + b.half.x),
                           clamp(at.z, b.p.z - b.half.z, b.p.z + b.half.z)};
        if ((nearest - at).length() > car.body.halfH + 1)
            continue;
        const bool facesX = std::abs(at.x - b.p.x) / b.half.x > std::abs(at.z - b.p.z) / b.half.z;
        const Vec2 wall = facesX ? Vec2{b.p.x + (at.x > b.p.x ? 1 : -1) * (b.half.x + .022), nearest.z}
                                 : Vec2{nearest.x, b.p.z + (at.z > b.p.z ? 1 : -1) * (b.half.z + .022)};
        if (scuffs.size() >= 96)
            scuffs.erase(scuffs.begin());
        scuffs.push_back(
            {true, 0, {float(wall.x), float(terrain.height(wall) + .828), float(wall.z)}, facesX, time});
        break;
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
            RoadPuddle puddle{centre, {}};
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
        bool wet = wetness > .45 && (body.roadLevel || layout.roadDistance(rear) < layout.roadHalf());
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
            // Tyres smoke in a hard slide and raise dust on sand, at the cadence of the
            // managed skid clock.
            if (distance < 9. && time >= contact.nextPuff && kind != MarkKind::wet)
            {
                contact.nextPuff = time + .08;
                if (kind == MarkKind::sand ? effectNoise() < .14 : std::abs(dot(body.v, right)) > 4)
                    emit(body.p, kind == MarkKind::sand ? .15 : .2, body.roadLevel,
                         kind == MarkKind::sand ? ParticleKind::sand : ParticleKind::smoke,
                         kind == MarkKind::sand ? 2 : 1, kind == MarkKind::sand ? 1.2 : .5);
            }
        }
        contact.wheels[0] = wheels[0];
        contact.wheels[1] = wheels[1];
        contact.kind = kind;
        contact.level = body.roadLevel;
        contact.active = true;
    }
}
} // namespace pz
