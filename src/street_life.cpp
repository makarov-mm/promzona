#include "promzona/world.hpp"

namespace Promzona
{
    void World::generate_street_life()
    {
        streetRandom = Random(seed ^ 0xbd45f7a3u);
        scooters.clear();
        animals.clear();
        habitat.clear();
        flocks.clear();
        playerScooter = -1;
        animalClock = 0;

        for (size_t i = 0; i < people.size(); ++i)
        {
            Person &p = people[i];
            p.ownedScooter = -1;
            p.courier = p.inCompany = false;
            p.watchUntil = p.voiceAt = p.idleVoiceAt = p.fleeUntil = 0;
            p.deliveryUntil = 0;

            if (p.dead || p.riding >= 0 || ((job.passenger() || job.kind == JobKind::elimination) && job.actor == int(i)))
                continue;

            // Denser cities keep about the managed game's share of couriers and scooters.
            const bool dense = density != Density::low;
            p.courier = dense ? i % 34 == 4 || i % 78 == 7 : i % 17 == 4 || i % 39 == 7;
            p.deliveryDistance = p.distance + 115 + streetRandom.next() * 90;
            if (i % 13 != 7 || (dense && scooters.size() >= people.size() / 25))
                continue;
            p.ownedScooter = static_cast<int>(scooters.size());
            Scooter scooter;
            scooter.owner = scooter.rider = static_cast<int>(i);
            auto &b = scooter.body;
            b.id = 0x71000000u + static_cast<unsigned>(p.ownedScooter);
            b.halfW = .18;
            b.halfH = .48;
            b.mass(13.);
            b.friction = .63;
            b.verticalExtent = .32;
            b.p = p.body.p;
            b.angle = -p.heading;
            b.roadLevel = p.body.roadLevel;
            b.height = surface(b.p, b.roadLevel) + .15;
            scooters.push_back(scooter);
        }

        streetRandom = Random(seed ^ 0x219d8317u);

        for (int j = 0; j < nodes - 1; ++j)
            for (int i = 0; i < nodes - 1; ++i)
            {
                if (!layout.park(i, j))
                    continue;

                Vec2 centre = nodePoint(j * nodes + i) + Vec2{spacing * .5, spacing * .5};

                if (layout.urbanSite(centre, 4) && !levelAt(centre, 5) && !insideBuilding(centre, terrain.height(centre) + .4, 4))
                    habitat.push_back(centre);
            }

        if (habitat.empty())
            return;

        auto add = [&](Species species, Vec2 home, int flock, Vec2 offset)
        {
            Animal a;
            a.species = species;
            a.variant = species == Species::cat   ? streetRandom.index(2)
                        : species == Species::dog ? 2 + streetRandom.index(2)
                                                  : 4;
            a.flock = flock;
            a.p = a.goal = home + offset;
            a.offset = offset;
            a.altitude = terrain.height(a.p);
            a.heading = streetRandom.next() * 2 * pi;
            a.thinkAt = time + streetRandom.next() * 2;
            animals.push_back(a);
        };

        auto home = [&]
        {
            return habitat[streetRandom.index(static_cast<int>(habitat.size()))];
        };

        // The full population has the managed game's animals as well: about 960 pigeons and
        // 160 cats, dogs and lone birds (ResetLife, AnimalPopulationRevision 2). The lower
        // densities keep the animals their saves were made with.
        const bool full = density >= Density::full;
        const int strays = full ? 106 : 72, pigeonFlocks = full ? 120 : 32, loneBirds = full ? 54 : 10;
        // As in the managed game the animals live on the pavements as well as in the parks:
        // cats, dogs and lone birds wherever their block falls, a flock of pigeons in a park
        // nearly four times out of five. The places come from a generator of their own. The
        // main one decides how many animals there are, and a save made when they all lived
        // in parks must still find the same number of each.
        Random placement(seed ^ 0x6c8e9cf5u);

        auto settle = [&](Vec2 park, bool lawn, int members) -> Perch
        {
            for (int attempt = 0; attempt < 12; ++attempt)
            {
                const std::optional<Perch> perch = animal_perch(placement, lawn);

                if (!perch)
                    break;

                // A flock arrives whole or not at all: no bird inside a wall or on the road.
                bool fits = true;

                for (int n = 1; n < members && fits; ++n)
                {
                    const Vec2 at = perch->at + flock_offset(n, perch->pavement);
                    fits = layout.urbanSite(at, .3) && !insideBuilding(at, terrain.height(at) + .4, .3) &&
                           (!perch->pavement || layout.roadDistance(at) > layout.roadHalf() + .2);
                }

                if (fits)
                    return *perch;
            }

            return {
                .at = park, 
                .pavement = false
            };
        };

        for (int i = 0; i < strays; ++i)
        {
            const Vec2 park = home();
            add(i % 2 ? Species::cat : Species::dog, settle(park, false, 1).at, -1, {});
        }

        for (int i = 0; i < pigeonFlocks + loneBirds; ++i)
        {
            const Vec2 park = home();
            bool solo = i >= pigeonFlocks;
            int count = solo ? 1 : 6 + streetRandom.index(5);
            const Perch perch = settle(park, !solo && placement.next() < .78, count);

            flocks.push_back({
                .landing = perch.at, 
                .flying = false, 
                .landedAt = time});

            for (int n = 0; n < count; ++n)
            {
                add(solo ? Species::bird : Species::pigeon, perch.at, i, flock_offset(n, perch.pavement));
            }
        }
    }

