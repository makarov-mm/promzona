#include "promzona/world.hpp"
#include <filesystem>
#include <fstream>
#include <print>
#include <sstream>
#include <stdexcept>
#include <source_location>
#include <format>

using namespace Promzona;
namespace
{
void require(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
// A save as System.Text.Json writes it for the managed game: PascalCase members, enum
// dictionary keys as names, enum values as numbers, actor state as loose dictionaries.
struct ManagedSave
{
    int format{3}, seed{20240519}, money{740}, revision{4}, nodes{49}, selected{4};
    double x{}, z{}, heading{}, hour{21.5}, damage{.4};
    bool raining{true}, wide{true}, world{true};
    std::string ammunition{R"({"Pistol":24,"Bazooka":0,"Flamethrower":120})"};
    // More members of World.Clock, and the arrays of cars and passers-by.
    std::string clock;
    std::string cars{
        R"([{"State":{"Kind":2,"Money":999999},"Body":{"X":1,"Z":2,"Angle":0,"Vx":0,"Vz":0,"W":0},)"
        R"("Path":[{"X":1,"Z":2},{"X":3,"Z":4}],"Bezier":true}])"};
    std::string people{R"([{"State":{"GunDamage":1},"Body":null}])"};

    std::string text() const
    {
        std::ostringstream s;
        s.precision(17);
        s << R"({"Format":)" << format << R"(,"Seed":)" << seed << R"(,"X":)" << x << R"(,"Z":)" << z
          << R"(,"Heading":)" << heading << R"(,"Hour":)" << hour << R"(,"Money":)" << money
          << R"(,"Raining":)" << (raining ? "true" : "false") << R"(,"World":)";
        if (!world)
            return s.str() + "null}";
        s << R"({"WideRoads":)" << (wide ? "true" : "false") << R"(,"RoadRevision":)" << revision
          << R"(,"TransitRevision":1,"ScooterRevision":1,"Scooters":[{"State":{"Kind":9},"Body":null}])"
          << R"(,"Player":{"State":{"IsPlayer":true,"GunDamage":)" << damage
          << R"(,"Cruise":1.4},"Body":{"X":)" << x << R"(,"Z":)" << z
          << R"(,"Angle":0,"Vx":0,"Vz":0,"W":0},"Path":null,"Bezier":false})"
          << R"(,"TransitRiders":[],"Clock":{"Hour":)" << hour << clock << R"(},"Weather":{"Rain":0.5})"
          << R"(,"Cars":)" << cars << R"(,"People":)" << people
          << R"(,"Animals":[],"Props":[],"Marks":[],"Cash":[],"Pickups":[],"Ammunition":)" << ammunition
          << R"(,"SelectedWeapon":)" << selected
          << R"(,"Services":[],"Groups":[],"Incidents":[],"Reservations":[)";
        for (int i = 0; i < nodes * nodes; ++i)
            s << (i ? ",0" : "0");
        s << R"(],"PedPlaces":[1,2,3]}})";
        return s.str();
    }
};
// A member of a captured state as System.Text.Json writes the backing field of an
// auto-property: with the angle brackets escaped.
std::string backing(const char *name)
{
    return std::string(R"("\u003C)") + name + R"(\u003Ek__BackingField")";
}
// One saved car or passer-by.
std::string actor(const std::string &state, Vec2 at, double heading, bool body = true)
{
    std::ostringstream s;
    s.precision(17);
    s << R"({"State":{)" << state;
    if (!body)
        s << (state.empty() ? "" : ",") << backing("Position") << R"(:{"X":)" << at.x << R"(,"Z":)" << at.z
          << "}," << backing("Heading") << ":" << heading << R"(},"Body":null)";
    else
        s << R"(},"Body":{"X":)" << at.x << R"(,"Z":)" << at.z << R"(,"Angle":)" << -heading
          << R"(,"Vx":0,"Vz":0,"W":0})";
    s << R"(,"Path":[{"X":1,"Z":2},{"X":3,"Z":4}],"Bezier":false})";
    return s.str();
}
std::filesystem::path folder;
std::filesystem::path write(const std::string &text, const char *name = "managed.json")
{
    const auto file = folder / name;
    std::ofstream(file, std::ios::binary) << text;
    return file;
}
bool rejected(const std::string &text)
{
    World w;
    w.money = 31;
    try
    {
        w.importManaged(write(text, "rejected.json"));
    }
    catch (const std::exception &)
    {
        // A refused import must leave the running game as it was.
        return w.money == 31 && w.cars.empty();
    }
    return false;
}
} // namespace

int main()
{
    try
    {
        folder = std::filesystem::temp_directory_path() / "promzona-managed-import";
        std::filesystem::create_directories(folder);

        // A current managed save: the newest street plan is an adaptation in the native
        // game, so the progress is carried over and the player starts at the usual place.
        ManagedSave current;
        World w;
        auto report = w.importManaged(write(current.text()));
        require(report.format == 3 && report.roadRevision == 4 && !report.samePlan && !report.positionKept,
                "current managed save was not recognised");
        require(w.seed == 20240519u && report.seed == w.seed, "city seed was not carried over");
        require(w.layout.revision() == CityLayout::districtRevision,
                "current save must use the district plan");
        require(w.money == 740, "money was not carried over");
        require(w.density == Density::low && w.people.size() == populationSize(Density::low).people,
                "an import that names no population must get the low one");
        require(std::abs(w.clock.hour - 21.5) < 1e-9, "time of day was not carried over");
        require(w.rain == 1 && w.rainTarget == 1, "weather was not carried over");
        require(std::abs(w.health - .6) < 1e-9, "health was not carried over");
        require(report.weapons == 3 && w.inventory.ammo(WeaponId::pistol) == 24 &&
                    w.inventory.ammo(WeaponId::flamethrower) == 120,
                "ammunition was not carried over");
        require(w.inventory.owns(WeaponId::bazooka) && w.inventory.ammo(WeaponId::bazooka) == 0,
                "an empty weapon must stay owned");
        require(!w.inventory.owns(WeaponId::automatic), "a weapon appeared from nowhere");
        require(w.inventory.active() && w.inventory.active()->id == WeaponId::flamethrower,
                "selected weapon was not carried over");
        require(w.controlledCar == 0 && w.cars.size() >= 260 && w.people.size() == 720,
                "imported city is not a complete native city");

        // The result is an ordinary native save.
        const auto native = folder / "imported.json";
        std::filesystem::remove(native);
        w.save(native);
        World loaded;
        loaded.load(native);
        require(loaded.seed == w.seed && loaded.money == 740 &&
                    loaded.inventory.ammo(WeaponId::pistol) == 24 &&
                    loaded.inventory.owns(WeaponId::bazooka) && std::abs(loaded.health - .6) < 1e-9 &&
                    loaded.layout.revision() == CityLayout::districtRevision,
                "imported progress did not survive a native save");

        // An old revision-0 save of the full-size city has the same street plan in the
        // native game, so the player continues on foot where the save was made.
        World plan;
        plan.generate(4242, {}, 0);
        Vec2 spot{};
        bool found = false;
        for (double dx = 6; dx < 30 && !found; dx += 1.5)
            for (double dz = -12; dz < 12 && !found; dz += 1.5)
            {
                const Vec2 p = plan.position() + Vec2{dx, dz};
                const double centre = plan.terrain.height(p) + .85;
                if (plan.layout.urbanSite(p, 3) && !plan.levelAt(p, 4) &&
                    !plan.insideBuilding(p, centre, 2) && plan.vehicleSpace(p, centre, nullptr, false))
                    spot = p, found = true;
            }
        require(found, "no free ground near the start");
        ManagedSave old;
        old.seed = 4242;
        old.revision = 0;
        old.x = spot.x;
        old.z = spot.z;
        old.heading = .75;
        old.damage = 0;
        old.raining = false;
        old.ammunition = R"({"1":7,"Automatic":123456,"Bazooka":-4,"Laser":50})";
        old.selected = 0;
        report = w.importManaged(write(old.text()));
        require(report.samePlan && report.positionKept && w.layout.revision() == 0,
                "old plan was not recognised");
        require(w.controlledCar == -1 && (w.position() - spot).length() < 1e-9,
                "saved position was not restored");
        require(std::abs(w.player.angle + .75) < 1e-9, "saved heading was not restored");
        require(w.health == 1 && w.rain == 0, "healthy dry save was changed");
        require(w.inventory.ammo(WeaponId::pistol) == 7 &&
                    w.inventory.ammo(WeaponId::automatic) == Inventory::ammo_limit &&
                    w.inventory.owns(WeaponId::bazooka) && w.inventory.ammo(WeaponId::bazooka) == 0 &&
                    report.weapons == 3,
                "ammunition was not normalised");
        for (int i = 0; i < 120; ++i)
            w.step({}, 1. / 60);
        require((w.position() - spot).length() < 1.5 && w.health == 1,
                "restored player did not stand safely");
        std::filesystem::remove(native);
        w.save(native);
        loaded.load(native);
        require(loaded.controlledCar == -1 && loaded.layout.revision() == 0,
                "old plan did not survive a native save");

        // A position that is not free ground in the native city falls back to the usual start.
        const auto &house = plan.buildings.at(plan.buildings.size() / 2);
        old.x = house.p.x;
        old.z = house.p.z;
        report = w.importManaged(write(old.text()));
        require(report.samePlan && !report.positionKept && w.controlledCar == 0,
                "a position inside a building was accepted");

        // The small city of the first managed versions cannot be rebuilt natively.
        ManagedSave small = old;
        small.nodes = 33;
        small.wide = false;
        report = w.importManaged(write(small.text()));
        require(!report.samePlan && !report.positionKept &&
                    w.layout.revision() == CityLayout::districtRevision && w.seed == 4242u,
                "small-city save was not moved to a native city");
        ManagedSave first;
        first.format = 1;
        first.world = false;
        first.seed = -17;
        first.money = 15;
        first.hour = 8;
        first.raining = false;
        report = w.importManaged(write(first.text()));
        require(report.format == 1 && w.money == 15 && w.seed == std::uint32_t(-17) && w.health == 1 &&
                    !w.inventory.has_weapons() && w.rain == 0,
                "format 1 save was not imported");

        // The city of an imported game is peopled as a new game would be.
        report = w.importManaged(write(current.text()), Density::full);
        require(w.density == Density::full && w.people.size() == populationSize(Density::full).people &&
                    w.trafficPopulation == populationSize(Density::full).cars && w.money == 740,
                "an imported game did not get the chosen population");
        const auto dense = folder / "imported-full.json";
        w.save(dense);
        World again;
        again.load(dense);
        require(again.density == Density::full && again.money == 740 &&
                    again.people.size() == w.people.size(),
                "an imported full city does not load");

        // The career and the car the player sat in come along on any street plan.
        {
            ManagedSave driving;
            driving.clock = "," + backing("Wanted") + ":2.4," + backing("JobsCompleted") + ":7," +
                            backing("JobsFailed") + ":1," + backing("JobEarnings") + ":2400," +
                            backing("LastJobPay") + ":350," + backing("ChainStage") + ":1," +
                            backing("ChainsCompleted") + ":2," + backing("EnergySeconds") + ":12.5," +
                            backing("OnFoot") + ":false";
            driving.cars =
                "[" +
                actor(backing("Hero") + ":true," + backing("Kind") + R"(:2,"Damage":0.35,"Paint":3)", {1, 2},
                      0) +
                "]";
            report = w.importManaged(write(driving.text()));
            require(std::abs(w.wanted - 2.4) < 1e-9 && w.jobsCompleted == 7 && w.jobsFailed == 1 &&
                        w.jobEarnings == 2400 && w.lastPay == 350 && w.seriesStage == 1 &&
                        w.seriesCompleted == 2 && std::abs(w.energySeconds - 12.5) < 1e-9,
                    "the career was not carried over");
            require(report.driving && report.carKept && !report.positionKept && w.controlledCar == 0 &&
                        w.cars[0].kind == 2 && std::abs(w.cars[0].health - .65) < 1e-9 &&
                        w.cars[0].paint == 3 && !w.cars[0].driverless,
                    "the player's truck was not carried over");
            for (size_t i = 1; i < w.cars.size(); ++i)
                require((w.cars[i].body.p - w.cars[0].body.p).length() >
                            w.cars[0].body.halfH + w.cars[i].body.halfW,
                        "the truck was put into another car");
            std::filesystem::remove(native);
            w.save(native);
            loaded.load(native);
            require(std::abs(loaded.wanted - 2.4) < 1e-9 && loaded.jobsCompleted == 7 &&
                        loaded.seriesCompleted == 2 && loaded.cars[0].kind == 2 && loaded.cars[0].paint == 3,
                    "the career or the truck did not survive a native save");
            for (int i = 0; i < 120; ++i)
                w.step({}, 1. / 60);
            require(!w.cars[0].destroyed && w.health > 0, "the imported truck did not stand safely");
            // A career whose parts disagree is made whole; a tram is not a car to start in.
            driving.clock = "," + backing("JobsCompleted") + ":7," + backing("ChainStage") + ":1," +
                            backing("ChainsCompleted") + ":9," + backing("JobEarnings") + ":9000000000," +
                            backing("Wanted") + ":40," + backing("OnFoot") + ":false";
            driving.cars = "[" + actor(backing("Hero") + ":true," + backing("Kind") + ":9", {1, 2}, 0) + "]";
            report = w.importManaged(write(driving.text()));
            require(w.seriesCompleted == 2 && w.jobEarnings == 700000 && w.wanted == 6 &&
                        w.cars[0].kind == 0 && report.driving,
                    "an inconsistent career was not made whole");
            std::filesystem::remove(native);
            w.save(native);
            loaded.load(native);
            // A chain cannot be one stage in before any job has been completed.
            driving.clock = "," + backing("JobsCompleted") + ":0," + backing("ChainStage") + ":1";
            w.importManaged(write(driving.text()));
            w.save(native);
            loaded.load(native);
            require(loaded.seriesStage == 0 && loaded.seriesCompleted == 0, "unearned stage after import");

            // On foot in a city with other streets: beside the car a new game starts in.
            driving.clock = "," + backing("OnFoot") + ":true";
            report = w.importManaged(write(driving.text()));
            require(!report.driving && !report.positionKept && w.controlledCar == -1 &&
                        w.cars[0].driverless && (w.position() - w.cars[0].body.p).length() < 4 &&
                        w.cars[0].kind == 0,
                    "a player on foot was left in the car");
            std::filesystem::remove(native);
            w.save(native);
            loaded.load(native);
            require(loaded.controlledCar == -1, "a player on foot did not survive a native save");
        }

        // On the old street plan the traffic and the passers-by stand where they stood.
        {
            // Places of the native city of that seed: two stretches of street with nobody
            // on them, and a free piece of pavement.
            struct Lane
            {
                Vec2 at;
                double heading{};
            };
            std::vector<Lane> lanes;
            auto empty = [&](Vec2 at, double reach)
            {
                return std::ranges::none_of(plan.cars,
                                            [&](const Car &c) { return (c.body.p - at).length() < reach; }) &&
                       std::ranges::none_of(lanes,
                                            [&](const Lane &l) { return (l.at - at).length() < 60; }) &&
                       (plan.position() - at).length() > 90;
            };
            for (int from = 0; from < World::nodes * World::nodes && lanes.size() < 3; ++from)
                for (int to : plan.neighbours(from))
                {
                    if (plan.levelEdge(from, to) || lanes.size() == 3)
                        continue;
                    const Vec2 f = (World::nodePoint(to) - World::nodePoint(from)).unit();
                    // The second place is in the kerb lane, for a bus.
                    const Vec2 at = World::nodePoint(from) + f * 20 +
                                    perpendicular(f) * CityLayout::laneOffset(lanes.size() == 1 ? 1 : 0);
                    if (plan.layout.urbanSite(at, 2) && !plan.levelAt(at, 20) && empty(at, 25))
                        lanes.push_back({at, std::atan2(f.x, f.z)});
                }
            require(lanes.size() == 3 && plan.layout.wide(), "no empty streets in the old plan");
            Vec2 pavement{};
            const auto &block = plan.buildings.at(plan.buildings.size() / 3);
            bool free = false;
            for (int node = 0; node < World::nodes * World::nodes && !free; ++node)
            {
                pavement = World::corner(node, 1, 1) + Vec2{0, 7};
                const double centre = plan.terrain.height(pavement) + .85;
                free = plan.layout.urbanSite(pavement, 2) && !plan.levelAt(pavement, 6) &&
                       !plan.openCut(pavement, 1) &&
                       plan.layout.roadDistance(pavement) > plan.layout.roadHalf() + .5 &&
                       !plan.insideBuilding(pavement, centre, .6) &&
                       plan.vehicleSpace(pavement, centre, nullptr, true) &&
                       // Out of reach of the player's car: nobody there is asked to hail a taxi.
                       (lanes[2].at - pavement).length() > 400;
            }
            require(free, "no free pavement in the old plan");
            const Vec2 junction = World::nodePoint(World::nearestNode(lanes[0].at));
            const Vec2 carriageway = lanes[2].at + Vec2{.4, .4};

            ManagedSave town = old;
            town.x = spot.x;
            town.z = spot.z;
            town.clock = "," + backing("OnFoot") + ":false";
            const std::string sedan = backing("Kind") + R"(:0,"Damage":0.25,"Paint":5)";
            town.cars = "[" + actor(backing("Hero") + ":true", lanes[2].at, lanes[2].heading) + "," +
                        actor(sedan, lanes[0].at + Vec2{.3, -.2}, lanes[0].heading + .05) + "," +
                        // The same place again, a tram, a wreck and a car in a junction: none is seated.
                        actor(sedan, lanes[0].at, lanes[0].heading) + "," +
                        actor(backing("Kind") + R"(:9,"TransitRoute":1)", lanes[1].at, lanes[1].heading) +
                        "," +
                        actor(backing("Kind") + R"(:0,"Destroyed":true)", lanes[1].at, lanes[1].heading) +
                        "," + actor(backing("Kind") + ":0", junction, 0) + "," +
                        // A kind no car has, and one no number of a kind can be.
                        actor(backing("Kind") + ":47", lanes[1].at, lanes[1].heading) + "," +
                        actor(backing("Kind") + ":1e300", lanes[1].at, lanes[1].heading) + "," +
                        // A bus saved without a body: its captured position and heading are used.
                        actor(backing("Kind") + ":5", lanes[1].at, lanes[1].heading, false) + "]";
            town.people = "[" + actor("", pavement, 0, false) + "," +
                          actor(backing("Dead") + ":true", pavement + Vec2{0, 2}, 0) + "," +
                          actor("", block.p, 0) + "," + actor("", carriageway, 0) + "]";
            report = w.importManaged(write(town.text()));
            require(report.samePlan && report.driving && report.positionKept && report.cars == 2 &&
                        report.people == 1,
                    "the traffic of the old plan was not put back");
            require(w.controlledCar == 0 && (w.cars[0].body.p - lanes[2].at).length() < 1e-9 &&
                        std::abs(w.cars[0].heading() - lanes[2].heading) < 1e-9,
                    "the player's car is not where it was");
            auto seatedAt = [&](const Lane &lane, int kind) -> const Car *
            {
                for (size_t i = 1; i < w.cars.size(); ++i)
                    if ((w.cars[i].body.p - lane.at).length() < .6 && w.cars[i].kind == kind)
                        return &w.cars[i];
                return nullptr;
            };
            const Car *car = seatedAt(lanes[0], 0), *bus = seatedAt(lanes[1], 5);
            require(car && std::abs(car->health - .75) < 1e-9 && car->paint == 5 && car->route &&
                        std::abs(wrap(car->heading() - lanes[0].heading)) < 1e-6 && car->laneIndex == 0,
                    "the saved car does not stand in its lane");
            require(bus && bus->laneIndex == 1 && bus->health == 1 && bus->route,
                    "the saved bus is not at its kerb");
            for (const auto &other : w.cars)
                for (const Car *seated : {car, bus})
                    require(&other == seated || (other.body.p - seated->body.p).length() >= 8,
                            "a car of the city stands in a saved one");
            const auto walker = std::ranges::find_if(w.people, [&](const Person &p)
                                                     { return (p.body.p - pavement).length() < 1e-9; });
            require(walker != w.people.end() && !walker->dead && walker->riding < 0 &&
                        (walker->target - pavement).length() > 1,
                    "the saved passer-by is not on the pavement");
            require(w.people.size() == plan.people.size() && w.cars.size() == plan.cars.size(),
                    "the population of the imported city changed");
            const Vec2 carStart = car->body.p, busStart = bus->body.p;
            const size_t carSlot = size_t(car - w.cars.data()), busSlot = size_t(bus - w.cars.data()),
                         walkerSlot = size_t(walker - w.people.begin());
            std::filesystem::remove(native);
            w.save(native);
            loaded.load(native);
            require((loaded.cars[carSlot].body.p - carStart).length() < 1e-6 &&
                        loaded.cars[carSlot].paint == 5,
                    "the imported traffic did not survive a native save");
            for (int i = 0; i < 60 * 30; ++i)
                w.step({}, 1. / 60);
            require((w.cars[carSlot].body.p - carStart).length() > 5 &&
                        (w.cars[busSlot].body.p - busStart).length() > 5 && !w.cars[carSlot].destroyed &&
                        !w.cars[busSlot].destroyed,
                    "the imported traffic does not drive");
            require((w.people[walkerSlot].body.p - pavement).length() > 2 && !w.people[walkerSlot].dead,
                    "the imported passer-by does not walk");
        }

        // A whole city: the traffic and the passers-by of a full town, saved after it has
        // run for a while, are put back and go on.
        {
            World town;
            town.generate(4242, {}, 0, Density::full);
            town.dispatchAt = town.patrolScanAt = town.nextRescueDispatch = 1e9;
            for (int i = 0; i < 60 * 20; ++i)
                town.step({}, 1. / 60);
            ManagedSave full = old;
            full.x = town.position().x;
            full.z = town.position().z;
            full.clock = "," + backing("OnFoot") + ":false";
            std::string cars =
                "[" + actor(backing("Hero") + ":true", town.cars[0].body.p, town.cars[0].heading());
            int ordinary = 0;
            for (size_t i = 1; i < town.trafficPopulation; ++i)
            {
                const auto &c = town.cars[i];
                cars += "," + actor(backing("Kind") + ":" + std::to_string(c.kind) +
                                        (c.line >= 0 ? R"(,"TransitRoute":1)" : ""),
                                    c.body.p, c.heading(), i % 3 != 0);
                ordinary += c.line < 0 && c.kind != 3 && c.kind != 4 && c.kind <= 8;
            }
            full.cars = cars + "]";
            std::string people = "[";
            int afoot = 0;
            for (const auto &p : town.people)
                if (p.riding < 0 && !p.dead)
                {
                    // Every other one is saved without a body.
                    people += (afoot ? "," : "") + actor("", p.body.p, p.heading, afoot % 2 == 0);
                    ++afoot;
                }
            full.people = people + "]";
            report = w.importManaged(write(full.text()), Density::full);
            std::println("whole city: {} of {} cars and {} of {} passers-by put back", report.cars, ordinary,
                         report.people, afoot);
            require(report.samePlan && report.cars > ordinary / 2 && report.people > afoot / 2,
                    "most of a saved city was not put back");
            // Nothing stands in anything else: a seat is eight metres from the next.
            int close = 0;
            for (size_t a = 1; a < w.trafficPopulation; ++a)
                for (size_t b = a + 1; b < w.trafficPopulation; ++b)
                    close += w.cars[a].line < 0 && w.cars[b].line < 0 && !w.cars[a].body.roadLevel &&
                             !w.cars[b].body.roadLevel &&
                             (w.cars[a].body.p - w.cars[b].body.p).length() <
                                 std::min(w.cars[a].body.halfH + w.cars[b].body.halfH, 4.5);
            require(close == 0, "cars of an imported city stand in each other");
            std::filesystem::remove(native);
            w.save(native);
            loaded.load(native);
            w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
            std::vector<Vec2> before;
            for (const auto &c : w.cars)
                before.push_back(c.body.p);
            for (int i = 0; i < 60 * 40; ++i)
                w.step({}, 1. / 60);
            int moved = 0, wrecked = 0;
            for (size_t i = 1; i < w.trafficPopulation; ++i)
            {
                moved += (w.cars[i].body.p - before[i]).length() > 10;
                wrecked += w.cars[i].destroyed;
            }
            require(moved > int(w.trafficPopulation) * 3 / 4 && wrecked == 0 && w.clearedJams == 0,
                    "an imported city does not drive on");
        }

        // Everything the managed game refuses is refused here too.
        ManagedSave bad;
        bad.format = 4;
        require(rejected(bad.text()), "unknown format accepted");
        bad = {};
        bad.money = -1;
        require(rejected(bad.text()), "negative money accepted");
        bad = {};
        bad.x = 961;
        require(rejected(bad.text()), "position outside the map accepted");
        bad = {};
        bad.world = false;
        require(rejected(bad.text()), "format 3 without a world accepted");
        bad = {};
        bad.revision = 5;
        require(rejected(bad.text()), "unknown road revision accepted");
        bad = {};
        bad.nodes = 40;
        require(rejected(bad.text()), "invalid city size accepted");
        require(rejected("not json"), "garbage accepted");
        require(rejected("[1,2,3]"), "non-object accepted");
        require(rejected(R"({"format":"promzona-cpp-7","seed":5})"), "native save accepted as managed");
        require(
            rejected(R"({"Format":3,"Seed":1.5,"X":0,"Z":0,"Heading":0,"Hour":1,"Money":1,"Raining":false})"),
            "fractional seed accepted");
        World missing;
        bool thrown = false;
        try
        {
            missing.importManaged(folder / "absent.json");
        }
        catch (const std::exception &)
        {
            thrown = true;
        }
        require(thrown, "missing file accepted");
        std::filesystem::remove_all(folder);
        std::println("managed import tests passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "FAILED: {}", e.what());
        return 1;
    }
}
