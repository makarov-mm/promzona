#include "promzona/appearance.hpp"
#include "promzona/fleet.hpp"
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
using U = Person::Uniform;
using R = Person::Role;
void require(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
}
constexpr double frame = 1. / 60;
PlayerInput parked()
{
    PlayerInput input;
    input.car.handbrake = true;
    return input;
}
size_t count(const World &w, R role)
{
    return size_t(std::ranges::count(w.people, role, &Person::role));
}
bool plain(const World &w, size_t i)
{
    const auto &p = w.people[i];
    return p.riding < 0 && p.waitingLine < 0 && !p.courier && !p.inCompany && p.ownedScooter < 0 && !p.dead &&
           !p.body.roadLevel && p.role == R::none && p.uniform == U::none && !p.elder() && !p.drunk() &&
           !p.homeless() && !w.personEngaged(int(i));
}
size_t walker(const World &w, bool distant, size_t skip = size_t(-1))
{
    for (size_t i = 0; i < w.people.size(); ++i)
        if (i != skip && plain(w, i) &&
            (distant ? !((w.people[i].body.p - w.position()).length() < 320)
                     : (w.people[i].body.p - w.position()).length() < 150))
            return i;
    throw std::runtime_error("no suitable walker");
}
void rolePass(World &w)
{
    w.nextRoleStep = 0;
    w.step(parked(), frame);
}

// Clothes, build and the standing-pose marks of the skin.
void looks()
{
    for (std::uint32_t id = 900; id < 909; ++id)
    {
        const auto a = appearance(id, id % 2);
        require(a.outfit == int(id % 9) && a.tone == int(id % 6) && a.model == int(id % 2) && !a.officer(),
                "everyday look does not follow the id");
        require(a.height > .92f && a.height < 1.07f && a.width > .78f && a.width < 1.f && a.depth > .93f &&
                    a.depth < 1.09f,
                "build is outside the managed range");
    }
    require(appearance(902, 0).height != appearance(903, 0).height &&
                appearance(902, 0).width != appearance(903, 0).width,
            "everybody has the same build");
    const auto grey = [](const Appearance &a) { return a.hair.x > .5f && a.hair.x < .55f; };
    const auto pensioner = appearance(901, 8), elder = appearance(901, 2);
    require(pensioner.outfit == Appearance::bagElder && pensioner.model == 1 && grey(pensioner) &&
                pensioner.longTrousers,
            "the pensioner with bags is wrong");
    require(elder.outfit == 901 % 9 && grey(elder) && elder.longTrousers && elder.longSleeves,
            "the elder is wrong");
    require(appearance(901, 9).outfit == Appearance::drunk &&
                appearance(901, 10).outfit == Appearance::homeless && appearance(901, 10).longTrousers,
            "street personas are wrong");
    const std::pair<U, int> services[]{{U::medical, Appearance::medic},
                                       {U::police, Appearance::militia},
                                       {U::rosguard, Appearance::rosguard},
                                       {U::fsb, Appearance::fsb},
                                       {U::army, Appearance::army}};
    for (const auto &[uniform, outfit] : services)
    {
        // A uniform hides the persona and the habit of whoever wears it.
        const auto a = appearance(901, 9, uniform, R::beach);
        require(a.outfit == outfit && a.longSleeves && a.longTrousers, "service outfit is wrong");
        require(a.officer() == (uniform != U::medical) && (!a.officer() || a.model == 0),
                "service model is wrong");
    }
    const auto militia = appearance(7, 1, U::police), fsb = appearance(7, 1, U::fsb),
               army = appearance(7, 1, U::army);
    require(militia.cap().z > fsb.cap().z && army.cap().y > army.cap().z && fsb.cap().x < .1f,
            "forces share one cap");
    const auto night = appearance(901, 0, U::none, R::night), beach = appearance(901, 0, U::none, R::beach);
    require(night.outfit == Appearance::nightWorker && night.model == 1, "night shift look is wrong");
    require(beach.outfit == Appearance::beach && !beach.longTrousers && beach.model == 0,
            "beach look is wrong");
    const auto hero = heroAppearance();
    require(hero.outfit == Appearance::hero && hero.longTrousers && hero.longSleeves && hero.flags() == 3 &&
                hero.model == 0,
            "the player has no look of his own");
    Person medic;
    medic.body.id = 44;
    medic.style = 1;
    require(appearance(medic, true).outfit == Appearance::medic && appearance(medic).outfit == 44 % 9,
            "paramedic on a call has no coat");

    // Two vertices on a 24-joint rig whose only pose lifts joint 0 by one metre and the
    // forearm (joint 10) by half a metre.
    SkinData skin;
    skin.joints = 24;
    skin.poses = 1;
    skin.bones.assign(size_t(24) * 16, 0.f);
    for (int j = 0; j < 24; ++j)
        skin.bones[j * 16] = skin.bones[j * 16 + 5] = skin.bones[j * 16 + 10] = skin.bones[j * 16 + 15] = 1;
    skin.bones[0 * 16 + 13] = 1;
    skin.bones[10 * 16 + 13] = .5f;
    Vertex body, arm, mixed;
    body.position = {0, .2f, 0};
    arm.position = {0, .3f, 0};
    arm.joints = {10, 0, 0, 0};
    mixed.position = {0, .2f, 0};
    mixed.joints = {0, 10, 0, 0};
    mixed.weights = {.75f, .25f, 0, 0};
    skin.mesh.vertices = {body, arm, mixed};
    markStandingPose(skin);
    const auto &marked = skin.mesh.vertices;
    require(std::abs(marked[0].uv[1] - .6f) < 1e-6f && std::abs(marked[1].uv[1] - 1.4f) < 1e-6f &&
                std::abs(marked[2].uv[1] - (.75f * 1.2f + .25f * .7f) * .5f) < 1e-6f,
            "standing pose was not recorded");
    skin.joints = 8;
    bool refused = false;
    try
    {
        markStandingPose(skin);
    }
    catch (const std::exception &)
    {
        refused = true;
    }
    require(refused, "a rig without arms was accepted");
}

