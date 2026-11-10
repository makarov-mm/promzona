#include "promzona/world.hpp"
#include "json.hpp"
#include <fstream>
#include <iostream>
#include <chrono>
#include <stdexcept>
using namespace pz;
namespace
{
void check(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
}
void run()
{
    World w;
    w.generate(20261105);
    w.toggleDrone(0);
    check(!w.drone.active, "launch from car");
    w.enterExit();
    w.toggleDrone(0);
    check(w.drone.active && w.drone.targets[0] != w.drone.targets[1], "launch or targets");
    auto pilot = w.player.p;
    PlayerInput flight;
    flight.collective = 1;
    flight.running = true;
    flight.shooting = true;
    const double launch = w.drone.altitude;
    for (int i = 0; i < 240; ++i)
        w.step(flight, 1. / 120);
    check(w.drone.active && w.drone.altitude > launch + 9.9 && (w.player.p - pilot).length() < .2 &&
              !w.firing,
          "remote flight must not move or fire the operator");
    auto &b = w.buildings[w.drone.targets[0]];
    w.refreshDroneObstacles(b.p);
    check(!w.droneClear(b.p, w.terrain.height(b.p) + 2, .35), "collision with building");
    const auto path = std::filesystem::temp_directory_path() /
        ("promzona-drone-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".json");
    int initial = w.money;
    for (int n = 0; n < 3; ++n)
    {
        auto &roof = w.buildings[w.drone.targets[n]];
        w.drone.position = roof.p + Vec2{0, -2.5};
        w.drone.altitude = w.terrain.height(roof.p) + roof.height + 4;
        w.refreshDroneObstacles(w.drone.position);
        PlayerInput scan;
        scan.shooting = true;
        scan.lookPitch = std::atan2(-3.3, 2.5);
        auto miss = scan;
        miss.lookHeading = pi;
        for (int i = 0; i < 40; ++i)
            w.stepDrone(miss, 1. / 120);
        check(w.drone.scanTime == 0, "off-target scan accrued time");
        for (int i = 0; i < 241 && w.drone.scanned == n; ++i)
            w.stepDrone(scan, 1. / 120);
        check(w.drone.scanned == n + 1, "failed roof scan");
        if (n == 0)
        {
            w.save(path);
            World loaded;
            loaded.load(path);
            check(loaded.drone.active && loaded.drone.targets == w.drone.targets && loaded.drone.scanned == 1 &&
                  loaded.drone.battery == w.drone.battery && loaded.drone.pitch == w.drone.pitch &&
                  !loaded.drone.obstacles.empty(), "mid-flight save");
            w = std::move(loaded);
        }
    }
    check(!w.drone.active && w.money == initial + 200 && w.drone.readyAt > w.time, "job reward/cooldown");
    w.toggleDrone(0);
    check(!w.drone.active, "cooldown");
    w.save(path);
    World restored;
    restored.load(path);
    check(!restored.drone.active && restored.drone.scanned == 3 && restored.drone.readyAt == w.drone.readyAt,
          "cooldown lost on load");
    auto data = nlohmann::json::parse(std::ifstream(path));
    data["drone"]["active"] = true;
    data["drone"]["scanned"] = 0;
    data["drone"]["targets"] = {0, 0, 0};
    std::ofstream(path) << data;
    bool rejected = false;
    try { restored.load(path); } catch (const std::runtime_error &) { rejected = true; }
    check(rejected && !restored.drone.active, "accepted invalid targets");
    data.erase("drone");
    std::ofstream(path) << data;
    restored.load(path);
    check(!restored.drone.active && restored.drone.readyAt == 0, "old save without drone");
    std::filesystem::remove(path);
    std::filesystem::remove(path.string() + ".bak");
    w.time = w.drone.readyAt + 1;
    w.toggleDrone(0);
    check(w.drone.active, "relaunch after cooldown");
    w.drone.battery = .001;
    w.stepDrone({}, .01);
    check(!w.drone.active && w.money == initial + 200, "battery depletion");
}
} // namespace
int main()
{
    try
    {
        run();
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
