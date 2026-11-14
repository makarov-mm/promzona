#include "promzona/world.hpp"
#include <utility>

namespace Promzona
{
    namespace
    {
        bool can_board(const Car &car)
        {
            return !car.destroyed && !car.falling && !car.recovering && car.fireTime <= 0 && car.grounded() && car.body.v.length2() <= 2.5 * 2.5;
        }
    }

    Vec2 World::driver_door(const Car &car, int side)
    {
        return car.body.p + rotate({
            .x = side * (car.body.halfW + .9), 
            .z = std::min(1., .25 * car.body.halfH)}, 
            car.body.angle);
    }

    bool World::entry_path(const Car &car, Vec2 door) const
    {
        Vec2 delta = door - player.p;
        int samples = std::max(1, static_cast<int>(std::ceil(delta.length() / .32)));

        for (int i = 1; i <= samples; ++i)
        {
            Vec2 at = player.p + delta * (double(i) / samples);
            if (!vehicleSpace(at, player.height, nullptr, false) ||
                std::abs(surface(at, player.roadLevel) + .85 - car.body.height) > 1.5)
                return false;
        }

        // Also check the narrow space between the handle and the outside of the sill.
        const Vec2 inward = rotate(door - car.body.p, -car.body.angle);
        const double side = inward.x < 0 ? -1. : 1.;
        for (double offset : {.65, .35, .05})
        {
            Vec2 at = car.body.p + rotate({side * (car.body.halfW + offset), inward.z}, car.body.angle);
            if (!vehicleSpace(at, player.height, &car, false))
                return false;
        }
        return true;
    }

    bool World::unload_crew(Car &car)
    {
        if (car.driverless || car.personal || car.summoned || car.id == 0)
            return true;
        const int index = int(car.id);
        const bool police = car.kind == 3 || car.kind == 6 || car.kind == 7 || car.kind == 13 || car.kind == 14;
        int count = police          ? (car.kind == 7 || car.kind == 14 ? 4 : 2)
                    : car.kind == 4 ? 2
                    : car.line >= 0 ? 1
                                    : 1 + index % 3;
        std::vector<int> occupants;
        auto includeCrew = [&](int actor)
        {
            if (actor < 0)
                return;
            if (!people[actor].dead && people[actor].riding == index)
                occupants.push_back(actor);
            else
                --count; // A paramedic treating a patient or a patrolman on foot is already outside.
        };
        if (patrol.car == index)
            includeCrew(patrol.officer);
        if (auto *call = ambulanceCall(index))
            includeCrew(call->medic);

        // The native population has stable IDs shared by saves, routes and missions.
        // Materialise anonymous occupants using off-screen walkers with no commitments.
        for (int i = 0; int(occupants.size()) < count && i < int(people.size()); ++i)
        {
            const Person &p = people[i];
            if (p.dead || p.riding >= 0 || p.ownedScooter >= 0 || p.courier || p.inCompany ||
                p.waitingLine >= 0 || waitingForTaxi(i) || paramedic_call(i) || i == patrol.officer ||
                (job.kind != JobKind::none && job.actor == i) || i == conversation.actor || p.burnUntil > time ||
                p.alertUntil > time || p.fear > .1 || p.watchUntil > time ||
                (p.body.p - player.p).length2() < 150. * 150.)
                continue;
            occupants.push_back(i);
        }
        if (int(occupants.size()) < count)
            return false;
        std::vector<Vec2> positions;
        for (int seat = 0; seat < count; ++seat)
        {
            for (int attempt = 0; attempt < 32; ++attempt)
            {
                int side = (seat + attempt) % 2 ? -1 : 1;
                double lateral = side * (car.body.halfW + 1. + (attempt / 8) * .55);
                double along = .6 - (seat / 2) * 1.2 + ((attempt / 2) % 4 - 1.5) * .65;
                Vec2 p = car.body.p + rotate({lateral, along}, car.body.angle);
                if (std::abs(surface(p, car.body.roadLevel) + .85 - car.body.height) < 1.5 &&
                    vehicleSpace(p, car.body.height + .05, nullptr, true) &&
                    std::ranges::none_of(positions, [&](Vec2 q) { return (p - q).length2() <= .75 * .75; }))
                {
                    positions.push_back(p);
                    break;
                }
            }
            if (int(positions.size()) == seat)
                return false;
        }
        // Commit only once every occupant has a legal landing place.
        auto retainUniform = [&](int actor, Person::Uniform uniform)
        {
            if (actor >= 0)
                people[actor].uniform = uniform;
        };
        if (patrol.car == index)
            retainUniform(patrol.officer, Person::Uniform::police);
        if (auto *call = ambulanceCall(index))
            retainUniform(call->medic, Person::Uniform::medical);
        for (int i = 0; i < count; ++i)
        {
            auto &p = people[occupants[i]];
            resumeWalk(p, positions[i], car.body.roadLevel);
            p.body.v = (positions[i] - car.body.p) * .65;
            p.cover = {};
            p.aimSeconds = p.watchUntil = p.alertUntil = p.fear = 0;
            p.burstLeft = 0;
            p.uniform = car.kind == 7    ? Person::Uniform::rosguard
                        : car.kind == 13 ? Person::Uniform::fsb
                        : car.kind == 14 ? Person::Uniform::army
                        : police         ? Person::Uniform::police
                        : car.kind == 4  ? Person::Uniform::medical
                                         : Person::Uniform::none;
            scarePerson(p, player.p, police ? .1 : 1.);
            if (police)
            {
                p.sidearm = car.kind >= 7 ? WeaponId::automatic : WeaponId::pistol;
                p.alertUntil = time + 12;
            }
            else
                p.sidearm = car.kind == 4 ? WeaponId::none : Person::civilianWeapon(seed, p.body.id);
        }
        if (patrol.car == index)
            patrol = {};
        for (auto &r : rescues)
            if (r.car == index)
            {
                if (r.patient >= 0 && people[r.patient].dead)
                    people[r.patient].rescueAfter = time + 25;
                r = {};
            }
        if (count)
            reportCrime(police ? 1.25 : .65);
        if (count > 1)
            car.doorSide = 0;
        return true;
    }

