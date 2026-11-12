#include "promzona/world.hpp"

namespace pz
{
void World::generateStreetLife()
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
        auto &p = people[i];
        p.ownedScooter = -1;
        p.courier = p.inCompany = false;
        p.watchUntil = p.voiceAt = p.idleVoiceAt = p.fleeUntil = 0;
        p.deliveryUntil = 0;
        if (p.dead || p.riding >= 0 ||
            ((job.passenger() || job.kind == JobKind::elimination) && job.actor == int(i)))
            continue;
        // Denser cities keep about the managed game's share of couriers and scooters.
        const bool dense = density != Density::low;
        p.courier = dense ? i % 34 == 4 || i % 78 == 7 : i % 17 == 4 || i % 39 == 7;
        p.deliveryDistance = p.distance + 115 + streetRandom.next() * 90;
        if (i % 13 != 7 || (dense && scooters.size() >= people.size() / 25))
            continue;
        p.ownedScooter = int(scooters.size());
        Scooter scooter;
        scooter.owner = scooter.rider = int(i);
        auto &b = scooter.body;
        b.id = 0x71000000u + unsigned(p.ownedScooter);
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
            if (layout.urbanSite(centre, 4) && !levelAt(centre, 5) &&
                !insideBuilding(centre, terrain.height(centre) + .4, 4))
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
    auto home = [&] { return habitat[streetRandom.index(int(habitat.size()))]; };
    // The full population has the managed game's animals as well: about 960 pigeons and
    // 160 cats, dogs and lone birds (ResetLife, AnimalPopulationRevision 2). The lower
    // densities keep the animals their saves were made with.
    const bool full = density == Density::full;
    const int strays = full ? 106 : 72, pigeonFlocks = full ? 120 : 32, loneBirds = full ? 54 : 10;
    for (int i = 0; i < strays; ++i)
        add(i % 2 ? Species::cat : Species::dog, home(), -1, {});
    for (int i = 0; i < pigeonFlocks + loneBirds; ++i)
    {
        Vec2 centre = home();
        bool solo = i >= pigeonFlocks;
        flocks.push_back({centre, false, time});
        int count = solo ? 1 : 6 + streetRandom.index(5);
        for (int n = 0; n < count; ++n)
            add(solo ? Species::bird : Species::pigeon, centre, i, forward(n * 2.4) * (.65 * std::sqrt(n)));
    }
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

int World::nearbyScooter() const
{
    int nearest = -1;
    double best = 1.8 * 1.8;
    for (int i = 0; i < int(scooters.size()); ++i)
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

bool World::useScooter()
{
    if (playerScooter >= 0)
    {
        releaseScooter(playerScooter, player.v * .65);
        return true;
    }
    int nearest = nearbyScooter();
    if (nearest < 0)
        return false;
    auto &s = scooters[nearest];
    playerScooter = nearest;
    s.rider = Scooter::playerRider;
    player.angle = s.body.angle;
    return true;
}

void World::riderIntent(Person &p, Vec2 &dir, double &speed)
{
    if (p.ownedScooter >= 0)
    {
        auto &s = scooters[p.ownedScooter];
        if (s.rider == Scooter::free && time >= s.returnAt && p.fear <= 0)
        {
            auto delta = s.body.p - p.body.p;
            if (delta.length2() <= 1.4 * 1.4 && std::abs(s.body.height + .7 - p.body.height) < .8)
                s.rider = s.owner;
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

void World::updateScooters(double dt)
{
    for (auto &s : scooters)
    {
        if (s.rider >= int(people.size()))
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
        for (const auto &c : cars)
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
} // namespace pz
