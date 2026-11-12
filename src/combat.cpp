#include "promzona/world.hpp"
#include <deque>
#include <limits>

namespace pz
{
namespace
{
constexpr double noHit = std::numeric_limits<double>::infinity();
constexpr std::array<double, 6> wantedCost{1, 1.6, 2.5, 3.8, 5.5, 7.5};
bool slab(double origin, double direction, double extent, double &near, double &far)
{
    if (std::abs(direction) < 1e-10)
        return std::abs(origin) <= extent;
    double lo = (-extent - origin) / direction, hi = (extent - origin) / direction;
    if (lo > hi)
        std::swap(lo, hi);
    near = std::max(near, lo);
    far = std::min(far, hi);
    return near <= far;
}
} // namespace

// Port of CityTraffic's slab ray tests; test all three axes, including entering a roof.
double rayPrism(Vec2 origin, Vec2 direction, double y, double slope, const Body &b, double limit)
{
    auto o = rotate(origin - b.p, -b.angle), d = rotate(direction, -b.angle);
    double near = 0, far = limit;
    return slab(o.x, d.x, b.halfW, near, far) && slab(o.z, d.z, b.halfH, near, far) &&
                   slab(y - b.height, slope, b.verticalExtent * .5, near, far) && far >= 0
               ? near
               : noHit;
}
double rayPerson(Vec2 origin, Vec2 direction, double y, double slope, const Body &b, double limit)
{
    auto delta = b.p - origin;
    double along = dot(delta, direction), side = delta.length2() - along * along;
    if (side > b.radius * b.radius)
        return noHit;
    double half = std::sqrt(std::max(0., b.radius * b.radius - side));
    double near = std::max(0., along - half), far = std::min(limit, along + half);
    if (near > far || !slab(y - b.height, slope, b.verticalExtent * .5, near, far))
        return noHit;
    return near;
}
double wantedPoints(double level)
{
    if (!std::isfinite(level))
        return 0;
    double points = 0;
    for (int i = 0; i < 6; i++)
        points += clamp(level - i, 0., 1.) * wantedCost[i];
    return points;
}
double wantedFromPoints(double points)
{
    if (!std::isfinite(points))
        return 0;
    points = std::max(0., points);
    for (int i = 0; i < 6; i++)
    {
        if (points <= wantedCost[i])
            return i + points / wantedCost[i];
        points -= wantedCost[i];
    }
    return 6;
}
std::span<const int> policeRoster(int level)
{
    static constexpr std::array<int, 9> one{3, 6}, two{3, 6, 3}, three{3, 6, 3, 7, 7},
        four{3, 6, 7, 7, 13, 13}, five{3, 6, 7, 7, 13, 13, 14, 14}, six{3, 6, 7, 7, 13, 13, 14, 14, 12};
    switch (level)
    {
    case 1:
        return {one.data(), 2};
    case 2:
        return {two.data(), 3};
    case 3:
        return {three.data(), 5};
    case 4:
        return {four.data(), 6};
    case 5:
        return {five.data(), 8};
    case 6:
        return six;
    default:
        return {};
    }
}

AttackHit World::traceAttack(Vec2 from, Vec2 dir, double y, double slope, double range, int ignoreCar,
                             bool includePlayer, const Person *shooter) const
{
    AttackHit hit{HitKind::none, -1, range};
    // The visible water is a horizontal plane clipped by the bed. A nearer deck,
    // bank, vehicle or person still wins the same closest-hit test.
    if (slope < 0 && y >= CityLayout::waterLevel)
    {
        double at = (CityLayout::waterLevel - y) / slope;
        if (at <= range)
        {
            Vec2 point = from + dir * at;
            if (terrain.bedHeight(point.x, point.z) < CityLayout::waterLevel)
                hit = {HitKind::water, -1, at};
        }
    }
    for (double at = .25; at <= hit.distance; at += .25)
        if (scenerySolid(from + dir * at, y + slope * at, .03))
        {
            hit = {HitKind::scenery, -1, at};
            break;
        }
    for (auto &b : staticBodies)
    {
        double at = rayPrism(from, dir, y, slope, b, hit.distance);
        if (at < hit.distance)
            hit = {HitKind::scenery, -1, at};
    }
    for (size_t i = 0; i < cars.size(); i++)
    {
        if (int(i) == ignoreCar || cars[i].falling)
            continue;
        double at = rayPrism(from, dir, y, slope, cars[i].body, hit.distance);
        if (at < hit.distance)
            hit = {HitKind::car, int(i), at};
    }
    for (size_t i = 0; i < people.size(); i++)
    {
        if (&people[i] == shooter || people[i].dead || people[i].riding >= 0)
            continue;
        double at = rayPerson(from, dir, y, slope, people[i].body, hit.distance);
        if (at < hit.distance)
            hit = {HitKind::person, int(i), at};
    }
    if (includePlayer && controlledCar < 0 && health > 0)
    {
        double at = rayPerson(from, dir, y, slope, player, hit.distance);
        if (at < hit.distance)
            hit = {HitKind::player, -1, at};
    }
    return hit;
}
bool World::blastVisible(Vec2 from, double y, Vec2 to, double targetY) const
{
    auto delta = to - from;
    double distance = delta.length();
    if (distance < .01)
        return true;
    auto dir = delta / distance;
    double slope = (targetY - y) / distance;
    for (auto &b : staticBodies)
        if (rayPrism(from, dir, y, slope, b, distance) < distance - .01)
            return false;
    for (double at = .4; at < distance; at += .4)
        if (scenerySolid(from + dir * at, y + slope * at, .02))
            return false;
    return true;
}
void World::addCrime(double severity)
{
    if (!std::isfinite(severity) || severity <= 0 || health <= 0)
        return;
    if (job.passenger() && job.acquired && job.ride.car == controlledCar && job.ride.scares < 3 &&
        time - job.ride.crimeAt > .01)
    {
        ++job.ride.scares;
        job.ride.crimeAt = time;
        notify(L"Пассажир испуган. Оплата снижена");
    }
    wanted = wantedFromPoints(wantedPoints(wanted) + severity);
    wantedCooldown = 12;
    lastCrimeAt = time;
}
void World::reportCrime(double severity, double radius, bool sight)
{
    auto from = position();
    double y = controlledCar >= 0 ? cars[controlledCar].body.height : player.height;
    for (auto &p : people)
        if (!p.dead && (p.body.p - from).length2() < radius * radius &&
            (!sight || blastVisible(from, y, p.body.p, p.body.height)))
        {
            addCrime(severity);
            return;
        }
    for (auto &c : cars)
        if ((c.model == 3 || c.responding) && !c.destroyed &&
            &c != (controlledCar >= 0 ? &cars[controlledCar] : nullptr) &&
            (c.body.p - from).length2() < 70 * 70 &&
            (!sight || blastVisible(from, y, c.body.p, c.body.height)))
        {
            addCrime(severity);
            return;
        }
}
void World::killPerson(Person &p, Vec2 velocity, bool playerCaused, bool drowned)
{
    if (p.dead)
        return;
    if (p.ownedScooter >= 0 && scooters[p.ownedScooter].rider >= 0)
        releaseScooter(p.ownedScooter, velocity);
    p.riding = -1;
    p.waitingLine = p.waitingStop = -1;
    p.dead = true;
    p.diedAt = time;
    p.drowned = drowned;
    if (!drowned)
        addMark({p.body.p, p.body.p, time, 85., p.body.roadLevel, MarkKind::blood});
    p.rescueAfter = time + 7.;
    p.knockedUntil = p.injuredUntil = 0;
    p.health = 0;
    p.aimSeconds = p.alertUntil = 0;
    p.burstLeft = 0;
    p.burnUntil = 0;
    p.body.v = velocity;
    p.body.wake();
    if (job.kind == JobKind::elimination && &p == &people[job.actor])
        finishJob(playerCaused);
    else if (job.passenger() && &p == &people[job.actor])
        finishJob(false);
    if (!drowned)
        alarmWitnesses(p.body.p, p.body.height, playerCaused);
    if (p.sidearm != WeaponId::none && pickups.size() < 5000)
        pickups.push_back({p.body.p, p.sidearm, false, 1., false, true, p.body.roadLevel});
    p.sidearm = WeaponId::none;
    if (std::ranges::count_if(pickups, [](const Pickup &drop)
                              { return !drop.health && !drop.energy && drop.weapon == WeaponId::none; }) <
        240)
        pickups.push_back({p.body.p, WeaponId::none, false, 1., false, true, p.body.roadLevel});
    if (!drowned)
        sounds.push_back({"impact", p.body.p, .8});
}
void World::damageCar(Car &c, double amount, bool playerCaused)
{
    if (c.destroyed || c.falling || !std::isfinite(amount) || amount <= 0)
        return;
    if (godMode && controlledCar >= 0 && !transitPassenger() && &c == &cars[controlledCar])
        return;
    amount *= c.kind == 11 ? .11 : .5; // Original durability budget and tank armour.
    c.health = std::max(-.5, c.health - amount);
    c.lastDamageAt = time;
    c.safeDrivingTime = 0;
    if (playerCaused && (c.fireTime > 0 || c.health <= .32))
        c.firePlayerCaused = true;
    if (c.fireTime > 0)
        c.fireTime += amount * 12;
    else if (c.health <= .32)
    {
        c.fireTime = .001;
        sounds.push_back({"fire", c.body.p, .6});
    }
}
// CityTraffic.AfterContacts measures the solver's change in velocity, rather than
// speed alone: a fast car travelling without contact must not take damage.
void World::vehicleImpact(Car &c, double hit, Vec2 change)
{
    if (c.recovering)
        return;
    if (hit > 1.5 && std::isfinite(hit))
        c.applyImpactRotation(change);
    if (c.destroyed || c.impactCooldown > 0)
        return;
    if (!std::isfinite(hit) || hit <= 1.5)
        return;
    c.impactCooldown = .4;
    sounds.push_back({"impact", c.body.p, std::min(1., hit / 10)});
    if (controlledCar >= 0 && !transitPassenger() && &c == &cars[controlledCar] && hit > 2.5)
        reportCrime(std::min(.3, hit * .018));
    damageCar(c, hit * .025);
    c.verticalVelocity = std::max(c.verticalVelocity, hit > 6 ? std::min(2.8, (hit - 6) * .14) : 0.);
    if (particles.size() + 18 <= 5000)
        for (int n = 0; n < 18; n++)
            particles.push_back(
                {{float(c.body.p.x), float(c.body.height), float(c.body.p.z)},
                 {float(random.next() * 5 - 2.5), float(random.next() * 3), float(random.next() * 5 - 2.5)},
                 {1, .6f, .2f},
                 .4f,
                 .035f});
    if (c.health <= 0)
        destroyCar(c);
}
void World::destroyCar(Car &c)
{
    if (c.destroyed)
        return;
    c.destroy(); // Finite mass and continuing gravity/contact integration.
    c.fireTime = 0;
    explode(c.body.p, c.body.height, 1, true, c.firePlayerCaused);
}
void World::explode(Vec2 p, double y, double strength, bool vehicleBlast, bool playerCaused)
{
    struct Blast
    {
        Vec2 p;
        double y, strength;
        bool vehicle;
    };
    std::deque<Blast> pending{{p, y, strength, vehicleBlast}};
    // No recursive chain reactions. A car is marked destroyed before queuing its blast.
    while (!pending.empty())
    {
        auto b = pending.front();
        pending.pop_front();
        scareWildlife(b.p, b.y, 5. * b.strength);
        sounds.push_back({"explosion", b.p, .95});
        if (playerCaused)
            reportCrime(.45, 80, false);
        for (int i = 0; i < 95 && particles.size() < 5000; i++)
        {
            double a = random.next() * 2 * pi, r = random.next() * 8;
            particles.push_back({{float(b.p.x), float(b.y), float(b.p.z)},
                                 {float(std::cos(a) * r), float(random.next() * 10), float(std::sin(a) * r)},
                                 {1, float(.15 + random.next() * .55), .04f},
                                 float(1 + random.next() * 2),
                                 float(.12 + random.next() * .25)});
        }
        auto effect = [&](Vec2 target, double targetY, double radius)
        {
            double distance = std::hypot((target - b.p).length(), targetY - b.y);
            return distance < radius && blastVisible(b.p, b.y, target, targetY)
                       ? (1 - distance / radius) * b.strength
                       : 0.;
        };
        for (auto &c : cars)
        {
            if (c.falling)
                continue;
            double s = effect(c.body.p, c.body.height, b.vehicle ? 10 : 8);
            if (s <= 0)
                continue;
            auto d = c.body.p - b.p;
            c.body.impulse(d / std::max(.5, d.length()) * (s * (b.vehicle ? 6250 : 5000)));
            c.verticalVelocity += s * (b.vehicle ? 1.4 : 2);
            c.wreckSupported = false;
            damageCar(c, (b.vehicle ? 2.1 : 3.2) * s, playerCaused);
            if (c.health <= 0 && !c.destroyed)
            {
                c.destroy();
                c.fireTime = 0;
                pending.push_back({c.body.p, c.body.height, 1, true});
            }
        }
        for (auto &person : people)
        {
            if (person.dead)
                continue;
            double s = effect(person.body.p, person.body.height, b.vehicle ? 7.5 : 8);
            if (s <= 0)
                continue;
            person.fear = 2;
            if (playerCaused && person.sidearm != WeaponId::none)
                person.alertUntil = time + 8.;
            person.health -= b.vehicle ? 1 : s * 2;
            auto d = person.body.p - b.p;
            auto v = d / std::max(.5, d.length()) * (6 * s);
            person.body.v += v;
            if (person.health <= 0)
                killPerson(person, v, playerCaused);
        }
        if (controlledCar < 0)
            hurtPlayer(6 * effect(player.p, player.height, 8));
    }
}
void World::shoot(double heading, double pitch)
{
    if (shotCooldown > 0 || health <= 0)
        return;
    if (transitPassenger() || (controlledCar >= 0 && cars[controlledCar].destroyed))
        return;
    bool mounted = controlledCar >= 0 && cars[controlledCar].mountedGun();
    bool cannon = mounted && cars[controlledCar].kind == 11;
    auto w = mounted ? weapon(cannon ? WeaponId::bazooka : WeaponId::automatic) : inventory.active();
    if (!w && controlledCar >= 0)
        return;
    if (!mounted && w && !inventory.consume(w->id))
        return;
    shotCooldown = cannon ? 1.65 : w ? w->interval : .6;
    firing = true;
    auto start = position(), dir = forward(heading);
    double y =
        controlledCar >= 0 ? cars[controlledCar].body.height + (mounted ? 1.45 : .3) : player.height + .47;
    if (w)
        scareWildlife(start, y, 0);
    double slope = std::tan(clamp(pitch, -1.2, 1.2));
    reportCrime(w ? .055 : .015, w ? 60 : 5, false);
    if (w && w->id == WeaponId::flamethrower)
    {
        flame(dir, y);
        return;
    }
    sounds.push_back({w ? std::string(w->sound) : "punch", start, .75});
    if (w && w->id == WeaponId::bazooka)
    {
        double muzzle = controlledCar >= 0 ? cars[controlledCar].body.halfH + .3 : .8;
        auto hit = traceAttack(start, dir, y, slope, muzzle, controlledCar);
        if (hit.kind != HitKind::none)
        {
            if (hit.kind == HitKind::water)
                splash(start + dir * hit.distance, 2.);
            explode(start + dir * std::max(0., hit.distance - .18), y + hit.distance * slope, 1, false, true);
        }
        else
        {
            if (rockets.size() >= 16)
                rockets.erase(rockets.begin());
            rockets.push_back({start + dir * muzzle, dir, y + muzzle * slope, slope, 0, controlledCar});
        }
        return;
    }
    auto hit = traceAttack(start, dir, y, slope, w ? w->range : 1.55, controlledCar);
    if (w && hit.kind == HitKind::water)
        splash(start + dir * hit.distance, mounted ? .75 : .35);
    for (int i = 1; w && i < std::min(80, int(hit.distance * 2)); i++)
        particles.push_back(
            {{float(start.x + dir.x * i * .5), float(y + slope * i * .5), float(start.z + dir.z * i * .5)},
             {},
             {1, .7f, .22f},
             .08f,
             .04f});
    if (hit.kind == HitKind::car)
    {
        auto &c = cars[hit.index];
        damageCar(c, w ? w->carDamage : 0, true);
        if (c.health <= 0)
            destroyCar(c);
    }
    if (hit.kind == HitKind::person)
    {
        auto &p = people[hit.index];
        p.health -= w ? w->personDamage : .26;
        if (p.sidearm != WeaponId::none)
            p.alertUntil = time + 8;
        p.fear = 2;
        if (p.health <= 0)
            killPerson(p, dir * 4, true);
        else
            p.body.impulse(dir * 90);
    }
}
void World::flame(Vec2 direction, double y)
{
    const double heading = std::atan2(direction.x, direction.z),
                 range = weapon(WeaponId::flamethrower)->range;
    std::vector<bool> heated(cars.size());
    for (int ray = -2; ray <= 2; ray++)
    {
        auto dir = forward(heading + ray * .075);
        auto hit = traceAttack(position(), dir, y, 0, range, controlledCar);
        for (int n = 0; n < 6 && particles.size() < 5000; n++)
        {
            double start = .9 + random.next() * 2.1;
            if (hit.distance <= start)
                continue;
            auto at = position() + dir * start;
            particles.push_back({{float(at.x), float(y), float(at.z)},
                                 {float(dir.x * 18), .5f, float(dir.z * 18)},
                                 {1, .3f, .03f},
                                 float(std::min(.38, (hit.distance - start) / 18)),
                                 .1f});
        }
        if (hit.kind == HitKind::car && !heated[hit.index])
        {
            heated[hit.index] = true;
            auto &c = cars[hit.index];
            if (!c.destroyed)
            {
                damageCar(c, .015, true);
                c.firePlayerCaused = true;
                c.fireTime = std::max(.001, c.fireTime);
            }
        }
        if (hit.kind == HitKind::person)
        {
            auto &p = people[hit.index];
            p.burnUntil = std::max(p.burnUntil, time + 9);
            p.burnPlayerCaused = true;
            p.fear = 2;
        }
    }
    // The player's disc and vehicle are excluded above. Indirect vehicle fire can still burn them.
    if (time >= nextFlameSound)
    {
        sounds.push_back({"fire", position(), .35});
        nextFlameSound = time + .65;
    }
}
void World::combatStep(double dt)
{
    for (size_t i = rockets.size(); i-- > 0;)
    {
        auto &r = rockets[i];
        double remaining = std::min(dt, .25) * 44 / std::sqrt(1 + r.slope * r.slope);
        bool impact = false;
        while (remaining > 1e-8 && r.travel < weapon(WeaponId::bazooka)->range)
        {
            double step = std::min({.5, remaining, weapon(WeaponId::bazooka)->range - r.travel});
            auto hit =
                traceAttack(r.p, r.direction, r.height, r.slope, step + .001, r.travel < 5 ? r.ownerCar : -1);
            if (hit.kind != HitKind::none && hit.distance <= step)
            {
                if (hit.kind == HitKind::water)
                    splash(r.p + r.direction * hit.distance, 2.);
                r.p += r.direction * (hit.distance - .18);
                r.height += r.slope * hit.distance;
                impact = true;
                break;
            }
            r.p += r.direction * step;
            r.height += r.slope * step;
            r.travel += step;
            remaining -= step;
        }
        if (impact)
        {
            auto p = r.p;
            double y = r.height;
            rockets.erase(rockets.begin() + static_cast<std::ptrdiff_t>(i));
            explode(p, y, 1, false, true);
        }
        else if (r.travel >= weapon(WeaponId::bazooka)->range)
            rockets.erase(rockets.begin() + static_cast<std::ptrdiff_t>(i));
        else if (random.next() < dt * 40 && particles.size() < 5000)
            particles.push_back(
                {{float(r.p.x), float(r.height), float(r.p.z)}, {0, .5f, 0}, {.4f, .4f, .4f}, .65f, .1f});
    }
    for (auto &c : cars)
    {
        if (c.destroyed)
            continue;
        if (c.health <= 0)
        {
            destroyCar(c);
            continue;
        }
        if (c.fireTime <= 0)
            continue;
        c.fireTime += dt;
        if (controlledCar >= 0 && !transitPassenger() && &c == &cars[controlledCar])
        {
            if (time - c.lastDamageAt > 4 && c.body.v.length() > 2 && c.grounded())
                c.safeDrivingTime += dt;
            if (c.safeDrivingTime >= 18)
            {
                c.fireTime = 0;
                c.firePlayerCaused = false;
                c.safeDrivingTime = 0;
            }
        }
        else if (c.fireTime > 12)
            destroyCar(c);
        if (!c.destroyed && (c.body.p - position()).length2() < 190 * 190 && random.next() < dt * 35 &&
            particles.size() < 5000)
            particles.push_back({{float(c.body.p.x), float(c.body.height + .4), float(c.body.p.z)},
                                 {0, 2, 0},
                                 {1, .25f, .03f},
                                 .7f,
                                 .25f});
    }
    fireClock += dt;
    if (fireClock < .1)
        return;
    double elapsed = std::min(fireClock, .2);
    fireClock = 0;
    for (auto &c : cars)
    {
        if (c.fireTime <= 0 || c.destroyed || (c.body.p - position()).length2() > 100 * 100)
            continue;
        double radius = std::max(c.body.halfW, c.body.halfH) + 1.1;
        for (auto &p : people)
            if (!p.dead && (p.body.p - c.body.p).length2() < radius * radius &&
                std::abs(p.body.height - c.body.height) < 1.8 &&
                blastVisible(c.body.p, c.body.height, p.body.p, p.body.height))
            {
                p.burnPlayerCaused = c.firePlayerCaused || (p.burnUntil > time && p.burnPlayerCaused);
                p.burnUntil = std::max(p.burnUntil, time + 9);
            }
        if (controlledCar < 0 && (player.p - c.body.p).length2() < radius * radius &&
            std::abs(player.height - c.body.height) < 1.8 &&
            blastVisible(c.body.p, c.body.height, player.p, player.height) && !godMode)
            playerBurnUntil = std::max(playerBurnUntil, time + 6);
    }
    for (auto &p : people)
    {
        if (p.dead || p.burnUntil <= time)
            continue;
        // Water above the knee puts a burning person out, as in the managed BurnPerson.
        if (waterDepth(p.body.p, p.body.height - .85) > .35)
        {
            p.burnUntil = 0;
            p.burnPlayerCaused = false;
            continue;
        }
        p.health -= .18 * elapsed;
        p.fear = 2;
        if (p.health <= 0)
            killPerson(p, {}, p.burnPlayerCaused);
        else if ((p.body.p - position()).length2() < 110 * 110 && particles.size() < 5000)
            particles.push_back({{float(p.body.p.x), float(p.body.height), float(p.body.p.z)},
                                 {0, 1, 0},
                                 {1, .25f, .03f},
                                 .6f,
                                 .15f});
    }
    if (playerBurnUntil > time && waterDepth(position(), ground()) > .35)
        playerBurnUntil = 0;
    if (playerBurnUntil > time)
        hurtPlayer(.065 * elapsed);
}
} // namespace pz
