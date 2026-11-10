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
        std::cout << "Dispatch, routes, save, mounted gun, flight, wreck and release passed\n";
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
