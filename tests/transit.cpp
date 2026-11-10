#include "promzona/world.hpp"
#include <chrono>
#include <iostream>
using namespace pz;
namespace
{
void check(bool ok, const char *s)
{
    if (!ok)
        throw std::runtime_error(s);
}
void test(std::uint32_t seed)
{
    World w;
    w.generate(seed);
    std::cout << "City " << seed << " lines " << w.lines.size() << '\n';
    check(w.lines.size() == 4, "missing municipal circuit");
    for (const auto &line : w.lines)
    {
        check(line.track.length() > 1600 && line.stops.size() > 6, "short service line");
        for (const auto p : line.track.points())
            check(w.layout.roadDistance(p) <= w.layout.roadHalf() &&
                      w.terrain.height(p) > CityLayout::waterLevel,
                  "track left the street");
        check((line.track.points().front() - line.track.points().back()).length() < .01, "track loop open");
    }
    auto &tram = *std::ranges::find(w.cars, 9, &Car::kind);
    auto &line = w.lines[tram.line];
    int riders = 0;
    for (const auto &p : w.people)
        if (p.riding == int(tram.id))
            ++riders;
    check(riders == 2, "tram has no passengers");
    if (seed != 17)
        return;
    auto save = std::filesystem::temp_directory_path() /
                ("pz-transit-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) +
                 ".json");
    w.save(save);
    World loaded;
    try
    {
        loaded.load(save);
    }
    catch (...)
    {
        std::filesystem::remove(save);
        throw;
    }
    std::filesystem::remove(save);
    for (size_t i = 0; i < w.people.size(); ++i)
        check(loaded.people[i].riding == w.people[i].riding && loaded.people[i].seat == w.people[i].seat &&
                  loaded.people[i].alight == w.people[i].alight,
              "rider identity lost across save");
    for (int kind : {9, 10})
    {
        auto &c = *std::ranges::find(w.cars, kind, &Car::kind);
        const auto &route = w.lines[c.line];
        c.along = route.stops.front();
        c.body.p = route.pose(c.along).first;
        c.body.v = {};
        c.nextStop = 0;
        for (int frame = 0; frame < 810; ++frame)
        {
            w.time += 1. / 60;
            w.runTransit(c, {}, false, false, 1. / 60);
            if (frame == 100)
                check(c.dwell > 0 && c.nextStop == 1, "did not wait at stop");
        }
        check(c.dwell == 0 && c.nextStop == 1 && c.along > route.stops.front() + 1,
              "stalled after exchanging riders");
    }
    w.controlledCar = int(tram.id);
    tram.body.v = {};
    auto start = tram.along;
    for (int i = 0; i < 90; ++i)
    {
        PlayerInput input;
        input.car.throttle = -1;
        input.car.steering = 1;
        w.step(input, 1. / 60);
    }
    check(std::remainder(tram.along - start, line.track.length()) < -.3, "tram cannot reverse");
    check((tram.body.p - line.pose(tram.along).first).length() < .12, "player steered tram off track");
}
} // namespace
int main()
{
    try
    {
        for (auto seed : {17u, 1839u, 20261105u})
            test(seed);
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
