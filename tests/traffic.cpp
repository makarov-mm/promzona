#include "promzona/city_mesh.hpp"
#include <iostream>
#include <stdexcept>
using namespace pz;
namespace
{
int checks{};
void check(bool value, const char *message)
{
    ++checks;
    if (!value)
        throw std::runtime_error(message);
}
void pathClear(const World &w, const Path &path, int level = 0)
{
    for (double s = 0; s <= path.length(); s += .35)
    {
        auto p = path.sample(s).first;
        if (level)
        {
            auto road = w.levelById(level);
            check(road && road->contains(p) && std::abs(p.z - road->centreZ(p.x)) < 4.3,
                  "route stays on raised/sunken carriageway");
        }
        else
        {
            if (level)
            {
                auto road = w.levelById(level);
                check(road && road->contains(p) && std::abs(p.z - road->centreZ(p.x)) < 4.3,
                      "route stays on raised/sunken carriageway");
            }
            else
            {
                check(w.layout.roadDistance(p) < w.layout.roadHalf(), "route stays on existing road");
                auto cutting = w.levelAt(p, .3);
                if (cutting && cutting->open(p, .3) && std::abs(p.x - cutting->x) < RoadLevel::length - 12)
                {
                    std::cerr << "cut at " << p.x << "," << p.z << " level=" << cutting->id
                              << " center=" << cutting->x << "," << cutting->z << '\n';
                    check(false, "ordinary traffic never crosses an open cutting");
                }
            }
            auto cutting = w.levelAt(p, .3);
            if (cutting && cutting->open(p, .3) && std::abs(p.x - cutting->x) < RoadLevel::length - 12)
            {
                std::cerr << "cut at " << p.x << "," << p.z << " level=" << cutting->id
                          << " center=" << cutting->x << "," << cutting->z << '\n';
                check(false, "ordinary traffic never crosses an open cutting");
            }
        }
        check(w.surface(p, level) > CityLayout::waterLevel, "route does not enter canal or ocean");
    }
}
} // namespace
int main()
{
    try
    {
        World w;
        w.generate(20261105);
        // Walk real route transitions over the random graph, including its dead ends.
        int deadEnds = 0;
        for (int n = 0; n < World::nodes * World::nodes; ++n)
        {
            if (w.neighbours(n).size() != 1)
                continue;
            ++deadEnds;
            Car c;
            c.routeFrom = w.neighbours(n)[0];
            c.routeTo = n;
            c.routePhase = 0;
            auto f = (World::nodePoint(n) - World::nodePoint(c.routeFrom)).unit();
            auto start = World::nodePoint(n) - f * 11 + perpendicular(f) * CityLayout::lane;
            w.newRoute(c);
            check(c.routePhase == 1 && c.routeFrom == n, "dead end enters turnaround");
            check((start - c.route->sample(0).first).length() < 1e-8, "turnaround starts on incoming lane");
            pathClear(w, *c.route, c.routeLevel);
            auto end = c.route->sample(c.route->length());
            w.newRoute(c);
            auto next = c.route->sample(0);
            check((end.first - next.first).length() < 1e-8,
                  "turnaround joins outgoing lane without teleport");
            check(std::abs(wrap(end.second - next.second)) < .1, "turnaround exits facing correct way");
        }
        check(deadEnds > 0, "seed exercises dead ends");
        Car c;
        c.routeFrom = 1200;
        c.routeTo = w.neighbours(1200)[0];
        c.routePhase = 1;
        w.newRoute(c);
        for (int i = 0; i < 1000; ++i)
        {
            pathClear(w, *c.route, c.routeLevel);
            auto end = c.route->sample(c.route->length());
            w.newRoute(c);
            auto begin = c.route->sample(0);
            check((begin.first - end.first).length() < 1e-8, "street and crossing endpoints agree");
            check(std::abs(wrap(begin.second - end.second)) < .13, "street and crossing directions agree");
        }
        for (auto &p : w.pickups)
            check(w.terrain.height(p.p) > CityLayout::waterLevel, "pickup spawns on dry land");
        for (auto &p : w.people)
            check(w.terrain.height(p.body.p) > CityLayout::waterLevel, "pedestrian spawns on dry land");

        // Vertex heights on bridge edge and road paint must agree with collision support.
        double riverX = w.layout.coordinate(16) + 18, bridgeZ = w.layout.coordinate(24);
        auto chunk = cityChunk(w, riverX - 72, bridgeZ - 72);
        int deckVertices = 0;
        for (auto &v : chunk[Road].vertices)
        {
            Vec2 p{v.position.x, v.position.z};
            if (std::abs(p.x - riverX) < 6 && std::abs(p.z - bridgeZ) < 7.4 && v.position.y > 0)
            {
                ++deckVertices;
                check(std::abs(v.position.y - w.terrain.height(p)) < 1e-4,
                      "rendered bridge matches support height");
            }
        }
        check(deckVertices > 0, "mesh contains visible bridge deck");
        for (auto &v : chunk[Pavement].vertices)
        {
            Vec2 p{v.position.x, v.position.z};
            if (std::abs(p.x - riverX) < 6 && std::abs(std::abs(p.z - bridgeZ) - 10.55) < 1e-4)
                check(v.position.y > 3, "deck edge never stretches down to river bed");
        }
        for (size_t i = 0; i < chunk[Paint].vertices.size(); i += 4)
        {
            Vec2 centre{};
            for (int j = 0; j < 4; ++j)
            {
                auto p = chunk[Paint].vertices[i + j].position;
                centre += Vec2{p.x, p.z} * .25;
            }
            check(w.layout.roadDistance(centre) < w.layout.roadHalf(), "no paint outside street");
            check(w.terrain.height(centre) > CityLayout::waterLevel, "no paint on water");
        }
        // Signals have a clearance interval with both axes red.
        for (double t = 0; t < 28; t += .1)
            check(!(CityLayout::green(3, 5, 0, t) && CityLayout::green(3, 5, 1, t)),
                  "cross traffic never both green");

        // Keep one car in physical simulation for four simulated minutes.
        // This catches steering/endpoint failures which path geometry checks miss.
        w.cars.resize(1);
        w.people.clear();
        w.pickups.clear();
        w.controlledCar = -1;
        auto &ai = w.cars[0];
        ai.model = 0;
        ai.body.v = {};
        ai.routeFrom = 1200;
        ai.routeTo = w.neighbours(1200)[0];
        ai.routePhase = 1;
        w.newRoute(ai);
        auto initial = ai.route->sample(0);
        ai.body.p = initial.first;
        ai.body.angle = -initial.second;
        int transitions = 0;
        double maxError = 0, distance = 0;
        for (int step = 0; step < 240 * 120; ++step)
        {
            w.player.p = ai.body.p + Vec2{30, 30};
            int oldNode = ai.routeFrom, oldPhase = ai.routePhase;
            auto before = ai.body.p;
            w.step({}, 1. / 120);
            w.sounds.clear();
            if (ai.routeFrom != oldNode || ai.routePhase != oldPhase)
                ++transitions;
            distance += (ai.body.p - before).length();
            maxError = std::max(maxError,
                                ai.routeLevel
                                    ? std::abs(ai.body.p.z - w.levelById(ai.routeLevel)->centreZ(ai.body.p.x))
                                    : w.layout.roadDistance(ai.body.p));
            check(std::isfinite(ai.body.p.x) && std::isfinite(ai.body.p.z), "finite local traffic");
        }
        std::cout << "Local traffic: transitions=" << transitions << " distance=" << distance
                  << " maxRoadError=" << maxError << '\n';
        check(!ai.destroyed, "autonomous drive survives normal route");
        check(transitions > 30 && distance > 500,
              "local car continues through junctions instead of circling endpoint");
        check(maxError < w.layout.roadHalf(), "local car stays on carriageway");
        std::cout << checks << " traffic/geometry checks passed\n";
    }
    catch (const std::exception &e)
    {
        std::cerr << "Traffic check " << checks << ": " << e.what() << '\n';
        return 1;
    }
}
