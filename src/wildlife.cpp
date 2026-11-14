#include "promzona/world.hpp"

namespace Promzona
{
    World::Perch World::landing_site(Vec2 near, Vec2 away)
    {
        Perch best {
            .at = near, 
            .pavement = false
        };

        double score = -1e9;

        // A flock of the managed game settles in a park nearly four times out of five and
        // on a pavement otherwise; a startled one keeps to that.
        const bool street = streetRandom.next() >= .78;

        for (int i = 0; i < 32; ++i)
        {
            Perch perch {
                .at = habitat[streetRandom.index(static_cast<int>(habitat.size()))],
                .pavement = false
            };

            if (street)
            {
                const std::optional<Perch> pavement = animalPerch(streetRandom, false);

                if (!pavement || !pavement->pavement)
                    continue;

                perch = *pavement;
            }

            Vec2 delta = perch.at - near;

            if (delta.length2() < 20 * 20)
                continue;

            double merit = 55 * dot(delta.unit(), away) - std::abs(delta.length() - 110);

            if (merit > score)
            {
                score = merit;
                best = perch;
            }
        }

        if (score == -1e9 && !habitat.empty())
        {
            return {
                .at = *std::ranges::max_element(habitat, {}, [&](const Vec2 p) { return (p - near).length2(); }), 
                .pavement = false
            };
        }

        return best;
    }

    void World::take_flight(int index, Vec2 from, Vec2 away)
    {
        Flock &f = flocks[index];
        if (f.flying)
            return;

        const Perch perch = landing_site(from, away);
        f.landing = perch.at;
        f.flying = true;

        // The flock forms up for where it is going: a spiral on a lawn, two files along a
        // pavement, so that no bird comes down on the roadway.
        int member = 0;

        for (Animal &a : animals)
            if (a.flock == index)
                a.offset = flockOffset(member++, perch.pavement);

        if ((from - position()).length2() < 45 * 45)
            sounds.push_back({
                .name = "wings", 
                .source = from, 
                .gain = .36
            });
    }

    void World::scare_wildlife(Vec2 origin, double height, double lethalRadius)
    {
        for (Animal &a : animals)
        {
            if (a.deadUntil > 0)
                continue;

            auto away = a.p - origin;
            double dy = a.altitude + .24 - height;
            double d2 = away.length2() + dy * dy;
            if (d2 > 24 * 24)
                continue;
            if (d2 < lethalRadius * lethalRadius && blastVisible(origin, height, a.p, a.altitude + .24))
            {
                a.deadUntil = time + 14;
                a.velocity = {};
            }
            if (a.flock >= 0)
                take_flight(a.flock, a.p, away.unit());
            else
            {
                a.goal = a.p + away.unit() * 11;
                a.fear = 3;
            }
        }
    }

