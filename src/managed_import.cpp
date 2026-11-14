#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include "json.hpp"
#include <algorithm>
#include <optional>
#include <bit>
#include <cctype>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <unordered_set>

namespace Promzona
{
namespace
{
using nlohmann::json;
// A managed save of a whole city is tens of megabytes of actor records. The cars and the
// passers-by are read for the few fields the import uses; the other arrays are skipped
// while parsing.
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
    static const std::unordered_set<std::string> bulky{"Animals",   "Props",     "Marks",         "Cash",
                                                       "Pickups",   "Scooters",  "Groups",        "Services",
                                                       "Incidents", "PedPlaces", "TransitRiders", "Path"};
    // What is kept of the state and the body of each car and passer-by (World.Cars[n].State.*
    // is five levels down). A field is captured under its own name, an auto-property under
    // the name of its backing field.
    static const std::unordered_set<std::string> kept{
        "X",         "Z",     "Angle",    "Damage", "Destroyed", "Falling",  "Driverless", "TransitRoute",
        "RoadLevel", "Paint", "Detached", "Kind",   "Hero",      "Position", "Heading",    "Dead",
        "KnockedFor"};
    const json::parser_callback_t filter = [](int depth, json::parse_event_t event, json &parsed)
    {
        if (event != json::parse_event_t::key)
            return true;
        const auto &key = parsed.get_ref<const std::string &>();
        if (bulky.contains(key))
            return false;
        if (depth != 5)
            return true;
        const bool backing = key.size() > 17 && key.front() == '<' && key.ends_with(">k__BackingField");
        return kept.contains(backing ? key.substr(1, key.size() - 17) : key);
    };
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
// A value of a captured state (CityState.Capture): a field under its own name, an
// auto-property under the name of its backing field.
const json *captured(const json &state, const char *name)
{
    if (!state.is_object())
        return nullptr;
    if (const auto it = state.find(name); it != state.end())
        return &*it;
    if (const auto it = state.find('<' + std::string(name) + ">k__BackingField"); it != state.end())
        return &*it;
    return nullptr;
}
double capturedNumber(const json &state, const char *name, double fallback = 0)
{
    const auto *value = captured(state, name);
    if (!value || !value->is_number())
        return fallback;
    const double number = value->get<double>();
    return std::isfinite(number) ? number : fallback;
}
bool capturedFlag(const json &state, const char *name)
{
    const auto *value = captured(state, name);
    return value && value->is_boolean() && value->get<bool>();
}
const json &part(const json &object, const char *name)
{
    static const json none = json::object();
    if (!object.is_object())
        return none;
    const auto it = object.find(name);
    return it != object.end() && it->is_object() ? *it : none;
}
// Where a saved actor is and, for a car, which way it faces: its body while it has one,
// otherwise the position it was captured with.
struct Pose
{
    Vec2 at;
    double heading{};
};
std::optional<Pose> pose(const json &actor)
{
    const json &body = part(actor, "Body"), &state = part(actor, "State");
    Pose result;
    if (body.contains("X") && body.contains("Z"))
    {
        result.at = {capturedNumber(body, "X", 1e9), capturedNumber(body, "Z", 1e9)};
        result.heading = -capturedNumber(body, "Angle");
    }
    else
    {
        const json *position = captured(state, "Position");
        if (!position || !position->is_object())
            return std::nullopt;
        result.at = {capturedNumber(*position, "X", 1e9), capturedNumber(*position, "Z", 1e9)};
        result.heading = capturedNumber(state, "Heading");
    }
    if (std::abs(result.at.x) > 2000 || std::abs(result.at.z) > 2000)
        return std::nullopt;
    return result;
}
// The first model of the native fleet of a managed kind; trams, trolleybuses and kinds
// the native fleet lacks have none.
std::optional<unsigned> modelOfKind(int kind)
{
    if (kind == 9 || kind == 10)
        return std::nullopt;
    for (unsigned model = 0; model < std::size(fleet); ++model)
        if (fleet[model].kind == kind)
            return model;
    return std::nullopt;
}
// Ordinary traffic of a save made on the same street plan goes back to where it was: each
// saved car that stands on a street takes a native car of its kind. Cars that are left
// keep the place the city gave them, or get another if a saved car now stands there.
int seatCars(World &w, const json &saved)
{
    struct Seat
    {
        int from{-1}, to{-1}, lane{};
        double along{}, miss{1e9};
    };
    // The street a car is on: between two neighbouring crossings, in one of its lanes and
    // facing along it. Routes of the native traffic begin nine metres past a crossing and
    // end at the stop line of the next, so a car saved inside a junction is put at the
    // nearer end of the street it faces along. The decks of overpasses are left out.
    constexpr double first = 9.5, last = World::spacing - stopLine - .1;
    auto seatAt = [&](const Pose &place, int kind)
    {
        Seat best;
        const int node = World::nearestNode(place.at);
        std::vector<int> around{node};
        for (int next : w.neighbours(node))
            around.push_back(next);
        const Vec2 facing = forward(place.heading);
        for (int from : around)
            for (int to : w.neighbours(from))
            {
                if (w.levelEdge(from, to))
                    continue;
                const Vec2 start = World::nodePoint(from), f = (World::nodePoint(to) - start).unit();
                const double along = dot(place.at - start, f), side = dot(place.at - start, perpendicular(f));
                if (along < -1 || along > World::spacing + 1 || dot(facing, f) < .5)
                    continue;
                const double kept = clamp(along, first, last);
                for (int lane = 0; lane < (w.layout.wide() ? 2 : 1); ++lane)
                {
                    // A bus keeps to the kerb, where its stops are.
                    if (kind == 5 && w.layout.wide() && lane == 0)
                        continue;
                    const double miss = std::abs(side - CityLayout::laneOffset(lane));
                    if (miss < 1.7 && miss + std::abs(along - kept) < best.miss)
                        best = {from, to, lane, kept, miss + std::abs(along - kept)};
                }
            }
        return best.from >= 0 ? std::optional<Seat>{best} : std::nullopt;
    };
    // Places that are taken, each with half the length of what stands there.
    std::vector<std::pair<Vec2, double>> taken;
    std::vector<int> waiting[15];
    std::vector<bool> open(w.cars.size());
    for (int i = 0; i < int(w.cars.size()); ++i)
    {
        const auto &c = w.cars[i];
        open[i] = i > 0 && i < int(w.trafficPopulation) && c.kind <= 8 && c.kind != 3 && c.kind != 4 &&
                  !c.body.roadLevel && !c.destroyed && !c.driverless && !w.carEngaged(i);
        if (open[i])
            waiting[c.kind].push_back(i);
        else
            taken.emplace_back(c.body.p, c.body.halfH);
    }
    for (auto &queue : waiting)
        std::ranges::reverse(queue);
    // Nobody is seated on top of a player who stands in the street.
    if (w.controlledCar < 0)
        taken.emplace_back(w.position(), 3.);
    // Bumper to bumper a queue leaves under two metres between its cars.
    auto clear = [&](Vec2 at, double half)
    {
        return std::ranges::none_of(taken, [&](const auto &other)
                                    { return (other.first - at).length() < other.second + half + 1.8; });
    };
    auto seat = [&](int slot, const Seat &place, double health, int paint)
    {
        const Car &old = w.cars[slot];
        Car car;
        car.id = old.id;
        car.body.id = old.body.id;
        fit_vehicle(car, old.model);
        car.body.friction = .5;
        car.laneIndex = place.lane;
        car.routeFrom = place.from;
        car.routeTo = place.to;
        car.routePhase = 1;
        w.newRoute(car);
        car.routeDistance = clamp(place.along - 9, 0., car.route->length());
        const auto [at, heading] = car.route->sample(car.routeDistance);
        car.body.p = at;
        car.body.angle = -heading;
        car.body.roadLevel = car.routeLevel;
        car.body.height = w.surface(at, car.body.roadLevel) + .8;
        car.health = health;
        car.paint = paint;
        taken.emplace_back(at, car.body.halfH);
        w.cars[slot] = std::move(car);
    };
    int seated = 0;
    for (size_t n = 1; n < saved.size(); ++n)
    {
        const json &state = part(saved[n], "State");
        const int kind = int(clamp(capturedNumber(state, "Kind"), -1., 99.));
        if (kind < 0 || kind > 8 || waiting[kind].empty() || capturedFlag(state, "Hero") ||
            capturedFlag(state, "Destroyed") || capturedFlag(state, "Falling") ||
            capturedFlag(state, "Driverless") || capturedNumber(state, "TransitRoute") != 0 ||
            capturedNumber(state, "RoadLevel") != 0)
            continue;
        const auto place = pose(saved[n]);
        if (!place)
            continue;
        const auto found = seatAt(*place, kind);
        if (!found)
            continue;
        const Vec2 start = World::nodePoint(found->from), f = (World::nodePoint(found->to) - start).unit();
        const int slot = waiting[kind].back();
        if (!clear(start + f * found->along + perpendicular(f) * CityLayout::laneOffset(found->lane),
                   w.cars[slot].body.halfH))
            continue;
        waiting[kind].pop_back();
        open[slot] = false;
        seat(slot, *found, clamp(1 - capturedNumber(state, "Damage"), .05, 1.),
             kind <= 2 ? int(clamp(capturedNumber(state, "Paint"), 0., 12.)) : 0);
        ++seated;
    }
    // The rest of the traffic: where the city put it, unless a saved car stands there now.
    Random elsewhere(w.seed ^ 0x494d5054u);
    for (int slot = 0; slot < int(w.cars.size()); ++slot)
    {
        if (!open[slot])
            continue;
        if (clear(w.cars[slot].body.p, w.cars[slot].body.halfH))
        {
            taken.emplace_back(w.cars[slot].body.p, w.cars[slot].body.halfH);
            continue;
        }
        const int kind = w.cars[slot].kind;
        for (int attempt = 0; attempt < 60; ++attempt)
        {
            const int from = elsewhere.index(World::nodes * World::nodes);
            const auto next = w.neighbours(from);
            if (next.empty())
                continue;
            const int to = next[size_t(elsewhere.index(int(next.size())))];
            if (w.levelEdge(from, to))
                continue;
            const Seat place{from, to, kind == 5 && w.layout.wide() ? 1 : 0,
                             first + elsewhere.next() * (last - first), 0};
            const Vec2 start = World::nodePoint(from), f = (World::nodePoint(to) - start).unit();
            const Vec2 at = start + f * place.along + perpendicular(f) * CityLayout::laneOffset(place.lane);
            if (!clear(at, w.cars[slot].body.halfH) || (at - w.position()).length2() < 60 * 60)
                continue;
            seat(slot, place, w.cars[slot].health, w.cars[slot].paint);
            break;
        }
    }
    return seated;
}
// Passers-by of a save made on the same street plan stand where they stood: each saved
// walker on free ground off the carriageway takes an ordinary native passer-by.
int placeWalkers(World &w, const json &saved)
{
    std::vector<int> ordinary;
    for (int i = int(w.people.size()) - 1; i >= 0; --i)
    {
        const auto &p = w.people[i];
        if (p.riding < 0 && p.waitingLine < 0 && !p.courier && !p.inCompany && p.ownedScooter < 0 &&
            !p.dead && p.role == Person::Role::none && !p.body.roadLevel && !w.personEngaged(i))
            ordinary.push_back(i);
    }
    int placed = 0;
    for (const json &actor : saved)
    {
        if (ordinary.empty())
            break;
        const json &state = part(actor, "State");
        if (capturedFlag(state, "Dead") || capturedFlag(state, "Detached") ||
            capturedNumber(state, "KnockedFor") > 0 || capturedNumber(state, "RoadLevel") != 0)
            continue;
        const auto place = pose(actor);
        if (!place)
            continue;
        const Vec2 at = place->at;
        const double centre = w.terrain.height(at) + .85;
        if (!w.layout.urbanSite(at, .5) || w.openCut(at, .4) || w.levelAt(at, 2) ||
            w.layout.roadDistance(at) < w.layout.roadHalf() + .3 ||
            !w.vehicleSpace(at, centre, nullptr, true))
            continue;
        auto &p = w.people[size_t(ordinary.back())];
        ordinary.pop_back();
        w.resumeWalk(p, at, 0);
        p.boardAfter = 0;
        p.heading = std::atan2(p.target.x - at.x, p.target.z - at.z);
        ++placed;
    }
    return placed;
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
// the city seed, clock, weather, money, health and weapons. The city is peopled as a new
// game would be. The source file is only read.
ManagedImport World::importManaged(const std::filesystem::path &file, Density population)
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
                  report.samePlan ? 0 : CityLayout::districtRevision, population);
    next.money = int(cash);
    next.clock.setHour(hour);
    const bool raining = document.value("Raining", false);
    next.setWeather(raining ? 1 : 0);
    next.weatherClock = 90;

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
            ammo[std::to_underlying(id)] = count;
            owned[std::to_underlying(id)] = true;
            next.inventory.restore(ammo, owned);
            ++report.weapons;
        }
    if (const auto selected = managed.find("SelectedWeapon"); selected != managed.end())
        if (const auto id = weaponId(*selected); id != WeaponId::none)
            next.inventory.select(id);