void paces()
{
    Person p;
    p.body.id = 12;
    auto pace = [&](std::uint32_t style)
    {
        p.style = style;
        return World::walkingPace(p);
    };
    require(pace(9) < pace(8) && pace(8) < pace(2) && pace(2) < pace(10) && pace(10) < pace(0),
            "personas share one walking speed");
    require(pace(0) >= 1.15 && pace(0) <= 1.5 && pace(9) == .7, "walking speed is outside the range");
    p.style = 2;
    const double oldRun = World::fleeingPace(p);
    p.style = 0;
    require(oldRun < World::fleeingPace(p) && World::fleeingPace(p) == 3.5, "elders run like everybody else");
    p.style = 9;
    p.uniform = U::police;
    require(World::walkingPace(p) == 1.3 && World::fleeingPace(p) == 3.5, "an officer staggers like a drunk");

    // In the city: an elder covers less ground than a neighbour of the same stretch.
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    const size_t slow = walker(w, true), brisk = walker(w, true, slow);
    w.people[slow].style = 2;
    for (int i = 0; i < 240; ++i)
        w.step(parked(), frame);
    const double a = w.people[slow].body.v.length(), b = w.people[brisk].body.v.length();
    require(std::abs(a - 1.) < .05 && std::abs(b - World::walkingPace(w.people[brisk])) < .05 && a < b,
            "walkers ignore their pace");
}

