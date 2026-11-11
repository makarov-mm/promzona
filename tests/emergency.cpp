#include "promzona/world.hpp"
#include <chrono>
#include <iostream>
using namespace pz;
namespace
{
void check(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
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
                ("pz-rescue-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) +
                 ".json");
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
        w.updateRescues();
        check(w.rescues[0].car < 0, "ambulance dispatched instantly");
        w.time = 8;
        w.updateRescues();
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
        w.updateRescues();
        w.time += 3;
        w.updateRescues();
        check(w.rescues[1].car >= 0 && w.rescues[0].car != w.rescues[1].car &&
                  w.rescues[0].patient != w.rescues[1].patient && w.rescues[0].medic != w.rescues[1].medic,
              "incident queue duplicated an ambulance or patient");
        int medic = r.medic;
        w.controlledCar = r.car;
        w.updateRescues();
        check(w.people[medic].riding < 0 && w.people[who].dead, "stealing the ambulance healed the patient");
        std::filesystem::remove(path);
        std::cout << "Ambulance dispatch, treatment, reboarding, queue limits and save/load passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::filesystem::remove(path);
        std::cerr << e.what() << '\n';
        return 1;
    }
}