    void World::enter_exit()
    {
        if (vehicleEntry.car >= 0)
        {
            vehicleEntry = {};
            return;
        }
        if (transitPassenger())
        {
            rideTransit();
            return;
        }
        if (drone.active || health <= 0)
            return;
        if (controlledCar < 0 && useScooter())
            return;
        if (controlledCar >= 0)
        {
            auto &car = cars[controlledCar];
            if (car.body.v.length2() > 2.5 * 2.5 || car.lift > .4 || car.falling || car.recovering)
                return;
            auto clear = player_vehicle_exit(car);
            if (!clear)
                return;
            car.doorSide = rotate(*clear - car.body.p, -car.body.angle).x < 0 ? -1 : 1;
            car.doorOpen = 1.;
            car.doorHold = .65;
            car.driverless = true;
            helicopterLanding = false;
            player.p = *clear;
            player.v = car.body.v;
            player.roadLevel = selectLevel(player.p, car.body.roadLevel, car.body.height - .8);
            player.height = surface(player.p, player.roadLevel) + .85;
            player.verticalSpeed = car.verticalVelocity;
            player.wake();
            controlledCar = -1;
            physics.clear();
            return;
        }

        double closest = 25.;

        for (int i = 0; std::cmp_less(i, cars.size()); ++i)
        {
            const Car &car = cars[i];
            const double range = car.body.halfW + car.body.halfH + 6.;

            if ((car.body.p - player.p).length2() > range * range || car.body.roadLevel != player.roadLevel || std::abs(player.height - car.body.height) > 1.5 || !can_board(car))
                continue;

            for (int side : {-1, 1})
            {
                Vec2 door = driver_door(car, side);
                double d = (player.p - door).length2();

                if (d <= closest && entry_path(car, door))
                {
                    closest = d;

                    vehicleEntry = {
                        .car = i, 
                        .side = side, 
                        .progress = 0., 
                        .deadline = time + 4.
                    };
                }
            }
        }

        if (vehicleEntry.car < 0)
            talk();
        else
            conversation = {};
    }

    void World::step_entry(PlayerInput &input, double dt)
    {
        if (vehicleEntry.car < 0)
            return;

        if (vehicleEntry.car >= static_cast<int>(cars.size()) || controlledCar >= 0 || drone.active || playerScooter >= 0 || health <= 0 || openMarket >= 0 || time > vehicleEntry.deadline || input.walking.length2() > .01 || input.shooting)
        {
            vehicleEntry = {};
            return;
        }

        auto &car = cars[vehicleEntry.car];
        Vec2 door = driver_door(car, vehicleEntry.side);
        Vec2 delta = door - player.p;

        if (!can_board(car) || car.body.roadLevel != player.roadLevel || std::abs(player.height - car.body.height) > 1.5 || delta.length2() > 36. || !entry_path(car, door))
        {
            vehicleEntry = {};
            return;
        }
        if (delta.length2() > .35 * .35)
        {
            Vec2 next = player.p + delta.unit() * std::min(delta.length(), .32);

            if (!vehicleSpace(next, player.height, nullptr, false))
            {
                vehicleEntry = {};
                return;
            }

            input.walking = delta.unit();
            input.running = false;
            input.lookHeading = std::atan2(delta.x, delta.z);
            vehicleEntry.progress = 0.;
            return;
        }

        auto [x, z] = car.body.p - player.p;
        input.lookHeading = std::atan2(x, z);
        vehicleEntry.progress += dt;
        car.doorSide = vehicleEntry.side;
        car.doorOpen = std::min(1., vehicleEntry.progress * 2.);

        if (vehicleEntry.progress < .85)
            return;

        if (unload_crew(car))
        {
            car.driverless = true;
            car.doorHold = 1.1;
            car.responding = car.seesSuspect = false;
            controlledCar = vehicleEntry.car;
            car.body.wake();
            physics.clear();
        }
        vehicleEntry = {};
    }
}