    void World::step_wildlife(double dt)
    {
        // Perception and travel use a fixed, lower-rate clock independent of rendering.
        if (habitat.empty())
            return;
        animalClock += dt;
        constexpr double tick = .1;
        if (animalClock + 1e-9 < tick)
            return;
        animalClock = std::max(0., animalClock - tick);
        auto focus = position();
        std::vector<const Body *> walkers, vehicles;
        for (auto &p : people)
            if (!p.dead && p.riding < 0 && (p.body.p - focus).length2() < 115 * 115)
                walkers.push_back(&p.body);
        if (controlledCar < 0)
            walkers.push_back(&player);
        for (auto &c : cars)
            if ((c.body.p - focus).length2() < 120 * 120 && !c.destroyed)
                vehicles.push_back(&c.body);
        std::vector<bool> airborne(flocks.size(), false);
        for (auto &a : animals)
        {
            bool local = (focus - a.p).length2() < 100 * 100;
            if (a.deadUntil > 0 || a.knockedUntil > time)
            {
                // Port of the knocked branch of LifeBeforeContacts: whatever a car threw aside
                // flies, comes down and slides to rest.
                // The clock of the animals is coarse: the arc of each step is taken exactly.
                const double ground = terrain.height(a.p);
                a.altitude = std::max(ground, a.altitude + a.rise * tick - .5 * 9.81 * tick * tick);
                a.rise -= 9.81 * tick;
                const bool landed = a.altitude <= ground;
                if (landed)
                    a.rise = 0;
                a.velocity *= std::exp(-tick * (landed ? 4 : .25));
                const Vec2 next = a.p + a.velocity * tick;
                if (a.velocity.length2() < .0004 || !layout.urbanSite(next, .2) ||
                    insideBuilding(next, a.altitude + .3, .28))
                    a.velocity = {};
                else
                {
                    a.p = next;
                    a.altitude = std::max(a.altitude, terrain.height(next));
                }
                if (a.deadUntil == 0 || a.deadUntil > time || local)
                    continue;
                Vec2 respawn = a.flock >= 0 ? flocks[a.flock].landing + a.offset
                                            : habitat[streetRandom.index(int(habitat.size()))];
                if (a.flock < 0)
                    if (const auto perch = animalPerch(streetRandom, false))
                        respawn = perch->at;
                if ((respawn - focus).length2() < 105 * 105)
                    continue;
                a.p = a.goal = respawn;
                a.altitude = terrain.height(a.p);
                a.velocity = {};
                a.deadUntil = a.fear = a.knockedUntil = a.rise = 0;
            }
            Vec2 danger{};
            double urgency = 0;
            auto senseThreat = [&](Vec2 p, double range)
            {
                Vec2 delta = a.p - p;
                double weight = 1 - delta.length() / range;
                if (weight > urgency)
                {
                    urgency = weight;
                    danger = delta.length2() > .0001 ? delta.unit() : forward(a.heading);
                }
            };
            if (local && a.altitude < terrain.height(a.p) + 2)
            {
                for (auto *b : walkers)
                    if (std::abs(b->height - a.altitude) < 2.2 && (b->p - a.p).length2() < 16)
                        senseThreat(b->p, a.species == Species::cat   ? 2.8
                                          : a.species == Species::dog ? 0.9
                                                                      : 3.5);
                if (a.species != Species::dog)
                    for (auto &other : animals)
                        if (&other != &a && other.deadUntil == 0 && other.flock < 0 &&
                            (other.species == Species::dog || a.flock >= 0) && (other.p - a.p).length2() < 49)
                            senseThreat(other.p, other.species == Species::cat ? 5. : 7.);
                for (auto *b : vehicles)
                {
                    auto delta = a.p - b->p;
                    if (delta.length2() > 16 * 16 || std::abs(b->height - a.altitude) > 2.)
                        continue;
                    auto at = rotate(delta, -b->angle);
                    // Port of the managed rule: a car that closes at more than 1.1 m/s throws
                    // the animal aside and stuns it for three seconds; above 4 m/s it kills.
                    const double approach = dot(b->v - a.velocity, delta) / std::max(.25, delta.length());
                    if (approach >= 1.1 && std::abs(at.x) < b->halfW + .12 && std::abs(at.z) < b->halfH + .15 &&
                        a.altitude < b->height + .5)
                    {
                        a.knockedUntil = time + 3;
                        a.rise = std::min(4., approach * .21);
                        a.velocity = b->v * .5;
                        a.fear = 0;
                        if (approach > 4)
                            a.deadUntil = time + 14;
                        sounds.push_back({"impact", a.p, .3});
                        break;
                    }
                    if (b->v.length2() > 6)
                        senseThreat(b->p, 13);
                }
            }
            if (a.deadUntil > 0 || a.knockedUntil > time)
                continue;
            double speed = 0;
            a.fear = std::max(0., a.fear - tick);
            if (urgency > 0)
            {
                if (a.flock >= 0)
                    take_flight(a.flock, a.p, danger);
                else
                {
                    if (a.fear == 0 && (a.p - focus).length2() < 35 * 35)
                        sounds.push_back({a.species == Species::cat ? "cat" : "dog", a.p, .37});
                    a.fear = 2.4;
                    a.goal = a.p + danger * (5. + urgency * 5.);
                }
            }
            bool flying = a.flock >= 0 && flocks[a.flock].flying;
            if (a.species == Species::bird && !flying &&
                time > flocks[a.flock].landedAt + 35 + int(a.heading * 2))
            {
                take_flight(a.flock, a.p, forward(a.heading));
                flying = true;
            }
            Vec2 target = a.goal;
            if (flying)
            {
                target = flocks[a.flock].landing + a.offset;
                speed = a.species == Species::bird ? 9. : 7.5;
            }
            else if (a.fear > 0)
                speed = a.species == Species::dog ? 5.3 : 4.5;
            else if (a.flock >= 0)
            {
                if (time >= a.thinkAt)
                {
                    Vec2 centre = flocks[a.flock].landing + a.offset;
                    Vec2 candidate = centre + forward(streetRandom.next() * 2 * pi) * .9;
                    if (layout.urbanSite(candidate, .3) && !openCut(candidate, .4) &&
                        !insideBuilding(candidate, terrain.height(candidate) + .4, .35))
                        a.goal = candidate;
                    a.thinkAt = time + 2. + streetRandom.next() * 6.;
                }
                target = a.goal;
                speed = .31;
            }
            else
            {
                // A stray of the managed game walks the pavements from corner to corner, the
                // graph the people use, and sets off again on arrival or after 12 to 32
                // seconds, wherever that finds it.
                if (time >= a.thinkAt || (a.goal - a.p).length2() < .7 * .7)
                {
                    const int last = layout.nodes() - 1;
                    const Vec2 step = CityLayout::direction(streetRandom.index(4));
                    const int i = clamp(int(std::lround((a.p.x - layout.coordinate(0)) / spacing)) +
                                            int(std::lround(step.x)),
                                        0, last),
                              j = clamp(int(std::lround((a.p.z - layout.coordinate(0)) / spacing)) +
                                            int(std::lround(step.z)),
                                        0, last);
                    a.goal = {layout.coordinate(i) + layout.sidewalk(), layout.coordinate(j) + layout.sidewalk()};
                    a.thinkAt = time + 12. + streetRandom.next() * 20.;
                }
                target = a.goal;
                speed = a.species == Species::dog ? 1.35 : .85;
            }
            Vec2 delta = target - a.p;
            Vec2 next = a.p + delta.unit() * std::min(delta.length(), speed * tick);
            double floor = terrain.height(next);
            if (!flying &&
                (!layout.urbanSite(next, .2) || openCut(next, .25) || insideBuilding(next, floor + .4, .28)))
            {
                next = a.goal = a.p;
                a.thinkAt = time;
            }
            if (flying)
            {
                double cruise = std::min(10., delta.length() * .23);
                double routeFloor = std::max(flightFloor(next), flightFloor(next + delta.unit() * 2.));
                if (routeFloor > floor + .8)
                    routeFloor += .55;
                double desired = std::max(floor + cruise, routeFloor);
                double oldAltitude = a.altitude;
                a.altitude += clamp(desired - a.altitude, -3. * tick, 4.5 * tick);
                // Climb before entering a facade. A flock can cross roofs without clipping them.
                if (routeFloor > oldAltitude + .25 && routeFloor > floor + .8)
                    next = a.p;
                if (delta.length() > .35 || a.altitude > floor + .35)
                    airborne[a.flock] = true;
            }
            else
                a.altitude = terrain.height(next);
            a.velocity = (next - a.p) / tick;
            a.distance += (next - a.p).length();
            if (a.velocity.length2() > .002)
                a.heading = std::atan2(a.velocity.x, a.velocity.z);
            a.p = next;
        }

        for (size_t i = 0; i < flocks.size(); ++i)
            if (flocks[i].flying && !airborne[i])
            {
                flocks[i].flying = false;
                flocks[i].landedAt = time;

                for (Animal &animal : animals)
                    if (animal.flock == static_cast<int>(i))
                        animal.goal = animal.p;
            }
    }
}