    // What the managed CityTraffic keeps of the player's career (World.Clock).
    const json &career = part(managed, "Clock");
    next.wanted = clamp(capturedNumber(career, "Wanted"), 0., 6.);
    next.jobsCompleted = int(clamp(capturedNumber(career, "JobsCompleted"), 0., 1e7));
    next.jobsFailed = int(clamp(capturedNumber(career, "JobsFailed"), 0., 1e7));
    next.lastPay = int(clamp(capturedNumber(career, "LastJobPay"), 0., 1e5));
    next.seriesStage = int(clamp(capturedNumber(career, "ChainStage"), 0., 2.));
    next.seriesCompleted = int(clamp(capturedNumber(career, "ChainsCompleted"), 0., 1e6));
    // A native save holds a career only if its parts agree with each other.
    next.seriesStage = std::min(next.seriesStage, next.jobsCompleted);
    next.seriesCompleted = std::min(next.seriesCompleted, (next.jobsCompleted - next.seriesStage) / 3);
    next.jobEarnings =
        std::int64_t(clamp(capturedNumber(career, "JobEarnings"), 0., double(next.jobsCompleted) * 100000));
    next.energySeconds = clamp(capturedNumber(career, "EnergySeconds"), 0., 25.);

    // The car the player sat in, if any: the first of the saved cars.
    const auto savedCars = managed.find("Cars");
    const json *ride = nullptr;
    if (const auto *onFoot = captured(career, "OnFoot");
        onFoot && onFoot->is_boolean() && !onFoot->get<bool>() && !capturedFlag(career, "PassengerMode") &&
        savedCars != managed.end() && savedCars->is_array() && !savedCars->empty())
    {
        const json &state = part(savedCars->front(), "State");
        if (capturedFlag(state, "Hero") && !capturedFlag(state, "Destroyed") &&
            !capturedFlag(state, "Falling"))
            ride = &savedCars->front();
    }
    report.driving = ride != nullptr;
    auto &own = next.cars[next.controlledCar];
    if (ride)
    {
        const json &state = part(*ride, "State");
        // The same kind of vehicle where the native fleet has it and there is room for it.
        if (const auto model = modelOfKind(int(clamp(capturedNumber(state, "Kind"), -1., 99.)));
            model && fleet[*model].kind != own.kind)
        {
            const Car before = own;
            fit_vehicle(own, *model);
            const double reach = std::hypot(own.body.halfW, own.body.halfH) + .6;
            if (std::ranges::any_of(next.cars,
                                    [&](const Car &other)
                                    {
                                        return &other != &own &&
                                               (other.body.p - own.body.p).length() <
                                                   reach + std::hypot(other.body.halfW, other.body.halfH);
                                    }))
                own = before;
        }
        own.health = clamp(1 - capturedNumber(state, "Damage"), .05, 1.);
        own.paint = own.kind <= 2 ? int(clamp(capturedNumber(state, "Paint"), 0., 12.)) : 0;
        report.carKept = true;
    }

