#include "promzona/world.hpp"
#include <chrono>
#include <print>
#include <source_location>
#include <format>
#include <exception>
#include <stdexcept>
using namespace Promzona;
namespace
{
void check(bool ok, const char *s, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", s, where.line()));
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
        car.next_stop = (stop + 1) % int(line.stops.size());
        car.dwell = 4;
    };
    w.controlledCar = -1;
    int stop = 0;
    for (; stop < int(line.stops.size()); ++stop)
    {
        park(stop);
        w.player.p = line.curb(stop);
        w.player.height = w.terrain.height(w.player.p) + .85;
        if (w.boardableTransit() == int(car.id) && w.platformExit(car, stop))
            break;
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

    auto it = std::ranges::find_if(w.people,
                                   [&](const auto &p)
                                   {
                                       return !p.dead && p.riding < 0 && p.ownedScooter < 0 && !p.courier &&
                                              !p.inCompany &&
                                              w.canTakePassengerJob(int(&p - w.people.data()));
                                   });
    check(it != w.people.end(), "missing civilian for platform test");
    int actor = int(it - w.people.begin());
    for (auto &p : w.people)
    {
        p.waitingLine = -1;
        p.waitingStop = -1;
        p.boardAfter = w.time + 90;
    }
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
    check(walker.riding < 0 && (walker.platformTarget - door).length() < .01,
          "pedestrian must approach an open door before boarding");

    car.dwell = 0;
    w.enter_exit();
    check(w.transitPassenger() && w.transitExitRequested, "exit between stops");
    w.save(save);
    World restored;
    restored.load(save);
    std::filesystem::remove(save);
    check(restored.transitPassenger() && restored.controlledCar == int(car.id) &&
              restored.playerTransitSeat == w.playerTransitSeat && restored.transitExitRequested &&
              restored.people.at(actor).waitingStop == stop &&
              restored.people.at(actor).waitingLine == car.line,
          "lost player seat, stop request or waiting pedestrian after load");

    car.dwell = 3;
    w.updatePlatforms();
    check(w.controlledCar < 0 && !w.transitExitRequested && (w.player.p - line.curb(stop)).length() < 3,
          "missed requested stop");
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
    std::println("City {} lines {}", seed, w.lines.size());
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
        check(std::abs(w.terrain.height(road) - w.terrain.height(pavement)) < 1. &&
                  !w.insideBuilding(pavement, w.terrain.height(pavement) + 1., .6),
              "trolley doors open onto an inaccessible pavement");
    }
    if (seed != 17)
        return;
    auto save =
        std::filesystem::temp_directory_path() /
        std::format("pz-transit-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
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
        c.next_stop = 0;
        for (int frame = 0; frame < 810; ++frame)
        {
            w.time += 1. / 60;
            w.runTransit(c, {}, false, false, 1. / 60);
            if (frame == 100)
                check(c.dwell > 0 && c.next_stop == 1, "did not wait at stop");
        }
        check(c.dwell == 0 && c.next_stop == 1 && c.along > route.stops.front() + 1,
              "stalled after exchanging riders");
    }
    // A trolleybus runs on the wires over its route: three metres from them the pedal does
    // nothing, the brake still does, and back under them it drives again. The kerb is on
    // the other side: the trolleybus is put towards the middle of the street.
    {
        auto &trolley = *std::ranges::find(w.cars, 10, &Car::kind);
        const auto &wires = w.lines[trolley.line];
        const auto [on, heading] = wires.pose(trolley.along);
        const Vec2 ahead = forward(heading);
        auto put = [&](double aside, double pace)
        {
            trolley.body.p = on + perpendicular(ahead) * aside;
            trolley.body.angle = -heading;
            trolley.body.v = ahead * pace;
            trolley.body.w = 0;
            trolley.steering = 0;
            trolley.body.height = w.surface(trolley.body.p, 0) + .8;
            w.notice.clear();
            w.wiresNoticeAt = 0;
        };
        auto drive = [&](double pedal, bool brake, int frames)
        {
            PlayerInput input;
            input.car.throttle = pedal;
            input.car.brake = brake;
            for (int i = 0; i < frames; ++i)
                w.step(input, 1. / 60);
            return dot(trolley.body.v, ahead);
        };
        w.controlledCar = int(trolley.id);
        trolley.driverless = false;
        // The street may slope: what the pedal adds is the difference to a run without it.
        auto pedal = [&](double aside, double pace)
        {
            put(aside, pace);
            const double pressed = drive(1, false, 120);
            const bool told = w.notice == L"Штанги сошли с проводов: тяги нет";
            put(aside, pace);
            return std::pair{pressed - drive(0, false, 120), told};
        };
        check(pedal(0, 0).first > 1.5 && !pedal(0, 0).second, "a trolleybus under its wires has no power");
        check(pedal(-2, 0).first > 1.5, "a trolleybus near its wires has no power");
        const auto away = pedal(-5, 0);
        check(std::abs(away.first) < .01 && away.second,
              "a trolleybus drives away from its wires, or its driver is not told why not");
        // What speed it has carries it on.
        put(-5, 6);
        const double coasting = drive(1, false, 30);
        check(coasting > 3 && coasting < 7, "a trolleybus off its wires stops dead or speeds up");
        put(-5, 6);
        check(drive(1, true, 240) < .3, "a trolleybus off its wires has no brakes");
        w.controlledCar = -1;
        w.notice.clear();
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
// In this city a tram line and a trolleybus line share a street as far as a junction where
// the tram turns right out of the middle lane and the trolleybus left from the kerb.
void crossing()
{
    World w;
    w.generate(99u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    const int node = 39 * World::nodes + 3;
    const Vec2 centre = World::nodePoint(node);
    check(w.lines.size() == 4 && w.lines[2].kind == 9 && w.lines[1].kind == 10,
          "the lines of city 99 changed");
    Car *tram = nullptr, *trolley = nullptr;
    for (auto &c : w.cars)
    {
        if (c.line == 2 && !tram)
            tram = &c;
        else if (c.line == 1 && !trolley)
            trolley = &c;
        else if (&c != &w.cars[0] && (c.body.p - centre).length() < 160)
        {
            // Nobody else is near: the two meet on an empty street.
            if (c.line >= 0)
                c.along = w.lines[c.line].wrap_distance(c.along + 500);
            else
                c.destroy();
            c.body.p = c.line >= 0 ? w.lines[c.line].pose(c.along).first
                                   : Vec2{World::landEdge - 20, -300. + 6. * double(c.id % 90)};
        }
    }
    check(tram && trolley, "city 99 has no tram or no trolleybus");
    auto put = [&](Car &c, Vec2 at)
    {
        const auto &line = w.lines[c.line];
        c.along = line.track.closest_distance(at);
        const auto [p, heading] = line.pose(c.along);
        check((p - at).length() < .4 && forward(heading).z > .99, "a line of city 99 left this street");
        // Past the junction one has turned right and the other left.
        check(forward(line.pose(c.along + (c.kind == 9 ? 24 : 36)).second).x * (c.kind == 9 ? -1 : 1) > .99,
              "the lines of city 99 no longer cross here");
        c.next_stop = line.next_stop_after(c.along);
        c.dwell = c.check_traffic_at = 0;
        c.claimed = -1;
        c.body.p = p;
        c.body.angle = -heading;
        c.body.v = forward(heading);
        c.body.height = w.terrain.height(p) + .8;
        return c.along;
    };
    const double tramStart = put(*tram, centre + Vec2{-1.9, -15.5});
    const double trolleyStart = put(*trolley, centre + Vec2{-5.2, -16.4});
    for (int i = 0; i < 60 * 60; ++i)
        w.step({}, 1. / 60);
    check(w.lines[2].wrap_distance(tram->along - tramStart) > 45 &&
              w.lines[1].wrap_distance(trolley->along - trolleyStart) > 45,
          "a tram and a trolleybus that turn across each other stand at the junction");
}
// A tram turns into a street where somebody stands on the crossing.
void zebra()
{
    World w;
    w.generate(99u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    const int node = 22 * World::nodes + 25;
    const Vec2 centre = World::nodePoint(node);
    check(w.lines.size() == 4 && w.lines[2].kind == 9, "the lines of city 99 changed");
    const auto &line = w.lines[2];
    // The player watches from the pavement: near the player a tram is driven, not moved.
    auto &own = w.cars[0];
    own.body.p = {World::landEdge - 30, 40};
    own.body.height = w.terrain.height(own.body.p) + .8;
    own.driverless = true;
    w.controlledCar = -1;
    w.player.p = centre + Vec2{9.3, 24};
    w.player.height = w.terrain.height(w.player.p) + .85;
    Car *tram = nullptr;
    for (auto &c : w.cars)
    {
        if (c.line == 2 && !tram)
            tram = &c;
        else if (&c != &own && (c.body.p - centre).length() < 160)
        {
            if (c.line >= 0)
                c.along = w.lines[c.line].wrap_distance(c.along + 500);
            else
                c.destroy();
            c.body.p = c.line >= 0 ? w.lines[c.line].pose(c.along).first
                                   : Vec2{World::landEdge - 20, -300. + 6. * double(c.id % 90)};
        }
    }
    check(tram, "city 99 has no tram on its third line");
    const double start = line.track.closest_distance(centre + Vec2{-1.9, -22});
    check((line.pose(start).first - centre - Vec2{-1.9, -22}).length() < .4 &&
              forward(line.pose(start).second).z > .99 && forward(line.pose(start + 34).second).x < -.99,
          "the tram line of city 99 no longer turns here");
    tram->along = start;
    tram->next_stop = line.next_stop_after(start);
    tram->dwell = tram->check_traffic_at = 0;
    tram->claimed = -1;
    tram->body.p = line.pose(start).first;
    tram->body.angle = -line.pose(start).second;
    tram->body.v = {0, 2};
    tram->body.height = w.terrain.height(tram->body.p) + .8;
    // Everybody else is sent away; one walker stands on the crossing, on the rails.
    Person *walker = nullptr;
    for (auto &p : w.people)
        if (p.riding < 0 && (p.body.p - centre).length() < 70)
            p.body.p += Vec2{0, 240};
        else if (!walker && p.riding < 0 && !p.dead && p.role == Person::Role::none && p.ownedScooter < 0)
            walker = &p;
    check(walker, "nobody to stand on the crossing");
    const Vec2 spot = line.pose(start + 24).first;
    check(std::abs(spot.x - centre.x + 9.1) < 2.5,
          "the crossing is not where the tram comes out of the turn");
    walker->body.p = walker->target = spot;
    walker->body.v = {};
    walker->body.height = w.terrain.height(spot) + .85;
    walker->walkWait = 1e9;
    for (int i = 0; i < 60 * 24; ++i)
        w.step({}, 1. / 60);
    check(w.pedestrianImpacts == 0 && !walker->dead && (walker->body.p - spot).length() < .5,
          "a tram that turns into a street runs over whoever stands on its crossing");
    check(tram->body.v.length() < .2 && line.wrap_distance(tram->along - start) > 8 &&
              (walker->body.p - tram->body.p).length() > tram->body.halfH + .5,
          "a tram does not pull up before the walker on the crossing");
    // The walker goes, and so does the tram.
    walker->body.p += Vec2{0, 240};
    walker->target = walker->body.p;
    const double held = tram->along;
    for (int i = 0; i < 60 * 12; ++i)
        w.step({}, 1. / 60);
    check(line.wrap_distance(tram->along - held) > 12, "the tram stays where the walker stood");
}

// A tram comes to the steepest stretch of its line, where another stands.
void hill()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    size_t steepest = 0;
    double drop = 0, at = 0;
    for (size_t l = 0; l < w.lines.size(); ++l)
        if (w.lines[l].kind == 9)
            for (double a = 0; a < w.lines[l].track.length(); a += 1)
            {
                const double d = std::abs(w.terrain.height(w.lines[l].pose(a + 10).first) -
                                          w.terrain.height(w.lines[l].pose(a).first));
                if (d > drop)
                {
                    drop = d;
                    at = a;
                    steepest = l;
                }
            }
    check(drop > 2.5, "the tram lines of the city have no steep street");
    const auto &line = w.lines[steepest];
    const Vec2 slope = line.pose(at).first;
    Car *lead = nullptr, *follower = nullptr;
    for (auto &c : w.cars)
    {
        if (c.line == int(steepest) && !lead)
            lead = &c;
        else if (c.line == int(steepest) && !follower)
            follower = &c;
        else if (&c != &w.cars[0] && (c.body.p - slope).length() < 160)
        {
            if (c.line >= 0)
                c.along = w.lines[c.line].wrap_distance(c.along + 500);
            else
                c.destroy();
            c.body.p = c.line >= 0 ? w.lines[c.line].pose(c.along).first
                                   : Vec2{World::landEdge - 20, -300. + 6. * double(c.id % 90)};
        }
    }
    check(lead && follower, "the line has fewer than two trams");
    auto put = [&](Car &c, double along, double speed)
    {
        c.along = line.wrap_distance(along);
        c.next_stop = line.next_stop_after(c.along);
        c.check_traffic_at = 0;
        c.claimed = -1;
        const auto [p, heading] = line.pose(c.along);
        c.body.p = p;
        c.body.angle = -heading;
        c.body.v = forward(heading) * speed;
        c.body.height = w.terrain.height(p) + .8;
    };
    put(*lead, at + 12, 0);
    lead->dwell = 60;
    put(*follower, at - 26, 6.5);
    follower->dwell = 0;
    check(std::abs(lead->body.height - follower->body.height) > 2.5, "the two trams are on a level");
    for (int i = 0; i < 60 * 15; ++i)
        w.step({}, 1. / 60);
    // Behind it, or before the junction the other stands on: never inside it.
    const double gap = line.wrap_distance(lead->along - follower->along);
    check(follower->body.v.length() < .1 && gap > 2 * lead->body.halfH + .5 &&
              gap < 2 * lead->body.halfH + 16,
          "a tram does not stop behind the one that stands on the hill ahead of it");
}
} // namespace
int main()
{
    try
    {
        for (auto seed : {17u, 1839u, 20261105u})
            test(seed);
        crossing();
        zebra();
        hill();
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