    std::optional<World::Perch> World::animal_perch(Random &source, bool lawn) const
    {
        for (int attempt = 0; attempt < 40; ++attempt)
        {
            int i = source.index(nodes - 1), j = source.index(nodes - 1);

            for (int k = 0; lawn && k < 30 && !layout.park(i, j); ++k)
            {
                i = source.index(nodes - 1);
                j = source.index(nodes - 1);
            }

            const bool park = layout.park(i, j);
            const Vec2 at = nodePoint(j * nodes + i) + Vec2{park ? 10 + source.next() * 16 : layout.sidewalk(), 10 + source.next() * 16};
            const double radius = park ? .6 : .45;

            if (!layout.urbanSite(at, radius) || levelAt(at, 2) || insideBuilding(at, terrain.height(at) + .4, radius))
                continue;

            if (!park)
            {
                // The pavement of a street that exists: not a lawn, a yard or the roadway.

                if (const double from_road = layout.roadDistance(at) - layout.roadHalf(); from_road < .3 || from_road > 2.6)
                    continue;
            }

            return Perch{at, !park};
        }
        return std::nullopt;
    }

    Vec2 World::flock_offset(int member, bool pavement)
    {
        return pavement 
            ? Vec2{(member % 2) * .40, (member / 2) * .48}
            : forward(member * 2.4) * (.65 * std::sqrt(member));
    }

    void World::releaseScooter(int index, Vec2 velocity)
    {
        if (index < 0 || index >= int(scooters.size()))
            return;
        auto &s = scooters[index];
        if (s.rider == Scooter::free)
            return;
        if (s.rider == Scooter::playerRider)
            playerScooter = -1;
        const Body &rider = s.rider >= 0 && s.rider < int(people.size()) ? people[s.rider].body : player;
        s.body.p = rider.p;
        s.body.roadLevel = rider.roadLevel;
        s.body.height = rider.height - .7;
        s.body.v = velocity;
        s.body.w = clamp(velocity.length() * .42, 0., 3.);
        s.body.wake();
        s.rider = Scooter::free;
        s.returnAt = time + 7.;
    }

    int World::nearby_scooter() const
    {
        int nearest = -1;
        double best = 1.8 * 1.8;

        for (int i = 0; i < static_cast<int>(scooters.size()); ++i)
        {
            const auto &s = scooters[i];
            double d = (s.body.p - player.p).length2();
            if (s.rider != Scooter::free || d >= best || std::abs(s.body.height - (player.height - .7)) > .85)
                continue;
            best = d;
            nearest = i;
        }

        return nearest;
    }

    bool World::use_scooter()
    {
        if (playerScooter >= 0)
        {
            releaseScooter(playerScooter, player.v * .65);
            return true;
        }

        int nearest = nearby_scooter();
        if (nearest < 0) return false;
        Scooter &s = scooters[nearest];
        playerScooter = nearest;
        s.rider = Scooter::playerRider;
        player.angle = s.body.angle;
        return true;
    }

    void World::rider_intent(Person &p, Vec2 &dir, double &speed)
    {
        if (p.ownedScooter >= 0)
        {
            Scooter &s = scooters[p.ownedScooter];

            if (s.rider == Scooter::free && time >= s.returnAt && p.fear <= 0)
            {
                Vec2 delta = s.body.p - p.body.p;

                if (delta.length2() <= 1.4 * 1.4 && std::abs(s.body.height + .7 - p.body.height) < .8)
                {
                    s.rider = s.owner;
                }
                else if (delta.length2() < 30 * 30 && p.body.roadLevel == s.body.roadLevel)
                {
                    dir = delta.unit();
                    speed = 2.2;
                    return;
                }
            }

            if (s.rider == s.owner)
                speed = p.fear > 0 ? 4.8 : 3.4;
            else if (s.rider == Scooter::free && time < s.returnAt && p.fear <= 0)
                speed = 0;
        }

        if (!p.courier || p.fear > 0)
            return;

        if (p.distance >= p.deliveryDistance)
        {
            p.deliveryUntil = time + 2 + streetRandom.next() * 3;
            p.deliveryDistance = p.distance + 115 + streetRandom.next() * 90;
        }

        if (p.deliveryUntil > time)
            speed = 0;
    }

    void World::update_scooters(double dt)
    {
        for (Scooter &s : scooters)
        {
            if (s.rider >= static_cast<int>(people.size()))
            {
                s.rider = Scooter::free;
                s.owner = -1;
            }

            if (s.rider == Scooter::free)
            {
                s.body.v *= std::exp(-dt * 2.8);
                s.body.w *= std::exp(-dt * 3.2);
                s.body.roadLevel = selectLevel(s.body.p, s.body.roadLevel, s.body.height - .15);
                s.body.height = surface(s.body.p, s.body.roadLevel) + .15;
                continue;
            }

            const Body &rider = s.rider == Scooter::playerRider ? player : people[s.rider].body;
            s.body.p = rider.p;
            s.body.v = rider.v;
            s.body.angle = s.rider == Scooter::playerRider ? player.angle : -people[s.rider].heading;
            s.body.roadLevel = rider.roadLevel;
            s.body.height = rider.height - .7;

            if (s.rider != Scooter::playerRider)
                continue;

            for (const Car &c : cars)
                if ((c.body.p - rider.p).length2() < 12 && std::abs(c.body.height - rider.height) < 1.6 &&
                    (c.body.v - rider.v).length2() > 16)
                {
                    Vec2 at = rotate(rider.p - c.body.p, -c.body.angle);
                    if (std::abs(at.x) < c.body.halfW + .39 && std::abs(at.z) < c.body.halfH + .39)
                    {
                        releaseScooter(playerScooter, c.body.v * .5);
                        break;
                    }
                }
        }
    }
}