    // Saved positions are meaningful only on the identical street plan.
    const Vec2 saved{x, z};
    const double heading = document.value("Heading", 0.);
    // Body heights are measured at the centre: .85 m above the feet for a walker.
    const double floorHeight = next.terrain.height(saved), centre = floorHeight + .85;
    auto freeGround = [&](Vec2 at, double middle)
    {
        return next.layout.urbanSite(at, .5) && !next.levelAt(at, 2) &&
               next.terrain.height(at) > CityLayout::waterLevel + .3 && !next.insideBuilding(at, middle, .5);
    };
    if (ride && report.samePlan)
    {
        // Back in the driving seat where the save was made, if that is a street here.
        const auto place = pose(*ride);
        const Vec2 at = place ? place->at : saved;
        if (freeGround(at, next.terrain.height(at) + .8) &&
            next.layout.roadDistance(at) < next.layout.roadHalf() - own.body.halfW &&
            std::ranges::none_of(next.cars, [&](const Car &other)
                                 { return &other != &own && (other.body.p - at).length2() < 64; }))
        {
            own.body.p = at;
            own.body.angle = place ? -place->heading : (std::isfinite(heading) ? -heading : 0);
            own.body.v = {};
            own.body.w = 0;
            own.body.roadLevel = 0;
            own.body.height = next.surface(at, 0) + .8;
            own.body.wake();
            report.positionKept = true;
        }
    }
    else if (!ride)
    {
        // On foot: where the save was made on the same plan, otherwise beside the car a new
        // game starts in.
        std::optional<Vec2> stand;
        if (report.samePlan && freeGround(saved, centre) && next.vehicleSpace(saved, centre, nullptr, false))
        {
            stand = saved;
            report.positionKept = true;
        }
        else if (const auto *onFoot = captured(career, "OnFoot");
                 onFoot && onFoot->is_boolean() && onFoot->get<bool>())
            stand = next.vehicleExit(own);
        if (stand)
        {
            own.driverless = true;
            next.controlledCar = -1;
            next.player.p = *stand;
            next.player.v = {};
            next.player.roadLevel = 0;
            next.player.height = next.terrain.height(*stand) + .85;
            next.player.angle = report.positionKept && std::isfinite(heading) ? -heading : own.body.angle;
            next.player.wake();
        }
    }
    if (report.samePlan)
    {
        if (savedCars != managed.end() && savedCars->is_array())
            report.cars = seatCars(next, *savedCars);
        if (const auto walkers = managed.find("People"); walkers != managed.end() && walkers->is_array())
            report.people = placeWalkers(next, *walkers);
    }
    report.seed = next.seed;
    *this = std::move(next);
    return report;
}
} // namespace Promzona
