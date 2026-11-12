#include "promzona/world.hpp"
#include "json.hpp"
#include <bit>
#include <cctype>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <unordered_set>

namespace pz
{
namespace
{
using nlohmann::json;
// A managed save of a whole city is tens of megabytes of actor records. Only the
// player's progress is carried over, so the large arrays are skipped while parsing.
json readProgress(const std::filesystem::path &file)
{
    std::error_code ec;
    const auto size = std::filesystem::file_size(file, ec);
    if (ec)
        throw std::runtime_error("Cannot open the save file");
    if (size > 90'000'000)
        throw std::runtime_error("Save file is too large");
    std::ifstream in(file, std::ios::binary);
    if (!in)
        throw std::runtime_error("Cannot open the save file");
    static const std::unordered_set<std::string> bulky{
        "Cars",     "People", "Animals",  "Props",     "Marks",     "Cash",          "Pickups",
        "Scooters", "Groups", "Services", "Incidents", "PedPlaces", "TransitRiders", "Path"};
    const json::parser_callback_t filter = [](int, json::parse_event_t event, json &parsed)
    { return event != json::parse_event_t::key || !bulky.contains(parsed.get<std::string>()); };
    auto document = json::parse(in, filter, false);
    if (document.is_discarded() || !document.is_object())
        throw std::runtime_error("The file is not a PROMZONA save");
    return document;
}
double number(const json &object, const char *key)
{
    const auto it = object.find(key);
    if (it == object.end() || !it->is_number())
        throw std::runtime_error(std::string("Managed save is missing ") + key);
    const double value = it->get<double>();
    if (!std::isfinite(value))
        throw std::runtime_error(std::string("Managed save has an invalid ") + key);
    return value;
}
// System.Text.Json writes enum dictionary keys as names and enum values as numbers.
WeaponId weaponId(const json &value)
{
    if (value.is_number_integer())
    {
        const int id = value.get<int>();
        return id >= 1 && id <= 4 ? WeaponId(id) : WeaponId::none;
    }
    if (!value.is_string())
        return WeaponId::none;
    auto name = value.get<std::string>();
    for (char &c : name)
        c = char(std::tolower(static_cast<unsigned char>(c)));
    if (name == "1" || name == "pistol")
        return WeaponId::pistol;
    if (name == "2" || name == "automatic")
        return WeaponId::automatic;
    if (name == "3" || name == "bazooka")
        return WeaponId::bazooka;
    if (name == "4" || name == "flamethrower")
        return WeaponId::flamethrower;
    return WeaponId::none;
}
} // namespace

// Import of a managed (C#) save, formats 1-3. The native city generator does not
// reproduce the managed actor population, so this carries over the player's progress:
// the city seed, clock, weather, money, health and weapons. The source file is only read.
ManagedImport World::importManaged(const std::filesystem::path &file)
{
    const auto document = readProgress(file);
    ManagedImport report;
    report.format = int(number(document, "Format"));
    if (report.format < 1 || report.format > 3)
        throw std::runtime_error("Unsupported managed save format");
    const double x = number(document, "X"), z = number(document, "Z"), hour = number(document, "Hour");
    const double cash = number(document, "Money"), seedValue = number(document, "Seed");
    if (std::abs(x) > 960 || std::abs(z) > 960 || cash < 0 || cash > 999999999 || hour < 0 || hour > 24 ||
        seedValue < -2147483648. || seedValue > 2147483647. || seedValue != std::floor(seedValue))
        throw std::runtime_error("Unsupported or invalid save file");
    const json none = json::object();
    const auto found = document.find("World");
    const json &managed = found != document.end() && found->is_object() ? *found : none;
    if (report.format >= 2 && managed.empty())
        throw std::runtime_error("Managed save has no world");
    report.roadRevision = managed.value("RoadRevision", 0);
    if (report.roadRevision < 0 || report.roadRevision > 4)
        throw std::runtime_error("Unsupported road layout revision");
    // The managed game stores one reservation per crossing, which gives the city size.
    // Format 1 predates the stored world and always used the small 33-node city.
    const auto crossings = managed.find("Reservations");
    const size_t cells = crossings != managed.end() && crossings->is_array() ? crossings->size() : 0;
    if (report.format >= 2 && cells != 33 * 33 && cells != 49 * 49)
        throw std::runtime_error("Invalid city size");
    // The native generator reproduces only the 49-node wide-road plan of revision 0.
    report.samePlan = cells == 49 * 49 && report.roadRevision == 0 && managed.value("WideRoads", false);

    World next;
    next.generate(std::bit_cast<std::uint32_t>(std::int32_t(seedValue)), {},
                  report.samePlan ? 0 : CityLayout::districtRevision);
    next.money = int(cash);
    next.clock.setHour(hour);
    const bool raining = document.value("Raining", false);
    next.rain = next.rainTarget = raining ? 1 : 0;
    next.weatherClock = raining ? 80 : 180;

    if (const auto hero = managed.find("Player"); hero != managed.end() && hero->is_object())
        if (const auto state = hero->find("State"); state != hero->end() && state->is_object())
        {
            const double damage = state->value("GunDamage", 0.);
            if (std::isfinite(damage))
                next.health = clamp(1 - damage, .05, 1.);
        }
    if (const auto ammunition = managed.find("Ammunition");
        ammunition != managed.end() && ammunition->is_object())
        for (const auto &[key, rounds] : ammunition->items())
        {
            const auto id = weaponId(json(key));
            if (id == WeaponId::none || !rounds.is_number_integer())
                continue;
            const int count = clamp(rounds.get<int>(), 0, Inventory::ammoLimit);
            // Zero rounds still means the weapon is owned in the managed game.
            auto ammo = next.inventory.capture();
            auto owned = next.inventory.ownership();
            ammo[size_t(id)] = count;
            owned[size_t(id)] = true;
            next.inventory.restore(ammo, owned);
            ++report.weapons;
        }
    if (const auto selected = managed.find("SelectedWeapon"); selected != managed.end())
        if (const auto id = weaponId(*selected); id != WeaponId::none)
            next.inventory.select(id);

    // The saved position is meaningful only on the identical street plan.
    const Vec2 saved{x, z};
    // Body heights are measured at the centre: .85 m above the feet for a walker.
    const double floorHeight = next.terrain.height(saved), centre = floorHeight + .85;
    if (report.samePlan && next.layout.urbanSite(saved, .5) && !next.levelAt(saved, 2) &&
        floorHeight > CityLayout::waterLevel + .3 && !next.insideBuilding(saved, centre, .5) &&
        next.vehicleSpace(saved, centre, nullptr, false))
    {
        // Vehicles are regenerated, so the player continues on foot where the save was made.
        if (next.controlledCar >= 0)
            next.cars[next.controlledCar].driverless = true;
        next.controlledCar = -1;
        next.player.p = saved;
        next.player.v = {};
        next.player.roadLevel = 0;
        next.player.height = centre;
        const double heading = document.value("Heading", 0.);
        next.player.angle = std::isfinite(heading) ? -heading : 0;
        next.player.wake();
        report.positionKept = true;
    }
    report.seed = next.seed;
    *this = std::move(next);
    return report;
}
} // namespace pz