// The beach, the homeless corners and the night shift.
void habits()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    require(count(w, R::beach) == w.people.size() / 16 && w.roleQuota(R::beach) == 45 &&
                w.roleQuota(R::night) == 9 && count(w, R::night) == 0,
            "new city has the wrong habits");
    require(w.rolesTaken == 0 && w.rolesLeft == 0, "settling counts as play");
    size_t groups = 0, corners = 0;
    for (size_t i = 0; i < w.people.size(); ++i)
    {
        const auto &p = w.people[i];
        if (p.role == R::none)
            continue;
        require(w.roleGround(p.role, p.roleHome) && (p.body.p - p.roleHome).length() < 1e-9 && !p.courier &&
                    !p.inCompany && p.ownedScooter < 0 && p.riding < 0,
                "habit on bad ground or with other duties");
        if (p.role == R::vagrant)
        {
            require(p.homeless(), "somebody else lives on the street");
            ++corners;
            continue;
        }
        const double rim = std::max(std::abs(p.roleHome.x), std::abs(p.roleHome.z));
        require(rim > World::landEdge - 46 && rim < World::landEdge - 22 &&
                    w.terrain.height(p.roleHome) > CityLayout::waterLevel + .6 && !p.elder() && !p.drunk(),
                "beach visitor is not on the shore");
        require(appearance(p).outfit == Appearance::beach, "beach visitor is dressed for town");
        for (size_t k = 0; k < i; ++k)
            groups += w.people[k].role == R::beach && (w.people[k].roleHome - p.roleHome).length() < 3;
    }
    require(groups >= 10 && corners >= 10, "no company on the sand or nobody on the corners");
    {
        // The same seed settles the same people in the same places.
        World again;
        again.generate(20261105u);
        for (size_t i = 0; i < w.people.size(); ++i)
            require(again.people[i].role == w.people[i].role &&
                        again.people[i].roleHome.x == w.people[i].roleHome.x,
                    "habits are not seeded");
        World dense;
        dense.generate(20261105u, {}, CityLayout::districtRevision, Density::medium);
        require(count(dense, R::beach) == 112 && dense.roleQuota(R::night) == 22,
                "dense city has the wrong habits");
    }

    // A quiet minute: people keep to their places, nobody wades in and nobody stands in the road.
    std::vector<Vec2> start;
    for (const auto &p : w.people)
        start.push_back(p.body.p);
    for (int i = 0; i < 60 * 45; ++i)
        w.step(parked(), frame);
    size_t strolled = 0;
    for (size_t i = 0; i < w.people.size(); ++i)
    {
        const auto &p = w.people[i];
        // Whoever settled on a corner only during this minute is not measured.
        if (p.role == R::none || (start[i] - p.roleHome).length() > 1e-9)
            continue;
        const double reach = p.role == R::beach ? 5 : 7;
        require(!p.dead && (p.body.p - p.roleHome).length() < reach + 2, "habit did not keep its place");
        require(w.layout.roadDistance(p.body.p) > w.layout.roadHalf() &&
                    w.terrain.height(p.body.p) > CityLayout::waterLevel,
                "habit led onto the road or into the water");
        strolled += (p.body.p - start[i]).length() > 1;
    }
    require(strolled >= 15 && w.drownings == 0, "nobody strolls about their place");

    // Talking to them: the managed lines.
    auto address = [&](size_t who)
    {
        auto &p = w.people[who];
        w.controlledCar = -1;
        w.player.p = p.body.p - Vec2{0, 1.45};
        w.player.height = p.body.height;
        w.player.roadLevel = 0;
        w.player.angle = 0;
        for (size_t i = 0; i < w.people.size(); ++i)
            if (i != who && (w.people[i].body.p - w.player.p).length2() < 9)
                w.people[i].body.p += Vec2{0, 12};
        w.time += 1;
        w.conversation = {};
        return w.talk();
    };
    const size_t sunbather = size_t(std::ranges::find(w.people, R::beach, &Person::role) - w.people.begin());
    require(address(sunbather) && w.notice == L"Водичка — класс! Иди окунись.",
            "beach visitor has nothing to say");
    w.people[sunbather].role = R::night;
    require(address(sunbather) && w.notice == L"Не сегодня, милый.", "night worker has nothing to say");
    w.people[sunbather].role = R::beach;

    // A fright sends a visitor running; afterwards the visitor comes back.
    {
        w.controlledCar = 0;
        size_t shy = 0;
        while (w.people[shy].role != R::beach || shy == sunbather ||
               (w.people[shy].body.p - w.position()).length() < 320)
            ++shy;
        auto &p = w.people[shy];
        p.body.p = p.roleHome;
        w.scarePerson(p, p.roleHome + Vec2{1, .3}, 2.);
        for (int i = 0; i < 120; ++i)
            w.step(parked(), frame);
        require((p.body.p - p.roleHome).length() > 3 && p.role == R::beach, "frightened visitor stood still");
        for (int i = 0; i < 60 * 40; ++i)
            w.step(parked(), frame);
        require(p.role == R::beach && (p.body.p - p.roleHome).length() < 7, "visitor did not come back");
        // Chased far off, the visitor gives the place up and joins the walkers.
        p.body.p = p.roleHome + Vec2{std::abs(p.roleHome.x) > std::abs(p.roleHome.z) ? 0. : 80., 0} +
                   Vec2{0, std::abs(p.roleHome.x) > std::abs(p.roleHome.z) ? 80. : 0.};
        const int left = w.rolesLeft;
        rolePass(w);
        require(p.role == R::none && w.rolesLeft == left + 1 && p.roleHome.length() == 0 &&
                    (p.target - (World::nodePoint(p.node) + Vec2{9.1, 9.1})).length() < 1e-9,
                "a chased visitor kept the place");
    }

    // The night shift arrives after nine in the evening, out of sight, at the kerb.
    w.dayNightCycle = false;
    w.clock.setHour(22);
    const size_t before = count(w, R::beach);
    for (int pass = 0; pass < 40; ++pass)
        rolePass(w);
    require(count(w, R::night) == w.roleQuota(R::night), "the night shift did not turn out");
    size_t watched = w.people.size();
    for (size_t i = 0; i < w.people.size(); ++i)
    {
        const auto &p = w.people[i];
        if (p.role != R::night)
            continue;
        const double street = w.layout.roadDistance(p.roleHome);
        require(w.outOfSight(p.roleHome) && w.roleGround(R::night, p.roleHome) && street > 7.85 &&
                    street < 8.6 && p.node != p.next,
                "night worker is not at a kerb out of sight");
        require(appearance(p).outfit == Appearance::nightWorker && appearance(p).model == 1 && !p.elder() &&
                    !p.homeless(),
                "night worker look is wrong");
        watched = i;
    }
    require(count(w, R::beach) >= before, "the beach emptied for the night shift");
    // At dawn they go home, except the one the player is looking at.
    {
        auto &seen = w.people[watched];
        const Vec2 kerb = seen.roleHome, home = w.position() + Vec2{60, 0};
        seen.body.p = seen.roleHome = home;
        w.clock.setHour(6);
        rolePass(w);
        require(count(w, R::night) == 1 && seen.role == R::night,
                "the night shift did not end out of sight only");
        seen.body.p = seen.roleHome = kerb;
        rolePass(w);
        require(count(w, R::night) == 0 && seen.node != seen.next, "a night worker stayed after dawn");
    }

    // Habits travel with the save; a file without them is the city of an earlier build.
    const auto file = std::filesystem::temp_directory_path() / "promzona-people.json";
    std::filesystem::remove(file);
    w.clock.setHour(23);
    for (int pass = 0; pass < 6; ++pass)
        rolePass(w);
    require(count(w, R::night) >= 5, "no night shift to save");
    const size_t trooper = walker(w, true);
    w.people[trooper].uniform = U::army;
    w.save(file);
    World loaded;
    loaded.load(file);
    for (size_t i = 0; i < w.people.size(); ++i)
        require(loaded.people[i].role == w.people[i].role &&
                    (loaded.people[i].roleHome - w.people[i].roleHome).length() < 1e-9,
                "habit was not saved");
    require(loaded.people[trooper].uniform == U::army, "uniform of the force was not saved");
    nlohmann::json j;
    {
        std::ifstream in(file, std::ios::binary);
        j = nlohmann::json::parse(in);
    }
    auto rejects = [&](const nlohmann::json &document)
    {
        std::ofstream(file, std::ios::binary | std::ios::trunc) << document.dump();
        try
        {
            World broken;
            broken.load(file);
        }
        catch (const std::exception &)
        {
            return true;
        }
        return false;
    };
    const size_t settled = size_t(std::ranges::find(w.people, R::night, &Person::role) - w.people.begin());
    auto bad = j;
    bad["people"][settled]["role"][0] = 4;
    require(rejects(bad), "unknown habit was accepted");
    bad = j;
    bad["people"][settled]["role"][1] = 5000;
    require(rejects(bad), "habit outside the city was accepted");
    bad = j;
    bad["people"][trooper]["uniform"] = 6;
    require(rejects(bad), "unknown uniform was accepted");
    bad = j;
    bad["people"][trooper]["role"] = {1, 0, 0};
    require(rejects(bad), "a habit in uniform was accepted");
    for (auto &entry : j["people"])
        entry.erase("role");
    require(!rejects(j), "a save without habits was refused");
    loaded.load(file);
    loaded.dispatchAt = loaded.patrolScanAt = loaded.nextRescueDispatch = 1e9;
    require(count(loaded, R::beach) == 0 && count(loaded, R::vagrant) == 0, "habits came from nowhere");
    loaded.dayNightCycle = false;
    loaded.clock.setHour(12);
    for (int pass = 0; pass < 30; ++pass)
        rolePass(loaded);
    // One newcomer per pass on the beach; the homeless settle where they stand at once.
    require(count(loaded, R::beach) >= 25 && count(loaded, R::vagrant) >= 10 && count(loaded, R::night) == 0,
            "an earlier save does not get its habits back");
    for (const auto &p : loaded.people)
        require(p.role != R::beach || loaded.outOfSight(p.roleHome), "a visitor appeared in view");
    std::filesystem::remove(file);
}

