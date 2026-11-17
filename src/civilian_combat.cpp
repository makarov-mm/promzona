#include "promzona/world.hpp"

namespace Promzona
{
WeaponId Person::civilianWeapon(std::uint32_t citySeed, std::uint32_t id)
{
    // Do not advance the generator for the city or its routes when assigning equipment.
    Random equipment(citySeed ^ (id * 0x9e3779b9u) ^ 0x76213dafu);
    if (equipment.index(10) != 0)
        return WeaponId::none;
    return equipment.index(5) == 0 ? WeaponId::automatic : WeaponId::pistol;
}

void World::alarmWitnesses(Vec2 origin, double height, bool playerCaused)
{
    for (auto &witness : people)
    {
        if (witness.dead || witness.riding >= 0 || (witness.body.p - origin).length2() > 24 * 24)
            continue;
        if (!blastVisible(origin, height, witness.body.p, witness.body.height))
            continue;
        scare_person(witness, origin, 2.);
        witness.incident = origin;
        witness.watchUntil = time + 10. + (witness.body.id % 19);
        if (playerCaused && witness.sidearm != WeaponId::none)
            witness.alertUntil = time + 8.;
    }
}

bool World::surrendering() const
{
    if (controlledCar < 0)
        return player.v.length() < 1.2 && !aiming && playerKnockedUntil <= time && playerScooter < 0;
    const auto &car = cars[controlledCar];
    return car.body.v.length() < 1 && car.grounded() && !car.recovering;
}

void World::civilianCombat(Person &p, double dt)
{
    const auto *gun = weapon(p.sidearm);
    auto delta = position() - p.body.p;
    const double range = delta.length();
    if (!gun || p.dead || p.riding >= 0 || p.burnUntil > time || p.alertUntil <= time || health <= 0 ||
        (controlledCar >= 0 && (cars[controlledCar].destroyed || cars[controlledCar].falling)) ||
        range <= 3 || range >= 24)
    {
        p.aimSeconds = 0;
        p.burstLeft = 0;
        return;
    }

    // Below four stars the militia come to arrest, not to kill: an officer holds his fire at a
    // suspect who gives himself up -- standing still with no weapon raised, or with the car
    // stopped -- as the managed game's officers did.
    if (p.officer() && wantedLevel() < 4 && surrendering())
    {
        p.aimSeconds = 0;
        p.burstLeft = 0;
        return;
    }

    auto dir = delta / range;
    double desired = std::atan2(dir.x, dir.z);
    p.heading = wrap(p.heading + clamp(wrap(desired - p.heading), -6 * dt, 6 * dt));
    p.aimSeconds = std::min(1., p.aimSeconds + dt);
    if (p.aimSeconds < .65 || time < p.shotAt || std::abs(wrap(desired - p.heading)) > .12)
        return;

    double muzzle = p.body.height + .42;
    double targetY = controlledCar >= 0 ? cars[controlledCar].body.height : player.height;
    double slope = (targetY - muzzle) / range;
    auto hit = traceAttack(p.body.p, dir, muzzle, slope, range + 1, -1, true, &p);
    if (hit.kind != HitKind::player && !(hit.kind == HitKind::car && hit.index == controlledCar))
    {
        p.shotAt = time + .25;
        p.burstLeft = 0;
        return;
    }
    if (p.burstLeft == 0)
        p.burstLeft = gun->npcBurst;
    --p.burstLeft;
    p.shotAt = time + (p.burstLeft > 0 ? gun->npcInterval : gun->npcRest + (p.body.id % 5) * .04);
    sounds.push_back({std::string(gun->sound), p.body.p, .52});
    if (hit.kind == HitKind::car)
        damageCar(cars[hit.index], gun->npcCarDamage);
    else
        wound_player(gun->personDamage * .45);
    bulletSparks(p.body.p, dir, muzzle, slope, hit);
    for (double at = 1; at < hit.distance && particles.size() < 5000; at += 1.4)
        particles.push_back(
            {{float(p.body.p.x + dir.x * at), float(muzzle + slope * at), float(p.body.p.z + dir.z * at)},
             {},
             {1, .74f, .33f},
             .08f,
             .04f});
}
} // namespace Promzona
