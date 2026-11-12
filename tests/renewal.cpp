#include "promzona/fleet.hpp"
#include "promzona/world.hpp"
#include "json.hpp"
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>

using namespace pz;
namespace
{
void require(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
}
constexpr double frame = 1. / 60;
double away(const World &w, Vec2 p)
{
    return (p - w.position()).length();
}
bool plain(const World &w, size_t i)
{
    const auto &p = w.people[i];
    return p.riding < 0 && p.waitingLine < 0 && !p.courier && !p.inCompany && p.ownedScooter < 0 && !p.dead &&
           !p.body.roadLevel && !w.personEngaged(int(i));
}
// An ordinary passer-by inside or outside the player's view.
size_t walker(const World &w, bool distant, size_t skip = size_t(-1))
{
    for (size_t i = 0; i < w.people.size(); ++i)
        if (i != skip && plain(w, i) &&
            (distant ? away(w, w.people[i].body.p) > 320 : away(w, w.people[i].body.p) < 150))
            return i;
    throw std::runtime_error("no suitable walker");
}
size_t trafficCar(const World &w, bool distant, size_t skip = size_t(-1))
{
    for (size_t i = 1; i < w.trafficPopulation; ++i)
    {
        const auto &c = w.cars[i];
        if (i != skip && c.line < 0 && c.kind <= 2 && !c.destroyed && !c.driverless && !c.body.roadLevel &&
            !w.carEngaged(int(i)) && (distant ? away(w, c.body.p) > 320 : away(w, c.body.p) < 150))
            return i;
    }
    throw std::runtime_error("no suitable traffic car");
}
void renew(World &w)
{
    w.nextRenewal = 0;
    w.step({}, frame);
}
// Open water at least `least` and at most `most` metres deep, away from the bridges.
Vec2 water(const World &w, double least, double most)
{
    const double edge = World::landEdge;
    for (double z = -edge + 20; z < edge - 20; z += 7.3)
        for (double x = -edge - 30; x < edge + 30; x += .5)
        {
            const Vec2 p{x, z};
            const double bed = w.terrain.bedHeight(x, z);
            const double depth = CityLayout::waterLevel - bed;
            if (depth > least && depth < most && w.terrain.height(p) == bed && !w.levelAt(p, 6) &&
                std::max(std::abs(x), std::abs(z)) < edge - 4)
                return p;
        }
    throw std::runtime_error("no water of the requested depth");
}
} // namespace