// Crews in the colours of their force, and what becomes of them when the chase is over.
void duties()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    struct Force
    {
        unsigned model;
        U uniform;
        int crew;
        WeaponId weapon;
    };
    size_t slot = 1;
    for (const Force &force :
         {Force{3, U::police, 2, WeaponId::pistol}, Force{4, U::rosguard, 4, WeaponId::automatic},
          Force{5, U::fsb, 2, WeaponId::automatic}, Force{6, U::army, 4, WeaponId::automatic}})
    {
        // A traffic car on an ordinary street stands in for the response vehicle; a crew
        // gets out only where every one of them has room to stand.
        const auto uniformed = std::ranges::count(w.people, force.uniform, &Person::uniform);
        Car *unit = nullptr;
        for (; !unit; ++slot)
        {
            auto &c = w.cars.at(slot);
            if (c.line >= 0 || c.kind != 0 || c.body.roadLevel || w.carEngaged(int(slot)) ||
                (c.body.p - w.position()).length() < 320 || !w.layout.urbanSite(c.body.p, 12))
                continue;
            fitVehicle(c, force.model);
            c.body.v = {};
            if (w.unloadCrew(c))
                unit = &c;
        }
        auto &car = *unit;
        int found = 0;
        for (const auto &p : w.people)
            if (p.uniform == force.uniform && (p.body.p - car.body.p).length() < 8)
            {
                ++found;
                require(p.officer() && p.sidearm == force.weapon && p.role == R::none,
                        "crew is not armed or dressed for its force");
                require(appearance(p).officer() && appearance(p).model == 0, "crew look is wrong");
            }
        require(found == force.crew &&
                    std::ranges::count(w.people, force.uniform, &Person::uniform) == uniformed + force.crew,
                "crew size is wrong");
    }
    // Unloading a crew is a crime in view of witnesses; this part is about what follows.
    w.wanted = 0;
    const auto officers = std::ranges::count_if(w.people, &Person::officer);
    require(officers == 12, "officers are missing");
    // While the player is wanted they stay on duty.
    w.wanted = 2;
    rolePass(w);
    require(std::ranges::count_if(w.people, &Person::officer) == officers && w.dutiesEnded == 0,
            "officers left during a chase");
    // An officer in view stays what he is; so does the one making an arrest.
    w.wanted = 0;
    w.wantedCooldown = 1e9;
    size_t near = 0, busy = 0;
    for (size_t i = 0, n = 0; i < w.people.size(); ++i)
        if (w.people[i].officer())
            (n++ ? busy : near) = i;
    w.people[near].body.p = w.position() + Vec2{40, 0};
    w.patrol.officer = int(busy);
    rolePass(w);
    require(w.people[near].officer() && w.people[busy].officer() && w.dutiesEnded == int(officers) - 2,
            "the wrong officers went off duty");
    for (const auto &p : w.people)
        require(p.officer() ||
                    p.sidearm == (p.dead ? WeaponId::none : Person::civilianWeapon(w.seed, p.body.id)) ||
                    p.uniform != U::none,
                "a citizen kept the service weapon");
    w.patrol.officer = -1;
    w.people[near].body.p = w.people[busy].body.p;
    rolePass(w);
    require(std::ranges::none_of(w.people, &Person::officer), "officers stayed in the streets");
    // An ambulance crew set down far away turns back into citizens too.
    const size_t medic = walker(w, true);
    w.people[medic].uniform = U::medical;
    rolePass(w);
    require(w.people[medic].uniform == U::none, "a medic without a call stayed in the coat");
}

