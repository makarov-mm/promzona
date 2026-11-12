#include "promzona/world.hpp"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>

using namespace pz;
namespace
{
void require(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
}
// A save as System.Text.Json writes it for the managed game: PascalCase members, enum
// dictionary keys as names, enum values as numbers, actor state as loose dictionaries.
struct ManagedSave
{
    int format{3}, seed{20240519}, money{740}, revision{4}, nodes{49}, selected{4};
    double x{}, z{}, heading{}, hour{21.5}, damage{.4};
    bool raining{true}, wide{true}, world{true};
    std::string ammunition{R"({"Pistol":24,"Bazooka":0,"Flamethrower":120})"};

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
          << R"(,"TransitRiders":[],"Clock":{"Hour":)" << hour << R"(},"Weather":{"Rain":0.5})"
          << R"(,"Cars":[{"State":{"Kind":2,"Money":999999},"Body":{"X":1,"Z":2,"Angle":0,"Vx":0,"Vz":0,"W":0},)"
          << R"("Path":[{"X":1,"Z":2},{"X":3,"Z":4}],"Bezier":true}],"People":[{"State":{"GunDamage":1},"Body":null}])"
          << R"(,"Animals":[],"Props":[],"Marks":[],"Cash":[],"Pickups":[],"Ammunition":)" << ammunition
          << R"(,"SelectedWeapon":)" << selected
          << R"(,"Services":[],"Groups":[],"Incidents":[],"Reservations":[)";
        for (int i = 0; i < nodes * nodes; ++i)
            s << (i ? ",0" : "0");
        s << R"(],"PedPlaces":[1,2,3]}})";
        return s.str();
    }
};
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
                    w.inventory.ammo(WeaponId::automatic) == Inventory::ammoLimit &&
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
                    !w.inventory.hasWeapons() && w.rain == 0,
                "format 1 save was not imported");

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
        std::cout << "managed import tests passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
