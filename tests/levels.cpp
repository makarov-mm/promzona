#include "promzona/city_mesh.hpp"
#include <print>
#include <fstream>
#include <queue>
#include <set>
#include <stdexcept>
#include <source_location>
#include <format>
#include <iterator>
using namespace Promzona;
namespace
{
int checks{};
void check(bool v, const char *text, std::source_location where = std::source_location::current())
{
    ++checks;
    if (!v)
        throw std::runtime_error(std::format("{} (line {})", text, where.line()));
}
void near(double a, double b, double tolerance, const char *text,
          std::source_location where = std::source_location::current())
{
    check(std::abs(a - b) < tolerance, text, where);
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
void checkRecovery(World &w)
{
    const auto &tunnel = *std::ranges::find(w.roadLevels, true, &RoadLevel::tunnel);
    auto &car = w.cars.front();
    w.controlledCar = 0;
    Vec2 origin{tunnel.x + 100, tunnel.centre_z(tunnel.x + 100)};
    place(w, car, origin, tunnel.id);
    car.body.v = {2, 0};
    check(!w.recover(), "a moving vehicle cannot be evacuated");
    car.body.v = {};
    car.health = .43;
    car.fireTime = 1.2;
    car.roll = pi;
    w.money = 57;
    w.wanted = 2.4;
    check(w.recover(), "stationary vehicle rescued from tunnel cutting");
    const Vec2 road = car.body.p;
    check((road - origin).length() < 180 && (road - origin).length() > 1 && car.body.roadLevel == 0 &&
              w.safe_recovery_pose(car, road, car.heading()),
          "rescue lands on a clear, nearby street");
    check(car.health == .43 && car.fireTime == 1.2 && w.money == 57 && w.wanted == 2.4,
          "recovery preserves damage, fire, cash and pursuit");
    check(!w.recover(), "cannot restart lift midway");
    car.settle_attitude(.9, true);
    check(car.recovering && car.lift > 1 && std::abs(car.roll) > .1,
          "overturned car lifts and rotates visibly");
    const auto file = std::filesystem::current_path() / "promzona-recovery-test.json";
    w.save(file);
    World loaded;
    loaded.load(file);
    std::filesystem::remove(file);
    auto &resumed = loaded.cars.front();
    near(resumed.righting_time, car.righting_time, 1e-8, "load resumes same frame of lift");
    for (int tick = 0; tick < 200; ++tick)
    {
        car.settle_attitude(1. / 120, true);
        resumed.settle_attitude(1. / 120, true);
        near(resumed.roll, car.roll, 1e-8, "saved lift completes identically");
    }
    check(!car.recovering && car.grounded() && !loaded.recover(), "cooldown survives load and landing");
    Body obstacle = car.body;
    obstacle.angle += .41;
    w.staticBodies.push_back(obstacle);
    check(!w.safe_recovery_pose(car, road, car.heading()), "overlapping rotated hull blocks return");
    w.staticBodies.pop_back();

    Car struck;
    struck.apply_impact_rotation({8, 0});
    check(struck.roll_rate > 0 && struck.pitch_rate == 0, "side impact rolls chassis");
    struck.apply_impact_rotation({0, -8});
    check(struck.pitch_rate < 0, "rearward impulse pitches chassis");
    struck.roll = pi;
    struck.roll_rate = struck.pitch_rate = 0;
    for (int tick = 0; tick < 470; ++tick)
        struck.settle_attitude(1. / 120, true);
    check(!struck.recovering, "no instant automatic recovery");
    for (int tick = 0; tick < 20; ++tick)
        struck.settle_attitude(1. / 120, true);
    check(struck.recovering, "stationary overturned player car starts righting after four seconds");
    struck.destroy();
    for (int tick = 0; tick < 520; ++tick)
        struck.settle_attitude(1. / 120, true);
    check(!struck.recovering, "wreck cannot self-right");

    // Block all ordinary door positions; the emergency exit must still be in the tunnel.
    const Vec2 centre{tunnel.x, tunnel.centre_z(tunnel.x)};
    place(w, car, centre, tunnel.id);
    car.body.angle = -pi / 2;
    for (double side : {-1., 1.})
    {
        Body barrier = car.body;
        barrier.mass(0);
        barrier.halfW = .3;
        barrier.halfH = car.body.halfH * 1.5;
        barrier.p += rotate({side * (car.body.halfW + .9), 0}, barrier.angle);
        w.staticBodies.push_back(barrier);
    }
    check(!w.vehicleExit(car), "both rows of normal exits are obstructed");
    car.body.v = {1.8, 0};
    check(!w.player_vehicle_exit(car), "cannot jump over blocked doors at speed");
    car.body.v = {};
    w.enter_exit();
    check(w.controlledCar < 0 && w.player.roadLevel == tunnel.id && (w.player.p - centre).length() <= 10.01,
          "F finds a nearby tunnel-floor exit");
    near(w.player.height, w.surface(w.player.p, tunnel.id) + .85, 1e-8, "emergency exit height follows road");
    w.staticBodies.resize(w.staticBodies.size() - 2);

    const auto tram = std::ranges::find(w.cars, 9, &Car::kind);
    check(tram != w.cars.end() && tram->line >= 0, "tram fixture available");
    w.controlledCar = int(tram - w.cars.begin());
    w.recoveryReadyAt = 0;
    tram->body.v = {};
    const int line = tram->line;
    const Vec2 stop = tram->body.p;
    check(w.recover() && tram->line == line, "tram recovered to its own circuit");
    auto [rail, heading] = w.lines[line].pose(tram->along);
    near((rail - tram->body.p).length(), 0, 1e-7, "tram is directly on its track");
    check((rail - stop).length() < 180 && tram->next_stop == w.lines[line].next_stop_after(tram->along),
          "tram stop sequence resumes at the recovery point");
}
void generated(World &w)
{
    check(w.roadLevels.size() == (w.layout.variety() ? 14u : 9u),
          "nine road levels (fourteen in the varied city) generated");
    for (const auto &p : w.people)
    {
        // Beach visitors stand off the street grid and only remember the nearest corner.
        check(p.node != p.next || p.role == Person::Role::beach,
              "pedestrian starts on a usable sidewalk route");
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
    bool counted = true;
    for (int n = 0; n < World::nodes * World::nodes; ++n)
        counted = counted && w.degree(n) == int(w.neighbours(n).size());
    check(counted, "the neighbour count agrees with the neighbour list at every node");
    for (auto &s : w.roadLevels)
    {
        check(w.levelEdge(s.left_node, s.right_node) == &s && w.levelEdge(s.right_node, s.left_node) == &s,
              "route graph contains both level directions");
        for (int k = 0; k <= 288; ++k)
        {
            double x = s.x - 144 + k;
            Vec2 p{x, s.centre_z(x)};
            double floor = s.floor(w.terrain, p);
            check(floor > CityLayout::waterLevel + .5, "tunnel/deck above water datum");
            if (s.roof(x))
                check(w.terrain.height(p) - floor >= RoadLevel::clearance + 1.99,
                      "district has solid ground above tunnel roof");
            if (k > 0)
                check(std::abs(floor - s.floor_samples[k - 1]) < .65, "traversable longitudinal slope");
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
                c.body.p = {x, s.centre_z(x) + direction * CityLayout::lane};
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
    c.routeFrom = east ? s.left_node : s.right_node;
    c.routeTo = east ? s.right_node : s.left_node;
    c.routePhase = 1;
    w.new_route(c);
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
        maxError = std::max(maxError, std::abs(c.body.p.z - s.centre_z(c.body.p.x)));
        check(c.body.roadLevel == s.id, "AI remains on its selected support level");
        near(c.body.height - .8, s.floor(w.terrain, c.body.p), .06, "physical AI follows deck profile");
    }
    if (!finished)
        std::println(stderr, "stalled {} east={} at {},{} speed={} route={} maxError={}", s.id, east,
                     c.body.p.x, c.body.p.z, c.body.v.length(), c.routeDistance, maxError);
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
        // The varied city of new games: every level sound, the station and the clinics standing,
        // every street reachable, no two buildings in one another and a skyline of many heights.
        for (auto seed : {20261105u, 42u, 987654u})
        {
            w.generate(seed, {}, CityLayout::varietyRevision);
            generated(w);
            check(std::ranges::count_if(w.buildings, [](const Building &b) { return b.half.x == 9 && b.half.z == 8; }) >= 4,
                  "a clinic or the station was lost to a road level");
            const int n = w.layout.nodes();
            std::vector<bool> seen(size_t(n * n));
            std::queue<int> open;
            open.push(n / 2 + n / 2 * n);
            seen[size_t(open.front())] = true;
            int reached = 1, streets = 0;
            while (!open.empty())
            {
                const int k = open.front();
                open.pop();
                for (int m : w.layout.neighbours(k))
                    if (!seen[size_t(m)])
                    {
                        seen[size_t(m)] = true;
                        ++reached;
                        open.push(m);
                    }
            }
            for (int k = 0; k < n * n; ++k)
                streets += w.layout.degree(k) > 0;
            check(reached == streets, "a street of the varied city cannot be reached");
            for (size_t a = 0; a < w.buildings.size(); ++a)
                for (size_t b = a + 1; b < w.buildings.size(); ++b)
                {
                    const auto &x = w.buildings[a], &y = w.buildings[b];
                    check(std::abs(x.p.x - y.p.x) >= x.half.x + y.half.x - .01 ||
                              std::abs(x.p.z - y.p.z) >= x.half.z + y.half.z - .01,
                          "two buildings of the varied city overlap");
                }
            std::set<int> storeys;
            for (const auto &b : w.buildings)
                if (b.kind == BuildingKind::apartment)
                    storeys.insert(int(b.height / 3));
            check(storeys.size() >= 12 && *storeys.rbegin() >= 16, "the varied skyline is flat");
        }
        w.generate(20261105);
        checkRecovery(w);
        w.generate(20261105);
        w.cars.resize(1);
        w.people.clear();
        w.pickups.clear();
        for (auto &s : w.roadLevels)
        {
            Vec2 p{s.x, s.centre_z(s.x)};
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
                w.enter_exit();
                check(w.controlledCar == -1 && w.player.roadLevel == s.id,
                      "exit vehicle stays inside tunnel");
                near(w.player.height - .85, s.floor(w.terrain, w.player.p), .001,
                     "feet meet the sloped tunnel road at the actual exit point");
                check(std::abs(w.player.height - .85 - floor) < .3, "exit does not teleport onto district");
                w.enter_exit();
                check(w.vehicleEntry.car == 0 && w.controlledCar < 0, "approach starts inside tunnel");
                // This geometry fixture deliberately removes the traffic and pedestrians.
                for (int tick = 0; tick < 150 && w.vehicleEntry.car >= 0; ++tick)
                {
                    PlayerInput in{};
                    w.step_entry(in, 1. / 120);
                    w.player.p += in.walking * (2.7 / 120);
                }
                check(w.controlledCar == 0, "vehicle in same tunnel can be entered");
                w.controlledCar = -1;
                w.player.p = p;
                w.player.roadLevel = 0;
                w.player.height = ground + .85;
                w.enter_exit();
                check(w.controlledCar == -1, "cannot enter vehicle through tunnel roof");
                auto mesh = city_chunk(w, s.x - 144, s.z - 12, 144);
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
        place(w, w.cars[0], {t.x, t.centre_z(t.x)}, t.id);
        w.controlledCar = 0;
        // Preserve a valid route while saving an actor on a different part of that road.
        auto &car = w.cars[0];
        car.routeFrom = t.left_node;
        car.routeTo = t.right_node;
        car.routePhase = 1;
        w.new_route(car);
        auto file = std::filesystem::current_path() / "promzona-native-level-test.json";
        w.save(file);
        World loaded;
        loaded.load(file);
        std::filesystem::remove(file);
        check(loaded.cars[0].body.roadLevel == t.id, "save/load retains support level");
        near(loaded.ground(), w.ground(), 1e-8, "save/load retains absolute altitude");
        check(loaded.layout.revision() == CityLayout::districtRevision && loaded.roadLevels.size() == 9,
              "new saves retain revised city graph");
        World legacy;
        legacy.generate(123, {}, 0);
        legacy.save(file);
        std::string json;
        {
            std::ifstream input(file);
            json.assign(std::istreambuf_iterator<char>(input), {});
        }
        const std::string field = "\"layoutRevision\":0,";
        const auto offset = json.find(field);
        check(offset != std::string::npos, "revision field present in new save");
        json.erase(offset, field.size());
        std::ofstream(file) << json;
        loaded.load(file);
        std::filesystem::remove(file);
        check(loaded.layout.revision() == 0 && loaded.roadLevels.size() == 4 &&
                  loaded.layout.buildings.size() == legacy.layout.buildings.size(),
              "pre-revision saves reconstruct the original map");
        std::println("{} multilevel checks passed", checks);
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "Level check {}: {}", checks, e.what());
        return 1;
    }
}