// The walk signal and the standing car.
void streets()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    // A corner with lights whose northern arm exists, and the block west of it.
    int corner = -1;
    for (int n = 0; n < World::nodes * World::nodes && corner < 0; ++n)
    {
        const int i = n % World::nodes, j = n / World::nodes;
        if (i > 0 && w.layout.degree(n) == 4 && w.layout.road(i, j, 0) &&
            w.layout.urbanSite(World::nodePoint(n), 30) && !w.levelAt(World::nodePoint(n), 40) &&
            (World::nodePoint(n) - w.position()).length() > 320)
            corner = n;
    }
    require(corner >= 0, "no ordinary crossing");
    const int ci = corner % World::nodes, cj = corner / World::nodes;
    const Vec2 centre = World::nodePoint(corner);
    auto at = [&](double phase)
    {
        // The junction clock is offset by its place in the grid.
        w.time = 28 * 50 + phase - (ci * 3 + cj * 5) % 9;
        require(std::abs(CityLayout::signalPhase(ci, cj, w.time) - phase) < 1e-9, "signal phase is wrong");
    };
    const size_t who = walker(w, true);
    auto &p = w.people[who];
    p.node = corner - 1;
    p.next = corner;
    p.target = centre + Vec2{9.1, 9.1};
    const double kerb = w.layout.roadHalf();
    p.body.p = centre + Vec2{-kerb - .5, 9.1};
    p.body.height = w.terrain.height(p.body.p) + .85;
    // Eastbound walkers cross the north-south street: its cars have green from 14 to 26.
    at(15);
    require(std::abs(w.kerbWait(p) - 11) < 1e-9, "walker steps out in front of green traffic");
    at(26.5);
    require(w.kerbWait(p) == 0, "walker waits although the cars have just stopped");
    at(5.5);
    require(w.kerbWait(p) == 0, "walker waits with eight and a half seconds to spare");
    at(7);
    require(std::abs(w.kerbWait(p) - 19) < 1e-9, "walker starts seven seconds before the cars");
    // Away from the kerb nobody waits, and neither does anybody at an arm with no street.
    at(15);
    p.body.p = centre + Vec2{-kerb - 3, 9.1};
    require(w.kerbWait(p) == 0, "walker waits in the middle of the block");
    p.body.p = centre + Vec2{-2, 9.1};
    require(w.kerbWait(p) == 0 && w.onCrossing(p, true), "walker in the road is not hurrying");
    at(27);
    require(w.onCrossing(p, false) && !w.onCrossing(p, true), "walker hurries under a walk signal");
    {
        const int top = World::nodes * (World::nodes - 1) + ci;
        require(!w.layout.road(ci, World::nodes - 1, 0), "the city has a street beyond its edge");
        Person edge = p;
        edge.node = top - 1;
        edge.next = top;
        edge.body.p = World::nodePoint(top) + Vec2{-kerb - .5, 9.1};
        at(15);
        w.time += (ci * 3 + cj * 5) % 9 - (ci * 3 + (World::nodes - 1) * 5) % 9;
        require(w.kerbWait(edge) == 0, "walker waits where there is nothing to cross");
    }
    // Westbound the crossing comes first: the walker leaves the corner's own anchor.
    p.node = corner;
    p.next = corner - 1;
    p.body.p = centre + Vec2{kerb + .5, 9.1};
    at(20);
    require(std::abs(w.kerbWait(p) - 6) < 1e-9, "westbound walker ignores the lights");
    // Northbound walkers cross the east-west street, whose cars have green from 0 to 12.
    p.node = corner - World::nodes;
    p.next = corner;
    p.body.p = centre + Vec2{9.1, -kerb - .5};
    at(3);
    require(std::abs(w.kerbWait(p) - 9) < 1e-9, "northbound walker ignores the lights");
    at(13);
    require(w.kerbWait(p) == 0, "northbound walker waits for nothing");

    // In the street: the player watches a walker wait for the lights and then cross.
    {
        w.controlledCar = -1;
        w.player.p = centre + Vec2{-30, 12};
        w.player.height = w.terrain.height(w.player.p) + .85;
        p.node = corner - 1;
        p.next = corner;
        p.target = centre + Vec2{9.1, 9.1};
        p.body.p = centre + Vec2{-kerb - 3, 9.1};
        p.body.height = w.terrain.height(p.body.p) + .85;
        p.body.v = {};
        p.fear = 0;
        at(14.5);
        double furthest = -1e9;
        while (CityLayout::signalPhase(ci, cj, w.time) < 25.5)
        {
            w.step({}, frame);
            furthest = std::max(furthest, p.body.p.x - centre.x);
            // Traffic passing the kerb is no reason to run.
            require(p.fear == 0, "a car in its lane frightened the walker at the kerb");
        }
        require(furthest < -kerb - .6 && furthest > -kerb - 1.6 && p.body.v.length() < .2,
                "walker did not wait at the kerb");
        // Cars waiting at the lights stand with their noses over the walked line; allow for
        // the squeeze past them.
        for (int i = 0; i < 60 * 15 && p.body.p.x - centre.x < kerb; ++i)
            w.step({}, frame);
        require(p.body.p.x - centre.x >= kerb && !p.dead, "walker did not cross on the walk signal");
    }
}

