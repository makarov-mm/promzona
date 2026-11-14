#include "promzona/world.hpp"
#include "json.hpp"
#include <fstream>
#include <print>
#include <chrono>
#include <stdexcept>
#include <source_location>
#include <format>
using namespace Promzona;
namespace
{
void check(bool condition, const char *message, std::source_location where = std::source_location::current())
{
    if (!condition)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void run()
{
    World w;
    w.generate(20261105);
    w.toggle_drone(0);
    check(!w.drone.active, "launch from car");
    w.enter_exit();
    w.toggle_drone(0);
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
    const auto wallStart = w.drone.position;
    const double wallHeight = w.drone.altitude;
    Body wall;
    wall.p = wallStart + Vec2{1., 0};
    wall.height = wallHeight;
    wall.verticalExtent = 20;
    wall.halfW = .5;
    wall.halfH = 5;
    w.staticBodies.push_back(wall);
    w.refresh_drone_obstacles(wallStart);
    w.drone.velocity = {8, 0};
    PlayerInput alongWall;
    alongWall.walking = {1, 0};
    alongWall.collective = 1;
    for (int i = 0; i < 120; ++i)
        w.step_drone(alongWall, 1. / 120);
    check(w.drone.position.x < wallStart.x + .16 && w.drone.altitude > wallHeight + 4.9,
          "drone must climb along a wall without crossing it");
    check(w.drone.health < 1 && w.drone.health > .8, "only the initial hard contact should damage the drone");
    w.staticBodies.pop_back();
    w.drone.velocity = {};
    auto &b = w.buildings[w.drone.targets[0]];
    w.refresh_drone_obstacles(b.p);
    check(!w.droneClear(b.p, w.terrain.height(b.p) + 2, .35), "collision with building");
    const auto path =
        std::filesystem::temp_directory_path() /
        std::format("promzona-drone-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    int initial = w.money;
    for (int n = 0; n < 3; ++n)
    {
        auto &roof = w.buildings[w.drone.targets[n]];
        w.drone.position = roof.p + Vec2{0, -2.5};
        w.drone.altitude = w.terrain.height(roof.p) + roof.height + 4;
        w.refresh_drone_obstacles(w.drone.position);
        PlayerInput scan;
        scan.shooting = true;
        scan.lookPitch = std::atan2(-3.3, 2.5);
        auto miss = scan;
        miss.lookHeading = pi;
        for (int i = 0; i < 40; ++i)
            w.step_drone(miss, 1. / 120);
        check(w.drone.scanTime == 0, "off-target scan accrued time");
        for (int i = 0; i < 241 && w.drone.scanned == n; ++i)
            w.step_drone(scan, 1. / 120);
        check(w.drone.scanned == n + 1, "failed roof scan");
        if (n == 0)
        {
            w.save(path);
            World loaded;
            loaded.load(path);
            check(loaded.drone.active && loaded.drone.targets == w.drone.targets &&
                      loaded.drone.scanned == 1 && loaded.drone.battery == w.drone.battery &&
                      loaded.drone.pitch == w.drone.pitch && !loaded.drone.obstacles.empty(),
                  "mid-flight save");
            w = std::move(loaded);
        }
    }
    check(!w.drone.active && w.money == initial + 200 && w.drone.readyAt > w.time, "job reward/cooldown");
    w.toggle_drone(0);
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
    try
    {
        restored.load(path);
    }
    catch (const std::runtime_error &)
    {
        rejected = true;
    }
    check(rejected && !restored.drone.active, "accepted invalid targets");
    data.erase("drone");
    std::ofstream(path) << data;
    restored.load(path);
    check(!restored.drone.active && restored.drone.readyAt == 0, "old save without drone");
    std::filesystem::remove(path);
    std::filesystem::remove(path.string() + ".bak");
    w.time = w.drone.readyAt + 1;
    w.toggle_drone(0);
    check(w.drone.active, "relaunch after cooldown");
    w.drone.battery = .001;
    w.step_drone({}, .01);
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
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
