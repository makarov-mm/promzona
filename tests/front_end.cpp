#include "promzona/front_end.hpp"
#include "promzona/world.hpp"
#include <chrono>
#include <fstream>
#include <iostream>
#include <stdexcept>

namespace fs = std::filesystem;
namespace
{
void require(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
}
struct TestProfile
{
    fs::path dir =
        fs::temp_directory_path() /
        ("promzona-slot-test-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    TestProfile() { require(fs::create_directory(dir), "create isolated profile"); }
    ~TestProfile()
    {
        std::error_code ec;
        fs::remove_all(dir, ec);
    }
};
} // namespace

int main(int argc, char **argv)
{
    try
    {
        TestProfile profile;
        pz::SaveSlots slots(profile.dir / "save.json");
        require(slots.path(1).filename() == "save.json" && slots.path(2).filename() == "save-2.json" &&
                    slots.path(3).filename() == "save-3.json",
                "native slot names");
        auto prefs = profile.dir / "graphics.txt";
        {
            std::ofstream out(prefs);
            out << "9999 -4 0 1";
        }
        auto settings = pz::GraphicsPreferences::load(prefs);
        require(settings.distance == 3000 && settings.quality == 0 && !settings.effects && settings.bloom,
                "clamp out-of-range settings");
        {
            std::ofstream out(prefs);
            out << "1000 broken";
        }
        require(pz::GraphicsPreferences::load(prefs).distance == 1000 &&
                    pz::GraphicsPreferences::load(prefs).quality == 2,
                "defaults for incomplete settings");
        require(settings.save(prefs) && !pz::GraphicsPreferences::load(prefs).effects, "save settings");

        pz::World live;
        if (argc == 2)
        {
            fs::copy_file(argv[1], slots.path(1));
            require(slots.occupied(1) && !slots.occupied(2), "existing native game is in slot one");
            live.load(slots.path(1));
            std::cout << "Loaded existing native save, seed=" << live.seed << " cars=" << live.cars.size()
                      << '\n';
        }
        else
            live.generate(20261105);

        live.money = 120;
        slots.write(live, 1);
        live.money = 240;
        slots.write(live, 2);
        require(slots.occupied(2) && !slots.occupied(3), "pending writes reserve only their own slot");
        slots.finish();
        pz::World restored;
        restored.load(slots.path(1));
        require(restored.money == 120, "snapshot outlives mutations on game thread");
        restored.load(slots.path(2));
        require(restored.money == 240, "second save captured independently");

        auto temporary = slots.path(2);
        temporary += ".tmp";
        fs::create_directory(temporary);
        live.money = 480;
        slots.write(live, 2);
        bool failed = false;
        try
        {
            slots.finish();
        }
        catch (const std::exception &)
        {
            failed = true;
        }
        require(failed, "asynchronous storage errors reach game thread");
        restored.load(slots.path(2));
        require(restored.money == 240, "interrupted write preserved the previous save");
        fs::remove(temporary);
        slots.write(live, 2);
        slots.finish();
        auto backup = slots.path(2);
        backup += ".bak";
        restored.load(backup);
        require(restored.money == 240, "backup contains last complete save");
        restored.load(slots.path(2));
        require(restored.money == 480, "retry after storage failure");
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
    return 0;
}
