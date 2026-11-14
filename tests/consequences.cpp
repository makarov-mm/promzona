#include "promzona/world.hpp"
#include <chrono>
#include <filesystem>
#include <print>
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
constexpr double frame = 1. / 60;
// An ordinary walker: not riding, waiting, delivering or walking with friends.
size_t plainWalker(const World &w, size_t skip = size_t(-1))
{
    for (size_t i = 0; i < w.people.size(); ++i)
    {
        const auto &p = w.people[i];
        if (i != skip && p.riding < 0 && p.waitingLine < 0 && !p.courier && !p.inCompany &&
            p.ownedScooter < 0 && !p.dead && p.sidearm == WeaponId::none && i != size_t(w.patrol.officer) &&
            !w.paramedic_call(int(i)))
            return i;
    }
    throw std::runtime_error("no plain walker in the city");
}
// Move the walker onto the carriageway straight ahead of the player's car.
Person &victimAhead(World &w, size_t index, double distance)
{
    auto &car = w.cars.at(w.controlledCar);
    auto &p = w.people.at(index);
    p.body.p = car.body.p + forward(car.heading()) * (car.body.halfH + distance);
    p.body.v = {};
    p.body.roadLevel = 0;
    p.body.height = w.terrain.height(p.body.p) + .85;
    p.target = p.body.p;
    return p;
}
void clearStreet(World &w, size_t keep, size_t alsoKeep = size_t(-1))
{
    // Keep other actors out of the scenario without changing the generated city.
    const auto centre = w.position();
    for (size_t i = 0; i < w.people.size(); ++i)
        if (i != keep && i != alsoKeep && w.people[i].riding < 0 &&
            (w.people[i].body.p - centre).length2() < 60 * 60)
            w.people[i].body.p += Vec2{400, 0};
    for (size_t i = 1; i < w.cars.size(); ++i)
        if (w.cars[i].line < 0 && (w.cars[i].body.p - centre).length2() < 60 * 60)
            w.cars[i].body.p += Vec2{0, 400};
}
} // namespace

