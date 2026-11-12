#include "promzona/world.hpp"
#include "promzona/fleet.hpp"

namespace pz
{
namespace
{
// SpawnDistance of the managed game: nothing appears or disappears nearer to the player.
constexpr double spawnDistance = 245;
// A body nobody treated, a wreck and a sunk car leave after this many seconds.
constexpr double corpseSeconds = 180, wreckSeconds = 6, sinkSeconds = 3;
// A driver who has not moved for this long is in a jam that will not clear by itself.
constexpr double jamSeconds = 120;
// Managed thresholds: a walker drowns, a car sinks, a fire goes out.
constexpr double drownDepth = 1.15, sinkDepth = .85;
} // namespace

// The drone is a second pair of eyes: what it hovers over must not change either.
bool World::outOfSight(Vec2 at) const
{
    const double limit = spawnDistance * spawnDistance;
    return (at - position()).length2() > limit && (!drone.active || (at - drone.position).length2() > limit);
}

double World::waterDepth(Vec2 point, double feet) const
{
    if (feet >= CityLayout::waterLevel)
        return 0;
    return std::max(0., CityLayout::waterLevel - terrain.bedHeight(point.x, point.z));
}

// Port of the managed WaterStep and of the water branch of VehicleLife: a walker out of
// its depth drowns, a car in the canal sinks and is out of play.
void World::waterStep()
{
    nextWaterCheck = time + .25;
    for (auto &p : people)
        if (!p.dead && p.riding < 0 && !p.body.roadLevel &&
            waterDepth(p.body.p, p.body.height - .85) > drownDepth)
            drownPerson(p);
    for (size_t i = 0; i < cars.size(); ++i)
    {
        auto &c = cars[i];
        if (int(i) == controlledCar || c.falling || c.line >= 0 || c.body.roadLevel)
            continue;
        if (waterDepth(c.body.p, c.body.height - .8) > sinkDepth)
            sinkCar(c);
    }
    // An animal on the ground in water it cannot stand in is lost, as in the managed game.
    for (auto &a : animals)
        if (a.deadUntil == 0 && a.altitude - terrain.height(a.p) < .5 && waterDepth(a.p, a.altitude) > .75)
        {
            splash(a.p, .4);
            a.deadUntil = time + 14;
            a.velocity = {};
            ++drownings;
        }
}

void World::sinkCar(Car &c)
{
    c.falling = true;
    c.driverless = true;
    c.personal = c.responding = c.seesSuspect = false;
    c.fireTime = 0;
    c.lastDamageAt = time;
    c.body.v *= .2;
    c.body.w *= .2;
    ++sunkCars;
    splash(c.body.p, 2.2);
}

void World::drownPerson(Person &p)
{
    splash(p.body.p, .8);
    killPerson(p, p.body.v * .2, false, true);
    ++drownings;
}

bool World::personEngaged(int actor) const
{
    return actor == patrol.officer || actor == conversation.actor || paramedicCall(actor) ||
           (job.kind != JobKind::none && job.actor == actor) ||
           std::ranges::any_of(rescues, [&](const Rescue &r) { return r.patient == actor; }) ||
           std::ranges::any_of(hailers, [&](const StreetHail &h) { return h.actor == actor; });
}

bool World::carEngaged(int car) const
{
    const auto &c = cars[car];
    return car == controlledCar || car == vehicleEntry.car || car == patrol.car || ambulanceCall(car) ||
           c.line >= 0 || c.personal || c.summoned || c.responding ||
           (job.kind != JobKind::none && (job.actor == car || job.ride.car == car)) ||
           std::ranges::any_of(people, [&](const Person &p) { return p.riding == car; });
}

// A new passer-by takes the slot of a body the player left behind. The slot keeps its
// stable roles (courier, scooter owner); everything about the previous person is dropped.
bool World::renewPerson(int actor)
{
    auto &old = people[actor];
    Scooter *scooter = old.ownedScooter >= 0 ? &scooters[old.ownedScooter] : nullptr;
    if (scooter && (scooter->rider != Scooter::free || !outOfSight(scooter->body.p)))
        return false;
    for (int attempt = 0; attempt < 16; ++attempt)
    {
        const int node = random.index(nodes * nodes), next = chooseWalkNext(node, -1);
        if (next == node)
            continue;
        const Vec2 from = nodePoint(node) + Vec2{9.1, 9.1}, target = nodePoint(next) + Vec2{9.1, 9.1};
        const Vec2 at = from + (target - from) * random.next();
        const double centre = terrain.height(at) + .85;
        if (!outOfSight(at) || !layout.urbanSite(at, .5) || openCut(at, .4) ||
            !vehicleSpace(at, centre, nullptr, true))
            continue;
        Person fresh;
        fresh.body = old.body;
        fresh.body.p = at;
        fresh.body.v = {};
        fresh.body.w = 0;
        fresh.body.roadLevel = 0;
        fresh.body.height = centre;
        fresh.body.verticalSpeed = 0;
        fresh.body.wake();
        fresh.node = node;
        fresh.next = next;
        fresh.target = target;
        fresh.heading = std::atan2(target.x - at.x, target.z - at.z);
        fresh.style = random.index(14);
        fresh.phase = random.next() * 8;
        fresh.sidearm = Person::civilianWeapon(seed, fresh.body.id);
        fresh.courier = old.courier;
        fresh.ownedScooter = old.ownedScooter;
        fresh.distance = old.distance;
        fresh.deliveryDistance = fresh.distance + 115 + random.next() * 90;
        old = fresh;
        if (scooter)
        {
            scooter->owner = scooter->rider = actor;
            scooter->body.v = {};
            scooter->body.w = 0;
            scooter->returnAt = 0;
        }
        ++renewedPeople;
        return true;
    }
    return false;
}

// The slot of a wreck, a sunk or an abandoned car goes back into traffic on a road the
// player cannot see, as SpawnCar refills the managed city.
bool World::renewCar(int slot)
{
    for (int attempt = 0; attempt < 12; ++attempt)
    {
        Car car;
        car.id = cars[slot].id;
        car.body.id = cars[slot].body.id;
        fitVehicle(car, streetModel(slot));
        car.body.friction = .5;
        car.laneIndex = layout.wide() ? random.index(2) : 0;
        if (car.kind == 5 && layout.wide())
            car.laneIndex = 1;
        car.routeFrom = random.index(nodes * nodes);
        if (neighbours(car.routeFrom).empty())
            continue;
        car.routeTo = chooseNext(car.routeFrom, -1);
        car.routePhase = 1;
        newRoute(car);
        car.routeDistance = random.next() * car.route->length();
        const auto [at, heading] = car.route->sample(car.routeDistance);
        if (!outOfSight(at))
            continue;
        if (std::ranges::any_of(cars, [&](const Car &other)
                                { return &other != &cars[slot] && (other.body.p - at).length2() < 64; }) ||
            std::ranges::any_of(people, [&](const Person &p) { return (p.body.p - at).length2() < 16; }))
            continue;
        car.body.p = at;
        car.body.angle = -heading;
        car.body.roadLevel = car.routeLevel;
        car.body.height = surface(at, car.body.roadLevel) + .8;
        cars[slot] = std::move(car);
        ++renewedCars;
        return true;
    }
    return false;
}

// Port of the managed LifeAfterContacts / VehicleLifeAfter rules. The managed game removes
// such actors and spawns new ones; native slots are stable, so the slot itself is renewed.
void World::renewCity()
{
    nextRenewal = time + 2;
    int budget = 4;
    for (size_t n = 0; n < people.size() && budget; ++n)
    {
        const size_t i = (renewalCursor + n) % people.size();
        const auto &p = people[i];
        if (!p.dead || time - p.diedAt < corpseSeconds || !outOfSight(p.body.p) || personEngaged(int(i)))
            continue;
        if (renewPerson(int(i)))
            --budget;
    }
    renewalCursor = (renewalCursor + 97) % std::max<size_t>(1, people.size());
    budget = 2;
    // Slot 0 is the player's own car, as in the managed game; response and summoned
    // vehicles beyond the traffic population have their own reuse rules.
    for (size_t i = 1; i < std::min(cars.size(), trafficPopulation) && budget; ++i)
    {
        auto &c = cars[i];
        if ((c.body.p - c.stallAnchor).length2() > 1 || c.stallSince > time)
        {
            c.stallAnchor = c.body.p;
            c.stallSince = time;
        }
        // The last resort against a standstill: nothing in the rules of the road should
        // produce one, and whatever still does must not freeze the city behind it.
        if (!c.destroyed && !c.falling && !c.driverless && time - c.stallSince > jamSeconds &&
            outOfSight(c.body.p) && !carEngaged(int(i)))
        {
            if (renewCar(int(i)))
            {
                --budget;
                ++clearedJams;
            }
            continue;
        }
        if (!(c.destroyed || c.falling || c.driverless) || carEngaged(int(i)))
            continue;
        const bool far = outOfSight(c.body.p);
        const bool wreck = c.destroyed && far && time - c.lastDamageAt > wreckSeconds;
        // A sunk car that the water covers completely may leave in view, as it does in C#.
        const bool sunk = c.falling && time - c.lastDamageAt > sinkSeconds &&
                          (far || CityLayout::waterLevel - (c.body.height - .8) > c.body.verticalExtent + .3);
        const bool abandoned = c.driverless && !c.destroyed && !c.falling && far;
        if ((wreck || sunk || abandoned) && renewCar(int(i)))
            --budget;
    }
}
} // namespace pz
