#include "promzona/world.hpp"
#include "promzona/animation.hpp"
#include "promzona/audio.hpp"
#include <fstream>
#include <print>
#include <limits>
#include <set>
#include <sstream>
#include <source_location>
#include <exception>
#include <iterator>

using namespace Promzona;
int failures = 0, checks = 0;
void check(bool condition, const char *name, std::source_location where = std::source_location::current())
{
    ++checks;
    if (!condition)
    {
        ++failures;
        std::println(stderr, "FAIL {} (line {})", name, where.line());
    }
}
void near(double a, double b, double tolerance, const char *name,
          std::source_location where = std::source_location::current())
{
    check(std::isfinite(a) && std::abs(a - b) <= tolerance, name, where);
}
Car vehicle()
{
    Car c;
    c.body.id = 1;
    c.body.mass(c.mass);
    return c;
}
int main(int argc, char **argv)
{
    try
    {
        ActorMotion motion;
        ActorState actor;
        actor.player = true;
        actor.speed = 5.7;
        actor.distance = 2.8;
        actor.aiming = true;
        auto runningShot = motion.sample(actor, 1);
        check(runningShot.to >= 512 && runningShot.to < 576, "run and shoot keeps running legs");
        auto duplicate = motion.sample(actor, 1);
        check(duplicate.from == runningShot.from && duplicate.to == runningShot.to &&
                  duplicate.blend == runningShot.blend,
              "repeated render sample does not advance animation");
        actor.speed = 0;
        auto stopping = motion.sample(actor, 1.05);
        check(stopping.from >= 512 && stopping.to == 192 && stopping.blend < 1,
              "running shot blends into stationary aim");
        actor.dead = true;
        auto fallen = motion.sample(actor, 1.1);
        check(fallen.to < 64 && fallen.blend == 1, "dead actor clears aiming state");
        ActorMotion legacy;
        actor.dead = false;
        actor.speed = 5;
        auto fallback = legacy.sample(actor, 0, 193);
        check(fallback.to >= 128 && fallback.to < 192, "legacy skin uses run instead of invalid gun clip");
        Inventory inv;
        check(!inv.active(), "empty inventory uses fists");
        inv.give(WeaponId::pistol);
        inv.give(WeaponId::automatic);
        inv.give(WeaponId::bazooka);
        check(inv.ammo(WeaponId::bazooka) == 20, "bazooka gives 20 rounds");
        check(inv.active()->id == WeaponId::bazooka, "strongest weapon selected");
        for (int i = 0; i < 20; i++)
            check(inv.consume(WeaponId::bazooka), "consume rocket");
        check(inv.active()->id == WeaponId::automatic, "depletion falls back to automatic");
        check(inv.owns(WeaponId::bazooka) && inv.hasWeapons(), "empty weapon remains owned");
        Inventory full;
        full.give(WeaponId::pistol, 9999);
        check(!full.give(WeaponId::pistol), "reject pickup at ammo cap");
        inv.select(WeaponId::pistol);
        inv.cycle(-1);
        check(!inv.active(), "cycle to fists");
        check(!inv.give(static_cast<WeaponId>(100)), "reject invalid weapon");
        auto input = Input{std::numeric_limits<double>::quiet_NaN(), INFINITY}.sanitized();
        check(input.throttle == 0 && input.steering == 0, "reject nonfinite controls");
        Path path({0, 0}, {0, 10}, {10, 10}, {10, 20});
        auto middle = path.sample(path.length() * .5);
        near(middle.first.x, 5, 1e-10, "Bezier midpoint x");
        near(middle.first.z, 10, 1e-10, "Bezier midpoint z");
        near(path.closestDistance(middle.first), path.length() * .5, 1e-10, "path inverse query");
        auto c = vehicle();
        for (int i = 0; i < 600; i++)
            step_vehicle(c, {1, 0, false, false}, 1. / 120);
        check(c.body.v.z > 15, "engine accelerates");
        double initial = c.body.v.length();
        for (int i = 0; i < 360; i++)
            step_vehicle(c, {0, 0, false, true}, 1. / 120);
        check(c.body.v.length() < initial * .1, "handbrake stops within 3 seconds");
        c.destroy();
        c.body.impulse({5000, 0});
        stepWreck(c, 1. / 120);
        check(c.body.v.x > 4, "wreck retains impulse and finite mass");
        c.lift = 10;
        c.verticalVelocity = -2;
        c.body.v = {5, 4};
        stepWreck(c, .01);
        check(c.body.v.length() > 6.3, "airborne wreck does not acquire ground friction");
        ContactWorld solver;
        Body a, b;
        a.id = 1;
        b.id = 2;
        a.p = {-.8, 0};
        b.p = {.8, 0};
        a.halfW = b.halfW = 1;
        a.halfH = b.halfH = 1;
        a.mass(1000);
        b.mass(2000);
        a.v = {10, 0};
        b.v = {};
        a.friction = b.friction = 0;
        std::array bodies{&a, &b};
        double momentum = a.v.x * 1000;
        solver.step(bodies, .01);
        near(a.v.x * 1000 + b.v.x * 2000, momentum, 1e-8, "collision conserves linear momentum");
        check(b.v.x > 0, "collision transfers momentum");
        a.p = b.p = {0, 0};
        a.height = 0;
        b.height = 10;
        a.v = b.v = {};
        solver.clear();
        solver.step(bodies, .01);
        check(solver.contactCount == 0, "separate road decks do not collide");
        a.height = b.height = 0;
        a.circle = b.circle = true;
        a.radius = b.radius = .3;
        a.p = {0, 0};
        b.p = {.5, 0};
        solver.clear();
        solver.step(bodies, .01);
        check(a.v.x < 0 && b.v.x > 0, "pedestrians separate symmetrically");
        Clock clock;
        clock.set_hour(7);
        near(clock.hour, 7, 1e-10, "new game morning");
        clock.cycleMinutes = 20;
        for (int i = 0; i < 144000; i++)
            clock.step(1. / 120);
        near(clock.hour, 7, 1e-7, "full solar cycle returns to same time");
        World world;
        world.generate(20261105);
        check(world.cars.size() == 260 && world.people.size() == 720, "seeded world populations");
        for (auto &building : world.buildings)
        {
            double h = world.terrain.height(building.p);
            for (int dx : {-1, 1})
                for (int dz : {-1, 1})
                    near(world.terrain.height(building.p + Vec2{building.half.x * dx, building.half.z * dz}),
                         h, .001, "building foundation is level");
        }
        auto before = world.position();
        for (int i = 0; i < 120; i++)
            world.step({{1, 0, false, false}, {}, false, false, 0}, 1. / 120);
        check((world.position() - before).length() > .5, "world integration advances player");
        for (auto &car : world.cars)
            check(std::isfinite(car.body.p.x) && std::isfinite(car.body.p.z) && std::isfinite(car.body.v.x),
                  "traffic remains finite");
        auto save = std::filesystem::current_path() / "promzona-cpp-native-test.json";
        world.money = 432;
        world.inventory.give(WeaponId::bazooka);
        world.inventory.give(WeaponId::pistol);
        world.inventory.select(WeaponId::pistol);
        // Only pedestrians on the street are eligible for these test events.
        const auto firstWalker =
            std::ranges::find_if(world.people, [](const auto &p) { return p.riding < 0; });
        const auto secondWalker = std::ranges::find_if(std::next(firstWalker), world.people.end(),
                                                       [](const auto &p) { return p.riding < 0; });
        const auto victim = firstWalker - world.people.begin();
        const auto witness = secondWalker - world.people.begin();
        world.people[victim].dead = true;
        world.people[victim].health = 0;
        world.people[witness].fear = .75;
        world.people[witness].burnUntil = world.time + 8;
        world.people[witness].burnPlayerCaused = true;
        world.cars[7].fireTime = 3.25;
        world.cars[7].health = .1;
        world.cars[7].lastDamageAt = world.time;
        world.cars[7].impactCooldown = .2;
        world.cars[8].route.reset();
        world.rain = .6;
        world.rainTarget = .9;
        world.wanted = 4;
        world.playerBurnUntil = world.time + 3;
        world.rockets.push_back({{0, 0}, {0, 1}, 50, -.1, 2, 0});
        world.clock.set_hour(22);
        world.save(save);
        World loaded;
        loaded.load(save);
        near(loaded.position().x, world.position().x, 1e-8, "save position x");
        near(loaded.position().z, world.position().z, 1e-8, "save position z");
        check(loaded.money == 432 && loaded.inventory.ammo(WeaponId::bazooka) == 20,
              "save inventory and money");
        check(loaded.inventory.active()->id == WeaponId::pistol, "manual weapon selection survives load");
        check(loaded.people[victim].dead && loaded.people[witness].fear == .75, "save pedestrian state");
        check(loaded.cars[7].fireTime == 3.25 && loaded.cars[7].health == .1, "save burning vehicle state");
        check(loaded.cars[7].lastDamageAt == world.time && loaded.cars[7].impactCooldown == .2 &&
                  loaded.playerBurnUntil == world.playerBurnUntil &&
                  loaded.people[witness].burnUntil == world.people[witness].burnUntil &&
                  loaded.people[witness].burnPlayerCaused,
              "save damage cooldown and ongoing fire state");
        check(loaded.rockets.size() == 1 && loaded.rockets[0].slope == -.1 && loaded.rockets[0].travel == 2 &&
                  loaded.rockets[0].ownerCar == 0,
              "save live rocket trajectory and owner");
        check(!loaded.cars[8].route, "save absent route stays absent");
        check(loaded.random.state == world.random.state && loaded.rain == .6 && loaded.rainTarget == .9,
              "save deterministic random and weather state");
        check(loaded.time == world.time && loaded.wanted == 4, "save simulation time and pursuit");
        near(loaded.clock.hour, 22, 1e-10, "save solar time");
        for (size_t i = 0; i < world.cars.size(); ++i)
        {
            auto &originalCar = world.cars[i];
            auto &loadedCar = loaded.cars[i];
            check(originalCar.route.has_value() == loadedCar.route.has_value(), "save route ownership");
            if (originalCar.route)
            {
                near(originalCar.route->length(), loadedCar.route->length(), 1e-10,
                     "save full curved route length");
                near(originalCar.route->sample(originalCar.routeDistance).first.x,
                     loadedCar.route->sample(loadedCar.routeDistance).first.x, 1e-10, "save route progress");
            }
        }
        auto right = spatial_audio({-10, 0}, {}, 0, 1);
        auto left = spatial_audio({10, 0}, {}, 0, 1);
        check(right.right > right.left && left.left > left.right, "audio follows camera right vector");
        near(right.right, left.left, 1e-7, "stereo mirror symmetry");
        auto rotated = spatial_audio({0, 10}, {}, pi / 2, 1);
        near(rotated.right, right.right, 1e-7, "audio rotates with camera, not vehicle");
        for (double heading : {0., .7, pi, -1.5})
        {
            auto f = forward(heading), r = perpendicular(f);
            auto view = Mat4::look_at({float(-f.x * 10), 5, float(-f.z * 10)}, {0, 0, 0});
            check(view.m[0] * r.x + view.m[8] * r.z > .99, "D strafe projects to screen right");
        }
        auto farAudio = spatial_audio({0, 100}, {}, 0, 1);
        auto nearAudio = spatial_audio({0, 3}, {}, 0, 1);
        check(farAudio.left < nearAudio.left * .1, "distance attenuates audio");
        AudioMixer radio;
        radio.addClip("radio0", {.5f, .2f});
        radio.addClip("radio1", {-.4f});
        radio.addClip("click", {.5f});
        radio.play({"click", {}, 1., true});
        radio.pause(false, true);
        std::array<float, 8> song{};
        radio.mix(song);
        check(song[0] > song[2] && song[2] > 0 && song[4] < 0 && song[6] == song[0] && song[0] == song[1] &&
                  radio.voiceCount() == 1,
              "radio crosses a track boundary in the menu");
        radio.pause(true);
        radio.mix(song);
        check(std::ranges::all_of(song, [](float s) { return s == 0; }), "unfocused radio is silent");
        std::array<float, 2> one{};
        radio.pause(false, true);
        radio.mix(one);
        check(one[0] > 0 && one[0] < .04, "unfocused mixer preserved the radio cursor");
        radio.nextTrack(); // currently queued on track 1, switch back to the start of track 0
        radio.mix(one);
        check(one[0] > .04, "manual advance restarts the selected track");
        radio.setLevels({65, 0});
        radio.pause(false);
        radio.mix(song);
        check(song[0] > 0 && song[2] == 0 && radio.voiceCount() == 0, "radio mute leaves effects audible");
        AudioMixer oneTrack;
        oneTrack.addClip("radio1", {.5f});
        oneTrack.pause(false);
        oneTrack.mix(song);
        check(std::ranges::all_of(song, [](float s) { return s > 0; }),
              "one available track loops without silence");
        oneTrack.setLevels({0, 50});
        oneTrack.mix(song);
        check(song[0] == 0, "master mute includes music");

        auto levelsPath = save.parent_path() / "promzona-test-audio.txt";
        check(AudioLevels{75, 10}.save(levelsPath), "write volume preferences");
        auto levels = AudioLevels::load(levelsPath);
        check(levels.master == 75 && levels.radio == 10, "read independent volume preferences");
        {
            std::ofstream file(levelsPath);
            file << "95 garbage";
        }
        levels = AudioLevels::load(levelsPath);
        check(levels.master == 65 && levels.radio == 20, "malformed volume settings restore defaults");
        std::filesystem::remove(levelsPath);

        World street;
        street.controlledCar = -1;
        street.cars.resize(12);
        for (size_t i = 0; i < street.cars.size(); ++i)
        {
            street.cars[i].id = unsigned(i);
            street.cars[i].body.p = {double(i + 1), 0};
        }
        auto &unit = street.cars.back();
        unit.kind = 3;
        unit.responding = true;
        unit.body.p.x = 80;
        AudioMixer traffic;
        traffic.addClip("siren", {.5f});
        traffic.pause(false);
        traffic.update(street, 0);
        traffic.mix(one);
        check(traffic.voiceCount() == 1 && one[0] > one[1],
              "siren beyond ten motors follows approaching car");
        traffic.update(street, pi);
        traffic.mix(one);
        check(one[1] > one[0], "turning the camera reverses siren pan");
        unit.body.p.x = 105;
        traffic.update(street, 0);
        check(traffic.voiceCount() == 0, "out-of-range siren stops");
        unit.body.p.x = 20;
        unit.destroyed = true;
        traffic.update(street, 0);
        check(traffic.voiceCount() == 0, "destroyed patrol has no siren");

        AudioMixer mixer;
        mixer.addClip("test", std::vector<float>(100, .5f));
        for (int i = 0; i < 100; i++)
            mixer.play({"test", {}, 1, true});
        check(mixer.voiceCount() == 32, "voice count remains bounded");
        std::array<float, 400> mixed{};
        mixer.mix(mixed);
        check(mixer.voiceCount() == 32 && mixed[0] == 0, "paused mixer preserves voice cursor");
        mixer.pause(false);
        mixer.mix(mixed);
        check(mixer.voiceCount() == 0 && mixed[0] > 0, "one-shots mix together and are reclaimed");
        check(std::ranges::all_of(mixed, [](float s) { return std::isfinite(s) && std::abs(s) <= 1; }),
              "mixer limits peaks without numeric overflow");
        mixer.addClip("engine", std::vector<float>(100, .1f));
        mixer.addClip("rain", std::vector<float>(100, .1f));
        mixer.update(world, 0);
        check(mixer.voiceCount() > 0 && mixer.voiceCount() <= 11, "engine and weather loops are bounded");
        world.rain = 0;
        for (auto &car : world.cars)
            car.destroyed = true;
        mixer.update(world, 0);
        check(mixer.voiceCount() == 0, "removed engines and stopped rain release loops");
        {
            std::ofstream f(save);
            f << "{\"format\":\"unknown\"}";
        }
        bool rejected = false;
        try
        {
            loaded.load(save);
        }
        catch (...)
        {
            rejected = true;
        }
        check(rejected && loaded.money == 432, "invalid load leaves live world intact");
        std::filesystem::remove(save);
        std::error_code ec;
        std::filesystem::remove(save.string() + ".bak", ec);
        check(argc == 2, "C# parity fixture argument is required");
        if (argc == 2)
        {
            std::ifstream fixtures(argv[1]);
            check(bool(fixtures), "reference fixture exists");
            std::string line;
            std::set<std::string> cases;
            while (std::getline(fixtures, line))
            {
                if (line.empty() || line[0] == '#')
                    continue;
                std::istringstream row(line);
                std::string name;
                int kind, steps;
                double mass, vx, vz, angle, w, throttle, steer, dt, traction;
                int brake, hand;
                double outX, outZ, outW, outSteer;
                row >> name >> kind >> mass >> vx >> vz >> angle >> w >> throttle >> steer >> brake >> hand >>
                    steps >> dt >> traction >> outX >> outZ >> outW >> outSteer;
                check(!row.fail(), "parse C# parity fixture");
                check(name == "vehicle" && cases.insert(line).second,
                      "C# parity cases have expected type and distinct data");
                auto v = vehicle();
                v.kind = kind;
                v.mass = mass;
                v.body.mass(mass);
                v.body.v = {vx, vz};
                v.body.angle = angle;
                v.body.w = w;
                for (int i = 0; i < steps; i++)
                    step_vehicle(v, {throttle, steer, bool(brake), bool(hand)}, dt, traction);
                near(v.body.v.x, outX, 1e-9, "C# parity Vx");
                near(v.body.v.z, outZ, 1e-9, "C# parity Vz");
                near(v.body.w, outW, 1e-9, "C# parity yaw");
                near(v.steering, outSteer, 1e-12, "C# parity steering");
            }
            check(cases.size() == 60, "all 60 C# parity cases were executed");
        }
    }
    catch (const std::exception &e)
    {
        check(false, e.what());
    }
    std::println("{} checks, {} failures", checks, failures);
    return failures ? 1 : 0;
}
