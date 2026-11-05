#include "promzona/city_mesh.hpp"
#include <iostream>
#include <queue>
#include <stdexcept>
using namespace pz;
namespace
{
int checks{};
void check(bool v, const char *text)
{
    ++checks;
    if (!v)
        throw std::runtime_error(text);
}
void near(double a, double b, double tolerance, const char *text)
{
    check(std::abs(a - b) < tolerance, text);
}
void place(World &w, Car &c, Vec2 p, int level)
{
    c.body.p = p;
    c.body.roadLevel = level;
    c.body.height = w.surface(p, level) + .8;
    c.lift = c.verticalVelocity = 0;
    c.body.v = {};
    c.body.w = 0;
    c.body.wake();
}
void generated(World &w)
{
    check(w.roadLevels.size() == 4, "two overpasses and two tunnels generated");
    for (const auto &p : w.people)
    {
        check(p.node != p.next, "pedestrian starts on a usable sidewalk route");
        check(!w.openCut(p.body.p, .3), "pedestrian does not spawn above an open cutting");
    }
    std::vector<bool> seen(World::nodes * World::nodes);
    std::queue<int> q;
    q.push(0);
    seen[0] = true;
    while (!q.empty())
    {
        auto n = q.front();
        q.pop();
        for (auto next : w.neighbours(n))
            if (!seen[next])
            {
                seen[next] = true;
                q.push(next);
            }
    }
    check(std::ranges::all_of(seen, [](bool b) { return b; }), "whole city road graph remains connected");
    for (auto &s : w.roadLevels)
    {
        check(w.levelEdge(s.leftNode, s.rightNode) == &s && w.levelEdge(s.rightNode, s.leftNode) == &s,
              "route graph contains both level directions");
        for (int k = 0; k <= 288; ++k)
        {
            double x = s.x - 144 + k;
            Vec2 p{x, s.centreZ(x)};
            double floor = s.floor(w.terrain, p);
            check(floor > CityLayout::waterLevel + .5, "tunnel/deck above water datum");
            if (s.roof(x))
                check(w.terrain.height(p) - floor >= RoadLevel::clearance + 1.99,
                      "district has solid ground above tunnel roof");
            if (k > 0)
                check(std::abs(floor - s.floorSamples[k - 1]) < .65, "traversable longitudinal slope");
        }
        for (auto &b : w.buildings)
            check(!s.reserved(b.p, b.half.length()),
                  "buildings do not intersect open ramps or overpass corridor");
        for (int east : {0, 1})
        {
            auto route = s.route(east != 0);
            for (double d = 0; d < route.length(); d += .5)
            {
                auto p = route.sample(d).first;
                check(s.contains(p, -.8), "car route has clearance to retaining wall");
            }
            Car c;
            double direction = east ? 1. : -1.;
            Vec2 start{s.x - direction * 145, s.z + direction * CityLayout::lane};
            place(w, c, start, 0);
            for (int n = 1; n <= 2900; ++n)
            {
                auto before = c.body.p;
                double x = start.x + direction * n * .1;
                c.body.p = {x, s.centreZ(x) + direction * CityLayout::lane};
                w.supportCar(c, before, .0125);
                double floor =
                    s.contains(c.body.p) ? s.floor(w.terrain, c.body.p) : w.terrain.height(c.body.p);
                near(c.body.height - .8, floor, .01,
                     "ground-to-level transition follows the road without teleporting");
            }
            check(c.body.roadLevel == 0, "leaving level restores ordinary ground support");
        }
    }
}
void localDrive(World &w, const RoadLevel &s, bool east)
{
    auto &c = w.cars[0];
    c = Car{};
    c.id = 0;
    c.body.id = 2;
    c.body.mass(c.mass);
    c.routeFrom = east ? s.leftNode : s.rightNode;
    c.routeTo = east ? s.rightNode : s.leftNode;
    c.routePhase = 1;
    w.newRoute(c);
    auto first = c.route->sample(0);
    place(w, c, first.first, s.id);
    c.body.angle = -first.second;
    w.controlledCar = -1;
    w.health = 1;
    w.wanted = 0;
    w.physics.clear();
    double maxError = 0;
    bool finished = false;
    for (int n = 0; n < 90 * 120; ++n)
    {
        w.player.p = c.body.p + Vec2{0, 30};
        w.player.roadLevel = 0;
        w.player.height = w.terrain.height(w.player.p) + .85;
        w.step({}, 1. / 120);
        w.sounds.clear();
        check(!c.destroyed, "AI survives level traversal");
        if (c.routePhase != 2)
        {
            finished = true;
            break;
        }
        maxError = std::max(maxError, std::abs(c.body.p.z - s.centreZ(c.body.p.x)));
        check(c.body.roadLevel == s.id, "AI remains on its selected support level");
        near(c.body.height - .8, s.floor(w.terrain, c.body.p), .06, "physical AI follows deck profile");
    }
    if (!finished)
        std::cerr << "stalled " << s.id << " east=" << east << " at " << c.body.p.x << "," << c.body.p.z
                  << " speed=" << c.body.v.length() << " route=" << c.routeDistance
                  << " maxError=" << maxError << '\n';
    check(finished, "AI exits level and continues into junction");
    check(maxError < 3.4, "AI stays in its lane with wall clearance");
}
} // namespace
int main()
{
    try
    {
        World w;
        for (auto seed : {20261105u, 42u, 987654u})
        {
            w.generate(seed);
            generated(w);
        }
        w.generate(20261105);
        w.cars.resize(1);
        w.people.clear();
        w.pickups.clear();
        for (auto &s : w.roadLevels)
        {
            Vec2 p{s.x, s.centreZ(s.x)};
            double ground = w.terrain.height(p), floor = s.floor(w.terrain, p);
            if (!s.tunnel)
            {
                check(w.selectLevel(p, 0, ground) == 0, "underpass car never snaps onto overhead deck");
                check(w.selectLevel(p, s.id, floor) == s.id, "deck car keeps elevated support");
                check(!w.scenerySolid(p, ground + 1.2), "underpass has real clearance");
                check(w.scenerySolid(p, floor - .2), "bridge deck blocks projectiles/camera");
                Car falling;
                place(w, falling, p, s.id);
                falling.destroyed = true;
                auto old = falling.body.p;
                falling.body.p.z += 8;
                w.supportCar(falling, old, 1. / 120);
                near(falling.body.height - .8, floor, .01, "leaving deck preserves world altitude");
                for (int n = 0; n < 360; ++n)
                    w.supportCar(falling, falling.body.p, 1. / 120);
                near(falling.body.height - .8, w.terrain.height(falling.body.p), .001,
                     "vehicle falls to ground after leaving deck");
            }
            else
            {
                check(w.selectLevel(p, 0, ground) == 0, "above-tunnel actor keeps ground level");
                check(w.selectLevel(p, s.id, floor) == s.id, "inside-tunnel actor keeps road level");
                check(!w.scenerySolid(p, floor + 1.3), "tunnel is hollow for bullets/camera");
                check(w.scenerySolid(p, floor + RoadLevel::clearance + .2),
                      "tunnel ceiling blocks upward rays");
                auto &car = w.cars[0];
                place(w, car, p, s.id);
                w.controlledCar = 0;
                w.enterExit();
                check(w.controlledCar == -1 && w.player.roadLevel == s.id,
                      "exit vehicle stays inside tunnel");
                near(w.player.height - .85, floor, .01, "exit does not teleport onto district");
                w.enterExit();
                check(w.controlledCar == 0, "vehicle in same tunnel can be entered");
                w.controlledCar = -1;
                w.player.p = p;
                w.player.roadLevel = 0;
                w.player.height = ground + .85;
                w.enterExit();
                check(w.controlledCar == -1, "cannot enter vehicle through tunnel roof");
                auto mesh = cityChunk(w, s.x - 144, s.z - 12, 144);
                int roadVertices = 0;
                for (auto &v : mesh[Road].vertices)
                {
                    Vec2 point{v.position.x, v.position.z};
                    if (s.contains(point, -.001) && s.open(point))
                    {
                        near(v.position.y, s.floor(w.terrain, point), .002,
                             "open cutting has no phantom road above its floor");
                        ++roadVertices;
                    }
                }
                check(roadVertices > 100, "open cutting has visible road geometry");
                for (auto &v : mesh[Grass].vertices)
                {
                    Vec2 point{v.position.x, v.position.z};
                    check(!s.open(point, -.002), "no grass cap across tunnel cutting");
                }
            }
            localDrive(w, s, true);
            localDrive(w, s, false);
        }
        w.generate(20261105);
        const auto &t = w.roadLevels[1];
        place(w, w.cars[0], {t.x, t.centreZ(t.x)}, t.id);
        w.controlledCar = 0;
        // Preserve a valid route while saving an actor on a different part of that road.
        auto &car = w.cars[0];
        car.routeFrom = t.leftNode;
        car.routeTo = t.rightNode;
        car.routePhase = 1;
        w.newRoute(car);
        auto file = std::filesystem::current_path() / "promzona-native-level-test.json";
        w.save(file);
        World loaded;
        loaded.load(file);
        std::filesystem::remove(file);
        check(loaded.cars[0].body.roadLevel == t.id, "save/load retains support level");
        near(loaded.ground(), w.ground(), 1e-8, "save/load retains absolute altitude");
        std::cout << checks << " multilevel checks passed\n";
    }
    catch (const std::exception &e)
    {
        std::cerr << "Level check " << checks << ": " << e.what() << '\n';
        return 1;
    }
}
