#include "promzona/city_mesh.hpp"
#include <iostream>
#include <fstream>
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
void checkRecovery(World &w)
{
    const auto &tunnel = *std::ranges::find(w.roadLevels, true, &RoadLevel::tunnel);
    auto &car = w.cars.front();
    w.controlledCar = 0;
    Vec2 origin{tunnel.x+100, tunnel.centreZ(tunnel.x+100)};
    place(w, car, origin, tunnel.id);
    car.body.v = {2,0};
    check(!w.recover(), "a moving vehicle cannot be evacuated");
    car.body.v = {};
    car.health = .43;
    car.fireTime = 1.2;
    car.roll = pi;
    w.money = 57;
    w.wanted = 2.4;
    check(w.recover(), "stationary vehicle rescued from tunnel cutting");
    const Vec2 road = car.body.p;
    check((road-origin).length() < 180 && (road-origin).length() > 1 &&
          car.body.roadLevel == 0 && w.safeRecoveryPose(car,road,car.heading()),
          "rescue lands on a clear, nearby street");
    check(car.health == .43 && car.fireTime == 1.2 && w.money == 57 && w.wanted == 2.4,
          "recovery preserves damage, fire, cash and pursuit");
    check(!w.recover(), "cannot restart lift midway");
    car.settleAttitude(.9,true);
    check(car.recovering && car.lift > 1 && std::abs(car.roll) > .1,
          "overturned car lifts and rotates visibly");
    const auto file = std::filesystem::current_path() / "promzona-recovery-test.json";
    w.save(file);
    World loaded;
    loaded.load(file);
    std::filesystem::remove(file);
    auto &resumed = loaded.cars.front();
    near(resumed.rightingTime,car.rightingTime,1e-8,"load resumes same frame of lift");
    for (int tick=0; tick<200; ++tick)
    {
        car.settleAttitude(1./120,true);
        resumed.settleAttitude(1./120,true);
        near(resumed.roll,car.roll,1e-8,"saved lift completes identically");
    }
    check(!car.recovering && car.grounded() && !loaded.recover(), "cooldown survives load and landing");
    Body obstacle = car.body;
    obstacle.angle += .41;
    w.staticBodies.push_back(obstacle);
    check(!w.safeRecoveryPose(car,road,car.heading()), "overlapping rotated hull blocks return");
    w.staticBodies.pop_back();

    Car struck;
    struck.applyImpactRotation({8,0});
    check(struck.rollRate>0 && struck.pitchRate==0, "side impact rolls chassis");
    struck.applyImpactRotation({0,-8});
    check(struck.pitchRate<0, "rearward impulse pitches chassis");
    struck.roll = pi;
    struck.rollRate = struck.pitchRate = 0;
    for (int tick=0; tick<470; ++tick) struck.settleAttitude(1./120,true);
    check(!struck.recovering,"no instant automatic recovery");
    for (int tick=0; tick<20; ++tick) struck.settleAttitude(1./120,true);
    check(struck.recovering,"stationary overturned player car starts righting after four seconds");
    struck.destroy();
    for (int tick=0; tick<520; ++tick) struck.settleAttitude(1./120,true);
    check(!struck.recovering,"wreck cannot self-right");

    // Block all ordinary door positions; the emergency exit must still be in the tunnel.
    const Vec2 centre{tunnel.x,tunnel.centreZ(tunnel.x)};
    place(w,car,centre,tunnel.id);
    car.body.angle = -pi/2;
    for (double side : {-1.,1.})
    {
        Body barrier = car.body;
        barrier.mass(0);
        barrier.halfW = .3;
        barrier.halfH = car.body.halfH*1.5;
        barrier.p += rotate({side*(car.body.halfW+.9),0},barrier.angle);
        w.staticBodies.push_back(barrier);
    }
    check(!w.vehicleExit(car), "both rows of normal exits are obstructed");
    car.body.v = {1.8,0};
    check(!w.playerVehicleExit(car), "cannot jump over blocked doors at speed");
    car.body.v = {};
    w.enterExit();
    check(w.controlledCar < 0 && w.player.roadLevel == tunnel.id &&
          (w.player.p-centre).length() <= 10.01, "F finds a nearby tunnel-floor exit");
    near(w.player.height,w.surface(w.player.p,tunnel.id)+.85,1e-8,"emergency exit height follows road");
    w.staticBodies.resize(w.staticBodies.size()-2);

    const auto tram = std::ranges::find(w.cars,9,&Car::kind);
    check(tram != w.cars.end() && tram->line >= 0,"tram fixture available");
    w.controlledCar = int(tram - w.cars.begin());
    w.recoveryReadyAt = 0;
    tram->body.v = {};
    const int line = tram->line;
    const Vec2 stop = tram->body.p;
    check(w.recover() && tram->line == line,"tram recovered to its own circuit");
    auto [rail,heading] = w.lines[line].pose(tram->along);
    near((rail-tram->body.p).length(),0,1e-7,"tram is directly on its track");
    check((rail-stop).length()<180 && tram->nextStop==w.lines[line].nextStopAfter(tram->along),
          "tram stop sequence resumes at the recovery point");
}
void generated(World &w)
{
    check(w.roadLevels.size() == 9, "five overpasses and four tunnels generated");
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
        checkRecovery(w);
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
                near(w.player.height - .85, s.floor(w.terrain,w.player.p), .001,
                     "feet meet the sloped tunnel road at the actual exit point");
                check(std::abs(w.player.height-.85-floor) < .3, "exit does not teleport onto district");
                w.enterExit();
                check(w.vehicleEntry.car == 0 && w.controlledCar < 0, "approach starts inside tunnel");
                // This geometry fixture deliberately removes the traffic and pedestrians.
                for (int tick=0; tick<150 && w.vehicleEntry.car>=0; ++tick)
                {
                    PlayerInput in{};
                    w.stepEntry(in, 1./120);
                    w.player.p += in.walking * (2.7/120);
                }
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
        check(loaded.layout.revision()==CityLayout::districtRevision && loaded.roadLevels.size()==9,
              "new saves retain revised city graph");
        World legacy;
        legacy.generate(123,{},0);
        legacy.save(file);
        std::string json;
        {
            std::ifstream input(file);
            json.assign(std::istreambuf_iterator<char>(input), {});
        }
        const std::string field = "\"layoutRevision\":0,";
        const auto offset = json.find(field);
        check(offset != std::string::npos, "revision field present in new save");
        json.erase(offset,field.size());
        std::ofstream(file) << json;
        loaded.load(file);
        std::filesystem::remove(file);
        check(loaded.layout.revision()==0 && loaded.roadLevels.size()==4 &&
              loaded.layout.buildings.size()==legacy.layout.buildings.size(),
              "pre-revision saves reconstruct the original map");
        std::cout << checks << " multilevel checks passed\n";
    }
    catch (const std::exception &e)
    {
        std::cerr << "Level check " << checks << ": " << e.what() << '\n';
        return 1;
    }
}
