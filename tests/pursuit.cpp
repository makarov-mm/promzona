#include "promzona/world.hpp"
#include <print>
#include <chrono>
#include <source_location>
#include <format>
#include <exception>
#include <stdexcept>
using namespace Promzona;
namespace
{
void check(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void dispatch(World &w, int level)
{
    w.wanted = level;
    for (int i = 0; i < 32; ++i)
        w.updateDispatch();
    std::array<int, 15> expected{}, present{};
    for (int k : policeRoster(level))
        ++expected[k];
    for (const auto &c : w.cars)
        if (c.responding)
        {
            ++present.at(c.kind);
            check(!c.destroyed, "dispatched wreck");
        }
    check(expected == present, "wrong number or type of deployed units");
}
int unit(World &w, int kind)
{
    for (size_t i = 0; i < w.cars.size(); ++i)
        if (w.cars[i].responding && w.cars[i].kind == kind)
            return int(i);
    throw std::runtime_error("unit missing");
}
void arrestTest()
{
    World w;
    w.generate(20261105);
    w.controlledCar = -1;
    w.wanted = 2;
    w.wantedCooldown = 120;
    w.money = 400;
    w.inventory.give(WeaponId::pistol);
    w.updateDispatch();
    int unitId = unit(w, 3);
    auto &car = w.cars[unitId];
    // Dispatch put the patrol on a real road. Let the suspect wait by its passenger door.
    car.body.v = {};
    auto exit = w.vehicleExit(car);
    check(exit.has_value(), "patrol has no clear door");
    w.player.p = *exit + (*exit - car.body.p).unit() * 2.5;
    w.player.v = {};
    w.player.height = w.terrain.height(w.player.p) + .85;
    w.updatePatrol();
    int officer = w.patrol.officer;
    check(officer >= 0 && w.patrol.stage == World::PatrolStage::approaching, "officer failed to dismount");
    auto &cop = w.people[officer];
    check(!w.canTakePassengerJob(officer), "taxi can recruit arresting officer");
    w.player.p = cop.body.p + (cop.body.p - car.body.p).unit() * 1.1;
    w.player.height = w.terrain.height(w.player.p) + .85;
    auto advance = [&](int steps)
    {
        for (int i = 0; i < steps; ++i)
            w.arrestStep(.02);
    };
    w.aiming = true;
    advance(100);
    check(w.patrol.hold == 0, "armed suspect arrested");
    w.aiming = false;
    w.player.roadLevel = cop.body.roadLevel + 1;
    advance(100);
    check(w.patrol.hold == 0, "arrest through road deck");
    w.player.roadLevel = cop.body.roadLevel;
    w.wanted = 4;
    advance(100);
    check(w.patrol.hold == 0, "arrest during armed response");
    w.wanted = 2;
    Body barrier{};
    barrier.p = (w.player.p + cop.body.p) * .5;
    barrier.halfW = barrier.halfH = .15;
    barrier.verticalExtent = 2;
    barrier.height = cop.body.height;
    barrier.circle = false;
    w.staticBodies.push_back(barrier);
    advance(76);
    check(!w.arrests && w.patrol.hold < .1, "booked through a solid obstacle");
    w.staticBodies.pop_back();
    w.patrol.hold = 0;
    advance(45);
    check(w.patrol.hold > .85 && !w.arrests, "no continuous contact timer");
    w.player.v = {3, 0};
    advance(30);
    check(w.patrol.hold == 0, "moving suspect still in custody");
    w.player.v = {};
    advance(45);
    auto path =
        std::filesystem::temp_directory_path() /
        std::format("promzona-booking-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    w.save(path);
    World resumed;
    try
    {
        resumed.load(path);
    }
    catch (...)
    {
        std::filesystem::remove(path);
        throw;
    }
    std::filesystem::remove(path);
    check(resumed.patrol.officer == officer && resumed.patrol.hold == w.patrol.hold,
          "arrest timer lost on reload");
    for (int i = 0; i < 34; ++i)
        resumed.arrestStep(.02);
    check(resumed.arrests == 1 && resumed.money == 240 && resumed.lastFine == 160 && resumed.wanted == 0 &&
              !resumed.inventory.active() && std::abs(resumed.clock.hour - (w.clock.hour + 5)) < .001 &&
              (resumed.position() - resumed.stationEntrance).length() < 3,
          "wrong station, time or fine after booking");

    w.controlledCar = 0;
    auto &target = w.cars[0];
    target.body.p = w.player.p + Vec2{6, 0};
    target.body.angle = 0;
    target.body.v = {};
    target.body.roadLevel = 0;
    target.body.height = w.terrain.height(target.body.p) + .8;
    auto door = w.passengerDoor(target, cop.body.p);
    w.resumeWalk(cop, door + Vec2{-.4, 0}, 0);
    w.patrol.hold = 0;
    target.recovering = true;
    advance(150);
    check(w.patrol.hold == 0, "arrested a recovering vehicle");
    target.recovering = false;
    w.money = 30;
    advance(120);
    check(!w.arrests, "car booked before door contact time");
    advance(7);
    check(w.arrests == 1 && w.money == 0 && w.lastFine == 30 && w.controlledCar < 0,
          "vehicle booking failed");
}
void vehicleEntryTest()
{
    World w;
    w.generate(20261105);
    w.controlledCar = -1;
    auto candidate = std::ranges::find_if(w.cars,
                                          [&](const Car &c)
                                          {
                                              if (c.kind != 3 || c.body.roadLevel || w.levelAt(c.body.p, 14))
                                                  return false;
                                              w.player.p = World::driver_door(c, -1);
                                              w.player.height = w.surface(w.player.p, 0) + .85;
                                              return w.entry_path(c, w.player.p);
                                          });
    check(candidate != w.cars.end(), "missing accessible patrol car in fixture");
    auto &car = *candidate;
    const int id = int(car.id);
    car.body.v = {5, 0};
    w.enter_exit();
    check(w.vehicleEntry.car != id, "boarded a moving vehicle");
    w.vehicleEntry = {};
    car.body.v = {};
    w.enter_exit();
    check(w.vehicleEntry.car == id && w.controlledCar == -1, "expected approach to patrol door");
    PlayerInput input;
    input.walking = {1, 0};
    w.step_entry(input, .1);
    check(w.vehicleEntry.car < 0 && !car.driverless, "walking failed to cancel entry");
    w.enter_exit();
    input = {};
    w.step_entry(input, .4);
    check(w.controlledCar < 0 && car.doorOpen > .4, "transferred before the door opened");
    input.shooting = true;
    w.step_entry(input, .01);
    check(w.vehicleEntry.car < 0, "shooting failed to cancel entry");

    // Both sides blocked: nobody disembarks and there is no partial crime/transfer.
    const auto originalSize = w.staticBodies.size();
    for (int side : {-1, 1})
    {
        Body wall{};
        wall.circle = false;
        wall.invMass = 0;
        wall.angle = car.body.angle;
        wall.height = car.body.height;
        wall.verticalExtent = 3;
        wall.halfW = 2;
        wall.halfH = 6;
        wall.p = car.body.p + rotate({side * (car.body.halfW + 2.), 0}, car.body.angle);
        w.staticBodies.push_back(wall);
    }
    check(!w.unload_crew(car) && !car.driverless && !w.wanted, "blocked crew partially committed");
    w.staticBodies.resize(originalSize);
    w.enter_exit();
    input = {};
    for (int i = 0; i < 130 && w.controlledCar < 0; ++i)
        w.step_entry(input, .01);
    auto cops = [&] { return std::ranges::count(w.people, Person::Uniform::police, &Person::uniform); };
    check(w.controlledCar == id && cops() == 2 && car.driverless && w.wanted > 0, "patrol crew not ejected");
    w.enter_exit();
    check(w.controlledCar < 0 && car.driverless, "empty vehicle restored AI driver on exit");
    double crime = w.wanted;
    check(w.unload_crew(car) && cops() == 2 && w.wanted == crime, "duplicate crew or crime on re-entry");
    auto path =
        std::filesystem::temp_directory_path() /
        std::format("promzona-carjack-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    w.save(path);
    World resumed;
    try
    {
        resumed.load(path);
    }
    catch (...)
    {
        std::filesystem::remove(path);
        throw;
    }
    std::filesystem::remove(path);
    check(resumed.cars[id].driverless &&
              std::ranges::count(resumed.people, Person::Uniform::police, &Person::uniform) == 2,
          "empty car or expelled crew lost on reload");
}

void test()
{
    World w;
    w.generate(20261105);
    w.command("spawn tank");
    int tank = int(w.cars.size()) - 1;
    auto &armour = w.cars[tank];
    check(armour.kind == 11 && armour.summoned && armour.mass == 32000, "wrong summoned tank");
    w.controlledCar = -1;
    w.player.p = World::driver_door(armour, -1);
    w.player.height = armour.body.height;
    w.enter_exit();
    for (int tick = 0; tick < 150 && w.vehicleEntry.car >= 0; ++tick)
        w.step({}, 1. / 120);
    check(w.controlledCar == tank, "tank cannot be boarded");
    double bearing = armour.heading() + .52;
    w.shoot(bearing);
    check(w.rockets.size() == 1 && w.rockets[0].ownerCar == tank &&
              (w.rockets[0].direction - forward(bearing)).length() < 1e-9,
          "wrong shell heading or owner");
    w.shoot(bearing);
    check(w.rockets.size() == 1, "tank cooldown bypassed");
    w.shotCooldown = 0;
    w.command("spawn helicopter");
    check(w.cars.back().summoned && w.cars.back().kind == 12 && w.cars.back().lift == 0,
          "grounded helicopter missing");
    w.controlledCar = 0;
    for (int level = 1; level <= 6; ++level)
        dispatch(w, level);
    int heli = unit(w, 12), btr = unit(w, 14);
    check(w.cars[heli].lift >= 20, "helicopter did not spawn in flight");
    auto node = w.cars[btr].routeTo;
    for (int i = 0; node != w.pursuitNode && i < 100; ++i)
    {
        int next = w.pursuitNext(node, -1);
        check(w.pursuitDistances[next] < w.pursuitDistances[node], "route does not approach suspect");
        node = next;
    }
    check(node == w.pursuitNode, "police route did not reach suspect");
    auto path =
        std::filesystem::temp_directory_path() /
        std::format("promzona-pursuit-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    w.save(path);
    World loaded;
    try
    {
        loaded.load(path);
    }
    catch (...)
    {
        std::filesystem::remove(path);
        throw;
    }
    std::filesystem::remove(path);
    check(loaded.cars[tank].summoned && loaded.cars[tank].mountedGun(), "tank lost on save");
    check(loaded.cars.size() == w.cars.size() && loaded.cars[heli].responding &&
              loaded.cars[heli].lift == w.cars[heli].lift,
          "patrol lost on save");
    w.controlledCar = btr;
    w.cars[btr].responding = false;
    w.shoot(w.cars[btr].heading());
    check(w.firing && w.inventory.ammo(WeaponId::automatic) == 0, "mounted gun requires personal ammunition");
    dispatch(w, 6);
    check(unit(w, 14) != btr, "stolen unit counted against quota");
    auto &air = w.cars[heli];
    w.controlledCar = heli;
    air.responding = false;
    PlayerInput input;
    input.collective = 1;
    double start = air.body.height;
    for (int i = 0; i < 120; ++i)
        w.flyPatrol(air, input, 1. / 60.);
    check(air.body.height > start + 5, "manual ascent failed");
    input.collective = -1;
    for (int i = 0; i < 1200; ++i)
        w.flyPatrol(air, input, 1. / 60.);
    check(std::abs(air.body.height - .8 - w.flightFloor(air.body.p)) < .05, "manual landing failed");
    air.body.height += 18;
    air.lift += 18;
    air.destroy();
    w.controlledCar = 0;
    for (int i = 0; i < 400; ++i)
        w.supportCar(air, air.body.p, 1. / 60.);
    check(air.wreckSupported && std::abs(air.body.height - .8 - w.flightFloor(air.body.p)) < .05,
          "air wreck hung in mid-air");
    dispatch(w, 3);
    dispatch(w, 0);
    check(w.cars.size() <= w.trafficPopulation + w.maxResponseVehicles, "response population unbounded");
}
// A pursuit unit of the managed game backs out. With the suspect behind it, it reverses
// with the wheels turned and comes round, where it used to drive off down the street.
void reverseTest()
{
    World w;
    w.generate(20261105);
    w.weatherClock = 1000;
    // Four stars: nobody is arrested, every unit makes for the suspect.
    dispatch(w, 4);
    const int index = unit(w, 3);
    auto &car = w.cars[index];
    const Vec2 along = forward(w.cars[0].heading());
    // The suspect on foot in front of his car, the unit twenty-four metres further on with
    // its back to him: beyond the nineteen metres at which a unit stops for a man on foot.
    w.controlledCar = -1;
    w.player.p = w.cars[0].body.p + along * 7;
    w.player.v = {};
    w.player.roadLevel = 0;
    w.player.height = w.terrain.height(w.player.p) + .85;
    car.body.p = w.player.p + along * 24;
    car.body.angle = w.cars[0].body.angle;
    car.body.v = {};
    car.body.w = 0;
    car.body.roadLevel = 0;
    car.body.height = w.terrain.height(car.body.p) + .8;
    car.seesSuspect = true;
    car.nextSightCheck = 0;
    car.wedgedFor = car.reverseFor = 0;
    check(dot(w.player.p - car.body.p, forward(car.heading())) < -23, "the suspect is not behind the unit");
    double slowest = 0, nearest = 1e9, turned = -1;
    for (int tick = 0; tick < 600; ++tick)
    {
        w.step({}, 1. / 60);
        w.wanted = 4;
        const Vec2 nose = forward(car.heading()), toSuspect = w.player.p - car.body.p;
        slowest = std::min(slowest, dot(car.body.v, nose));
        nearest = std::min(nearest, toSuspect.length());
        turned = std::max(turned, dot(toSuspect.unit(), nose));
        if (tick == 90)
            check(slowest < -.5, "a unit with the suspect behind it did not back up");
    }
    // It comes round until the suspect is before the bonnet and stops nineteen metres off.
    check(turned > .5, "the unit did not come round to the suspect");
    check(nearest > 8, "the unit drove at a suspect on foot");
    check(!car.destroyed && w.health > 0, "the manoeuvre wrecked the unit or ran the suspect down");
}
// The other half of the managed rule: a unit that stands against something no driver
// waits for backs off it. A tree stands between the unit and the suspect's car; the unit
// used to push at the trunk for good.
void stuckTest()
{
    World w;
    w.generate(20261105);
    w.weatherClock = 1000;
    dispatch(w, 4);
    auto &car = w.cars[unit(w, 3)];
    // A tree with twelve metres of open lawn to its east and west.
    int chosen = -1;
    for (size_t k = 0; k < w.trees.size() && chosen < 0; ++k)
    {
        const Vec2 at{w.trees[k].foot.x, w.trees[k].foot.z};
        bool open = true;
        for (double s = -12; s <= 12 && open; s += 1)
            for (double side : {-2., 0., 2.})
            {
                const Vec2 q = at + Vec2{s, side};
                open = open &&
                       (std::abs(s) < 2.5 ||
                        (w.layout.urbanSite(q, 1) && !w.insideBuilding(q, w.terrain.height(q) + .8, 1.2) &&
                         !w.levelAt(q, 3) && w.layout.roadDistance(q) > w.layout.roadHalf() + 1));
            }
        for (size_t other = 0; other < w.trees.size() && open; ++other)
        {
            const Vec2 rel = Vec2{w.trees[other].foot.x, w.trees[other].foot.z} - at;
            open = other == k || std::abs(rel.x) >= 14 || std::abs(rel.z) >= 4;
        }
        if (open)
            chosen = int(k);
    }
    check(chosen >= 0, "no tree with open ground around it");
    const Vec2 tree{w.trees[size_t(chosen)].foot.x, w.trees[size_t(chosen)].foot.z};
    // The suspect sits in his car beyond the tree: for a man on foot the unit would stop
    // where it stands.
    auto &own = w.cars[0];
    w.controlledCar = 0;
    own.body.p = tree + Vec2{7, 0};
    own.body.angle = 0;
    own.body.v = {};
    own.body.w = 0;
    own.body.roadLevel = 0;
    own.body.height = w.terrain.height(own.body.p) + .8;
    car.body.p = tree - Vec2{6, 0};
    car.body.angle = -pi / 2; // facing east, at the trunk and the suspect behind it
    car.body.v = {};
    car.body.w = 0;
    car.body.roadLevel = 0;
    car.body.height = w.terrain.height(car.body.p) + .8;
    car.seesSuspect = true;
    car.nextSightCheck = 0;
    car.wedgedFor = car.reverseFor = 0;
    double slowest = 0, nearest = 1e9;
    bool stood = false;
    for (int tick = 0; tick < 900 && w.health == 1; ++tick)
    {
        w.step({}, 1. / 60);
        w.wanted = 4;
        const double speed = dot(car.body.v, forward(car.heading()));
        stood = stood || (tick > 60 && std::abs(speed) < .05 && std::abs(car.body.p.x - tree.x) < 4);
        slowest = std::min(slowest, speed);
        nearest = std::min(nearest, (w.position() - car.body.p).length());
        if (tick == 300)
            check(stood && slowest < -1, "a unit that stood against a tree did not back off it");
    }
    check(nearest < 5.5, "the unit did not get round the tree to the suspect");
}
// A four-way junction of ordinary streets with four more around it, away from ramps.
int plainJunction(const World &w)
{
    auto plain = [&](int n)
    {
        return w.layout.degree(n) == 4 && w.layout.urbanSite(World::nodePoint(n), 20) &&
               !w.levelAt(World::nodePoint(n), 40);
    };
    for (int n = 2 * World::nodes; n < World::nodes * (World::nodes - 2); ++n)
        if (plain(n) && plain(n - 1) && plain(n + 1) && plain(n - World::nodes) && plain(n + World::nodes) &&
            (World::nodePoint(n) - w.cars[0].body.p).length() > 300)
            return n;
    throw std::runtime_error("no plain junction");
}
// The suspect on foot at `at`, his own car out of the way, the streets around `centre` empty.
void stage(World &w, Vec2 at, Vec2 centre)
{
    auto &own = w.cars[0];
    own.body.p = {World::landEdge - 30, 40};
    own.body.height = w.terrain.height(own.body.p) + .8;
    own.driverless = true;
    w.controlledCar = -1;
    w.godMode = true;
    w.player.p = at;
    w.player.v = {};
    w.player.roadLevel = 0;
    w.player.height = w.terrain.height(at) + .85;
    w.patrolScanAt = w.nextRescueDispatch = 1e9;
    for (size_t i = 1; i < w.cars.size(); ++i)
        if (!w.cars[i].responding && (w.cars[i].body.p - centre).length() < 90)
        {
            w.cars[i].destroy();
            w.cars[i].body.p = {World::landEdge - 20, -300. + 8. * double(i % 60)};
        }
    for (auto &p : w.people)
        if ((p.body.p - centre).length() < 60)
            p.body.p += Vec2{0, 140};
    w.indexTraffic();
}
// On the straight that leads from one junction into the next, `back` metres before its end.
Car &onStreet(World &w, Car &car, int from, int to, double back)
{
    car.routeFrom = from;
    car.routeTo = to;
    car.routePhase = 1;
    car.laneIndex = 0;
    car.plannedTo = car.previousNode = -1;
    w.new_route(car);
    car.routeDistance = car.route->length() - back;
    const auto [at, heading] = car.route->sample(car.routeDistance);
    car.body.p = at;
    car.body.angle = -heading;
    car.body.v = {};
    car.body.w = 0;
    car.body.roadLevel = 0;
    car.body.height = w.surface(at, 0) + .8;
    car.lift = car.verticalVelocity = 0;
    car.health = 1;
    car.fireTime = 0;
    car.destroyed = car.driverless = false;
    car.seesSuspect = false;
    car.wedgedFor = car.reverseFor = car.avoid = car.overtake = 0;
    car.trafficLimit = 100;
    car.senseAt = car.nextSightCheck = 0;
    return car;
}
// Port of the managed PolicePursuit and PoliceRuleStep: traffic is a steering cost to a
// crew on a call, never a queue to wait in. Units used to stand behind whatever was in
// front of the bonnet; one that came round a junction with its nose at a car waiting at
// the opposite stop line stood there for the rest of the pursuit, and the junction with it.
void trafficTest()
{
    World w;
    w.generate(20261105);
    w.weatherClock = 1000;
    dispatch(w, 4);
    const int node = plainJunction(w);
    const Vec2 centre = World::nodePoint(node);
    // The suspect fifty metres down the street the unit comes along: near enough for the
    // unit to be driven by its own steering, too far to be seen from the queue.
    const Vec2 suspect = centre + Vec2{48, 9.3};
    stage(w, suspect, centre);
    auto hold = [&](int level)
    {
        w.wanted = level;
        w.dispatchAt = 1e9;
    };
    auto park = [&](size_t slot, int from, int to, double back) -> Car &
    {
        auto &parked = onStreet(w, w.cars[slot], from, to, back);
        parked.destroyed = parked.responding = false;
        parked.driverless = true; // it stands whatever the lights say
        parked.health = 1;
        parked.fireTime = 0;
        return parked;
    };
    hold(4);
    w.updateDispatch(); // the way to the suspect from every junction
    w.dispatchAt = 1e9;
    auto &car = w.cars[size_t(unit(w, 3))];
    // 1. Two cars stand at the stop line with nobody in them; the unit comes up the street
    //    behind them, through the junction before.
    {
        park(1, node - 1, node, 1);
        park(2, node - 1, node, 6.5);
        onStreet(w, car, node - 2, node - 1, 6);
        w.indexTraffic();
        const Vec2 street = (centre - World::nodePoint(node - 1)).unit();
        double passed = -1;
        for (int tick = 0; tick < 60 * 25 && passed < 0; ++tick)
        {
            w.step({}, 1. / 60);
            hold(4);
            check(car.responding && !car.distant, "the unit is not driven by its own steering");
            if (dot(car.body.p - centre, street) > -8)
                passed = tick / 60.;
        }
        check(passed >= 0, "a crew on a call waited behind the cars at the stop line");
        check(!car.destroyed && car.health > .5, "the crew wrecked its car on the queue");
    }
    // 2. In the junction, the bonnet at the van that waits at the opposite stop line: with
    //    the suspect out of sight down the street, and in sight of him.
    for (const double away : {58., 48.})
    {
        stage(w, centre + Vec2{away, 9.3}, centre);
        hold(4);
        w.updateDispatch();
        w.dispatchAt = 1e9;
        auto &van = park(2, node + 1, node, 1);
        van.body.p = centre + Vec2{13.39, -3.1};
        van.body.angle = -std::atan2(-.98, -.2);
        park(1, node - 2, node - 1, 1); // out of the way
        onStreet(w, car, node, node + 1, 13);
        car.body.p = centre + Vec2{10.05, .57};
        car.body.angle = -std::atan2(.52, -.85);
        w.indexTraffic();
        const Vec2 start = car.body.p;
        double left = -1;
        bool seen = false;
        for (int tick = 0; tick < 60 * 20 && left < 0; ++tick)
        {
            w.step({}, 1. / 60);
            hold(4);
            seen = seen || car.seesSuspect;
            if ((car.body.p - start).length() > 12)
                left = tick / 60.;
        }
        check(seen == (away < 50), "the suspect is not where the junction test wants him");
        check(left >= 0, "a crew stood in a junction with its bonnet at a waiting car");
    }
    // 3. Far from the suspect, on the rails of the distant model: past the queue, pulled
    //    out towards the middle of the street.
    {
        stage(w, centre + Vec2{300, 9.3}, centre);
        hold(4);
        w.updateDispatch();
        w.dispatchAt = 1e9;
        w.nextRenewal = 1e9; // cars left out of sight are not taken away during the test
        park(1, node - 1, node, 1);
        park(2, node - 1, node, 6.5);
        onStreet(w, car, node - 2, node - 1, 6);
        w.indexTraffic();
        const Vec2 street = (centre - World::nodePoint(node - 1)).unit();
        double pulledOut = 0, passed = -1;
        for (int tick = 0; tick < 60 * 12 && passed < 0; ++tick)
        {
            w.step({}, 1. / 60);
            hold(4);
            check(car.distant, "the unit was not on the rails of the distant model");
            pulledOut = std::min(pulledOut, car.overtake);
            if (dot(car.body.p - centre, street) > -8)
                passed = tick / 60.;
        }
        check(passed >= 0, "a distant crew waited behind the cars at the stop line");
        check(pulledOut < -2, "a distant crew drove through the queue instead of round it");
    }
    // 4. The stars go while a unit stands on the grass with its back to the street: it
    //    curves into the nearest lane that goes its way and is traffic again there.
    {
        stage(w, suspect, centre);
        w.nextRenewal = 0;
        hold(4);
        Vec2 lawn{};
        bool found = false;
        for (double x = 13; x <= 24 && !found; x += 1)
            for (double z = 13; z <= 24 && !found; z += 1)
            {
                lawn = centre + Vec2{x, z};
                bool open = true;
                for (double dx : {-3., 0., 3.})
                    for (double dz : {-3., 0., 3.})
                        open = open && w.chaseGround(lawn + Vec2{dx, dz});
                found = open;
            }
        check(found, "no open ground beside the junction");
        onStreet(w, car, node - 1, node, 5);
        car.body.p = lawn;
        car.body.angle = -std::atan2(1., 1.); // away from the junction
        car.body.height = w.surface(lawn, 0) + .8;
        car.seesSuspect = true;
        w.indexTraffic();
        w.wanted = 0;
        w.updateDispatch();
        check(!car.responding && !car.seesSuspect, "a unit kept its call after the stars had gone");
        check(car.route && (car.route->sample(0).first - lawn).length() < .01 && car.routePhase == 0,
              "a unit off the street was not given a way back to it");
        const Vec2 line = car.route->sample(car.route->length()).first;
        const Vec2 into = (World::nodePoint(car.routeTo) - World::nodePoint(car.routeFrom)).unit();
        check(w.layout.degree(car.routeTo) > 0 &&
                  std::abs(dot(World::nodePoint(car.routeTo) - line, into) - stopLine) < .01 &&
                  std::abs(dot(line - World::nodePoint(car.routeTo), perpendicular(into)) -
                           CityLayout::laneOffset(0)) < .01,
              "the way back to the street does not end at a stop line");
        w.dispatchAt = 1e9;
        double moved = 0;
        Vec2 last = car.body.p;
        bool onRoad = false;
        for (int tick = 0; tick < 60 * 25; ++tick)
        {
            w.step({}, 1. / 60);
            w.wanted = 0;
            moved += (car.body.p - last).length();
            last = car.body.p;
            onRoad = onRoad || (w.layout.roadDistance(car.body.p) < w.layout.roadHalf() - 1 && moved > 8);
        }
        check(onRoad, "a unit whose call was over stayed off the street");
        check(!car.destroyed && car.health > .9, "the way back to the street wrecked the unit");
    }
    // 5. A unit that stands in its lane keeps the stop line ahead of it.
    {
        onStreet(w, car, node - 1, node, 13);
        car.responding = true;
        car.body.p += perpendicular(forward(car.heading())) * 2.2; // out of line: the route is stale
        w.standDown(car);
        check(car.routeTo == node && car.routeFrom == node - 1 &&
                  dot(car.route->sample(car.route->length()).first - car.body.p, forward(car.heading())) > 8,
              "a unit in its lane turned round for a nearer stop line");
    }
    // 6. Units left in the junction where the suspect stood drive out of it the way they
    //    face. They used to make for the nearest stop line, all of them for the same one,
    //    from inside the box.
    {
        const std::array<Vec2, 4> facing{Vec2{1, 0}, Vec2{0, 1}, Vec2{-1, 0}, Vec2{0, -1}};
        const std::array<int, 4> exit{node + 1, node + World::nodes, node - 1, node - World::nodes};
        for (size_t k = 0; k < facing.size(); ++k)
        {
            onStreet(w, car, node - 1, node, 13);
            car.responding = true;
            car.body.p = centre + facing[k] * 2 + perpendicular(facing[k]) * .6;
            car.body.angle = -std::atan2(facing[k].x, facing[k].z);
            w.standDown(car);
            check(car.routeFrom == node && car.routeTo == exit[k] && car.routePhase == 0,
                  "a unit in the junction did not leave it the way it faced");
            const auto [end, heading] = car.route->sample(car.route->length());
            check(dot(forward(heading), facing[k]) > .99 &&
                      std::abs(dot(end - centre, perpendicular(facing[k])) - CityLayout::laneOffset(0)) < .01,
                  "the way out of the junction does not end in the lane");
        }
    }
    // 7. Two units left nose to nose, each on the other's way back to the street: neither
    //    has a lane yet nor anybody to queue behind. They used to wait for each other.
    {
        stage(w, centre + Vec2{9.3, 60}, centre);
        w.wanted = 0;
        w.dispatchAt = 1e9;
        size_t slot = w.trafficPopulation;
        while (slot < w.cars.size() && (&w.cars[slot] == &car || w.cars[slot].kind != 6))
            ++slot;
        check(slot < w.cars.size(), "no second unit");
        auto &other = w.cars[slot];
        onStreet(w, car, node, node + 1, 13);
        onStreet(w, other, node, node + 1, 13);
        car.body.p = centre + Vec2{12.1, 3.3};
        car.body.angle = -std::atan2(1., -.6); // east and a little north, at the other unit
        other.body.p = centre + Vec2{17.6, .2};
        other.body.angle = -std::atan2(-1., .5); // west and a little south, at the first
        car.responding = other.responding = true;
        w.indexTraffic();
        w.standDown(car);
        w.standDown(other);
        check(car.rejoining && other.rejoining, "the units were not given a way back to the street");
        const size_t patrol = car.id;
        const Vec2 first = car.body.p, second = other.body.p;
        auto checkReturn = [&](World &city)
        {
            for (int tick = 0; tick < 60 * 45; ++tick)
            {
                city.step({}, 1. / 60);
                city.wanted = 0;
            }
            const auto &a = city.cars.at(patrol);
            const auto &b = city.cars.at(slot);
            check((a.body.p - first).length() > 15 && (b.body.p - second).length() > 15,
                  "two units whose call was over stood nose to nose");
            check(!a.rejoining && !b.rejoining && !a.destroyed && !b.destroyed,
                  "a unit whose call was over did not return to traffic");
        };

        World resumed;
        auto checkReload = [&]
        {
            auto path = std::filesystem::temp_directory_path() /
                        std::format("promzona-rejoin-{}.json",
                                    std::chrono::steady_clock::now().time_since_epoch().count());
            w.save(path);
            resumed.load(path);
            std::filesystem::remove(path);
            for (size_t id : {patrol, slot})
            {
                const auto &before = w.cars.at(id);
                const auto &after = resumed.cars.at(id);
                check(after.rejoining == before.rejoining && after.wedgedFor == before.wedgedFor &&
                          after.reverseFor == std::max(0., before.reverseFor),
                      "reload lost the patrol's return manoeuvre");
            }
            checkReturn(resumed);
        };

        checkReload(); // both units have just stood down
        for (int tick = 0; tick < 60 * 15 && car.reverseFor <= 0 && other.reverseFor <= 0; ++tick)
        {
            w.step({}, 1. / 60);
            w.wanted = 0;
        }
        check((car.rejoining && car.reverseFor > 0) || (other.rejoining && other.reverseFor > 0),
              "neither returning unit started backing up");
        checkReload(); // one unit is already reversing
        checkReturn(w);
    }
}
} // namespace
int main()
{
    try
    {
        test();
        reverseTest();
        stuckTest();
        trafficTest();
        arrestTest();
        vehicleEntryTest();
        std::println("Dispatch, routes, save, mounted gun, flight, wreck and release passed");
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
