#include "promzona/world.hpp"

namespace pz
{
namespace
{
using Kind = World::FixtureKind;
struct FixtureShape
{
    double kg, halfWidth, halfDepth, height;
    bool round;
};
// Body and model share a centre pivot. The first three entries keep their saved indices.
constexpr std::array<FixtureShape, size_t(Kind::count)> shapes{{{22, .30, .30, .85, true},
                                                                {65, .25, .25, .82, true},
                                                                {110, .75, .55, 1.15, false},
                                                                {80, 1, .28, 1, false},
                                                                {180, .70, .70, 2.3, false},
                                                                {140, .3682, .3718, 1.85, false},
                                                                {28, .35, .35, 1.46, true},
                                                                {.08, .16, .16, .34, true},
                                                                {.4, .13, .13, .09, true},
                                                                {.06, .10, .10, .074, true},
                                                                {.06, .10, .10, .07, true},
                                                                {5, .27, .27, .56, true}}};
int cell(double x)
{
    return clamp(int(std::floor(x / World::spacing + (World::nodes - 1) * .5)), 0, World::nodes - 1);
}
double groundOffset(Kind kind)
{
    return kind <= Kind::dumpster ? .08 : .006;
}
} // namespace

void World::registerFixture(Body &body, FixtureKind kind)
{
    const auto &shape = shapes[size_t(kind)];
    body.circle = shape.round;
    body.radius = shape.halfWidth;
    body.halfW = shape.halfWidth;
    body.halfH = shape.halfDepth;
    body.verticalExtent = shape.height;
    body.height = terrain.height(body.p) + groundOffset(kind) + shape.height * .5;
    body.friction = .58;
    body.restitution = .18;
    body.mass(shape.kg);
    body.sleeping = true;
    fixtureByBody.resize(staticBodies.size() + 1, -1);
    fixtureByBody.back() = int(fixtures.size());
    fixtureCells[cell(body.p.z) * nodes + cell(body.p.x)].push_back(fixtures.size());
    fixtures.push_back({staticBodies.size(), body.p, kind, shape.height});
    fixtures.back().homeAngle = body.angle;
}

void World::fixturesNear(Vec2 centre, double radius, std::vector<size_t> &out) const
{
    out.clear();
    if (fixtureCells.empty())
        return;
    const double rr = radius * radius;
    for (int z = cell(centre.z - radius); z <= cell(centre.z + radius); ++z)
        for (int x = cell(centre.x - radius); x <= cell(centre.x + radius); ++x)
            for (auto id : fixtureCells[z * nodes + x])
                if (!fixtures[id].displaced && (fixtures[id].home - centre).length2() <= rr)
                    out.push_back(id);
    for (auto id : displacedFixtures)
        if ((staticBodies[fixtures[id].body].p - centre).length2() <= rr)
            out.push_back(id);
}

void World::generateStreetFurniture()
{
    // Reserve the visible footprint of buildings, lamps, trees and service entrances, then
    // each accepted prop. Buckets keep placement local even on a full 49 x 49 street grid.
    struct Footprint
    {
        Vec2 centre, half;
    };
    std::vector<std::vector<Footprint>> occupied(nodes * nodes);
    auto visitCells = [&](Vec2 p, Vec2 h, auto visit)
    {
        for (int z = cell(p.z - h.z); z <= cell(p.z + h.z); ++z)
            for (int x = cell(p.x - h.x); x <= cell(p.x + h.x); ++x)
                if (!visit(occupied[z * nodes + x]))
                    return false;
        return true;
    };
    auto reserve = [&](Vec2 p, Vec2 h)
    {
        visitCells(p, h,
                   [&](auto &bucket)
                   {
                       bucket.push_back({p, h});
                       return true;
                   });
    };
    for (const auto &b : staticBodies)
    {
        double c = std::abs(std::cos(b.angle)), s = std::abs(std::sin(b.angle));
        reserve(b.p, b.circle ? Vec2{b.radius, b.radius}
                              : Vec2{c * b.halfW + s * b.halfH, s * b.halfW + c * b.halfH});
    }
    for (const auto &site : sites)
        reserve(site.entrance, {2.3, 2.3});
    for (const auto &line : lines)
        for (int n = 0; n < int(line.stops.size()); ++n)
            reserve(line.curb(n), {2, 2});
    for (const auto &pickup : pickups)
        reserve(pickup.p, {.85, .85});

    auto place = [&](Kind kind, Vec2 p, double angle)
    {
        const auto &shape = shapes[size_t(kind)];
        double c = std::abs(std::cos(angle)), s = std::abs(std::sin(angle));
        Vec2 half = shape.round ? Vec2{shape.halfWidth, shape.halfWidth}
                                : Vec2{c * shape.halfWidth + s * shape.halfDepth,
                                       s * shape.halfWidth + c * shape.halfDepth};
        double radius = half.length();
        if (!layout.urbanSite(p, radius + .2) || layout.roadDistance(p) < layout.roadHalf() + radius + .1 ||
            std::ranges::any_of(roadLevels,
                                [&](const auto &level) { return level.reserved(p, radius + .3); }))
            return;
        Vec2 clearance = half + Vec2{.22, .22};
        if (!visitCells(p, clearance,
                        [&](const auto &bucket)
                        {
                            for (const auto &other : bucket)
                                if (std::abs(p.x - other.centre.x) < clearance.x + other.half.x &&
                                    std::abs(p.z - other.centre.z) < clearance.z + other.half.z)
                                    return false;
                            return true;
                        }))
            return;
        Body body;
        body.id = 0x60000000u + std::uint32_t(fixtures.size());
        body.p = p;
        body.angle = angle;
        registerFixture(body, kind);
        staticBodies.push_back(body);
        reserve(p, half);
    };
    // Keep the world RNG and all existing actor and fixture identities stable for earlier saves.
    Random noise(seed ^ 4191u);
    constexpr Kind kiosks[]{Kind::phoneBooth, Kind::drinkMachine, Kind::mailbox};
    constexpr Kind litter[]{Kind::bag, Kind::bottle, Kind::packet};
    for (int j = 0; j < nodes - 1; ++j)
        for (int i = 0; i < nodes - 1; ++i)
        {
            if (layout.canalColumn(i) || layout.clinic(i, j) || layout.station(i, j))
                continue;
            Vec2 corner{layout.coordinate(i), layout.coordinate(j)}, mid = corner + Vec2{18, 18};
            bool park = layout.park(i, j);
            if (park)
                for (int side : {-1, 1})
                    place(Kind::bench, mid + Vec2{side * 7., 6.}, pi);
            if ((i * 13 + j * 7) % 11 == 0)
                place(kiosks[(i + j) % 3], mid - Vec2{6.4, 6.4}, pi);
            for (int d = 0; d < 2; ++d)
                if (!layout.road(i, j, d))
                {
                    auto along = CityLayout::direction(d);
                    for (int side : {-1, 1})
                        place(Kind::bench, corner + along * (18 + side * 4.5) + perpendicular(along) * 2.5,
                              d == 0 ? pi / 2 : 0);
                }
            int count = 3 + noise.index(4);
            for (int n = 0; n < count; ++n)
            {
                bool lawn = park && n % 2 == 0;
                Vec2 p{mid.x - 11 + noise.next() * 22,
                       mid.z + (lawn ? -10 + noise.next() * 20 : -9.5 + noise.next() * .65)};
                Kind kind = n == 2 && (i + j) % 3 == 0 ? Kind::garbageBag
                            : lawn                     ? (n % 4 == 0 ? Kind::bottle : Kind::can)
                                                       : litter[n % 3];
                place(kind, p, noise.next() * 2 * pi - pi);
            }
        }
}

void World::supportFixture(StreetFixture &f)
{
    auto &b = staticBodies[f.body];
    double foot = b.height - b.verticalExtent * .5;
    b.roadLevel = selectLevel(b.p, b.roadLevel, foot);
    const auto &shape = shapes[size_t(f.kind)];
    double c = std::abs(std::cos(f.tilt)), s = std::abs(std::sin(f.tilt));
    b.verticalExtent = c * shape.height + s * 2 * shape.halfWidth;
    b.halfW = c * shape.halfWidth + s * shape.height * .5;
    b.circle = shape.round && std::abs(b.halfW - shape.halfWidth) < .02;
    b.height = surface(b.p, b.roadLevel) + groundOffset(f.kind) + f.lift + b.verticalExtent * .5;
}

void World::updateFixtures(Vec2 focus, double dt)
{
    for (auto index : nearbyStaticBodies)
    {
        if (index >= staticBodies.size() || index >= fixtureByBody.size() || fixtureByBody[index] < 0)
            continue;
        auto &b = staticBodies[index];
        auto &f = fixtures[fixtureByBody[index]];
        if ((b.p - focus).length2() > 130 * 130)
            continue;
        if (f.kind == Kind::bag)
        {
            double phase = double(index % 997);
            b.wake();
            b.v.x += (1.6 + .7 * std::sin(time * .38 + phase) - b.v.x) * dt * .8;
            b.v.z += (std::sin(time * .23 + phase * .7) - b.v.z) * dt * .6;
            if (f.lift < .01 && b.verticalSpeed <= 0 && std::sin(time * .7 + phase) > .97)
            {
                b.verticalSpeed = 1.5;
                f.spin = 1.2;
            }
        }
        if (f.lift < .02 && (f.kind == Kind::can || f.kind == Kind::bottle))
        {
            auto slope = terrain.gradient(b.p);
            double grade = slope.length();
            if (grade > .025)
            {
                b.wake();
                b.v -= slope * (9.81 / (1 + grade * grade) * (1 - .025 / grade) * dt);
            }
        }
        if (b.sleeping && f.lift == 0 && f.spin == 0)
            continue;
        double speed = b.v.length();
        if (!f.displaced && ((b.p - f.home).length2() > .035 * .035 || b.verticalSpeed > 0 ||
                             std::abs(wrap(b.angle - f.homeAngle)) > .035))
        {
            f.displaced = true;
            displacedFixtures.insert(size_t(fixtureByBody[index]));
            b.verticalSpeed = std::max(b.verticalSpeed, std::min(4., speed * .19));
            if (f.spin == 0)
                f.spin = std::min(7., speed * .4) * (b.v.x + b.v.z < 0 ? -1 : 1);
            if (f.kind == FixtureKind::hydrant)
                f.sprayUntil = time + 16;
        }
        if (!f.displaced)
            continue;
        b.verticalSpeed -= 9.81 * dt;
        f.lift += b.verticalSpeed * dt;
        f.tilt = wrap(f.tilt + f.spin * dt);
        if (f.lift <= 0)
        {
            f.lift = 0;
            b.verticalSpeed = b.verticalSpeed < -1 ? -b.verticalSpeed * .28 : 0;
            double drag = std::exp(-2.2 * dt);
            b.v = b.v * drag;
            b.w *= drag;
            f.spin *= drag;
            if (std::abs(f.spin) < .05)
            {
                f.spin = 0;
                // Settle on the nearest face rather than balance on an edge.
                double rest = std::round(f.tilt / (pi / 2)) * (pi / 2);
                f.tilt += (rest - f.tilt) * std::min(1., 9 * dt);
                if (std::abs(rest - f.tilt) < .015)
                    f.tilt = rest;
                else
                    b.wake();
            }
        }
        const double previousFoot = b.height - b.verticalExtent * .5;
        supportFixture(f);
        if (b.height - b.verticalExtent * .5 <= CityLayout::waterLevel &&
            terrain.bedHeight(b.p.x, b.p.z) < CityLayout::waterLevel - .15)
        {
            if (previousFoot > CityLayout::waterLevel)
                splash(b.p, clamp(b.halfW + .25 * b.halfH, .3, 1.1));
            double waterDrag = std::exp(-2. * dt);
            b.v = b.v * waterDrag;
            b.w *= waterDrag;
            f.spin *= waterDrag;
        }
    }
    if (time < nextFixtureRestore)
        return;
    nextFixtureRestore = time + 1;
    for (auto it = displacedFixtures.begin(); it != displacedFixtures.end();)
    {
        auto current = it++;
        auto &f = fixtures[*current];
        if (!f.displaced || f.body >= staticBodies.size() || (f.home - focus).length2() < 145 * 145)
            continue;
        auto &b = staticBodies[f.body];
        if ((b.p - focus).length2() < 145 * 145)
            continue;
        b.p = f.home;
        b.v = {};
        b.angle = f.homeAngle;
        b.w = b.verticalSpeed = 0;
        b.sleepTimer = 0;
        b.sleeping = true;
        b.roadLevel = 0;
        f.lift = f.tilt = f.spin = f.sprayUntil = 0;
        f.displaced = false;
        displacedFixtures.erase(current);
        supportFixture(f);
        staticQueryCount = size_t(-1);
    }
}
} // namespace pz
