#include "promzona/audio.hpp"
#include "promzona/front_end.hpp"
#include "promzona/world.hpp"
#include "json.hpp"
#include <algorithm>
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
// Every generated actor stands somewhere it could have walked or driven to. A city that
// has already run keeps its numbers, but its queues at the lights are no longer a fresh start.
void soundCity(const World &w, Density density, bool fresh = true)
{
    const auto size = populationSize(density);
    require(w.density == density && w.trafficPopulation == size.cars && w.cars.size() == size.cars &&
                w.people.size() == size.people,
            "population does not match the density");
    for (const auto &p : w.people)
    {
        require(std::isfinite(p.body.p.x) && std::isfinite(p.body.p.z) && !p.dead &&
                    (p.next != p.node || p.role == Person::Role::beach || !fresh),
                "pedestrian is not on a walkable edge");
        require(p.riding >= 0 || p.body.roadLevel || w.terrain.height(p.body.p) > CityLayout::waterLevel,
                "pedestrian starts in the water");
    }
    size_t crowded = 0;
    for (size_t a = 1; fresh && a < w.cars.size(); ++a)
    {
        const auto &c = w.cars[a];
        require(c.route && !c.destroyed && !c.falling, "car has no route");
        require(std::abs(c.body.height - .8 - w.surface(c.body.p, c.body.roadLevel)) < 1e-6,
                "car is off its road");
        for (size_t b = 0; b < a; ++b)
            crowded += w.cars[b].line < 0 && c.line < 0 && w.cars[b].body.roadLevel == c.body.roadLevel &&
                       (w.cars[b].body.p - c.body.p).length() < 4.5;
    }
    // The low density keeps its historical start; denser cities must not begin with pile-ups.
    require(density == Density::low || crowded * 100 <= w.cars.size(), "dense traffic starts inside itself");
    const auto couriers = size_t(std::ranges::count(w.people, true, &Person::courier));
    require(couriers * 100 >= w.people.size() && couriers * 10 <= w.people.size(), "courier share is off");
    if (density != Density::low)
        require(w.scooters.size() <= w.people.size() / 25 && !w.scooters.empty(), "scooter fleet is off");
    require(w.lines.size() == 4, "transit lines are missing");
}
} // namespace