int main()
{
    try
    {
        World w;
        w.generate(20261105u);
        // Police and ambulance dispatch are tested elsewhere; here they must not interfere.
        w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
        for (int i = 0; i < 30; ++i)
            w.step({}, frame);
        require(w.renewedPeople == 0 && w.renewedCars == 0 && w.drownings == 0 && w.sunkCars == 0,
                "a quiet city renewed or drowned somebody");

        // A body left behind for three minutes becomes a new passer-by elsewhere.
        const size_t body = walker(w, true);
        w.killPerson(w.people[body]);
        require(w.people[body].dead && std::abs(w.people[body].diedAt - w.time) < 1e-9,
                "time of death not recorded");
        renew(w);
        require(w.people[body].dead && w.renewedPeople == 0, "a fresh body was replaced at once");
        w.people[body].diedAt = w.time - 181;
        w.conversation.actor = int(body);
        renew(w);
        require(w.people[body].dead, "a person in use by the game was replaced");
        w.conversation.actor = -1;
        const auto id = w.people[body].body.id;
        renew(w);
        {
            const auto &p = w.people[body];
            require(!p.dead && p.health == 1 && w.renewedPeople == 1 && p.body.id == id,
                    "body was not replaced");
            require(away(w, p.body.p) > 245 && !p.body.roadLevel && p.next != p.node,
                    "new walker placed in view");
            require(w.terrain.height(p.body.p) > CityLayout::waterLevel &&
                        !w.insideBuilding(p.body.p, p.body.height, .2) && !w.openCut(p.body.p, .3),
                    "new walker placed on bad ground");
            require(p.uniform == Person::Uniform::none && p.fear == 0 && p.knockedUntil == 0,
                    "new walker kept old state");
        }
        const Vec2 start = w.people[body].body.p;
        for (int i = 0; i < 600; ++i)
            w.step({}, frame);
        require(!w.people[body].dead && (w.people[body].body.p - start).length() > 3,
                "new walker does not walk");

        const size_t close = walker(w, false);
        w.killPerson(w.people[close]);
        w.people[close].diedAt = w.time - 500;
        renew(w);
        require(w.people[close].dead && w.renewedPeople == 1, "a body in view was replaced");
        require(w.people.size() == 720, "population changed");

        // A wreck, and a car its driver left, go back into traffic out of view.
        const size_t wreck = trafficCar(w, true);
        w.cars[wreck].destroy();
        w.cars[wreck].lastDamageAt = w.time;
        renew(w);
        require(w.cars[wreck].destroyed && w.renewedCars == 0, "a wreck was replaced at once");
        w.cars[wreck].lastDamageAt = w.time - 7;
        w.job.kind = JobKind::vehicle;
        w.job.actor = int(wreck);
        renew(w);
        require(w.cars[wreck].destroyed, "the car of a running job was replaced");
        w.job = {};
        renew(w);
        {
            const auto &c = w.cars[wreck];
            require(!c.destroyed && c.health == 1 && !c.driverless && w.renewedCars == 1,
                    "wreck was not replaced");
            require(c.route && away(w, c.body.p) > 245 && c.model == streetModel(int(wreck)) && c.id == wreck,
                    "new car is not ordinary traffic out of view");
            require(std::abs(c.body.height - .8 - w.surface(c.body.p, c.body.roadLevel)) < 1e-6,
                    "new car is not on the road");
        }
        const Vec2 parked = w.cars[wreck].body.p;
        for (int i = 0; i < 600; ++i)
            w.step({}, frame);
        require((w.cars[wreck].body.p - parked).length() > 5, "new car does not drive");

        const size_t left = trafficCar(w, true, wreck);
        w.cars[left].driverless = true;
        w.cars[left].personal = true;
        renew(w);
        require(w.cars[left].driverless && w.renewedCars == 1, "a garage car was replaced");
        w.cars[left].personal = false;
        renew(w);
        require(!w.cars[left].driverless && w.renewedCars == 2, "abandoned car did not return to traffic");

        const size_t near = trafficCar(w, false);
        w.cars[near].destroy();
        w.cars[near].lastDamageAt = w.time - 60;
        const size_t stolen = trafficCar(w, false, near);
        w.cars[stolen].driverless = true;
        renew(w);
        require(w.cars[near].destroyed && w.cars[stolen].driverless && w.renewedCars == 2,
                "cars in view were replaced");
        require(w.cars.size() >= w.trafficPopulation, "traffic population changed");

        // A car in the canal sinks: it is out of play, then its slot returns to traffic.
        const Vec2 deep = water(w, 2.2, 50);
        const size_t sinking = trafficCar(w, true, left);
        {
            auto &c = w.cars[sinking];
            c.body.p = deep;
            c.body.height = w.terrain.height(deep) + .8;
            // At rest: the check is that the car leaves traffic, not how far a wreck coasts.
            c.body.v = {};
            c.body.w = 0;
            c.fireTime = 1;
            c.personal = true;
        }
        require(w.waterDepth(deep, w.terrain.height(deep)) > 2.2 &&
                    w.waterDepth(deep, CityLayout::waterLevel + .1) == 0,
                "water depth is wrong");
        w.nextWaterCheck = 0;
        w.step({}, frame);
        {
            const auto &c = w.cars[sinking];
            require(c.falling && c.driverless && !c.personal && c.fireTime == 0 && w.sunkCars == 1,
                    "car did not sink");
        }
        const Vec2 sunkAt = w.cars[sinking].body.p;
        for (int i = 0; i < 90; ++i)
            w.step({}, frame);
        require(w.cars[sinking].falling && (w.cars[sinking].body.p - sunkAt).length() < 2 && w.sunkCars == 1,
                "sunk car kept driving");
        // Three seconds under water, then the next renewal pass (every two seconds).
        for (int i = 0; i < 300; ++i)
            w.step({}, frame);
        require(!w.cars[sinking].falling && w.renewedCars == 3 && away(w, w.cars[sinking].body.p) > 245,
                "sunk car did not return to traffic");

        // A walker out of its depth drowns silently; nobody is blamed and no crew is sent.
        const size_t swimmer = walker(w, true, body);
        {
            auto &p = w.people[swimmer];
            p.body.p = p.target = deep;
            p.body.height = w.terrain.height(deep) + .85;
        }
        const size_t marks = w.surfaceMarks.size();
        w.nextWaterCheck = 0;
        w.step({}, frame);
        require(w.people[swimmer].dead && w.people[swimmer].drowned && w.drownings == 1,
                "walker did not drown");
        require(w.wanted == 0 && w.surfaceMarks.size() == marks, "drowning left blood or a wanted level");
        w.nextRescueDispatch = 0;
        for (int i = 0; i < 600; ++i)
            w.step({}, frame);
        require(std::ranges::none_of(w.rescues, [&](const auto &r) { return r.patient == int(swimmer); }),
                "an ambulance was sent to a drowned body");
        w.nextRescueDispatch = 1e9;

        // An animal on the ground drowns too; a flying bird over the water does not.
        require(w.animals.size() > 2, "city has animals");
        {
            auto &cat = w.animals[0];
            auto &bird = w.animals[1];
            cat.p = bird.p = deep;
            cat.altitude = w.terrain.height(deep);
            bird.altitude = CityLayout::waterLevel + 6;
            cat.deadUntil = bird.deadUntil = 0;
            const int before = w.drownings;
            w.nextWaterCheck = 0;
            w.step({}, frame);
            require(w.animals[0].deadUntil > w.time && w.drownings == before + 1,
                    "animal in deep water did not drown");
            require(w.animals[1].deadUntil == 0, "a bird over the water drowned");
        }

        // The drone is a second pair of eyes: nothing changes under it.
        {
            const size_t watched = walker(w, true, body);
            w.killPerson(w.people[watched]);
            w.people[watched].diedAt = w.time - 500;
            w.drone.active = true;
            w.drone.position = w.people[watched].body.p + Vec2{30, 0};
            require(!w.outOfSight(w.people[watched].body.p) &&
                        w.outOfSight(w.people[watched].body.p + Vec2{0, 400}),
                    "drone view is not respected");
            w.drone.active = false;
            require(w.outOfSight(w.people[watched].body.p), "a distant body counts as visible");
            const int renewed = w.renewedPeople;
            renew(w);
            require(!w.people[watched].dead && w.renewedPeople == renewed + 1,
                    "watched body was not replaced later");
        }

        // Water above the knee puts out a burning walker and the burning player.
        const Vec2 shallow = water(w, .5, 1.);
        const size_t burning = walker(w, true, body);
        {
            auto &p = w.people[burning];
            p.body.p = p.target = shallow;
            p.body.height = w.terrain.height(shallow) + .85;
            p.body.v = {};
            p.burnUntil = w.time + 9;
            p.burnPlayerCaused = true;
        }
        const double before = w.people[burning].health;
        for (int i = 0; i < 20; ++i)
            w.step({}, frame);
        require(!w.people[burning].dead && w.people[burning].burnUntil == 0 && !w.people[burning].drowned,
                "water did not put the walker out");
        require(w.people[burning].health > before - .05, "walker kept burning in water");

        // The dead keep their time of death in a save; older saves start the wait at load.
        const auto file = std::filesystem::temp_directory_path() / "promzona-renewal.json";
        std::filesystem::remove(file);
        // The crew sent out above may have revived the earlier casualty by now.
        const size_t fallen = walker(w, false, close);
        w.killPerson(w.people[fallen]);
        w.people[fallen].diedAt = w.time - 40;
        w.save(file);
        World loaded;
        loaded.load(file);
        require(loaded.people[swimmer].dead && loaded.people[swimmer].drowned && loaded.people[fallen].dead &&
                    !loaded.people[fallen].drowned &&
                    std::abs(loaded.people[fallen].diedAt - (w.time - 40)) < 1e-6,
                "deaths were not saved");
        require(loaded.cars[near].destroyed && loaded.cars[stolen].driverless, "cars were not saved");
        {
            std::ifstream in(file, std::ios::binary);
            auto j = nlohmann::json::parse(in);
            in.close();
            for (auto &entry : j.at("people"))
                entry.erase("death");
            std::ofstream(file, std::ios::binary | std::ios::trunc) << j.dump();
        }
        loaded.load(file);
        require(loaded.people[fallen].dead && std::abs(loaded.people[fallen].diedAt - loaded.time) < 1e-9 &&
                    !loaded.people[swimmer].drowned,
                "older save did not get a time of death");

        // After a massacre the city fills up again once the player has left.
        World city;
        city.generate(424242u);
        city.dispatchAt = city.patrolScanAt = city.nextRescueDispatch = 1e9;
        int victims = 0, wrecks = 0;
        for (size_t i = 0; i < city.people.size() && victims < 30; ++i)
            if (plain(city, i) && away(city, city.people[i].body.p) > 320)
            {
                city.killPerson(city.people[i]);
                city.people[i].diedAt = -200;
                ++victims;
            }
        for (size_t i = 1; i < city.trafficPopulation && wrecks < 20; ++i)
            if (city.cars[i].line < 0 && !city.carEngaged(int(i)) && away(city, city.cars[i].body.p) > 320)
            {
                city.cars[i].destroy();
                city.cars[i].lastDamageAt = -200;
                ++wrecks;
            }
        require(victims == 30 && wrecks == 20, "could not stage the massacre");
        for (int i = 0; i < 60 * 30; ++i)
            city.step({}, frame);
        require(city.renewedPeople == 30 && city.renewedCars == 20,
                "the city did not recover in half a minute");
        require(std::ranges::none_of(city.people, &Person::dead) &&
                    std::ranges::none_of(city.cars, &Car::destroyed),
                "bodies or wrecks remain");
        std::filesystem::remove(file);
        city.save(file);
        loaded.load(file);
        require(loaded.people.size() == 720 && std::ranges::none_of(loaded.people, &Person::dead),
                "recovered city did not survive a save");
        std::filesystem::remove(file);
        auto backup = file;
        backup += ".bak";
        std::filesystem::remove(backup);
        std::cout << "renewal tests passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