int main()
{
    try
    {
        const std::uint32_t seed = 20261105u;
        {
            World w;
            w.generate(seed);
            int bazookas = 0, flamethrowers = 0, pills = 0, energy = 0;
            for (const auto &p : w.pickups)
            {
                bazookas += p.weapon == WeaponId::bazooka;
                flamethrowers += p.weapon == WeaponId::flamethrower;
                pills += p.health;
                energy += p.energy;
            }
            require(bazookas == 2 && flamethrowers == 2,
                    "two rare launchers and two flamethrowers on the map");
            require(pills >= 48 && energy == 48, "medicine and energy drinks populate the map");
        }
        {
            // A slow contact knocks the walker down; the survivor limps where it fell.
            World w;
            w.generate(seed);
            const size_t walker = plainWalker(w);
            clearStreet(w, walker);
            auto &car = w.cars[0];
            auto &p = victimAhead(w, walker, 1.2);
            car.body.v = forward(car.heading()) * 3.2;
            PlayerInput idle;
            for (int i = 0; i < 90 && p.knockedUntil <= w.time; ++i)
                w.step(idle, frame);
            require(!p.dead && p.knockedUntil > w.time, "slow impact knocks the walker down");
            require(p.injuredUntil > p.knockedUntil + 40 && p.health <= .55, "survivor is injured");
            require(w.pedestrianImpacts == 1 && w.wanted > 0 && w.wanted < .5,
                    "a knock-down is a minor crime");
            const auto fell = p.injuryHome;
            car.body.v = {};
            while (p.knockedUntil > w.time)
                w.step({.car = {0, 0, true, true}}, frame);
            double fastest = 0;
            for (int i = 0; i < 600; ++i)
            {
                w.step({.car = {0, 0, true, true}}, frame);
                fastest = std::max(fastest, p.body.v.length());
            }
            require(!p.dead && fastest < 1.2 && (p.body.p - fell).length() < 6,
                    "survivor limps about the spot");
            const auto file = std::filesystem::temp_directory_path() /
                              std::format("promzona-injury-{}.json",
                                          std::chrono::steady_clock::now().time_since_epoch().count());
            w.save(file);
            World restored;
            restored.load(file);
            std::filesystem::remove(file);
            require(restored.people[walker].injuredUntil == p.injuredUntil &&
                        (restored.people[walker].injuryHome - fell).length() < 1e-9,
                    "injury survives saving");
        }
        {
            // At speed the same contact is fatal and counts as a serious crime.
            World w;
            w.generate(seed);
            const size_t walker = plainWalker(w);
            clearStreet(w, walker);
            auto &car = w.cars[0];
            auto &p = victimAhead(w, walker, 2.5);
            car.body.v = forward(car.heading()) * 13;
            PlayerInput drive;
            drive.car.throttle = 1;
            for (int i = 0; i < 90 && !p.dead; ++i)
                w.step(drive, frame);
            require(p.dead && p.health == 0, "fast impact is fatal");
            // The victim is the only possible witness and it is dead: nothing is reported.
            require(w.wanted == 0, "an unseen kill raises no wanted level");
        }
        {
            World w;
            w.generate(seed);
            const size_t walker = plainWalker(w), bystander = plainWalker(w, walker);
            clearStreet(w, walker, bystander);
            auto &car = w.cars[0];
            auto &p = victimAhead(w, walker, 2.5);
            auto &witness = w.people.at(bystander);
            witness.body.p = p.body.p + perpendicular(forward(car.heading())) * 9;
            witness.body.height = w.terrain.height(witness.body.p) + .85;
            witness.body.roadLevel = 0;
            car.body.v = forward(car.heading()) * 13;
            PlayerInput drive;
            drive.car.throttle = 1;
            for (int i = 0; i < 90 && !p.dead; ++i)
                w.step(drive, frame);
            require(p.dead && w.wanted > .5, "a witnessed kill is a serious crime");
        }
        {
            // The player on foot: a slow car hurts, a fast one kills, god mode protects.
            World w;
            w.generate(seed);
            w.enter_exit();
            require(w.controlledCar < 0, "player is on foot");
            const auto &car = w.cars[0];
            w.strike_player(car, 4);
            require(w.health < 1 && w.health > .5 && w.playerKnockedUntil > w.time,
                    "slow car knocks the player down");
            const double hurt = w.health;
            w.strike_player(car, 4);
            require(w.health == hurt, "no second hit while lying");
            w.playerKnockedUntil = 0;
            w.godMode = true;
            w.strike_player(car, 30);
            w.hurtPlayer(.9);
            require(w.health == hurt && w.playerKnockedUntil == 0, "god mode ignores damage");
            w.godMode = false;
            w.strike_player(car, 14);
            require(w.health == 0, "fast car is fatal on foot");
        }
        {
            // God mode also protects the driven vehicle; console spelling as in the managed game.
            World w;
            w.generate(seed);
            require(w.command("god on") == L"OK" && w.godMode, "god on");
            w.damageCar(w.cars[0], .4, true);
            require(w.cars[0].health == 1, "driven vehicle is protected");
            w.damageCar(w.cars[1], .4, true);
            require(w.cars[1].health < 1, "other vehicles still take damage");
            require(w.command("god off") == L"OK" && !w.godMode, "god off");
            w.damageCar(w.cars[0], .4, true);
            require(w.cars[0].health < 1, "protection ends with god mode");
            bool rejected = false;
            try
            {
                w.command("god maybe");
            }
            catch (const std::invalid_argument &)
            {
                rejected = true;
            }
            require(rejected, "god needs on/off");

            const size_t before = w.pickups.size();
            require(w.command("spawn pills") == L"OK" && w.command("spawn energy") == L"OK",
                    "spawn supplies");
            require(w.pickups.size() == before + 2 && w.pickups[before].health &&
                        w.pickups[before + 1].energy,
                    "supplies appear as pickups");
            require((w.pickups[before].p - w.position()).length() < 46, "supplies appear near the player");
            w.enter_exit();
            w.health = .4;
            w.player.p = w.pickups[before].p;
            w.player.height = w.terrain.height(w.player.p) + .85;
            for (int i = 0; i < 120 && w.health < .7; ++i)
                w.step({}, frame);
            require(std::abs(w.health - .75) < 1e-9, "pills restore 35% of health");
        }
        {
            // The wanted level holds for 12 s, then fades; five times faster once the trail is cold.
            World w;
            w.generate(seed);
            w.addCrime(1.2);
            // No units are sent: the suspect stays out of the militia's sight.
            w.dispatchAt = w.patrolScanAt = 1e9;
            const double initial = w.wanted;
            require(initial > 1 && w.wantedLevel() == 2, "crime points reach the second level");
            PlayerInput idle;
            for (int i = 0; i < 60 * 11; ++i)
                w.step(idle, frame);
            require(w.wanted == initial, "no fade during the first twelve seconds");
            for (int i = 0; i < 60 * 10; ++i)
                w.step(idle, frame);
            const double slow = initial - w.wanted;
            require(slow > .1 && slow < .25, "slow fade while the crime is recent");
            // 21 s have passed: four more slow seconds, then eight at the fast rate.
            const double before = w.wanted;
            for (int i = 0; i < 60 * 12; ++i)
                w.step(idle, frame);
            require(before - w.wanted > .75, "fast fade once out of sight");
        }
        {
            // L lands a piloted helicopter; ascending cancels the assistance.
            World w;
            w.generate(seed);
            auto &heli = w.cars[0];
            heli.kind = 12;
            w.toggleHelicopterLanding();
            require(!w.helicopterLanding, "nothing to land while on the ground");
            PlayerInput climb;
            climb.collective = 1;
            for (int i = 0; i < 60 * 6; ++i)
                w.step(climb, frame);
            require(heli.lift > 15, "helicopter climbed");
            w.toggleHelicopterLanding();
            require(w.helicopterLanding, "assisted landing engaged");
            PlayerInput hands;
            hands.car.throttle = 1; // ignored while the assistance flies the descent
            const auto over = heli.body.p;
            double fastest = 0;
            for (int i = 0; i < 60 * 40 && w.helicopterLanding; ++i)
            {
                w.step(hands, frame);
                fastest = std::min(fastest, heli.verticalVelocity);
            }
            require(!w.helicopterLanding && heli.lift < .06, "helicopter landed and released the controls");
            require(fastest > -2.3 && (heli.body.p - over).length() < 12,
                    "gentle descent without drifting away");
            for (int i = 0; i < 60 * 4; ++i)
                w.step(climb, frame);
            w.toggleHelicopterLanding();
            w.step(climb, frame);
            require(!w.helicopterLanding, "ascending cancels the assisted landing");
        }
        std::println("Consequence tests passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "Consequence test failed: {}", e.what());
        return 1;
    }
}
