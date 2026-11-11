#include "promzona/world.hpp"
#include <iostream>
#include <chrono>
using namespace pz;
namespace
{
void check(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
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
    auto advance = [&](int steps) { for (int i = 0; i < steps; ++i) w.arrestStep(.02); };
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
    w.player.v = {3,0};
    advance(30);
    check(w.patrol.hold == 0, "moving suspect still in custody");
    w.player.v = {};
    advance(45);
    auto path = std::filesystem::temp_directory_path() /
        ("promzona-booking-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".json");
    w.save(path);
    World resumed;
    try { resumed.load(path); }
    catch (...) { std::filesystem::remove(path); throw; }
    std::filesystem::remove(path);
    check(resumed.patrol.officer == officer && resumed.patrol.hold == w.patrol.hold, "arrest timer lost on reload");
    for (int i = 0; i < 34; ++i) resumed.arrestStep(.02);
    check(resumed.arrests == 1 && resumed.money == 240 && resumed.lastFine == 160 &&
          resumed.wanted == 0 && !resumed.inventory.active() &&
          std::abs(resumed.clock.hour - (w.clock.hour + 5)) < .001 &&
          (resumed.position() - resumed.stationEntrance).length() < 3, "wrong station, time or fine after booking");

    w.controlledCar = 0;
    auto &target = w.cars[0];
    target.body.p = w.player.p + Vec2{6,0};
    target.body.angle = 0;
    target.body.v = {};
    target.body.roadLevel = 0;
    target.body.height = w.terrain.height(target.body.p) + .8;
    auto door = w.passengerDoor(target, cop.body.p);
    w.resumeWalk(cop, door + Vec2{-.4,0}, 0);
    w.patrol.hold = 0;
    target.recovering = true;
    advance(150);
    check(w.patrol.hold == 0, "arrested a recovering vehicle");
    target.recovering = false;
    w.money = 30;
    advance(120);
    check(!w.arrests, "car booked before door contact time");
    advance(7);
    check(w.arrests == 1 && w.money == 0 && w.lastFine == 30 && w.controlledCar < 0, "vehicle booking failed");
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
    w.player.p = armour.body.p + Vec2{2, 0};
    w.player.height = armour.body.height;
    w.enterExit();
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
    auto path = std::filesystem::temp_directory_path() /
                ("promzona-pursuit-" +
                 std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".json");
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
} // namespace
int main()
{
    try
    {
        test();
        arrestTest();
        std::cout << "Dispatch, routes, save, mounted gun, flight, wreck and release passed\n";
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
