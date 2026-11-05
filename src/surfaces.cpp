#include "promzona/world.hpp"
namespace pz
{
const RoadLevel *World::levelById(int id) const
{
    for (const auto &s : roadLevels)
        if (s.id == id)
            return &s;
    return nullptr;
}
const RoadLevel *World::levelAt(Vec2 p, double margin) const
{
    for (const auto &s : roadLevels)
        if (s.contains(p, margin))
            return &s;
    return nullptr;
}
const RoadLevel *World::levelEdge(int from, int to) const
{
    for (const auto &s : roadLevels)
        if ((s.leftNode == from && s.rightNode == to) || (s.rightNode == from && s.leftNode == to))
            return &s;
    return nullptr;
}
double World::surface(Vec2 p, int level) const
{
    if (auto s = levelById(level); s && s->contains(p, .15))
        return s->floor(terrain, p);
    return terrain.height(p);
}
bool World::openCut(Vec2 p, double margin) const
{
    for (const auto &s : roadLevels)
        if (s.open(p, margin))
            return true;
    return false;
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
        return y < ground + radius;
    double floor = s->floor(terrain, p);
    bool wall = std::abs(p.z - s->centreZ(p.x)) > RoadLevel::halfWidth - .14 - radius &&
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
void World::supportCar(Car &c, Vec2 previousPosition, double dt)
{
    auto &b = c.body;
    double previousFloor = surface(previousPosition, b.roadLevel), foot = b.height - .8;
    b.roadLevel = selectLevel(b.p, b.roadLevel, foot);
    double floor = surface(b.p, b.roadLevel);
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
        c.verticalVelocity = 0;
    }
    c.lift = std::max(0., foot - floor);
    c.wreckSupported = c.lift < .03;
    b.height = foot + .8;
}
void World::supportPerson(Body &b, Vec2 previousPosition, double dt)
{
    double previousFloor = surface(previousPosition, b.roadLevel), foot = b.height - .85;
    b.roadLevel = selectLevel(b.p, b.roadLevel, foot);
    double floor = surface(b.p, b.roadLevel);
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
                Vec2 a{x, s.centreZ(x) + side * RoadLevel::halfWidth},
                    b{next, s.centreZ(next) + side * RoadLevel::halfWidth};
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
                    Vec2 p{x, s.centreZ(x) + side * 7.1};
                    box(p, terrain.height(p), s.floor(terrain, p) - .55, {.7, .7});
                }
                Vec2 p{x, s.centreZ(x)};
                box(p, s.floor(terrain, p) - 1.25, s.floor(terrain, p) - .55, {.8, 7.8});
            }
    }
}
int World::chooseWalkNext(int n, int previous)
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
    return options.empty() ? n : options[random.index(int(options.size()))];
}
} // namespace pz
