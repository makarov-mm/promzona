#include "promzona/world.hpp"
#include <print>
#include <limits>
#include <stdexcept>
#include <source_location>
using namespace Promzona;
namespace
{
int checks{}, failures{};
void check(bool value, const char *name, std::source_location where = std::source_location::current())
{
    checks++;
    if (!value)
    {
        failures++;
        std::println(stderr, "FAIL {} (line {})", name, where.line());
    }
}
void near(double value, double expected, const char *name,
          std::source_location where = std::source_location::current())
{
    check(std::abs(value - expected) < 1e-8, name, where);
}
Body box(Vec2 p, Vec2 half, double height = 1, double extent = 2)
{
    Body b;
    b.p = p;
    b.halfW = half.x;
    b.halfH = half.z;
    b.height = height;
    b.verticalExtent = extent;
    return b;
}
Car car(Vec2 p)
{
    Car c;
    c.body = box(p, {.9, 2.1}, .8, 1.6);
    c.body.mass(c.mass);
    return c;
}
Person person(Vec2 p)
{
    Person a;
    a.body = box(p, {.3, .3}, .85, 1.7);
    a.body.circle = true;
    a.body.radius = .33;
    a.body.mass(75);
    a.body.invI = 0;
    a.health = 1;
    return a;
}
World empty()
{
    World w;
    w.controlledCar = -1;
    w.player = person({0, 0}).body;
    w.player.id = 1;
    return w;
}
void advanceCombat(World &w, double seconds)
{
    for (int i = 0; i < int(std::round(seconds * 120)); i++)
    {
        w.time += 1. / 120;
        w.combatStep(1. / 120);
    }
}
} // namespace
int main()
{
    try
    {
        auto b = box({0, 10}, {2, 3});
        near(ray_prism({}, {0, 1}, 1, 0, b, 30), 7, "box front face");
        b.angle = pi * .5;
        near(ray_prism({}, {0, 1}, 1, 0, b, 30), 8, "rotated box, not fixed car radius");
        check(std::isinf(ray_prism({}, {0, 1}, 5, 0, b, 30)), "shot above vehicle misses");
        near(ray_prism({}, {0, 1}, 5, -.4, b, 30), 8, "descending shot enters prism");
        near(ray_prism({0, 10}, {0, 1}, 1, 0, b, 30), 0, "shot inside prism stops immediately");
        check(std::isinf(ray_prism({3.1, 0}, {0, 1}, 1, 0, b, 30)), "parallel slab misses outside box");
        auto p = person({0, 5});
        near(ray_person({}, {0, 1}, 1, 0, p.body, 10), 4.67, "cylinder entry");
        check(std::isinf(ray_person({}, {0, 1}, 4, 0, p.body, 10)), "people have finite vertical extent");
        for (int i = 0; i <= 600; i++)
            near(wanted_from_points(wanted_points(i * .01)), i * .01, "wanted nonlinear mapping roundtrip");
        near(wanted_points(6), 21.9, "all six increasing wanted costs");
        near(wanted_from_points(1.8), 1.5, "overflow crime carries into next tier");
        near(wanted_from_points(9999), 6, "wanted saturates");
        check(police_roster(1).size() == 2 && police_roster(6).size() == 9 && police_roster(6).back() == 12,
              "original police roster includes final air patrol kind");
        auto w = empty();
        {
            auto armed = person({0, -12});
            armed.sidearm = WeaponId::automatic;
            armed.alertUntil = 8;
            w.people.push_back(armed);
            auto &shooter = w.people.front();
            auto aim = [&](int frames)
            {
                for (int n = 0; n < frames; ++n)
                {
                    w.time += 1. / 120;
                    w.civilianCombat(shooter, 1. / 120);
                }
            };
            aim(70);
            check(w.health == 1, "civilian takes time to aim");
            w.staticBodies.push_back(box({0, -5}, {2, .3}, 1, 4));
            aim(40);
            check(w.health == 1 && w.sounds.empty(), "civilian cannot fire through a wall");
            w.staticBodies.clear();
            aim(92);
            check(std::ranges::count(w.sounds, std::string{"automatic"}, &SoundEvent::name) == 3 &&
                      std::ranges::count(w.sounds, std::string{"impact"}, &SoundEvent::name) == 3 &&
                      w.sounds.size() == 6 && w.health < 1,
                  "rifle fires a three-round burst then rests; each round that lands is heard");
            w.killPerson(shooter);
            auto count = w.pickups.size();
            w.killPerson(shooter);
            check(count == w.pickups.size() &&
                      std::ranges::count_if(w.pickups, [](const Pickup &drop)
                                            { return drop.weapon == WeaponId::automatic && drop.dropped; }) ==
                          1,
                  "armed civilian drops a single collectable rifle");
            auto hp = w.health;
            aim(200);
            check(w.health == hp, "dead civilians cannot continue shooting");
        }
        w = empty();
        {
            w.cars.push_back(car({0, 0}));
            w.controlledCar = 0;
            auto previous = w.player.p;
            w.staticBodies.push_back(box({0, 0}, {6, 6}, 1, 4));
            w.enter_exit();
            check(w.controlledCar == 0 && (w.player.p - previous).length() == 0,
                  "blocked doors keep driver and position intact");
            w.staticBodies.clear();
            w.enter_exit();
            check(w.controlledCar < 0 && (w.player.p - previous).length() > 1,
                  "driver exits on a clear side");
        }
        w = empty();
        w.cars.push_back(car({0, 10}));
        w.people.push_back(person({0, 5}));
        auto hit = w.traceAttack({}, {0, 1}, 1.3, 0, 30);
        check(hit.kind == HitKind::person && hit.index == 0, "first person occludes car");
        w.staticBodies.push_back(box({0, 2}, {3, .1}, 2, 4));
        hit = w.traceAttack({}, {0, 1}, 1.3, 0, 30);
        check(hit.kind == HitKind::scenery, "thin wall occludes first person");
        check(!w.blastVisible({}, 1.3, {0, 5}, 1.3), "explosion blocked by solid wall");
        check(w.blastVisible({}, 5, {0, 5}, 5), "above-wall blast ray clear");
        // A ray is tested only against the boxes it can reach. Its answers have to be those
        // of testing every box, whatever the direction, the length and the boxes are.
        w = empty();
        {
            std::uint32_t state = 20261117;
            auto uniform = [&](double low, double high)
            {
                state = state * 1664525u + 1013904223u;
                return low + (high - low) * (state >> 8) * (1. / 16777216.);
            };
            for (int i = 0; i < 400; ++i)
            {
                // One draw after another: the order of evaluating arguments is the compiler's.
                const Vec2 centre{uniform(-80, 80), uniform(-80, 80)}, half{uniform(.05, 9), uniform(.05, 9)};
                const double height = uniform(0, 6), extent = uniform(.5, 8);
                auto wall = box(centre, half, height, extent);
                wall.angle = uniform(-pi, pi);
                w.staticBodies.push_back(wall);
            }
            int different = 0, blocked = 0, struck = 0;
            for (int i = 0; i < 4000; ++i)
            {
                // Level or rising, so that the ground of the empty world stays out of it.
                const Vec2 from{uniform(-90, 90), uniform(-90, 90)};
                const double range = i % 4 ? uniform(.02, 40) : uniform(40, 260), y = uniform(.5, 7),
                             slope = uniform(0, .2);
                // Every seventh direction is not of unit length: the range counts its lengths.
                const Vec2 aim = forward(uniform(-pi, pi)), dir = aim * (i % 7 ? 1. : uniform(.2, 3));
                double nearest = range;
                for (const auto &wall : w.staticBodies)
                    nearest = std::min(nearest, ray_prism(from, dir, y, slope, wall, range));
                const auto shot = w.traceAttack(from, dir, y, slope, range);
                different += shot.distance != nearest || (shot.kind == HitKind::scenery) != (nearest < range);
                struck += nearest < range;
                // The same sums as blastVisible does, so that the comparison is exact.
                const Vec2 to = from + aim * range, delta = to - from;
                const double distance = delta.length(), targetY = y + slope * range;
                const Vec2 unit = delta / distance;
                const double rise = (targetY - y) / distance;
                bool clear = true;
                for (const auto &wall : w.staticBodies)
                    clear = clear && !(ray_prism(from, unit, y, rise, wall, distance) < distance - .01);
                different += w.blastVisible(from, y, to, targetY) != clear;
                blocked += !clear;
            }
            check(!different,
                  "rays that pass over the boxes out of their reach answer as rays tested on every box");
            check(blocked > 400 && blocked < 3600 && struck > 400 && struck < 3600,
                  "the rays of the comparison both hit and miss");
        }
        // The sight of a vehicle's gun stands where a shot along the look heading would land.
        w = empty();
        {
            check(!w.gun_sight(0), "nobody on foot has a gun sight");
            w.cars.push_back(car({0, 0}));
            w.controlledCar = 0;
            check(!w.gun_sight(0), "an ordinary car has no gun sight");
            w.cars[0].kind = 14;
            const double muzzle = w.cars[0].body.height + 1.45, reach = weapon(WeaponId::automatic)->range;
            auto sight = w.gun_sight(0);
            check(sight && std::abs(sight->x) < 1e-3 && std::abs(sight->y - muzzle) < 1e-3 &&
                      std::abs(sight->z - reach) < 1e-3,
                  "with nothing in the way the carrier's sight is at the end of its gun's range");
            w.staticBodies.push_back(box({0, 30}, {5, .5}, 2, 6));
            sight = w.gun_sight(0);
            check(sight && std::abs(sight->z - 29.5) < 1e-3,
                  "the sight stops at the wall the shot would hit");
            sight = w.gun_sight(pi * .5);
            check(sight && std::abs(sight->x - reach) < 1e-3 && std::abs(sight->z) < 1e-3,
                  "the sight follows the heading the gun is laid on");
            w.cars[0].turretPitch = .2;
            sight = w.gun_sight(pi * .5);
            check(sight && std::abs(sight->y - (muzzle + std::tan(.2) * reach)) < 1e-3,
                  "the sight rises with the barrel");
            w.cars[0].kind = 11;
            w.cars[0].turretPitch = 0;
            sight = w.gun_sight(pi);
            check(sight && std::abs(sight->z + weapon(WeaponId::bazooka)->range) < 1e-3,
                  "the tank's sight is at the range of its shell");
            w.cars[0].destroyed = true;
            check(!w.gun_sight(0), "a wreck has no gun sight");
        }
        // The sight of the weapon in the hand stands where its shot lands, and a shot fired
        // along the same look does land there.
        w = empty();
        {
            check(!w.hand_sight(0, 0), "empty hands have no sight");
            w.inventory.give(WeaponId::pistol);
            const double muzzle = w.player.height + .47, reach = weapon(WeaponId::pistol)->range;
            auto sight = w.hand_sight(0, 0);
            check(sight && std::abs(sight->x) < 1e-3 && std::abs(sight->y - muzzle) < 1e-3 &&
                      std::abs(sight->z - reach) < 1e-3,
                  "with nothing in the way the sight is at the end of the weapon's range");
            sight = w.hand_sight(pi * .5, .1);
            check(sight && std::abs(sight->x - reach) < 1e-3 &&
                      std::abs(sight->y - (muzzle + std::tan(.1) * reach)) < 1e-3,
                  "the sight follows the look");
            w.people.push_back(person({.2, 12}));
            sight = w.hand_sight(0, 0);
            check(sight && sight->z > 11.6 && sight->z < 12 && std::abs(sight->x) < 1e-3,
                  "the sight stops on the passer-by the shot would hit");
            w.shoot(0, 0);
            check(w.people[0].health < 1, "the shot lands where the sight stood");
            w.people[0].body.p = {.9, 12};
            sight = w.hand_sight(0, 0);
            check(sight && std::abs(sight->z - reach) < 1e-3, "the sight passes whom the shot would miss");
            w.inventory.give(WeaponId::flamethrower);
            w.inventory.select(WeaponId::flamethrower);
            sight = w.hand_sight(0, .4);
            check(sight && std::abs(sight->y - muzzle) < 1e-3 &&
                      std::abs(sight->z - weapon(WeaponId::flamethrower)->range) < 1e-3,
                  "the sight of the flamethrower stays on its level jet");
            w.cars.push_back(car({0, 0}));
            w.controlledCar = 0;
            check(!w.hand_sight(0, 0), "there is no sight of the hand at the wheel");
            w.controlledCar = -1;
            w.health = 0;
            check(!w.hand_sight(0, 0), "the dead have no sight");
        }
        w = empty();
        w.inventory.give(WeaponId::bazooka);
        w.cars.push_back(car({0, 25}));
        w.shoot(0);
        check(w.rockets.size() == 1 && w.cars[0].health == 1, "rocket launch does not hit instantly");
        check(w.inventory.ammo(WeaponId::bazooka) == 19, "rocket consumes one of twenty");
        advanceCombat(w, .2);
        check(w.rockets.size() == 1 && w.cars[0].health == 1, "rocket travels before impact");
        advanceCombat(w, .5);
        check(w.rockets.empty() && w.cars[0].destroyed, "rocket reaches and destroys target");
        check(w.cars[0].body.invMass > 0, "destroyed target remains physical");
        w = empty();
        w.inventory.give(WeaponId::bazooka);
        w.staticBodies.push_back(box({0, .4}, {2, .05}, 2, 4));
        w.cars.push_back(car({0, 6}));
        w.shoot(0);
        check(w.rockets.empty(), "muzzle never launches through wall");
        check(w.cars[0].health == 1, "wall also shields vehicle from launch impact blast");
        w = empty();
        w.inventory.give(WeaponId::bazooka, 100);
        for (int i = 0; i < 20; i++)
        {
            w.shotCooldown = 0;
            w.shoot(pi);
        }
        check(w.rockets.size() == 16, "rocket collection bounded");
        advanceCombat(w, 4);
        check(w.rockets.empty(), "unhit rockets expire by travel limit");
        w = empty();
        w.player.p = {30, 30};
        w.cars.push_back(car({0, 0}));
        w.cars.push_back(car({0, 2}));
        w.cars.push_back(car({0, 4}));
        w.cars[1].health = w.cars[2].health = .2;
        w.destroyCar(w.cars[0]);
        check(std::ranges::all_of(w.cars, [](auto &c) { return c.destroyed; }),
              "chain explosion destroys neighbours");
        size_t blasts = 0;
        for (auto &s : w.sounds)
            if (s.name == "explosion")
                blasts++;
        check(blasts == 3, "each vehicle explodes exactly once, no recursive cascade");
        w.cars[2].body.impulse({2000, 0});
        check(w.cars[2].body.v.x > 0 && !w.cars[2].body.sleeping, "wreck responds to later impact");
        w = empty();
        w.player.p = {40, 40};
        w.cars.push_back(car({0, 6}));
        w.people.push_back(person({0, 6}));
        w.staticBodies.push_back(box({0, 3}, {6, .1}, 2, 4));
        w.explode({}, 1);
        check(w.cars[0].health == 1 && !w.people[0].dead, "blast does not damage through building");
        w.staticBodies.clear();
        w.cars[0].body.height = 20;
        w.people[0].body.height = 20;
        w.explode({}, 1);
        check(w.cars[0].health == 1 && !w.people[0].dead, "blast respects vertical separation");
        w = empty();
        w.inventory.give(WeaponId::flamethrower);
        w.people.push_back(person({0, 3}));
        w.shoot(0);
        check(w.playerBurnUntil == 0 && w.health == 1, "own flame never directly burns player");
        check(w.people[0].burnUntil == 9, "flame ignites NPC for original nine seconds");
        advanceCombat(w, 7);
        check(w.people[0].dead, "sustained NPC burning is fatal");
        check(w.health == 1, "distant NPC flame does not damage owner");
        w = empty();
        w.cars.push_back(car({0, 2}));
        w.cars[0].fireTime = .001;
        advanceCombat(w, .2);
        check(w.playerBurnUntil > w.time && w.health < 1, "burning vehicle can ignite nearby player");
        w.player.p = {30, 30};
        advanceCombat(w, 7);
        double recovered = w.health;
        advanceCombat(w, 1);
        check(recovered > 0 && w.health == recovered, "player fire expires and can be survived");
        w = empty();
        w.cars.push_back(car({0, 0}));
        w.controlledCar = 0;
        w.cars[0].fireTime = .001;
        w.cars[0].health = .2;
        w.cars[0].body.v = {0, 3};
        advanceCombat(w, 24);
        check(w.cars[0].fireTime == 0 && !w.cars[0].destroyed, "safe player driving extinguishes fire");
        w.damageCar(w.cars[0], .5);
        w.combatStep(1. / 120);
        check(w.cars[0].destroyed, "fresh critical damage still destroys player car");
        w = empty();
        w.cars.push_back(car({0, 20}));
        w.cars[0].fireTime = .001;
        advanceCombat(w, 13);
        check(w.cars[0].destroyed, "NPC vehicle fire explodes after twelve seconds");
        // A burning car the player keeps standing in burns down as any other; stepping out
        // of it leaves the time it had, not a car already past its twelve seconds.
        w = empty();
        w.cars.push_back(car({0, 20}));
        w.controlledCar = 0;
        w.cars[0].fireTime = .001;
        advanceCombat(w, 8);
        check(!w.cars[0].destroyed && w.cars[0].fireTime > 7, "standing player car keeps burning");
        w.controlledCar = -1;
        advanceCombat(w, 1);
        check(!w.cars[0].destroyed, "player car exploded the moment it was left");
        advanceCombat(w, 4);
        check(w.cars[0].destroyed, "abandoned burning car never burned down");
        w = empty();
        w.cars.push_back(car({0, 20}));
        w.vehicleImpact(w.cars[0], 0);
        check(w.cars[0].health == 1, "no velocity change means no collision damage");
        w.vehicleImpact(w.cars[0], 8);
        near(w.cars[0].health, .9, "impact uses original damage and durability factors");
        check(w.cars[0].verticalVelocity > 0, "hard collision lifts the chassis");
        w.vehicleImpact(w.cars[0], 8);
        near(w.cars[0].health, .9, "contact cooldown prevents repeated damage per manifold");
        w.cars[0].impactCooldown = 0;
        w.cars[0].health = .05;
        w.vehicleImpact(w.cars[0], 8);
        check(w.cars[0].destroyed, "fresh collision destroys a critical car immediately");
        w = empty();
        w.reportCrime(1, 45, false);
        check(w.wanted == 0, "unwitnessed crime does not raise wanted");
        w.people.push_back(person({0, 20}));
        w.reportCrime(.5, 45, false);
        near(w.wanted, .5, "audible crime uses original point budget");
        w.staticBodies.push_back(box({0, 10}, {5, .1}, 2, 4));
        w.reportCrime(.5, 45, true);
        near(w.wanted, .5, "unseen quiet crime stays unwitnessed");
        w.reportCrime(.5, 45, false);
        near(w.wanted, 1, "loud crime can be heard behind wall");
    }
    catch (const std::exception &e)
    {
        check(false, e.what());
    }
    std::println("{} combat checks, {} failures", checks, failures);
    return failures ? 1 : 0;
}
