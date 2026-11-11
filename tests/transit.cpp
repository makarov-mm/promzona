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
void passengerJourney(World &w, Car &car, const std::filesystem::path &save)
{
    const auto &line = w.lines.at(car.line);
    auto park = [&](int stop)
    {
        car.along = line.stops.at(stop);
        const auto [pos, heading] = line.pose(car.along);
        car.body.p = pos;
        car.body.angle = -heading;
        car.body.v = {};
        car.body.height = w.terrain.height(pos) + .8;
        car.nextStop = (stop + 1) % int(line.stops.size());
        car.dwell = 4;
    };
    w.controlledCar = -1;
    int stop = 0;
    for (; stop < int(line.stops.size()); ++stop)
    {
        park(stop);
        w.player.p = line.curb(stop);
        w.player.height = w.terrain.height(w.player.p) + .85;
        if (w.boardableTransit() == int(car.id) && w.platformExit(car, stop)) break;
    }
    check(stop < int(line.stops.size()), "no reachable stop in city");
    double height = w.player.height;
    w.player.height += 6;
    check(!w.rideTransit(), "boarded through a different road level");
    w.player.height = height;
    car.body.v = {4, 0};
    check(!w.rideTransit(), "boarded a moving service");
    car.body.v = {};
    check(w.rideTransit() && w.transitPassenger(), "failed to board as passenger");
    for (const auto &p : w.people)
        check(p.riding != int(car.id) || p.seat != w.playerTransitSeat, "occupied NPC seat");

    auto it = std::ranges::find_if(w.people, [&](const auto &p) {
        return !p.dead && p.riding < 0 && p.ownedScooter < 0 && !p.courier && !p.inCompany &&
               w.canTakePassengerJob(int(&p - w.people.data()));
    });
    check(it != w.people.end(), "missing civilian for platform test");
    int actor = int(it - w.people.begin());
    for (auto &p : w.people) { p.waitingLine = -1; p.waitingStop = -1; p.boardAfter = w.time + 90; }
    auto &walker = *it;
    walker.boardAfter = w.time;
    walker.body.p = line.curb(stop);
    walker.body.height = w.terrain.height(walker.body.p) + .85;
    w.indexTraffic();
    w.updatePlatforms();
    check(walker.waitingLine == car.line && walker.waitingStop == stop && walker.riding < 0,
          "pedestrian did not queue at the curb");
    const auto door = w.passengerDoor(car, walker.body.p);
    w.updatePlatforms();
    check(walker.riding < 0 && (walker.platformTarget-door).length() < .01,
          "pedestrian must approach an open door before boarding");

    car.dwell = 0;
    w.enterExit();
    check(w.transitPassenger() && w.transitExitRequested, "exit between stops");
    w.save(save);
    World restored;
    restored.load(save);
    std::filesystem::remove(save);
    check(restored.transitPassenger() && restored.controlledCar == int(car.id) &&
          restored.playerTransitSeat == w.playerTransitSeat && restored.transitExitRequested &&
          restored.people.at(actor).waitingStop == stop && restored.people.at(actor).waitingLine == car.line,
          "lost player seat, stop request or waiting pedestrian after load");

    car.dwell = 3;
    w.updatePlatforms();
    check(w.controlledCar < 0 && !w.transitExitRequested &&
          (w.player.p-line.curb(stop)).length() < 3, "missed requested stop");
    car.dwell = 6;
    for (int frame = 0; frame < 350 && walker.riding < 0; ++frame)
        w.step({}, 1. / 60);
    check(walker.riding == int(car.id) && walker.waitingLine < 0 && walker.alight != stop,
          "NPC failed to board or choose a destination");
    park(walker.alight);
    w.exchangePassengers(car, walker.alight);
    check(walker.riding < 0 && walker.boardAfter > w.time, "NPC did not resume walking");
    park(stop);
    w.player.p = line.curb(stop);
    w.player.height = w.terrain.height(w.player.p) + .85;
    check(w.rideTransit(), "could not reboard");
    car.fireTime = 1.;
    w.updatePlatforms();
    check(w.controlledCar < 0, "stayed inside stopped burning transit");
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
        check(std::ranges::is_sorted(line.stops), "platform siting changed timetable order");
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
    if (seed == 20261105u)
    {
        // This trolley stop previously left the player in a foundation cut below the roadway.
        const auto &service = *std::ranges::find(w.cars, 10, &Car::kind);
        const auto &route = w.lines.at(service.line);
        auto road = route.pose(route.stops.at(3)).first;
        auto pavement = route.curb(3);
        check(std::abs(w.terrain.height(road)-w.terrain.height(pavement)) < 1. &&
              !w.insideBuilding(pavement,w.terrain.height(pavement)+1.,.6),
              "trolley doors open onto an inaccessible pavement");
    }
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
    passengerJourney(w, tram, save);
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