int main()
{
    try
    {
        require(populationSize(Density::low).cars == 260 && populationSize(Density::low).people == 720 &&
                    populationSize(Density::medium).cars == 540 &&
                    populationSize(Density::medium).people == 1800 &&
                    populationSize(Density::high).cars == 1080 &&
                    populationSize(Density::high).people == 3240,
                "population sizes changed");

        // The setting: three values, a stable file form and a tolerant reader.
        require(cycleDensity(Density::low, 1) == Density::medium &&
                    cycleDensity(Density::high, 1) == Density::low &&
                    cycleDensity(Density::low, -1) == Density::high,
                "density does not cycle");
        require(densityFromCode("high", Density::low) == Density::high &&
                    densityFromCode("medium", Density::low) == Density::medium &&
                    densityFromCode("low", Density::high) == Density::low &&
                    densityFromCode("crowd", Density::medium) == Density::medium,
                "density codes are wrong");
        const auto folder = std::filesystem::temp_directory_path() / "promzona-density";
        std::filesystem::create_directories(folder);
        const auto setting = folder / "density.txt";
        std::filesystem::remove(setting);
        require(loadDensity(setting, Density::high) == Density::high,
                "missing setting must use the fallback");
        require(saveDensity(setting, Density::medium) &&
                    loadDensity(setting, Density::high) == Density::medium,
                "density setting was not stored");
        FrontEnd menu;
        menu.page = MenuPage::settings;
        menu.population = Density::high;
        bool listed = false;
        for (const auto &row : menu.rows(false, {}, SceneMode::enhanced, {}))
            listed |= row.item == MenuItem::population && row.value == L"Высокая";
        require(listed, "settings do not offer the population");

        // A world made without naming a density is the city of every earlier save.
        World legacy;
        legacy.generate(20261105u);
        soundCity(legacy, Density::low);
        const auto file = folder / "city.json";
        std::filesystem::remove(file);
        legacy.save(file);
        {
            std::ifstream in(file, std::ios::binary);
            require(!nlohmann::json::parse(in).contains("density"), "a low-density save changed its format");
        }
        World loaded;
        loaded.load(file);
        require(loaded.density == Density::low && loaded.people.size() == 720,
                "low-density save did not load");

        for (Density density : {Density::medium, Density::high})
        {
            World w;
            w.generate(20261105u, {}, CityLayout::districtRevision, density);
            soundCity(w, density);
            // The same seed keeps its streets at every density.
            require(w.layout.revision() == legacy.layout.revision() &&
                        w.buildings.size() == legacy.buildings.size() &&
                        w.potholes.size() == legacy.potholes.size(),
                    "density changed the city itself");
            // A quiet minute: nobody dies, nothing burns and no one is wanted without the player.
            w.dispatchAt = w.patrolScanAt = 1e9;
            const int stolen = w.controlledCar;
            for (int i = 0; i < 60 * 40; ++i)
                w.step({}, frame);
            require(w.controlledCar == stolen && w.health == 1 && w.wanted == 0,
                    "idle player got into trouble");
            require(std::ranges::none_of(w.people, &Person::dead), "people died in a quiet city");
            require(std::ranges::none_of(w.cars, &Car::destroyed) && w.sunkCars == 0,
                    "cars were wrecked in a quiet city");
            for (const auto &c : w.cars)
                require(std::isfinite(c.body.p.x) && std::isfinite(c.body.p.z) &&
                            std::max(std::abs(c.body.p.x), std::abs(c.body.p.z)) < World::landEdge + 60,
                        "a car left the city");
            for (const auto &p : w.people)
                require(std::isfinite(p.body.p.x) && std::isfinite(p.body.p.z),
                        "a pedestrian position is invalid");

            // The density travels with the save, and the save restores the same crowd.
            std::filesystem::remove(file);
            w.save(file);
            {
                std::ifstream in(file, std::ios::binary);
                require(nlohmann::json::parse(in).value("density", -1) == int(density),
                        "density was not saved");
            }
            loaded.load(file);
            soundCity(loaded, density, false);
            require(loaded.time == w.time && loaded.people[100].body.p.x == w.people[100].body.p.x &&
                        loaded.cars[100].body.p.z == w.cars[100].body.p.z,
                    "dense save did not restore the same city");
            for (int i = 0; i < 120; ++i)
                loaded.step({}, frame);
            require(std::ranges::none_of(loaded.people, &Person::dead), "loaded dense city is unstable");

            // The city still makes up its losses out of view.
            int victims = 0, wrecks = 0;
            for (size_t i = 0; i < loaded.people.size() && victims < 12; ++i)
                if (loaded.people[i].riding < 0 && loaded.people[i].ownedScooter < 0 &&
                    !loaded.personEngaged(int(i)) && loaded.outOfSight(loaded.people[i].body.p))
                {
                    loaded.killPerson(loaded.people[i]);
                    loaded.people[i].diedAt = -1000;
                    ++victims;
                }
            for (size_t i = 1; i < loaded.trafficPopulation && wrecks < 8; ++i)
                if (loaded.cars[i].line < 0 && !loaded.carEngaged(int(i)) &&
                    loaded.outOfSight(loaded.cars[i].body.p))
                {
                    loaded.cars[i].destroy();
                    loaded.cars[i].lastDamageAt = -1000;
                    ++wrecks;
                }
            loaded.nextRescueDispatch = 1e9;
            for (int i = 0; i < 60 * 12; ++i)
                loaded.step({}, frame);
            require(victims == 12 && wrecks == 8 && loaded.renewedPeople == 12 && loaded.renewedCars == 8,
                    "dense city did not renew itself");
        }

        // An unknown density is refused instead of being guessed.
        {
            std::ifstream in(file, std::ios::binary);
            auto j = nlohmann::json::parse(in);
            in.close();
            j["density"] = 3;
            std::ofstream(file, std::ios::binary | std::ios::trunc) << j.dump();
        }
        bool refused = false;
        try
        {
            loaded.load(file);
        }
        catch (const std::exception &)
        {
            refused = true;
        }
        require(refused, "unknown density was accepted");
        // A dense save cannot pass for a low-density one.
        {
            std::ifstream in(file, std::ios::binary);
            auto j = nlohmann::json::parse(in);
            in.close();
            j.erase("density");
            std::ofstream(file, std::ios::binary | std::ios::trunc) << j.dump();
        }
        refused = false;
        try
        {
            loaded.load(file);
        }
        catch (const std::exception &)
        {
            refused = true;
        }
        require(refused, "population of the wrong density was accepted");
        std::filesystem::remove_all(folder);
        std::cout << "density tests passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
