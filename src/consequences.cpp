#include <utility>

#include "promzona/world.hpp"

namespace Promzona
{
    // Port of CityTraffic.CarPersonOverlap: distance from the vehicle's rectangle.
    bool World::car_touches(const Car &car, Vec2 point, double margin)
    {
        const auto f = forward(car.heading());
        const auto d = point - car.body.p;
        const double x = std::max(0., std::abs(dot(d, perpendicular(f))) - car.body.halfW),
                     z = std::max(0., std::abs(dot(d, f)) - car.body.halfH);
        return x * x + z * z < (.25 + margin) * (.25 + margin);
    }

    // Port of CityTraffic.HitPerson. Arcade consequences, not an injury model: a slow
    // contact knocks the walker down, 5.5 m/s and above is fatal.
    void World::strike_person(Person &p, const Car &car, double closing, bool playerDriving)
    {
        if (p.dead || p.knockedUntil > time || p.riding >= 0 || closing < 1.1)
            return;
        const int index = static_cast<int>(&p - people.data());
        if (p.ownedScooter >= 0 && scooters[p.ownedScooter].rider >= 0)
            releaseScooter(p.ownedScooter, car.body.v * .45);
        if (conversation.actor == index)
            conversation = {};
        const auto thrown = car.body.v * .55;
        ++pedestrianImpacts;
        if (closing >= fatalImpactSpeed)
        {
            p.body.verticalSpeed = closing > 10 ? std::min(6., closing * .28) : std::min(2., closing * .18);
            killPerson(p, thrown, playerDriving);
            // Reported after the death: a kill with nobody left to see it raises nothing.
            if (playerDriving)
                reportCrime(.85);
            return;
        }
        // The victim of a knock-down is a witness while alive.
        if (playerDriving)
            addCrime(.18);
        sounds.push_back({"impact", p.body.p, .6});
        p.knockedUntil = time + 2.2 + std::min(4., closing * .12);
        p.injuredUntil = p.knockedUntil + 40 + closing * 10;
        // The managed survivor limps about the place of the fall, in the road if that is where
        // it was; here they make for the kerb, where a car can pick them up.
        p.injuryHome = kerbside(p.body.p);
        p.health = std::min(p.health, .55);
        p.body.verticalSpeed = std::min(2., closing * .18);
        p.body.v = thrown;
        p.body.wake();
        p.fear = 0;
        p.aimSeconds = 0;
        p.burstLeft = 0;
        p.waitingLine = p.waitingStop = -1;
        if (playerDriving && p.sidearm != WeaponId::none)
            p.alertUntil = p.knockedUntil + 6;
    }

    Vec2 World::kerbside(Vec2 from) const
    {
        auto fits = [&](Vec2 at)
        {
            return layout.roadDistance(at) > layout.roadHalf() + 1.2 && !openCut(at, .5) && !levelAt(at, 2) &&
                   terrain.height(at) > CityLayout::waterLevel + .5 &&
                   !inside_building(at, terrain.height(at) + .85, .4);
        };
        if (fits(from))
            return from;
        for (double radius = 1; radius <= 18; radius += 1)
            for (int k = 0; k < 16; ++k)
            {
                const Vec2 at = from + forward(k * pi / 8) * radius;
                if (fits(at))
                    return at;
            }
        return from;
    }

    // The player is sturdier than a passer-by: the damage grows with the closing
    // speed and only a fast car is fatal at full health.
    void World::strike_player(const Car &car, double closing)
    {
        if (controlledCar >= 0 || health <= 0 || playerKnockedUntil > time || closing < 1.1)
            return;
        sounds.push_back({"impact", player.p, .7});
        releaseScooter(playerScooter, car.body.v * .45);
        vehicleEntry = {};
        conversation = {};
        openMarket = -1;
        if (drone.active)
            stop_drone(L"Связь с дроном прервана", 20);
        hurt_player(clamp((closing - 1.1) * .11, .05, 1.));
        if (godMode)
            return;
        playerKnockedUntil = time + 1.2 + std::min(2.4, closing * .12);
        player.verticalSpeed = std::min(3., closing * .2);
        player.v = car.body.v * .55;
        player.wake();
    }

    void World::hurt_player(double amount)
    {
        if (godMode || !std::isfinite(amount) || amount <= 0)
            return;
        health = std::max(0., health - amount);
    }

    void World::wound_player(double amount)
    {
        if (godMode || controlledCar >= 0 || health <= 0 || !std::isfinite(amount) || amount <= 0)
            return;

        sounds.push_back({
            .name = "impact", 
            .source = player.p, 
            .gain = .5
        });

        hurt_player(amount);
    }

