#include "promzona/names.hpp"
#include "promzona/world.hpp"
#include "promzona/audio.hpp"
#include "json.hpp"
#include <chrono>
#include <cmath>
#include <fstream>
#include <print>
#include <source_location>
#include <format>
#include <exception>
#include <stdexcept>
using namespace Promzona;

namespace
{
void check(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void scooterTest(World &w)
{
    auto &s = w.scooters.front();
    int index = int(&s - w.scooters.data());
    auto &p = w.people[s.owner];
    check(w.scooters.size() > 45 && std::ranges::count(w.people, true, &Person::courier) > 40,
          "missing riders");
    check(std::ranges::any_of(w.people, [](auto &a) { return a.courier && a.ownedScooter >= 0; }),
          "no mounted couriers");
    w.releaseScooter(index, {1, 0});
    check(s.rider == Scooter::free && s.body.invMass > 0 && s.body.v.x == 1,
          "fall must leave a dynamic scooter");
    Vec2 direction{0, 1};
    double speed = 1;
    w.riderIntent(p, direction, speed);
    check(s.rider == Scooter::free, "owner remounted without a recovery interval");
    w.time += 8;
    w.riderIntent(p, direction, speed);
    check(s.rider == s.owner && speed > 3, "owner did not reclaim scooter");
    w.releaseScooter(index, {});
    w.controlledCar = -1;
    w.player.p = s.body.p;
    w.player.height = s.body.height + 4;
    check(!w.useScooter(), "mounted from a different storey");
    w.player.height = s.body.height + .7;
    w.enter_exit();
    check(w.playerScooter == index && s.rider == Scooter::playerRider, "could not ride abandoned scooter");
    w.time += 8;
    w.riderIntent(p, direction, speed);
    check(s.rider == Scooter::playerRider, "owner stole player's scooter");
    w.toggle_drone(0);
    check(!w.drone.active, "rode a scooter and operated a drone at once");
    w.player.v = {0, 3};
    PlayerInput input;
    input.car.handbrake = true;
    input.shooting = true;
    w.inventory.give(WeaponId::pistol);
    int ammo = w.inventory.ammo(WeaponId::pistol);
    for (int n = 0; n < 60; n++)
        w.step(input, 1. / 120);
    check(w.player.v.length() < .8 && w.inventory.ammo(WeaponId::pistol) == ammo,
          "scooter brakes or firing lock failed");
}

void socialTest(World &w)
{
    auto at = w.habitat.front();
    auto &p = *std::ranges::find_if(
        w.people, [](const Person &a)
        { return !a.dead && !a.courier && !a.inCompany && a.riding < 0 && a.ownedScooter < 0; });
    auto old = p;
    p.body.p = at;
    p.body.height = w.terrain.height(at) + .85;
    w.alarmWitnesses(at + Vec2{2, 0}, p.body.height, false);
    check(p.watchUntil > w.time + 5 && p.fear > 0, "visible incident did not alarm its witness");
    w.scarePerson(p, at + Vec2{-2, 0}, 2.);
    double speed = 3.;
    Vec2 dir{};
    w.socialIntent(p, dir, speed);
    check(dir.x > .9, "recent threat must override curiosity");
    check(p.incident.x > at.x, "new scare replaced the remembered incident");
    p.fear = 0;
    p.body.p = at - Vec2{8, 0};
    speed = 1.2;
    w.socialIntent(p, dir, speed);
    check(dir.x > .9 && speed > 0, "calmed witness did not investigate");
    p.body.p = at;
    w.socialIntent(p, dir, speed);
    check(speed == 0, "bystander walked through incident");
    p.watchUntil = w.time - .1;
    speed = 1.2;
    dir = {0, 1};
    w.socialIntent(p, dir, speed);
    check(dir.z == 1 && speed == 1.2, "expired interest stopped normal walking");
    p = old;
    bool checkedCompany = false;
    for (size_t i = 0; i + 2 < w.people.size(); i += 28)
    {
        auto &a = w.people[i], &b = w.people[i + 1], &c = w.people[i + 2];
        if (!a.inCompany || !b.inCompany || !c.inCompany)
            continue;
        auto a0 = a, b0 = b, c0 = c;
        a.dead = true;
        b.body.p = at;
        b.heading = 0;
        c.body.p = at + Vec2{0, -4};
        c.fear = 0;
        c.watchUntil = 0;
        w.socialIntent(c, dir, speed);
        check(c.next == b.next && dir.z > .8 && speed > 1.2, "surviving friends lost their leader");
        w.scarePerson(c, at, 2.);
        check(c.fear > 0 && c.fear < 1.5, "company members should recover sooner");
        a = a0;
        b = b0;
        c = c0;
        checkedCompany = true;
        break;
    }
    check(checkedCompany, "no full company generated");
}

void voiceTest()
{
    World w;
    w.controlledCar = -1;
    w.people.emplace_back();
    auto &elder = w.people.front();
    elder.style = 2;
    Vec2 dir{0, 1};
    double speed = 1.;
    w.socialIntent(elder, dir, speed);
    check(elder.idleVoiceAt >= 30. && w.sounds.empty(), "pedestrians wait before speaking");
    w.time = elder.idleVoiceAt + .1;
    w.socialIntent(elder, dir, speed);
    check(w.sounds.size() == 1 && w.sounds.back().name.starts_with("elder_") && w.sounds.back().speaker == 0,
          "nearby pensioner speaks from their own position");
    w.scarePerson(elder, {}, 2.);
    check(w.sounds.size() == 2 && w.sounds.back().name.starts_with("fear_"),
          "a startle can immediately interrupt an idle line");
    w.sounds.clear();
    AudioMixer audio;
    audio.addClip("voice", std::vector<float>(64, .5f));
    audio.addClip("impact", std::vector<float>(64, .3f));
    w.sounds.push_back({"voice", {}, 1, false, 0});
    audio.update(w, 0);
    check(audio.voiceCount() == 1, "speaker did not start talking");
    audio.play({"voice", {}, 1, false, 0});
    check(audio.voiceCount() == 1, "a second line replaces the same speaker's first line");
    w.people[0].dead = true;
    w.sounds.push_back({"voice", {}, 1, false, 0});
    w.sounds.push_back({"impact", {}, 1});
    audio.update(w, 0);
    check(audio.voiceCount() == 1, "dying speaker continued talking over the impact");
    audio.pause(false);
    std::array<float, 8> samples{};
    audio.mix(samples);
    check(samples[0] > 0, "impact sound was interrupted with the voice");
}

void coverTest(const std::filesystem::path &path)
{
    World w;
    w.generate(17);
    w.controlledCar = -1;
    auto at = w.habitat.front();
    auto &car = w.cars[1];
    car.body.p = at;
    car.body.angle = 0;
    car.body.halfW = 1;
    car.body.halfH = 2;
    car.body.height = w.terrain.height(at) + .85;
    car.body.verticalExtent = 2;
    car.body.v = {};
    car.body.roadLevel = 0;
    w.player.p = at + Vec2{9, 0};
    w.player.height = w.terrain.height(w.player.p) + .85;
    Person p;
    p.body.p = at + Vec2{-3, -1};
    p.body.height = w.terrain.height(p.body.p) + .85;
    p.sidearm = WeaponId::pistol;
    p.alertUntil = 40;
    std::array<const Car *, 1> traffic{&car};
    double speed = 1;
    Vec2 dir;
    w.coverAllowsAim(p, traffic, dir, speed);
    w.time += 1.2;
    check(!w.coverAllowsAim(p, traffic, dir, speed) && p.cover.found && p.cover.vehicle == 1,
          "shooter could not find a screened position behind a stationary vehicle");
    p.body.p = p.cover.hide;
    check(!w.coverAllowsAim(p, traffic, dir, speed) && speed == 0, "shooter fired from concealment");
    w.time += 1.2;
    w.coverAllowsAim(p, traffic, dir, speed);
    check(p.cover.peeking && !w.coverAllowsAim(p, traffic, dir, speed), "shooter fired while moving to peek");
    p.body.p = p.cover.peek;
    check(w.coverAllowsAim(p, traffic, dir, speed), "shooter did not aim from the exposed side");
    auto saved =
        std::ranges::find_if(w.people, [](const Person &civilian)
                             { return !civilian.dead && civilian.riding < 0 && civilian.ownedScooter < 0; });
    saved->sidearm = p.sidearm;
    saved->alertUntil = p.alertUntil;
    saved->cover = p.cover;
    w.save(path);
    World loaded;
    loaded.load(path);
    const auto &resumed = loaded.people.at(size_t(saved - w.people.begin())).cover;
    check(resumed.found && resumed.peeking && resumed.vehicle == 1 &&
              (resumed.peek - p.cover.peek).length2() == 0,
          "lost armed witness cover on reload");
    car.body.v = {4, 0};
    w.coverAllowsAim(p, traffic, dir, speed);
    check(!p.cover.found, "shooter used a departed car as cover");
}

void conversationTest()
{
    World w;
    w.generate(35);
    w.controlledCar = -1;
    int who = -1;
    for (int i = 0; i < int(w.people.size()); ++i)
    {
        if (w.canTakePassengerJob(i) && w.layout.roadDistance(w.people[i].body.p) > w.layout.roadHalf() &&
            w.people[i].body.id % 5 == 0)
        {
            who = i;
            break;
        }
    }
    check(who >= 0, "no available pavement passenger");
    auto &p = w.people[who];
    w.player.p = p.body.p - Vec2{0, 1.45};
    w.player.height = p.body.height;
    w.player.angle = 0;
    for (int i = 0; i < int(w.people.size()); ++i)
        if (i != who && (w.people[i].body.p - w.player.p).length2() < 9)
            w.people[i].dead = true;
    p.style = 10;
    w.money = 8;
    w.enter_exit();
    check(w.conversation.actor == who && w.conversation.offer == World::StreetOffer::gift && w.money == 8,
          "F failed to address the person ahead");
    check(!w.talk() && w.money == 8, "same keypress accepted a gift twice");
    w.time += 1;
    w.player.angle = pi;
    check(!w.talk() && w.money == 8, "paid someone behind the player");
    w.player.angle = 0;
    w.player.roadLevel = 100;
    check(!w.talk(), "talked through a road deck");
    w.player.roadLevel = 0;
    check(w.talk() && w.money == 3 && w.sounds.back().name == "spend", "gift did not debit once");
    w.time += 1;
    p.style = 0;
    const auto originalId = p.body.id;
    p.body.id = 3;
    int market = w.nearestSite(ServiceKind::market, p.body.p);
    check(market >= 0, "no market for the local to point to");
    auto &entrance = w.sites[market].entrance;
    const auto originalEntrance = entrance;
    entrance = p.body.p + Vec2{0, 15};
    check(w.talk() && w.notice == L"Рынок — на " + streetAt(entrance) + L", 20 м на север.",
          "market directions disagree with map north");
    w.time += 1;
    entrance = p.body.p + Vec2{-15, 0};
    check(w.talk() && w.notice == L"Рынок — на " + streetAt(entrance) + L", 20 м на восток.",
          "market directions disagree with map east");
    entrance = originalEntrance;
    p.body.id = originalId;
    w.time += 1;
    check(w.talk() && w.conversation.offer == World::StreetOffer::taxi, "walk-up taxi offer missing");
    w.time += 1;
    check(w.talk() && w.job.kind == JobKind::taxi && w.job.actor == who,
          "different NPC became the passenger");
    w.clinicRespawn(w.player.p);
    check(w.conversation.actor < 0, "respawn retained conversation");
}

void chainBlastTest()
{
    World w;
    w.generate(17);
    // Isolate a secondary vehicle blast from street geometry and other traffic.
    w.cars.resize(1);
    auto &car = w.cars.front();
    car.body.p = {7, 0};
    car.body.height = 100;
    car.health = .01;
    w.animals.resize(1);
    auto &animal = w.animals.front();
    animal.flock = -1;
    animal.p = {11, 0};
    animal.altitude = car.body.height;
    w.explode({0, 0}, car.body.height);
    check(car.destroyed && animal.deadUntil > w.time,
          "wildlife beyond the first blast did not react to the exploding vehicle");
}

// Port of the managed rule for an animal under the wheels: thrown aside and stunned for
// three seconds; killed only when the car closes at more than four metres a second.
void knockTest()
{
    World w;
    w.generate(17);
    w.cars.resize(1);
    auto &car = w.cars.front();
    w.controlledCar = 0;
    // A stray on open ground with a car over it.
    const auto stray = std::ranges::find_if(w.animals, [&](const Animal &a)
                                            { return a.flock < 0 && a.species != Species::bird; });
    check(stray != w.animals.end(), "city has a stray");
    const Vec2 spot = stray->p;
    const double ground = w.terrain.height(spot);
    auto strike = [&](double speed)
    {
        *stray = Animal{stray->species, stray->variant, -1, spot, spot, {}, {}, ground};
        car.body.p = spot - Vec2{.4, 0};
        car.body.angle = -pi / 2;
        car.body.height = ground + .8;
        car.body.v = {speed, 0};
        w.sounds.clear();
        w.time += .1;
        w.step_wildlife(.1);
        // The car is taken away: what follows is the flight alone.
        car.body.p = spot + Vec2{0, 300};
        car.body.v = {};
    };
    strike(.9);
    check(stray->knockedUntil <= w.time && stray->deadUntil == 0 && w.sounds.empty(),
          "a car that creeps up knocked the animal down");
    strike(3);
    check(stray->knockedUntil > w.time + 2.8 && stray->deadUntil == 0 &&
              std::abs(stray->velocity.x - 1.5) < 1e-9 && std::abs(stray->rise - 3 * .21) < 1e-9 &&
              std::ranges::count(w.sounds, std::string{"impact"}, &SoundEvent::name) == 1 &&
              std::abs(w.sounds.front().gain - .3) < 1e-9,
          "a slow car did not knock the animal down");
    double highest = 0;
    for (int n = 0; n < 29; ++n)
    {
        w.time += .1;
        w.step_wildlife(.1);
        highest = std::max(highest, stray->altitude - w.terrain.height(stray->p));
        check(stray->knockedUntil > w.time - 1e-9 ? stray->fear == 0 : true, "a stunned animal is afraid");
    }
    check(highest > .005 && stray->altitude == w.terrain.height(stray->p) &&
              (stray->p - spot).length() > .15 && (stray->p - spot).length() < 1.5 &&
              stray->velocity.length() < .05 && stray->deadUntil == 0,
          "a knocked animal does not fly, land and come to rest");
    w.time += .2;
    w.step_wildlife(.1);
    w.step_wildlife(.1);
    check(stray->knockedUntil <= w.time && stray->deadUntil == 0, "a knocked animal does not get up");
    strike(9);
    check(stray->deadUntil > w.time && stray->knockedUntil > w.time &&
              std::abs(stray->rise - 9 * .21) < 1e-9 && std::abs(stray->velocity.x - 4.5) < 1e-9,
          "a fast car did not kill the animal");
    highest = 0;
    for (int n = 0; n < 40; ++n)
    {
        w.time += .1;
        w.step_wildlife(.1);
        highest = std::max(highest, stray->altitude - w.terrain.height(stray->p));
    }
    check(highest > .1 && stray->altitude == w.terrain.height(stray->p) && (stray->p - spot).length() > 1 &&
              stray->velocity.length() == 0 && stray->deadUntil > w.time,
          "a body that was thrown does not come down some way off");
    // A body in the air comes down whatever killed it.
    stray->altitude = ground + 12;
    stray->rise = 0;
    for (int n = 0; n < 20; ++n)
    {
        w.time += .1;
        w.step_wildlife(.1);
    }
    check(stray->altitude == w.terrain.height(stray->p), "a dead bird hangs in the air");
}

void wildlifeTest(World &w)
{
    auto first = std::ranges::find(w.animals, 0, &Animal::flock);
    check(first != w.animals.end() && std::ranges::count(w.animals, 0, &Animal::flock) >= 6,
          "solitary pigeon spawn");
    Vec2 home = first->p;
    w.scare_wildlife(home, first->altitude, 0);
    check(w.flocks[0].flying && (home - w.flocks[0].landing).length() > 15, "flock did not depart together");
    for (int n = 0; n < 20; n++)
    {
        w.time += .1;
        w.step_wildlife(.1);
    }
    for (auto &a : w.animals)
        if (a.flock == 0)
            check(a.altitude > w.terrain.height(a.p) + 1 && a.distance > 1, "flock member left on ground");
    w.scare_wildlife(first->p, first->altitude, 3);
    check(first->deadUntil > w.time, "blast missed nearby animal");
}
// The animals of the managed game live on the pavements as well as in the parks: strays
// walk from corner to corner, and a flock may settle beside a street.
void pavementAnimals()
{
    World w;
    w.generate(20261105, {}, CityLayout::districtRevision, Density::full);
    const auto &l = w.layout;
    auto fromRoad = [&](Vec2 p) { return l.roadDistance(p) - l.roadHalf(); };
    auto onPavement = [&](Vec2 p) { return fromRoad(p) > .2 && fromRoad(p) < 2.8; };
    auto inPark = [&](Vec2 p)
    {
        const int i = int(std::floor((p.x - l.coordinate(0)) / World::spacing)),
                  j = int(std::floor((p.z - l.coordinate(0)) / World::spacing));
        return i >= 0 && j >= 0 && i < l.nodes() - 1 && j < l.nodes() - 1 && l.park(i, j);
    };
    // The same animals, one for one, as when they all lived in parks: a save of that
    // time is checked against them when it loads.
    auto count = [&](const World &world, Species species)
    { return std::ranges::count(world.animals, species, &Animal::species); };
    check(w.animals.size() == 1133 && count(w, Species::pigeon) == 973 && count(w, Species::cat) == 53 &&
              count(w, Species::dog) == 53 && count(w, Species::bird) == 54 && w.flocks.size() == 174,
          "the full city has other animals than its saves");
    {
        World low;
        low.generate(20261105);
        check(low.animals.size() == 354 && low.flocks.size() == 42 && count(low, Species::cat) == 36 &&
                  count(low, Species::dog) == 36 && count(low, Species::bird) == 10,
              "the low city has other animals than its saves");
    }
    size_t strays = 0, streetStrays = 0;
    for (const auto &a : w.animals)
    {
        check(fromRoad(a.p) > 0, "an animal was put on the roadway");
        check(!w.insideBuilding(a.p, w.terrain.height(a.p) + .4, .2), "an animal was put inside a wall");
        if (a.flock >= 0)
            continue;
        ++strays;
        check(inPark(a.p) || onPavement(a.p), "a stray starts in a park or on a pavement");
        streetStrays += !inPark(a.p);
    }
    check(streetStrays * 2 > strays && streetStrays < strays,
          "most strays live in the streets, some in parks");
    size_t pigeonFlocks = 0, streetFlocks = 0;
    for (size_t f = 0; f < w.flocks.size(); ++f)
    {
        const auto first = std::ranges::find(w.animals, int(f), &Animal::flock);
        if (first == w.animals.end() || first->species != Species::pigeon)
            continue;
        ++pigeonFlocks;
        const bool street = !inPark(w.flocks[f].landing);
        streetFlocks += street;
        int member = 0;
        for (const auto &a : w.animals)
            if (a.flock == int(f))
            {
                const Vec2 offset = World::flockOffset(member++, street);
                check((a.p - w.flocks[f].landing - offset).length() < 1e-9 &&
                          (a.offset - offset).length() < 1e-9,
                      "a flock stands in a spiral on a lawn and in two files on a pavement");
                check(!street || onPavement(a.p), "a street flock keeps to the pavement");
            }
    }
    check(pigeonFlocks == 120 && streetFlocks >= 12 && streetFlocks <= 45,
          "about one flock of pigeons in five lives beside a street");

    // A stray far from the player, with nothing to fear, strolls the pavement graph.
    w.controlledCar = -1;
    auto &stray = *std::ranges::find_if(w.animals, [&](const Animal &a)
                                        { return a.flock < 0 && !inPark(a.p) && a.deadUntil == 0; });
    w.player.p = stray.p + Vec2{400, 0};
    if (std::abs(w.player.p.x) > 700)
        w.player.p = stray.p - Vec2{400, 0};
    stray.thinkAt = 0;
    stray.fear = 0;
    const double pace = stray.species == Species::dog ? 1.35 : .85;
    double walked = 0;
    for (int tick = 0; tick < 900; ++tick)
    {
        const Vec2 before = stray.p;
        w.time += .1;
        w.step_wildlife(.1);
        const double step = (stray.p - before).length();
        walked += step;
        check(step <= pace * .1 + 1e-9, "a stray strolls at the pace of the managed game");
        check(!w.insideBuilding(stray.p, w.terrain.height(stray.p) + .4, .2), "a stray walked into a wall");
        if ((stray.goal - stray.p).length2() > 1e-12)
        {
            const double gx = (stray.goal.x - l.coordinate(0) - l.sidewalk()) / World::spacing,
                         gz = (stray.goal.z - l.coordinate(0) - l.sidewalk()) / World::spacing;
            check(std::abs(gx - std::round(gx)) < 1e-9 && std::abs(gz - std::round(gz)) < 1e-9,
                  "a stray heads for a pavement corner");
        }
    }
    check(walked > 30, "a stray stood still for a minute and a half");

    // A startled flock comes down in a park or beside a street, clear of the roadway.
    int beside = 0, landings = 0;
    for (int n = 0; n < 300; ++n)
    {
        const auto perch =
            w.landing_site({double(n % 17) * 40 - 320, double(n % 13) * 50 - 300}, forward(n * .7));
        ++landings;
        if (!perch.pavement)
        {
            check(std::ranges::find_if(w.habitat, [&](Vec2 p) { return (p - perch.at).length2() < 1e-12; }) !=
                      w.habitat.end(),
                  "a lawn to land on is one of the parks");
            continue;
        }
        ++beside;
        for (int member = 0; member < 10; ++member)
            check(onPavement(perch.at + World::flockOffset(member, true)),
                  "a file of pigeons reached the roadway");
    }
    check(beside > landings / 10 && beside < landings * 4 / 10,
          "about one landing in five is beside a street");
    const auto leader = std::ranges::find(w.animals, 0, &Animal::flock);
    w.take_flight(0, leader->p, {1, 0});
    const bool files = !inPark(w.flocks[0].landing);
    int member = 0;
    for (const auto &a : w.animals)
        if (a.flock == 0)
            check((a.offset - World::flockOffset(member++, files)).length() < 1e-9,
                  "a flock forms up for the place it flies to");
}
} // namespace

int main()
{
    auto path = std::filesystem::temp_directory_path() /
                std::format("pz-street-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    try
    {
        World w;
        w.generate(17);
        voiceTest();
        conversationTest();
        coverTest(path);
        socialTest(w);
        scooterTest(w);
        wildlifeTest(w);
        pavementAnimals();
        chainBlastTest();
        knockTest();
        w.save(path);
        World copy;
        copy.load(path);
        check(copy.playerScooter == w.playerScooter && copy.flocks[0].flying &&
                  copy.streetRandom.state == w.streetRandom.state &&
                  copy.animals.size() == w.animals.size() &&
                  copy.animals.back().goal.x == w.animals.back().goal.x,
              "street state lost at load");
        // Observe the complete journey with the player far from the landing site.
        copy.controlledCar = -1;
        copy.player.p = *std::ranges::max_element(copy.habitat, {}, [&](Vec2 p)
                                                  { return (p - copy.flocks[0].landing).length2(); });
        for (int n = 0; n < 4200 && copy.flocks[0].flying; ++n)
        {
            copy.time += .1;
            copy.step_wildlife(.1);
        }
        check(!copy.flocks[0].flying, "flock stalled on its way to the new park");
        for (const auto &bird : copy.animals)
            if (bird.flock == 0 && !bird.deadUntil)
                check((bird.p - copy.flocks[0].landing).length() < 3 &&
                          bird.altitude < copy.terrain.height(bird.p) + .4,
                      "flock landed in the air");
        auto json = nlohmann::json::parse(std::ifstream(path));
        json["streetLife"]["scooters"][0]["owner"] = 99999;
        {
            std::ofstream out(path);
            out << json;
        }
        bool rejected = false;
        try
        {
            copy.load(path);
        }
        catch (const std::exception &)
        {
            rejected = true;
        }
        check(rejected && copy.playerScooter == w.playerScooter, "malformed ownership corrupted live game");
        json["format"] = "promzona-cpp-6";
        json.erase("streetLife");
        {
            std::ofstream out(path);
            out << json;
        }
        copy.load(path);
        check(copy.playerScooter < 0 && !copy.scooters.empty() && !copy.animals.empty(),
              "old save lacks street life");
        w.clinicRespawn(w.position());
        check(w.playerScooter < 0 && w.scooters.front().rider == Scooter::free, "respawn retained scooter");
        std::filesystem::remove(path);
        std::println("Street life passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::filesystem::remove(path);
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