// People on the same line of the pavement: before they gave each other room, two walkers
// who met head-on pushed each other like wrestlers, and whoever came up behind somebody
// waiting at the kerb pushed them towards the road.
void passing()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    int corner = -1;
    for (int n = 0; n < World::nodes * World::nodes && corner < 0; ++n)
    {
        const int i = n % World::nodes, j = n / World::nodes;
        if (i > 0 && j + 9 < World::nodes && w.layout.degree(n) == 4 && w.layout.road(i, j, 0) &&
            w.layout.urbanSite(World::nodePoint(n), 30) && !w.levelAt(World::nodePoint(n), 40) &&
            (World::nodePoint(n) - w.position()).length() > 320)
            corner = n;
    }
    require(corner >= 0, "no ordinary crossing");
    const int ci = corner % World::nodes, cj = corner / World::nodes;
    const Vec2 centre = World::nodePoint(corner);
    const double kerb = w.layout.roadHalf();
    w.controlledCar = -1;
    w.player.p = centre + Vec2{-30, 14};
    w.player.height = w.terrain.height(w.player.p) + .85;
    // The cast comes from elsewhere; whoever was about goes eight blocks north.
    std::vector<Person *> cast;
    for (size_t k = 0; k < w.people.size() && cast.size() < 4; ++k)
        if (plain(w, k) && (w.people[k].body.p - centre).length() > 200)
            cast.push_back(&w.people[k]);
    require(cast.size() == 4, "nobody to walk the block");
    for (auto &p : w.people)
        if (p.riding < 0 && std::ranges::find(cast, &p) == cast.end() && (p.body.p - centre).length() < 70)
        {
            const Vec2 away{0, 8 * World::spacing};
            p.body.p += away;
            p.target += away;
            p.roleHome += away;
            p.node += 8 * World::nodes;
            p.next += 8 * World::nodes;
        }
    auto put = [&](Person &p, int from, int to, Vec2 where)
    {
        p.node = from;
        p.next = to;
        p.target = World::nodePoint(to) + Vec2{9.1, 9.1};
        p.body.p = centre + where;
        p.body.height = w.terrain.height(p.body.p) + .85;
        p.body.v = {};
        p.fear = 0;
    };
    // Cars along z have green: whoever walks east waits at the kerb.
    w.time = 28 * 50 + 15 - (ci * 3 + cj * 5) % 9;
    auto &waiter = *cast[0], &follower = *cast[1], &east = *cast[2], &west = *cast[3];
    put(waiter, corner - 1, corner, {-kerb - 1.1, 9.1});
    put(follower, corner - 1, corner, {-kerb - 6, 9.1});
    put(east, corner - 1, corner, {-26, 9.1});
    put(west, corner, corner - 1, {-16, 9.1});
    require(w.kerbWait(waiter) > 9, "the waiter has nothing to wait for");
    const Vec2 spot = waiter.body.p;
    double pushed = 0, closest = 1e9, widest = 0;
    for (int i = 0; i < 60 * 7; ++i)
    {
        w.step({}, frame);
        pushed = std::max(pushed, (waiter.body.p - spot).length());
        closest = std::min(closest, (east.body.p - west.body.p).length());
        for (const Person *p : {&east, &west, &follower})
        {
            widest = std::max(widest, std::abs(p->body.p.z - centre.z - 9.1));
            require(w.layout.roadDistance(p->body.p) >= kerb, "somebody stepped into the road to pass");
        }
    }
    require(pushed < .15, "somebody waiting at the kerb is pushed along");
    const double reached = follower.body.p.x - centre.x;
    require(reached < -kerb && reached > -kerb - 1.65 && follower.body.v.length() < .2 &&
                (follower.body.p - waiter.body.p).length() > .6,
            "the second walker does not wait beside the first");
    require(east.body.p.x - west.body.p.x > 4 && closest > .58 && widest < 1.3,
            "walkers who meet head-on do not pass each other");

    // A company whose followers have got in front of the leader, as after a fright: they let
    // him by and fall in behind. They used to make for their places straight through him.
    {
        size_t lead = 0;
        auto fit = [&](size_t i)
        {
            const auto &m = w.people[i];
            return m.inCompany && m.riding < 0 && !m.dead && m.fear <= 0 && m.role == R::none &&
                   (m.body.p - centre).length() > 200;
        };
        while (lead + 2 < w.people.size() && !(fit(lead) && fit(lead + 1) && fit(lead + 2)))
            lead += 28;
        require(lead + 2 < w.people.size(), "no company of three");
        const Vec2 places[]{{-24, 9.1}, {-23.3, 9.25}, {-22.6, 8.95}};
        for (int k = 0; k < 3; ++k)
        {
            put(w.people[lead + k], corner - 1, corner, places[k]);
            w.people[lead + k].heading = pi * .5;
        }
        east.body.p += Vec2{0, 30};
        west.body.p += Vec2{0, 30};
        const auto &leader = w.people[lead];
        for (int i = 0; i < 60 * 9; ++i)
            w.step({}, frame);
        require(leader.body.p.x - centre.x > -24 + 6, "the company does not let its leader through");
        for (int k = 1; k < 3; ++k)
        {
            const Vec2 behind = w.people[lead + k].body.p - leader.body.p;
            require(behind.x < 0 && behind.length() < 3.5, "the company does not fall in behind its leader");
        }
        for (int k = 0; k < 3; ++k)
            w.people[lead + k].body.p += Vec2{0, 40};
    }

    // The side: the right when the way is blocked squarely, otherwise the nearer one, and
    // never the road.
    const Vec2 ahead{1, 0}, right = perpendicular(ahead);
    auto &p = east, &other = west;
    auto turn = [&](Vec2 from, double lateral, Vec2 pace)
    {
        p.body.p = centre + from;
        other.body.p = p.body.p + ahead * 1.1 + right * lateral;
        other.body.v = pace;
        p.body.height = other.body.height = w.terrain.height(p.body.p) + .85;
        Vec2 dir = ahead;
        w.passWalkers(p, {&p, &other}, dir, 1.3);
        require(std::abs(dir.length() - 1) < 1e-9, "the walking direction is not a direction");
        return dot(dir, right);
    };
    require(turn({-20, 11}, 0, {}) > .3, "a walker passes on the left");
    require(turn({-20, 11}, .25, {}) < -.3 && turn({-20, 11}, -.25, {}) > .3,
            "a walker passes on the far side");
    require(turn({-20, 11}, 0, ahead * 1.3) == 0, "a walker steps aside for somebody going the same way");
    require(turn({-20, 11}, .9, {}) == 0, "a walker steps aside for somebody beside the way");
    // A step off the pavement line towards the street is fine; off the kerb it is not.
    const double street = right.z > 0 ? -1 : 1;
    require(turn({-20, kerb + 1.6}, .25 * street, {}) * street < -.3,
            "a walker avoids the side by the street");
    require(turn({-20, kerb + .35}, .25 * street, {}) * street < -.3 &&
                turn({-20, kerb + .35}, -.25 * street, {}) * street < -.3,
            "a walker steps off the kerb to pass");
}