    // Port of DetectPedestrianImpacts for one walker; the caller supplies nearby cars.
    namespace
    {
        // Which way the vehicle's outline faces where it is nearest to `at`: along the car in
        // front of a bumper, across it beside a flank. Measured from the middle of the car, as it
        // was before, half the flank of a tram counted as its front: whoever brushed a tram that
        // was pulling away was run over by it.
        Vec2 outward(const Car &car, Vec2 at)
        {
            const Vec2 ahead = forward(car.heading()), d = at - car.body.p;
            const double spine = std::max(0., car.body.halfH - car.body.halfW);
            const Vec2 from = d - ahead * clamp(dot(d, ahead), -spine, spine);
            return from / std::max(.3, from.length());
        }
    }

    bool World::detect_impact(Person &p, const Car &car, bool playerDriving)
    {
        const auto d = p.body.p - car.body.p;
        const double distance = d.length();
        if (car.destroyed || std::abs(p.body.height - car.body.height) >= 1.8 || distance > car.body.halfH + 1 ||
            car.body.v.length2() < 1.21)
            return false;
        const auto toward = outward(car, p.body.p);
        // The vehicle itself must be driving into the walker; a person who walks into
        // the side of a passing car is pushed aside by the contact solver instead.
        const double driven = dot(car.body.v, toward);
        if (driven <= 1.1 || !car_touches(car, p.body.p, .06))
            return false;
        strike_person(p, car, std::max(driven, dot(car.body.v - p.body.v, toward)), playerDriving);
        return true;
    }

    void World::detect_player_impact(std::span<const Car *const> traffic)
    {
        if (controlledCar >= 0 || health <= 0 || playerKnockedUntil > time)
            return;
        for (const Car *nearby : traffic)
        {
            const auto &car = *nearby;
            const auto d = player.p - car.body.p;
            const double distance = d.length();
            if (car.destroyed || std::abs(player.height - car.body.height) >= 1.8 ||
                distance > car.body.halfH + 1 || car.body.v.length2() < 1.21)
                continue;
            const auto toward = outward(car, player.p);
            const double driven = dot(car.body.v, toward);
            if (driven <= 1.1 || !car_touches(car, player.p, .06))
                continue;
            strike_player(car, std::max(driven, dot(car.body.v - player.v, toward)));
            return;
        }
    }

    // A crewed pursuit vehicle or the arresting officer that can see the player now.
    bool World::policeSees() const
    {
        const auto at = position();
        const double y = ground() + .85;

        for (const Car &c : cars)
            if (c.responding && !c.destroyed && c.seesSuspect)
                return true;

        if (patrol.officer >= 0 && std::cmp_less(patrol.officer, people.size()))
        {
            if (const Person &officer = people[patrol.officer]; !officer.dead && (officer.body.p - at).length2() < 45 * 45 && blastVisible(officer.body.p, officer.body.height, at, y))
                return true;
        }

        return false;
    }

    // Port of the wanted fade in CityTraffic.ServicesStep, applied once the crime
    // cooldown has run out: slow while the militia can see the suspect, five times
    // faster once out of sight for 15 s and 25 s after the last crime.
    void World::fade_wanted(double dt)
    {
        if (wanted <= 0)
            return;

        if (time >= policeSightCheckAt)
        {
            policeSightCheckAt = time + .5;

            if (policeSees())
                lastPoliceSightAt = time;
        }

        const bool lost = time - lastPoliceSightAt > 15 && time - lastCrimeAt > 25;
        wanted = std::max(0., wanted - dt * (lost ? .09 : .018));
    }

    // Console "spawn pills|energy": a pickup on the nearest clear ground around the player.
    bool World::spawn_pickup(bool energy)
    {
        constexpr size_t pickupCap = 240;
        if (pickups.size() >= pickupCap)
            return false;
        const Vec2 centre = position();
        const int level = controlledCar >= 0 ? cars[controlledCar].body.roadLevel : player.roadLevel;

        for (double radius = 3; radius <= 45; radius += 3)
            for (int n = 0; n < 24; ++n)
            {
                const auto at = centre + forward(n * 2 * pi / 24) * radius;
                const double floor = surface(at, level);

                if (std::max(std::abs(at.x), std::abs(at.z)) > landEdge - 12 || floor < CityLayout::waterLevel + .1 || std::abs(floor - ground()) > 1.5 || inside_building(at, floor + .8, .5) || scenerySolid(at, floor + .8, .4) || (!level && openCut(at, .6)))
                {
                    continue;
                }

                Pickup pickup {
                    .p = at, 
                    .weapon = WeaponId::none, 
                    .health = !energy, 
                    .wait = 1., 
                    .energy = energy, 
                    .dropped = true,
                    .roadLevel = level
                };

                pickups.push_back(pickup);
                return true;
            }

        return false;
    }
}
