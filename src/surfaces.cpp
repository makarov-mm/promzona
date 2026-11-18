#include "promzona/world.hpp"
#include <bit>

namespace Promzona
{
namespace
{
// Bit for bit: what is read back is then the very number worked out at that place.
bool same_place(Vec2 a, Vec2 b)
{
    return std::bit_cast<std::uint64_t>(a.x) == std::bit_cast<std::uint64_t>(b.x) &&
           std::bit_cast<std::uint64_t>(a.z) == std::bit_cast<std::uint64_t>(b.z);
}
} // namespace

const RoadLevel *World::levelById(int id) const
{
    for (const auto &s : roadLevels)
        if (s.id == id)
            return &s;
    return nullptr;
}
void World::LevelCells::build(std::span<const RoadLevel> levels)
{
    *this = {};

    if (levels.empty())
        return;

    // The box RoadLevel::contains keeps a point in when asked with `margin`, a millimetre
    // wider: rounding in that test cannot put a point of the level outside the box.
    struct Box
    {
        double x0, x1, z0, z1;
    };
    const auto box = [](const RoadLevel &s)
    {
        const double along = RoadLevel::length + margin + .001, reach = RoadLevel::half_width + margin + .001;
        return Box{s.x - along, s.x + along, s.z - reach, s.z + RoadLevel::bend + reach};
    };

    Box all = box(levels.front());

    for (const RoadLevel &s : levels)
    {
        const Box b = box(s);
        all = {std::min(all.x0, b.x0), std::max(all.x1, b.x1), std::min(all.z0, b.z0),
               std::max(all.z1, b.z1)};
    }

    // The same rounding as in none_near(): a point between two others never falls in a cell
    // outside theirs.
    const auto index = [](double at, double from) { return int((at - from) / cell); };

    x = all.x0;
    z = all.z0;
    columns = index(all.x1, x) + 1;
    rows = index(all.z1, z) + 1;
    occupied.assign(size_t(columns) * rows, 0);

    for (const RoadLevel &s : levels)
    {
        const Box b = box(s);

        for (int row = index(b.z0, z); row <= index(b.z1, z); ++row)
            for (int column = index(b.x0, x); column <= index(b.x1, x); ++column)
                occupied[size_t(row) * columns + column] = 1;
    }
}

bool World::LevelCells::none_near(Vec2 p) const
{
    const double u = (p.x - x) / cell, v = (p.z - z) / cell;

    // Written so that a position that is not a number lies beyond the cells as well.
    if (!(u >= 0 && v >= 0 && u < columns && v < rows))
        return true;

    return !occupied[size_t(int(v)) * columns + int(u)];
}

const RoadLevel *World::levelAt(Vec2 p, double margin) const
{
    if (margin <= LevelCells::margin && level_cells.none_near(p))
        return nullptr;

    for (const auto &s : roadLevels)
        if (s.contains(p, margin))
            return &s;
    return nullptr;
}
const RoadLevel *World::levelEdge(int from, int to) const
{
    for (const auto &s : roadLevels)
        if ((s.left_node == from && s.right_node == to) || (s.right_node == from && s.left_node == to))
            return &s;
    return nullptr;
}
double World::surface(Vec2 p, int level) const
{
    // Level 0 is the ground: the ids of the road levels start at 1.
    if (level)
        if (auto s = levelById(level); s && s->contains(p, .15))
            return s->floor(terrain, p);
    return terrain.height(p);
}
bool World::openCut(Vec2 p, double margin) const
{
    return std::ranges::any_of(roadLevels, [&](const RoadLevel &s) { return s.open(p, margin); });
}
int World::selectLevel(Vec2 p, int previous, double foot) const
{
    auto s = levelAt(p, .001);
    if (!s)
        return 0;
    double y = s->floor(terrain, p), ground = terrain.height(p);
    if (previous == s->id)
        return previous;
    if (s->open(p))
        return s->id;
    // Never acquire a bridge overhead, or a tunnel underneath a district.
    if (std::abs(foot - y) < .65 && std::abs(foot - y) < std::abs(foot - ground) + .01)
        return s->id;
    // A falling actor can land on the topmost surface underneath it.
    if (!s->tunnel && foot >= y && foot - y < foot - ground)
        return s->id;
    return 0;
}
bool World::scenerySolid(Vec2 p, double y, double radius) const
{
    double ground = terrain.height(p);
    const auto *s = levelAt(p, radius);
    if (!s)
    {
        if (y >= ground + radius)
            return false;
        if (y >= ground - CityLayout::canalDeckThickness - radius || !layout.bridge(p))
            return true;
        return y < terrain.bed_height(p.x, p.z) + radius;
    }
    double floor = s->floor(terrain, p);
    bool wall = std::abs(p.z - s->centre_z(p.x)) > RoadLevel::half_width - .14 - radius &&
                std::abs(p.x - s->x) < RoadLevel::length - 3;
    double top = s->tunnel ? (s->roof(p.x) ? floor + RoadLevel::clearance : std::max(ground + .6, floor + .8))
                           : floor + .85;
    if (wall && y > floor - (s->tunnel ? 0 : .55) - radius && y < top + radius)
        return true;
    if (s->tunnel)
        return y < floor + radius ||
               (s->roof(p.x) && y > floor + RoadLevel::clearance - radius && y < ground + radius);
    return y < ground + radius || (y > floor - .55 - radius && y < floor + radius);
}
bool World::vehicleSpace(Vec2 at, double height, const Car *ignore, bool includePlayer) const
{
    auto overlaps = [&](const Body &body)
    {
        if (std::abs(body.height - height) > body.verticalExtent * .5 + .8)
            return false;
        const Vec2 delta = at - body.p;
        if (body.circle)
            return delta.length2() < (body.radius + .35) * (body.radius + .35);
        const double bound = body.halfW + body.halfH + .7;
        if (delta.length2() > bound * bound)
            return false;
        auto d = rotate(delta, -body.angle);
        return std::abs(d.x) < body.halfW + .35 && std::abs(d.z) < body.halfH + .35;
    };
    auto solidProp = [&](const Body &b) { return b.invMass < 1. && overlaps(b); };
    bool nearby = staticQueryCount == staticBodies.size() && (at - staticQueryCentre).length2() < 110. * 110.;
    const bool blocked =
        nearby ? std::ranges::any_of(nearbyStaticBodies, [&](size_t i) { return solidProp(staticBodies[i]); })
               : std::ranges::any_of(staticBodies, solidProp);
    return !(scenerySolid(at, height, .35) || inside_building(at, height, .35) || blocked ||
             std::ranges::any_of(cars, [&](const Car &other)
                                 { return &other != ignore && overlaps(other.body); }) ||
             std::ranges::any_of(people, [&](const Person &p)
                                 { return !p.dead && p.riding < 0 && overlaps(p.body); }) ||
             (includePlayer && controlledCar < 0 && overlaps(player)));
}

std::optional<Vec2> World::vehicleExit(const Car &car, std::optional<Vec2> toward) const
{
    const double first = toward && rotate(*toward - car.body.p, -car.body.angle).x < 0 ? -1. : 1.;
    for (double along : {0., -.6, .7, -1.3, 1.3})
        for (double side : {first, -first})
        {
            Vec2 at =
                car.body.p + rotate({side * (car.body.halfW + .9), along * car.body.halfH}, car.body.angle);
            if (!safe_exit_point(car, at))
                continue;
            return at;
        }
    return std::nullopt;
}

double World::floor_under(Vec2 p, int level, Footing *footing) const
{
    if (!footing)
        return surface(p, level);

    if (!footing->known || footing->level != level || !same_place(footing->at, p))
        *footing = {p, level, surface(p, level), true};

    return footing->floor;
}

Vec2 World::slope_under(Vec2 p, int level, Slope &slope) const
{
    if (!slope.known || slope.level != level || !same_place(slope.at, p))
    {
        const Vec2 gradient{(surface(p + Vec2{.25, 0}, level) - surface(p - Vec2{.25, 0}, level)) * 2,
                            (surface(p + Vec2{0, .25}, level) - surface(p - Vec2{0, .25}, level)) * 2};
        slope = {p, level, gradient, true};
    }

    return slope.gradient;
}

void World::supportCar(Car &c, Vec2 previousPosition, double dt, Footing *footing)
{
    auto &b = c.body;
    double previousFloor = floor_under(previousPosition, b.roadLevel, footing), foot = b.height - .8;
    b.roadLevel = selectLevel(b.p, b.roadLevel, foot);
    double floor = floor_under(b.p, b.roadLevel, footing);
    if (c.kind == 12 && c.destroyed)
    {
        double roof = flightFloor(b.p);
        if (foot >= roof - .15)
            floor = std::max(floor, roof);
        double oldRoof = flightFloor(previousPosition);
        if (foot >= oldRoof - .15)
            previousFloor = std::max(previousFloor, oldRoof);
    }
    bool attached = c.lift < .03 && c.verticalVelocity <= 0 && std::abs(foot - previousFloor) < .2;
    if (attached && std::abs(floor - previousFloor) < .45)
        foot = floor;
    else
    {
        c.verticalVelocity -= 9.81 * dt;
        foot += c.verticalVelocity * dt;
    }
    // Tunnel roof and bridge underside block an upward impulse; a deck cannot be
    // mistaken for the road below while falling past its edge.
    if (auto s = levelAt(b.p))
    {
        double ceiling = s->tunnel && b.roadLevel == s->id && s->roof(b.p.x) ? floor + RoadLevel::clearance
                         : !s->tunnel && b.roadLevel != s->id                ? s->floor(terrain, b.p) - .55
                                                                             : 1e9;
        if (foot + b.verticalExtent > ceiling && c.verticalVelocity > 0)
        {
            foot = ceiling - b.verticalExtent;
            c.verticalVelocity = std::min(0., -c.verticalVelocity * .15);
        }
    }
    if (foot <= floor)
    {
        foot = floor;
        // Port of WreckVerticalStep: a wreck that comes down hard thuds, raises smoke and
        // hops once more.
        // A car on its wheels hops as well (AfterContacts): 22 % of the speed it came
        // down with, until that is less than 0.15 m/s.
        const double landing = -c.verticalVelocity;
        c.verticalVelocity = c.destroyed           ? (landing > 1.8 ? landing * .12 : 0)
                             : landing * .22 < .15 ? 0
                                                   : landing * .22;
        if (c.destroyed && landing > 2 && (b.p - position()).length2() < 160 * 160)
        {
            sounds.push_back({"impact", b.p, std::min(1., landing / 12)});
            emit(Vec3{float(b.p.x), float(floor + .3), float(b.p.z)}, floor, ParticleKind::smoke, 8, 1.5);
        }
    }
    c.lift = std::max(0., foot - floor);
    c.wreckSupported = c.lift < .03 && c.verticalVelocity <= 0;
    b.height = foot + .8;
}
void World::supportPerson(Body &b, Vec2 previousPosition, double dt, Footing *footing)
{
    double previousFloor = floor_under(previousPosition, b.roadLevel, footing), foot = b.height - .85;
    b.roadLevel = selectLevel(b.p, b.roadLevel, foot);
    double floor = floor_under(b.p, b.roadLevel, footing);
    if (std::abs(foot - previousFloor) < .2 && std::abs(floor - previousFloor) < .45 && b.verticalSpeed <= 0)
        foot = floor;
    else
    {
        b.verticalSpeed -= 9.81 * dt;
        foot += b.verticalSpeed * dt;
    }
    if (foot <= floor)
    {
        foot = floor;
        b.verticalSpeed = 0;
    }
    b.height = foot + .85;
}
void World::appendLevelContacts(std::uint32_t &id)
{
    auto box = [&](Vec2 p, double low, double top, Vec2 half, double angle = 0)
    {
        if (top <= low)
            return;
        Body b;
        b.id = id++;
        b.p = p;
        b.height = (low + top) * .5;
        b.verticalExtent = top - low;
        b.halfW = half.x;
        b.halfH = half.z;
        b.angle = angle;
        staticBodies.push_back(b);
    };
    for (auto &s : roadLevels)
    {
        for (int k = 3; k < 285; ++k)
        {
            double x = s.x - RoadLevel::length + k, next = x + 1;
            for (int side : {-1, 1})
            {
                Vec2 a{x, s.centre_z(x) + side * RoadLevel::half_width},
                    b{next, s.centre_z(next) + side * RoadLevel::half_width};
                auto p = (a + b) * .5;
                double floor = s.floor(terrain, p);
                double top = s.tunnel ? (s.roof(p.x) ? floor + RoadLevel::clearance
                                                     : std::max(terrain.height(p) + .6, floor + .8))
                                      : floor + .85;
                box(p, floor - (s.tunnel ? 0 : .55), top, {(b - a).length() * .5 + .015, .10},
                    std::atan2(b.z - a.z, b.x - a.x));
            }
        }
        if (!s.tunnel)
            for (double x = s.x - 54; x <= s.x + 54; x += 36)
            {
                for (int side : {-1, 1})
                {
                    Vec2 p{x, s.centre_z(x) + side * 7.1};
                    box(p, terrain.height(p), s.floor(terrain, p) - .55, {.7, .7});
                }
                // The cap beam is drawn 0.55 to 1.25 m under the deck. A body's height is its
                // origin, 0.8 m above the wheels whatever the vehicle, and the contact test
                // centres the vertical extent on it: a MAZ or an ambulance on the deck reached
                // the beam under its own wheels and stood there, on the level deck and, with
                // the beam ahead higher than the wheels, on the ramps. The collider sits lower.
                Vec2 p{x, s.centre_z(x)};
                box(p, s.floor(terrain, p) - 3.1, s.floor(terrain, p) - 2.4, {.8, 7.8});
            }
    }
}
int World::chooseWalkNext(int n, int previous, Random *source)
{
    auto options = layout.neighbours(n);
    std::erase_if(options,
                  [&](int next)
                  {
                      auto a = nodePoint(n) + Vec2{9.1, 9.1}, b = nodePoint(next) + Vec2{9.1, 9.1};
                      for (int k = 0; k <= 36; ++k)
                          if (openCut(a + (b - a) * (k / 36.), .5))
                              return true;
                      return false;
                  });
    if (options.size() > 1)
        std::erase(options, previous);
    return options.empty() ? n : options[(source ? *source : random).index(int(options.size()))];
}
} // namespace Promzona