// Somebody who stops at a piece of litter sets off again. A body at rest that leans on a
// sleeping one used not to wake it: a cigarette packet held a walker at the kerb for good,
// and with him the truck that was waiting for him to cross.
void litter()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    int corner = -1;
    for (int n = 0; n < World::nodes * World::nodes && corner < 0; ++n)
    {
        const int i = n % World::nodes, j = n / World::nodes;
        if (i > 0 && w.layout.degree(n) == 4 && w.layout.road(i, j, 0) &&
            w.layout.urbanSite(World::nodePoint(n), 30) && !w.levelAt(World::nodePoint(n), 40) &&
            (World::nodePoint(n) - w.position()).length() > 320)
            corner = n;
    }
    require(corner >= 0, "no ordinary crossing");
    const Vec2 centre = World::nodePoint(corner);
    w.controlledCar = -1;
    w.player.p = centre + Vec2{-30, 14};
    w.player.height = w.terrain.height(w.player.p) + .85;
    for (auto &p : w.people)
        if (p.riding < 0 && (p.body.p - centre).length() < 60)
            p.body.p += Vec2{0, 8 * World::spacing};
    const size_t actor = walker(w, true);
    auto &p = w.people[actor];
    p.node = corner - 1;
    p.next = corner;
    p.target = centre + Vec2{9.1, 9.1};
    p.body.p = centre + Vec2{-22, 9.1};
    p.body.height = w.terrain.height(p.body.p) + .85;
    p.body.v = {};
    p.fear = 0;
    size_t packet = 0;
    while (packet < w.fixtures.size() && w.fixtures[packet].kind != World::FixtureKind::packet)
        ++packet;
    require(packet < w.fixtures.size(), "no cigarette packet in the city");
    auto &f = w.fixtures[packet];
    auto &b = w.staticBodies[f.body];
    // Touching, straight in the way.
    b.p = f.home = p.body.p + Vec2{p.body.radius + b.radius + .004, 0};
    b.v = {};
    b.sleeping = true;
    w.supportFixture(f);
    w.staticQueryCount = size_t(-1);
    const double from = p.body.p.x;
    for (int i = 0; i < 60 * 4; ++i)
        w.step({}, frame);
    require(p.body.p.x - from > 3, "a cigarette packet holds a walker who stopped at it");
    require(f.displaced, "the packet in the way was not kicked along");

    // What cannot be kicked along is gone round. A lamp post met almost squarely held a
    // walker by friction, and so did anything with a flat side across the way.
    auto meet = [&](Vec2 where, Vec2 obstacle, Vec2 half, const char *message)
    {
        p.target = centre + Vec2{9.1, 9.1};
        p.body.p = centre + where;
        p.body.height = w.terrain.height(p.body.p) + .85;
        p.body.v = {1.3, 0};
        p.blockedFor = p.roundUntil = 0;
        p.roundSide = 0;
        Body post;
        post.id = 0x7f000000u + std::uint32_t(w.staticBodies.size());
        post.p = centre + obstacle;
        post.halfW = half.x;
        post.halfH = half.z;
        post.height = w.terrain.height(post.p) + 1;
        post.verticalExtent = 2;
        w.staticBodies.push_back(post);
        double nearest = 1e9;
        for (int i = 0; i < 60 * 9; ++i)
        {
            w.step({}, frame);
            nearest = std::min(nearest, w.layout.roadDistance(p.body.p));
        }
        require(p.body.p.x - centre.x > obstacle.x + 3, message);
        require(nearest >= w.layout.roadHalf(), "the way round an obstacle leads through the road");
        w.staticBodies.pop_back();
    };
    meet({-22, 8.03}, {-21.648, 8.203}, {.09, .09}, "a lamp post holds whoever walks into it");
    meet({-22, 9.1}, {-21.4, 9.1}, {.15, .7}, "a wall across the pavement holds whoever walks into it");
    // Between the wall and the kerb there is no room: the way round is on the other side.
    meet({-22, 8.2}, {-21.4, 8.2}, {.15, .85}, "a wall at the kerb holds whoever walks into it");
}

