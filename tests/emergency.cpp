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
void check(bool condition, const char *message, std::source_location where = std::source_location::current())
{
    if (!condition)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void drop(World &w, int who, Vec2 at)
{
    auto &p = w.people.at(who);
    w.resumeWalk(p, at, 0);
    w.killPerson(p);
}
} // namespace
int main()
{
    auto path = std::filesystem::temp_directory_path() /
                std::format("pz-rescue-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    try
    {
        World w;
        w.generate(20261105);
        auto it = std::ranges::find_if(w.cars,
                                       [&](auto &c)
                                       {
                                           return c.kind == 4 && !c.body.roadLevel &&
                                                  (c.body.p - w.position()).length() > 180 &&
                                                  !w.levelAt(c.body.p, 15);
                                       });
        check(it != w.cars.end(), "no available street ambulance");
        auto initial = it->body.p;
        int who = 15;
        auto endpoint = World::nodePoint(it->routeTo);
        Vec2 at = endpoint + Vec2{7, 4};
        drop(w, who, at);
        w.update_rescues();
        check(w.rescues[0].car < 0, "ambulance dispatched instantly");
        w.time = 8;
        w.update_rescues();
        auto &r = w.rescues[0];
        check(r.stage == World::RescueStage::driving && r.patient == who && w.people[r.medic].riding == r.car,
              "missing crew or wrong patient");
        w.save(path);
        World copy;
        copy.load(path);
        check(copy.rescues[0].patient == who && !copy.rescues[0].distances.empty() &&
                  copy.people[r.medic].riding == r.car,
              "ambulance lost its crew or route on load");
        auto &live = copy.rescues[0];
        bool walked = false, seenCare = false;
        for (int tick = 0; tick < 7200; ++tick)
        {
            copy.step({}, 1. / 60);
            if (live.stage == World::RescueStage::care)
            {
                walked |= copy.people[live.medic].riding < 0;
                if (!seenCare)
                {
                    copy.save(path);
                    copy.load(path);
                    seenCare = true;
                }
            }
            if (!copy.people[who].dead && live.stage == World::RescueStage::standby)
                break;
        }
        check(seenCare && walked && !copy.people[who].dead && copy.people[who].health > 0 && live.car >= 0 &&
                  live.stage == World::RescueStage::standby && copy.people[live.medic].riding == live.car,
              "crew failed to arrive, treat patient and reboard");
        // Three simultaneous casualties must produce distinct dispatches, with the third queued.
        for (int i = 90; i < 93; ++i)
            drop(w, i, initial + Vec2{double(i - 90), 3});
        w.time += 9;
        w.update_rescues();
        w.time += 3;
        w.update_rescues();
        check(w.rescues[1].car >= 0 && w.rescues[0].car != w.rescues[1].car &&
                  w.rescues[0].patient != w.rescues[1].patient && w.rescues[0].medic != w.rescues[1].medic,
              "incident queue duplicated an ambulance or patient");
        int medic = r.medic;
        w.controlledCar = r.car;
        w.update_rescues();
        check(w.people[medic].riding < 0 && w.people[who].dead, "stealing the ambulance healed the patient");
        std::filesystem::remove(path);
        {
            // Port of the managed IsResponding: on its way to a patient the crew does not
            // wait for the lights. At a red light with nobody on the junction an ambulance
            // without a call waits and one with a call goes.
            World city;
            city.generate(20261105);
            int node = -1;
            for (int n = 0; n < World::nodes * World::nodes && node < 0; ++n)
                if (city.layout.degree(n) == 4 && city.layout.urbanSite(World::nodePoint(n), 40) &&
                    !city.levelAt(World::nodePoint(n), 60) &&
                    (World::nodePoint(n) - city.position()).length() > 400)
                    node = n;
            check(node >= 0, "no ordinary junction");
            const Vec2 centre = World::nodePoint(node);
            for (auto &c : city.cars)
                if ((c.body.p - centre).length() < 70)
                    c.body.p = {World::landEdge - 20, -300. + 8. * double(c.id % 60)};
            for (auto &p : city.people)
                if ((p.body.p - centre).length() < 40)
                    p.body.p += Vec2{0, 80};
            auto unit =
                std::ranges::find_if(city.cars, [](const Car &c) { return c.kind == 4 && c.line < 0; });
            check(unit != city.cars.end(), "no ambulance");
            auto &c = *unit;
            c.routeFrom = node - 1;
            c.routeTo = node;
            c.routePhase = 1;
            c.laneIndex = 0;
            city.new_route(c);
            c.routeDistance = c.route->length() - .5;
            const auto [stop, heading] = c.route->sample(c.routeDistance);
            c.body.p = stop;
            c.body.angle = -heading;
            c.body.v = {};
            c.body.roadLevel = 0;
            c.body.height = city.surface(stop, 0) + .8;
            c.planned_to = -1;
            city.indexTraffic();
            // Coming from the west the car travels along +x: direction 1 of the lights.
            while (CityLayout::green(node % World::nodes, node / World::nodes, 1, city.time))
                city.time += .5;
            for (auto &idle : city.rescues)
                idle = {};
            check(!city.intersectionClear(c), "an ambulance without a call went through a red light");
            city.rescues[0].car = int(c.id);
            city.rescues[0].patient = 15;
            city.rescues[0].stage = World::RescueStage::driving;
            check(city.intersectionClear(c), "an ambulance on a call waited for the lights");
            city.rescues[0].stage = World::RescueStage::returning;
            check(!city.intersectionClear(c), "an ambulance on its way back went through a red light");
            // A patient the crew cannot stop beside: he lies where no car stands, here on
            // a level that does not exist. The crew used to drive round him until the call
            // ran out. Driving past him it now has a ninth of the time; standing in a queue
            // near him it loses none.
            const int fallen = 15, doctor = 16;
            city.resumeWalk(city.people[fallen], centre + Vec2{9.3, 9.3}, 0);
            city.killPerson(city.people[fallen]);
            city.people[fallen].body.roadLevel = 7;
            auto call = [&]
            {
                city.rescues[0] = {};
                city.rescues[0].car = int(c.id);
                city.rescues[0].medic = doctor;
                city.rescues[0].patient = fallen;
                city.rescues[0].stage = World::RescueStage::driving;
                city.rescues[0].deadline = city.time + 300;
            };
            auto wait = [&](double seconds)
            {
                for (int tick = 0; tick < int(seconds * 4); ++tick)
                {
                    city.time += .25;
                    city.update_rescues();
                }
            };
            check((city.people[fallen].body.p - c.body.p).length() < 45,
                  "the patient is not near the ambulance");
            call();
            c.body.v = {};
            wait(120);
            check(city.rescues[0].patient == fallen, "a crew in a queue near its patient gave him up");
            call();
            c.body.v = {5, 0};
            wait(30);
            check(city.rescues[0].patient == fallen,
                  "a crew gave a patient up before it had looked for a place");
            wait(6);
            check(city.rescues[0].patient != fallen && city.people[fallen].dead &&
                      city.people[fallen].rescueAfter > city.time + 200,
                  "a crew kept driving round a patient it could not stop beside");
        }
        std::println("Ambulance dispatch, treatment, reboarding, queue limits, lights and save/load passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::filesystem::remove(path);
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