void parking()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    const Vec2 start = w.position();
    require(w.cars[0].terrainGradient.length() == 0, "start state changed");
    for (int i = 0; i < 60 * 8; ++i)
        w.step({}, frame);
    // The first street falls away to the north: before the hold the car rolled off by itself.
    require(w.cars[0].terrainGradient.length() > .02, "the test street is flat");
    require((w.position() - start).length() < .01 && w.cars[0].body.v.length() == 0 && w.cars[0].braking,
            "a car left alone rolls away");
    PlayerInput drive;
    drive.car.throttle = 1;
    for (int i = 0; i < 180; ++i)
        w.step(drive, frame);
    require((w.position() - start).length() > 5, "the hold keeps a driven car");
    // Coasting is not held: only a standing car is.
    const double coasting = w.cars[0].body.v.length();
    w.step({}, frame);
    require(coasting > 3 && w.cars[0].body.v.length() > coasting - .5 && !w.cars[0].braking,
            "letting go of the pedal brakes the car");
    PlayerInput stop;
    stop.car.brake = true;
    for (int i = 0; i < 60 * 6; ++i)
        w.step(stop, frame);
    const Vec2 rest = w.position();
    for (int i = 0; i < 60 * 20; ++i)
        w.step(parked(), frame);
    require((w.position() - rest).length() < 1e-9 && w.cars[0].body.v.length() == 0,
            "a braked car creeps across the slope");
}
} // namespace

int main()
{
    try
    {
        looks();
        paces();
        habits();
        duties();
        streets();
        passing();
        litter();
        parking();
        std::cout << "people tests passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
