#include <utility>

#include "promzona/world.hpp"

namespace Promzona
{
    namespace
    {
        int cell_coordinate(double x)
        {
            return clamp(static_cast<int>(std::floor(x / World::spacing + World::nodes * .5)), 0, World::nodes - 1);
        }

        // How far ahead in time a turning driver reckons with somebody walking towards the curve.
        constexpr double foresight = 1.6;

        template <class Visit> void nearby(const World &w, Vec2 pos, double radius, Visit visit)
        {
            // Include travel since the last index refresh; narrow phase uses live positions.
            const double range = radius + 12;

            for (int z = cell_coordinate(pos.z - range); z <= cell_coordinate(pos.z + range); ++z)
                for (int x = cell_coordinate(pos.x - range); x <= cell_coordinate(pos.x + range); ++x)
                    visit(w.trafficCells[z * World::nodes + x]);
        }

        bool same_surface(const World &world, const Body &a, const Body &b)
        {
            if (std::abs(a.height - b.height) < 2.5)
                return true;
            // Beside a ramp the level of a body on the street is not always the street's: two
            // bodies that both stand on the ground are on the same surface whatever it is called.
            auto grounded = [&](const Body &body)
            { return std::abs(body.height - world.surface(body.p, 0) - .8) < 1.2; };
            if (grounded(a) && grounded(b))
                return true;
            if (a.roadLevel != b.roadLevel)
                return false;
            // A queue may climb several metres on a steep street. Compare clearance
            // above the pavement so the next vehicle is still visible uphill.
            return std::abs(a.height - world.surface(a.p, a.roadLevel) - (b.height - world.surface(b.p, b.roadLevel))) < 2.5;
        }

        double projectedExtent(const Body &b, const Vec2 axis)
        {
            const auto facing = forward(-b.angle);
            return b.halfH * std::abs(dot(facing, axis)) + b.halfW * std::abs(cross(facing, axis));
        }
    }

    void World::walkers_near(const Vec2 pos, const double radius, std::vector<int> &out) const
    {
        out.clear();

        nearby(*this, pos, radius,
            [&](const TrafficCell &cell)
            {
                for (int i = cell.person; i >= 0; i = nextTrafficPerson[i])
                    if (people[i].riding < 0 && (people[i].body.p - pos).length2() <= radius * radius)
                        out.push_back(i);
            });
    }

    void World::index_traffic()
    {
        trafficCells.assign(nodes * nodes, {});

        auto cell = [&](Vec2 p) -> TrafficCell &
        {
            return trafficCells[cell_coordinate(p.z) * nodes + cell_coordinate(p.x)];
        };

        nextTrafficCar.resize(cars.size());

        for (int i = 0; std::cmp_less(i, cars.size()); ++i)
        {
            int &head = cell(cars[i].body.p).car;
            nextTrafficCar[i] = head;
            head = i;
        }

        nextTrafficPerson.resize(people.size());

        for (int i = 0; std::cmp_less(i, people.size()); ++i)
        {
            if (people[i].riding >= 0)
                continue;

            int &head = cell(people[i].body.p).person;
            nextTrafficPerson[i] = head;
            head = i;
        }

        trafficIndexAt = time + .18;
    }

    // The driver knows the next turn before the lights: the junction is entered only when
    // the street beyond it has room (managed CityTraffic.Enter). Emergency crews decide at
    // the last moment and go wherever they have to.
    void World::plan_exit(Car &car)
    {
        if (car.planned_to >= 0 || car.routePhase == 1 || car.responding || car.line >= 0 || !car.route)
            return;

        if (const Rescue *call = car.kind == 4 ? ambulanceCall(static_cast<int>(car.id)) : nullptr; (call && call->stage == RescueStage::driving) || !degree(car.routeTo))
            return;

        car.planned_to = choose_next(car.routeTo, car.routeFrom);

        // A bus does not turn into a street it could leave only by turning round. It is too
        // long for the turn at the end, and stood across the street there.
        if (car.kind == 5 && degree(car.planned_to) < 2)
        {
            std::vector<int> ways = neighbours(car.routeTo);

            std::erase_if(ways, [&](int n) { return n == car.routeFrom || degree(n) < 2; });

            if (!ways.empty())
                car.planned_to = ways[static_cast<size_t>(car.id + car.routeTo) % ways.size()];
        }
    }

    namespace
    {
        // Every car near a point until `visit` says no; the cell index when there is one.
        template <class Visit> bool eachCarNear(const World &w, Vec2 centre, double radius, Visit visit)
        {
            if (w.trafficCells.empty() || w.nextTrafficCar.size() != w.cars.size())
            {
                return std::ranges::all_of(w.cars, visit);
            }
            bool open = true;
            nearby(w, centre, radius,
                   [&](const World::TrafficCell &cell)
                   {
                       for (int i = cell.car; open && i >= 0; i = w.nextTrafficCar[i])
                           open = visit(w.cars[i]);
                   });
            return open;
        }
    }

    bool World::on_same_surface(const Body &a, const Body &b) const
    {
        return same_surface(*this, a, b);
    }

    // One crossing reservation per junction; released when the crossing path ends. Whoever
    // crosses this junction is on its surface: the height test that stood here let a second
    // car in on steep streets, where the one in the middle may stand metres higher.
    bool World::junction_held(int node, const Car *except) const
    {
        const Vec2 centre = nodePoint(node);

        return !eachCarNear(*this, centre, 15,
            [&](const Car &c)
            {
                return &c == except || c.destroyed || c.routePhase != 1 || c.routeFrom != node || c.line >= 0 || (c.body.p - centre).length() >= 15;
            });
    }

    bool World::intersection_clear(const Car &car) const
    {
        if (car.routePhase == 1 || car.responding)
            return true;
        const int node = car.routeTo;
        const Vec2 centre = nodePoint(node);
        auto f = (centre - nodePoint(car.routeFrom)).unit();
        int d = f.z > .5 ? 0 : f.x > .5 ? 1 : f.z < -.5 ? 2 : 3;
        // A crew on its way to a patient does not wait for the lights (the managed
        // IsResponding); whoever is crossing still has the junction, and beyond it there has
        // to be room, as for anybody.
        const auto *call = car.kind == 4 ? ambulanceCall(int(car.id)) : nullptr;
        // Asked by every car that waits at a junction, on every step: the neighbours are counted,
        // not listed.
        if (!(call && call->stage == RescueStage::driving) && degree(node) >= 3 &&
            !CityLayout::green(node % nodes, node / nodes, d, time))
            return false;
        auto each = [&](double radius, auto visit) { return eachCarNear(*this, centre, radius, visit); };
        if (junction_held(node, &car))
            return false;
        // A tram or a trolleybus on the junction, or about to be: they do not take reservations,
        // and a car that turned across one stood nose to nose with it until the end of the game.
        if (!each(26,
                  [&](const Car &c)
                  {
                      if (c.line < 0 || c.destroyed || !same_surface(*this, c.body, car.body))
                          return true;
                      const Vec2 way = forward(c.heading()), rel = c.body.p - centre;
                      const double along = dot(rel, way), speed = dot(c.body.v, way);
                      if (std::abs(cross(rel, way)) >= layout.roadHalf())
                          return true;
                      // On the junction now, or reaching it before a car could clear it.
                      const double entry = -layout.roadHalf() - c.body.halfH;
                      // Ready to cross and waiting for whoever is crossing now: the tram goes next.
                      // At a busy junction the cars took their turns one after another while it
                      // stood at its green light for minutes.
                      return !(c.claimed == node || std::abs(along) < -entry ||
                               (along < 0 && speed > 1 && entry - along < speed * 2.5 + 3));
                  }))
            return false;
        // Nobody starts across the zebra in front of the bumper while somebody is still on it:
        // the car that did stopped a step later, across the line everybody walks along.
        // Only where walkers keep to the lights themselves, near the player: further away they
        // cross as they come, and a driver who waited for every one of them never got a green.
        if ((car.body.p - position()).length2() < 110 * 110 && !trafficCells.empty() &&
            nextTrafficPerson.size() == people.size())
        {
            const Vec2 side = perpendicular(f);
            bool free = true;
            nearby(*this, car.body.p, 8,
                   [&](const TrafficCell &cell)
                   {
                       for (int i = cell.person; free && i >= 0; i = nextTrafficPerson[i])
                       {
                           const auto &walker = people[i].body;
                           if (people[i].riding >= 0 || !same_surface(*this, car.body, walker))
                               continue;
                           const Vec2 gap = walker.p - car.body.p;
                           const double before = dot(gap, f) - car.body.halfH;
                           // Whoever is walking towards the lane counts from where they will be
                           // when the car is on the zebra: a car that set off and then stopped
                           // for them stood across the line everybody walks along.
                           const double lateral = dot(gap, side), drift = dot(walker.v, side);
                           const double coming =
                               lateral * drift < 0 ? std::min(std::abs(lateral), std::abs(drift) * 3) : 0.;
                           free = before < -.5 || before > 4.5 ||
                                  std::abs(lateral) - coming > car.body.halfW + walker.radius + .5;
                       }
                   });
            if (!free)
                return false;
        }
        if (car.planned_to < 0)
            return true;
        // Do not block the box: beyond the crossing of the street the car leaves by there
        // must be room for all of it, or the queue ahead keeps it in the middle of the
        // junction and the cross traffic with it. A city of such junctions stops for good.
        const Vec2 out = (nodePoint(car.planned_to) - centre).unit(), side = perpendicular(out);
        const double lane = CityLayout::laneOffset(levelEdge(node, car.planned_to) ? 0 : car.laneIndex);
        const double need = 9.5 + 2 * car.body.halfH + 1.1;
        return each(need + 8,
                    [&](const Car &c)
                    {
                        if (&c == &car || !same_surface(*this, c.body, car.body))
                            return true;
                        const Vec2 delta = c.body.p - centre;
                        const double along = dot(delta, out), length = projectedExtent(c.body, out);
                        return along + length < 7.3 || along - length > need ||
                               std::abs(dot(delta, side) - lane) >
                                   car.body.halfW + projectedExtent(c.body, side) + .2 ||
                               (!c.destroyed && dot(c.body.v, out) > 2.5);
                    });
    }

    // Port of CityTraffic.BusStop: a bus that drives north along the kerb pulls up at the
    // shelter, stands there four to seven seconds and passes the stops by for the next half
    // minute. The managed bus stopped dead inside a box around the sign; this one brakes for it.
    double World::busStopLimit(Car &bus)
    {
        if (bus.destroyed || bus.driverless || bus.fireTime > 0 || bus.responding || int(bus.id) == controlledCar)
            return 100;
        if (bus.bus_until > time)
            return 0;
        if (time < bus.next_bus_stop || bus.routePhase != 0 || bus.routeTo != bus.routeFrom + nodes ||
            (layout.wide() && bus.laneIndex == 0) || busStopOfNode.empty())
            return 100;
        const int stop = busStopOfNode[size_t(bus.routeFrom)];
        if (stop < 0)
            return 100;
        const double ahead = busStops[size_t(stop)].z - bus.body.p.z;
        if (ahead < -2.5 || ahead > 30)
            return 100;
        if (ahead < 1.5 && bus.body.v.length2() < .36)
        {
            bus.bus_until = time + 4 + bus.id % 4;
            bus.next_bus_stop = bus.bus_until + 34;
            return 0;
        }
        return std::max(.5, std::sqrt(5.5 * std::max(0., ahead)));
    }

    bool World::tryChangeLane(Car &c, int destination)
    {
        if (!layout.wide() || !c.route || c.routePhase != 0 || c.body.roadLevel != 0 || c.line >= 0 ||
            c.responding || c.personal || c.summoned || destination < 0 || destination > 1 ||
            c.laneIndex == destination || time < c.next_lane_decision || c.destroyed || c.falling)
            return false;
        if (trafficCells.empty())
            index_traffic();
        auto from = nodePoint(c.routeFrom), to = nodePoint(c.routeTo);
        auto dir = (to - from).unit(), side = perpendicular(dir);
        auto end = to - dir * stopLine + side * CityLayout::laneOffset(destination);
        auto start = c.body.p;
        double ahead = dot(end - start, dir);
        if (ahead < 8 || ahead > spacing - 9 || dot(forward(c.heading()), dir) < .82 ||
            std::abs(dot(start - from, side) - CityLayout::laneOffset(c.laneIndex)) > 1.1)
            return false;

        bool clear = true;
        const double speed = dot(c.body.v, dir);
        auto personBlocks = [&](const Body &b)
        {
            return same_surface(*this, c.body, b) && std::abs(dot(b.p - end, side)) < c.body.halfW + .7 &&
                   std::abs(dot(b.p - start, dir)) < 12;
        };
        nearby(*this, start, 45,
               [&](const TrafficCell &cell)
               {
                   for (int i = cell.car; clear && i >= 0; i = nextTrafficCar[i])
                   {
                       const auto &other = cars[i];
                       if (&other == &c || other.falling || !same_surface(*this, c.body, other.body))
                           continue;
                       const auto &b = other.body;
                       bool reserved = i != controlledCar && !other.personal && !other.destroyed &&
                                       other.line < 0 && other.routePhase == 0 &&
                                       other.routeFrom == c.routeFrom && other.routeTo == c.routeTo &&
                                       other.laneIndex == destination;
                       if (!reserved &&
                           std::abs(dot(b.p - end, side)) > c.body.halfW + projectedExtent(b, side) + .45)
                           continue;
                       const double extent = c.body.halfH + projectedExtent(b, dir);
                       const double longitudinal = dot(b.p - start, dir), relative = dot(b.v, dir) - speed;
                       if (longitudinal > -extent - 4 - 2 * std::max(0., relative) &&
                           longitudinal < extent + 6 + 2 * std::max(0., -relative))
                           clear = false;
                   }
                   for (int i = cell.person; clear && i >= 0; i = nextTrafficPerson[i])
                       if (people[i].riding < 0 && personBlocks(people[i].body))
                           clear = false;
               });
        if (!clear || (controlledCar < 0 && personBlocks(player)))
            return false;

        double handle = ahead * .40;
        Path merge(start, start + dir * handle, end - dir * handle, end);
        c.route = std::move(merge);
        c.routeDistance = 0;
        c.laneIndex = destination;
        c.next_lane_decision = time + 8;
        c.blocked_since = -1;
        return true;
    }

    // Port of the managed pursuit (PolicePursuit and PoliceRuleStep in CityTraffic.Pursuit.cs):
    // an emergency crew treats traffic as a steering cost, never as a red light, a reservation
    // or a right of way. Whoever is ahead is gone round and slows the unit down; nothing but a
    // contact stops it. Before this port a unit queued like everybody else: one that came
    // round a junction with its bonnet at the car waiting at the opposite stop line stood
    // there for the rest of the pursuit, and the four streets of the junction with it.
    void World::sensePursuit(Car &c, bool local)
    {
        const auto &b = c.body;
        const Vec2 nose = forward(c.heading());
        c.blocked_since = -1;
        c.avoid = c.pullOut = 0;
        if (!local)
        {
            // PoliceRuleStep: on the rails of the distant model the unit pulls out towards the
            // middle of the street round whoever is ahead, at twelve metres a second at most.
            // Reckoned from the lane, not from where the unit has pulled out to: until it is
            // past, whoever it goes round is still in the lane.
            const Vec2 side = perpendicular(nose), lane = c.route ? c.route->sample(c.routeDistance).first : b.p;
            nearby(*this, lane, 20,
                   [&](const TrafficCell &cell)
                   {
                       for (int i = cell.car; i >= 0; i = nextTrafficCar[i])
                       {
                           const auto &other = cars[i];
                           if (&other == &c || i == controlledCar || !same_surface(*this, b, other.body))
                               continue;
                           const Vec2 gap = other.body.p - lane;
                           const double ahead = dot(gap, nose);
                           if (ahead > -b.halfH - other.body.halfH && ahead < 18 &&
                               std::abs(dot(gap, side)) < b.halfW + other.body.halfW + .5)
                           {
                               c.traffic_limit = 12;
                               c.pullOut = -2.8;
                           }
                       }
                   });
            return;
        }
        // The managed car may come up at 1.8 m/s for every metre left, and never slower than
        // four metres a second. From its full speed nothing brakes like that: here the unit also
        // comes up no faster than it can stop from, and so does not go on into those ahead.
        auto pace = [](double gap)
        { return std::max(4., std::min(gap * 1.8, std::sqrt(12 * std::max(0., gap)))); };
        if (c.seesSuspect || !c.route)
        {
            // PolicePursuit, straight for the suspect: steering away from whatever is in front
            // of the bonnet, never slower than four metres a second for a car and five for a
            // walker.
            const Vec2 side{nose.z, -nose.x};
            double steer = 0, speed = 100;
            nearby(*this, b.p, 18,
                   [&](const TrafficCell &cell)
                   {
                       for (int i = cell.car; i >= 0; i = nextTrafficCar[i])
                       {
                           const auto &other = cars[i];
                           if (&other == &c || i == controlledCar || !same_surface(*this, b, other.body))
                               continue;
                           const Vec2 gap = other.body.p - b.p;
                           const double ahead = dot(gap, nose), lateral = dot(gap, side);
                           if (gap.length2() < 18 * 18 && ahead > 0 && ahead < 16 &&
                               std::abs(lateral) < b.halfW + other.body.halfW + .8)
                           {
                               steer += std::copysign(.65, lateral == 0 ? (c.id % 2 ? -1. : 1.) : -lateral);
                               speed = std::min(speed, pace(ahead - b.halfH - other.body.halfH));
                           }
                       }
                       for (int i = cell.person; i >= 0; i = nextTrafficPerson[i])
                       {
                           const auto &walker = people[i];
                           if (walker.riding >= 0 || !same_surface(*this, b, walker.body))
                               continue;
                           const Vec2 gap = walker.body.p - b.p;
                           const double ahead = dot(gap, nose), lateral = dot(gap, side);
                           if (gap.length2() < 10 * 10 && ahead > 0 && ahead < 9 && std::abs(lateral) < 2)
                           {
                               steer += std::copysign(.5, lateral == 0 ? 1. : -lateral);
                               speed = std::min(speed, 5.);
                           }
                       }
                   });
            if (!b.roadLevel)
                for (const auto &animal : animals)
                {
                    const Vec2 gap = animal.p - b.p;
                    if (animal.deadUntil > time || animal.altitude > .3 || gap.length2() >= 7 * 7)
                        continue;
                    const double lateral = dot(gap, side);
                    if (dot(gap, nose) > 0 && std::abs(lateral) < 1.8)
                    {
                        steer += std::copysign(.3, -lateral);
                        speed = std::min(speed, 7.);
                    }
                }
            c.avoid = steer;
            c.traffic_limit = speed;
            return;
        }
        // Along the street the same cost is paid in metres: the unit pulls out of its lane to
        // where nobody is, as the managed rule-driven car does (OvertakeOffset), instead of
        // steering away from the bonnet's own line. Steered by the bonnet, a unit behind two
        // cars at a stop line swung out, lost sight of them, swung back in and pushed them.
        const auto [onRoute, heading] = c.route->sample(c.routeDistance);
        const Vec2 dir = forward(heading), across = perpendicular(dir);
        const double mine = dot(b.p - onRoute, across);
        // What lies ahead, as a stretch across the street and the metres left to it. Whoever
        // comes the other way is counted from further off, by as much as the two close in a
        // second and a half: pulled out into the oncoming lane, a unit met the first car in it.
        struct Shadow
        {
            double from, to, gap, closing;
            bool walker;
        };
        std::array<Shadow, 24> shadows;
        size_t count = 0;
        auto shade = [&](const Body &other, double reach, double halfWidth, double halfLength, bool walker)
        {
            const Vec2 gap = other.p - b.p;
            const double ahead = dot(gap, dir), closing = walker ? 0. : std::max(0., -dot(other.v, dir));
            if (count == shadows.size() || ahead <= -b.halfH - halfLength || ahead >= reach + closing * 1.5 ||
                !same_surface(*this, b, other))
                return;
            const double middle = dot(other.p - onRoute, across), width = b.halfW + halfWidth + .6;
            shadows[count++] = {middle - width, middle + width, ahead - b.halfH - halfLength, closing, walker};
        };
        nearby(*this, b.p, 34,
               [&](const TrafficCell &cell)
               {
                   for (int i = cell.car; i >= 0; i = nextTrafficCar[i])
                       if (&cars[i] != &c && i != controlledCar)
                           shade(cars[i].body, 16, projectedExtent(cars[i].body, across),
                                 projectedExtent(cars[i].body, dir), false);
                   for (int i = cell.person; i >= 0; i = nextTrafficPerson[i])
                       if (people[i].riding < 0)
                           shade(people[i].body, 9, people[i].body.radius + .5, people[i].body.radius, true);
               });
        // Where the unit may be: on the carriageway of a street, and a lane's width to either
        // side of a crossing path or a level road.
        double lowest = -3.4, highest = 3.4;
        if (c.routePhase == 0)
        {
            const Vec2 axis = (nodePoint(c.routeTo) - nodePoint(c.routeFrom)).unit();
            if (dot(axis, dir) > .95)
            {
                const double lane = dot(onRoute - nodePoint(c.routeFrom), perpendicular(axis)),
                             edge = layout.roadHalf() - b.halfW - .4;
                lowest = std::min(0., -edge - lane);
                highest = std::max(0., edge - lane);
            }
        }
        auto shaded = [&](double offset)
        {
            for (size_t k = 0; k < count; ++k)
                if (offset > shadows[k].from && offset < shadows[k].to)
                    return true;
            return false;
        };
        if (shaded(0))
        {
            // The free place nearest to where the unit already is; with none, straight on.
            double best = 1e9;
            for (size_t k = 0; k < count; ++k)
                for (const double offset : {shadows[k].from - .05, shadows[k].to + .05})
                    if (offset >= lowest && offset <= highest && !shaded(offset) &&
                        std::abs(offset - c.overtake) < best &&
                        (c.routePhase == 0 || chaseGround(b.p + dir * 6 + across * (offset - mine))))
                    {
                        best = std::abs(offset - c.overtake);
                        c.pullOut = offset;
                    }
        }
        // Slowed down only by what is in front of the bumper where the unit is now: to five
        // metres a second for somebody on foot, which knocks nobody down for good, and never
        // below four for a car.
        for (size_t k = 0; k < count; ++k)
            if (mine > shadows[k].from + .3 && mine < shadows[k].to - .3 && shadows[k].gap > -1)
                c.traffic_limit =
                    std::min(c.traffic_limit, shadows[k].walker ? 5. : pace(shadows[k].gap - shadows[k].closing));
    }

    void World::senseTraffic(Car &c, bool local)
    {
        if (trafficCells.empty())
            index_traffic();
        const double interval = local ? .15 : .38;
        c.sense_at = time + interval + (c.id % 7) * .012;
        c.traffic_limit = 100;
        if (c.responding)
        {
            sensePursuit(c, local);
            return;
        }
        const auto &b = c.body;
        // Traffic on a route looks along the route. Out of a turn the bonnet still points across
        // the lanes: looking along it, a car took the tram that waited in the oncoming lane for
        // an obstacle and stopped, the tram waited for the junction behind the car to clear, and
        // the truck in the junction waited for the car.
        const auto nose = forward(c.heading());
        const bool routed = c.route && c.routePhase != 1;
        const auto dir = routed ? forward(c.route->sample(c.routeDistance).second) : nose,
                   right = perpendicular(dir);
        // What stands right in front of the bumper counts whichever way the route goes.
        const bool askew = dot(nose, dir) < .99;
        // The bumper over the next two and a half metres: the car steers back to its route as
        // it goes. Reckoned straight ahead, the bonnet of a truck that came out of a left turn
        // pointed at the truck waiting in the oncoming lane; it stopped, the van behind it stood
        // in the junction, and the truck in the oncoming lane waited for the junction.
        std::array<Vec2, 6> bumper{};
        if (askew)
        {
            const double error = std::atan2(cross(nose, dir), dot(nose, dir)),
                         rate = .6 * std::tan(.68) / std::max(1.8, c.wheelbase);
            Vec2 at = b.p;
            for (size_t k = 0; k < bumper.size(); ++k)
            {
                const Vec2 heading =
                    rotate(nose, std::copysign(std::min(std::abs(error), rate * .5 * double(k)), error));
                bumper[k] = at + heading * b.halfH;
                at += heading * .5;
            }
        }
        // During a merge the bonnet still points down the original lane. Check the
        // actual path at each obstacle, including its nearer end, so a stopped car
        // keeps clearance while the following car moves sideways around it.
        auto pathSide = [&](double distance)
        {
            // A level road winds too: its oncoming lane is not ahead because the bonnet points at it.
            if (c.routePhase == 1 || !c.route)
                return 0.;
            auto [p, heading] = c.route->sample(c.routeDistance + distance);
            double over = c.routeDistance + distance - c.route->length();
            if (over > 0)
                p += forward(heading) * over;
            return dot(p - b.p, right);
        };
        auto limit = [&](double gap, double leaderSpeed)
        {
            const double space = std::max(0., gap - 1.1);
            const double leader = std::max(0., leaderSpeed);
            // Both stopping distance and time headway matter, in particular at a
            // stationary queue. A driver with no clearance must remain stationary.
            c.traffic_limit = std::min({c.traffic_limit, std::sqrt(leader * leader + 5.5 * space),
                                       space / .95 + std::min(leader, space)});
        };
        if (c.kind == 5)
            c.traffic_limit = std::min(c.traffic_limit, busStopLimit(c));
        const Car *blocker = nullptr;
        double nearest = 24;
        // In a turn the bonnet does not point where the car is going. Whoever is on the
        // crossing of the street the car turns into is watched along the curve, as the
        // managed Enter checks the crossing paths before a car may go.
        std::array<Vec2, 8> curve{}, curveAhead{};
        const bool turning = c.routePhase == 1 && c.route;
        // The walked line of the street the car turns into is 9.1 m from the middle of the
        // junction. A car that gives way does so before it, in the box: one that stopped
        // across the line stood in the way of everybody it was waiting for.
        Vec2 junction{}, exit{};
        double holdBack = 1e9;
        if (turning)
        {
            for (size_t k = 0; k < curve.size(); ++k)
            {
                const double s = c.routeDistance + 1.5 * double(k + 1), over = s - c.route->length();
                const auto [p, heading] = c.route->sample(s);
                curveAhead[k] = forward(heading);
                curve[k] = over > 0 ? p + curveAhead[k] * over : p;
            }
            junction = nodePoint(c.routeFrom);
            exit = (nodePoint(c.routeTo) - junction).unit();
            if (dot(b.p + dir * b.halfH - junction, exit) < 8.1)
                for (double s = 0; s < 16 && holdBack > 1e8; s += .5)
                {
                    const double at = c.routeDistance + s, over = at - c.route->length();
                    const auto [p, heading] = c.route->sample(at);
                    if (dot((over > 0 ? p + forward(heading) * over : p) - junction, exit) + b.halfH >= 8.1)
                        holdBack = s;
                }
        }
        auto yieldToWalker = [&](const Body &walker)
        {
            if (turning)
            {
                if (!same_surface(*this, b, walker))
                    return;
                // Whoever stands in front of the bumper is in the way wherever the route goes.
                const auto near = walker.p - b.p;
                const double before = dot(near, dir), room = b.halfW + walker.radius + .2;
                if (before > 0 && before < b.halfH + 1.2 && std::abs(dot(near, right)) < room)
                {
                    limit(before - b.halfH - walker.radius, 0);
                    return;
                }
                // Further on the bumper swings round with the turn. The box that used to reach
                // straight ahead of it lay across the pavement when a bus came round a corner,
                // and the bus stood in the junction for as long as anybody stood there.
                for (size_t k = 0; k < 3; ++k)
                    if ((walker.p - curve[k] - curveAhead[k] * b.halfH).length2() < room * room)
                    {
                        limit(1.5 * double(k + 1) - walker.radius - .3, 0);
                        return;
                    }
                // Somebody walking towards the curve counts from where they will be shortly.
                const double reach = b.halfW + walker.radius + .45;
                const Vec2 stride = walker.v * foresight;
                const double strides = stride.length2();
                for (size_t k = 0; k < curve.size(); ++k)
                {
                    const Vec2 from = curve[k] - walker.p;
                    const double t = strides > 1e-6 ? clamp(dot(from, stride) / strides, 0., 1.) : 0.;
                    if ((from - stride * t).length2() < reach * reach)
                    {
                        double gap = 1.5 * double(k + 1) - b.halfH - walker.radius;
                        const double across = dot(walker.p - junction, exit);
                        if (holdBack < 1e8 && across > 7.9 && across < 10.6)
                            gap = std::min(gap, holdBack + 1.1);
                        limit(gap, 0);
                        return;
                    }
                }
                return;
            }
            auto delta = walker.p - b.p;
            double along = dot(delta, dir);
            if (along <= 0 || along > 20 || !same_surface(*this, b, walker))
                return;
            double lateral = dot(delta, right) - pathSide(along);
            // In the way of the route, or simply in front of the bumper: a car that comes out
            // of a turn still points across the lane, and the first metres of the straight used
            // to take it over whoever stood beside the line it was steering for.
            const double before = dot(delta, nose);
            if (std::abs(lateral) < b.halfW + walker.radius + .35)
                limit(along - b.halfH - walker.radius, 0);
            else if (before > 0 && before < b.halfH + 3.5 &&
                     std::abs(dot(delta, perpendicular(nose))) < b.halfW + walker.radius + .2)
                limit(before - b.halfH - walker.radius, 0);
        };
        nearby(*this, b.p, 42,
               [&](const TrafficCell &cell)
               {
                   for (int i = cell.car; i >= 0; i = nextTrafficCar[i])
                   {
                       const auto &other = cars[i];
                       if (&other == &c)
                           continue;
                       auto delta = other.body.p - b.p;
                       if (turning)
                       {
                           // In a turn the bonnet sweeps across lanes the car never enters: a truck
                           // waiting at the opposite stop line looked like an obstacle and the two
                           // stood there for good. What counts is the spine of the other vehicle
                           // against the bumper and against the curve ahead.
                           if (delta.length2() > 28 * 28 || !same_surface(*this, b, other.body))
                               continue;
                           // Traffic waiting at this junction stands in the lanes that lead into it;
                           // the way out is never one of them. That goes for the end of a level
                           // road too: a truck on its way into the tunnel waited in the junction for
                           // the car that waited at the lights to come out of it.
                           const bool arriving = other.routePhase == 0 ||
                                                 (other.routePhase == 2 && other.route &&
                                                  other.route->length() - other.routeDistance < 2 * stopLine);
                           if (arriving && other.routeTo == c.routeFrom && !other.destroyed &&
                               !other.driverless && other.line < 0 && i != controlledCar)
                               continue;
                           // So does a tram that has not come into the box yet. It waits for whoever
                           // is crossing: a truck that took its nose for an obstacle waited for the
                           // tram that was waiting for the truck.
                           if (other.line >= 0 && !other.destroyed)
                           {
                               const Vec2 way = forward(other.heading()), rel = other.body.p - junction;
                               if (std::abs(cross(rel, way)) < layout.roadHalf() &&
                                   dot(rel, way) < -layout.roadHalf() - other.body.halfH + 1)
                                   continue;
                           }
                           const Vec2 spine =
                               forward(other.heading()) * std::max(0., other.body.halfH - other.body.halfW);
                           const Vec2 tail = other.body.p - spine;

                           // What is beside or behind the bumper cannot be braked for: a tram that
                           // has caught up from behind is not an obstacle.
                           auto away = [&](const Vec2 at)
                           {
                               const double t = spine.length2() > 1e-9
                                                    ? clamp(dot(at - tail, spine) / (2 * spine.length2()), 0., 1.)
                                                    : 0.;
                               const Vec2 closest = tail + spine * (2 * t);
                               return dot(closest - b.p, dir) < b.halfH - .3 ? 1e9 : (at - closest).length();
                           };

                           const double room = b.halfW + other.body.halfW + .25;
                           double conflict = -1;

                           for (double s = b.halfH; s <= b.halfH + 1.61 && conflict < 0; s += .8)
                               if (away(b.p + dir * s) < room)
                                   conflict = s - b.halfH;

                           for (size_t k = 0; k < curve.size() && conflict < 0; ++k)
                               if (1.5 * static_cast<double>(k + 1) > b.halfH && away(curve[k]) < room)
                                   conflict = 1.5 * static_cast<double>(k + 1) - b.halfH - .4;

                           if (conflict >= 0)
                               limit(conflict, std::max(0., dot(other.body.v, dir)));

                           continue;
                       }

                       // The oncoming lane of a level road is 3.8 m away, but at either end the road
                       // swings across the bonnet of whoever has just come onto it. A van took the
                       // truck that queued in that lane for an obstacle and stopped, the car behind
                       // the van stood in the junction, and the queue was waiting for the junction.
                       // Whoever stands in the oncoming lane is passed; whoever comes down it is
                       // still given the bend.
                       if (routed && c.routePhase == 2 && other.routePhase == 2 && other.route &&
                           other.routeLevel == c.routeLevel && other.routeFrom == c.routeTo &&
                           other.routeTo == c.routeFrom && i != controlledCar && !other.destroyed &&
                           !other.driverless && other.body.v.length2() < .25)
                       {
                           if (const auto [lane, heading] = other.route->sample(other.routeDistance); (other.body.p - lane).length2() < 1 && dot(forward(other.heading()), forward(heading)) > .85)
                               continue;
                       }

                       if (askew && delta.length2() < 14 * 14 && same_surface(*this, b, other.body))
                       {
                           // The spine of the other vehicle against the first metres the bumper
                           // travels: its length is not its width, however the two are turned.
                           const Vec2 spine = forward(other.heading()) * std::max(0., other.body.halfH - other.body.halfW);
                           const double room = b.halfW + other.body.halfW + .15;

                           for (size_t k = 0; k < bumper.size(); ++k)
                           {
                               const Vec2 from = bumper[k] - other.body.p;
                               const double t = spine.length2() > 1e-9
                                   ? clamp(dot(from, spine) / spine.length2(), -1., 1.)
                                   : 0.;

                               if ((from - spine * t).length2() < room * room)
                               {
                                   limit(.5 * static_cast<double>(k), std::max(0., dot(other.body.v, nose)));
                                   break;
                               }
                           }
                       }

                       const double along = dot(delta, dir);

                       if (along <= 0 || along > 38 || std::abs(dot(delta, right)) > 9 || !same_surface(*this, b, other.body))
                           continue;

                       double gap = along - b.halfH - projectedExtent(other.body, dir);
                       const double lateral = dot(delta, right);

                       // Whoever is alongside is not ahead: two trucks in neighbouring lanes are 21 cm
                       // apart and used to hold each other at the lights for ever.
                       if (gap < -1 && std::abs(lateral) > b.halfW + .3)
                           continue;

                       const double width = b.halfW + projectedExtent(other.body, right) + .20;
                       const double speed = dot(other.body.v, dir);

                       if (std::abs(lateral) < width && along < nearest && (other.destroyed || other.body.v.length2() < .5 || (dot(dir, forward(other.heading())) > .8 && (speed < 4.4 || (other.line >= 0 && other.dwell > 0)))))
                       {
                           nearest = along;
                           blocker = &other;
                       }
                       if (std::abs(lateral - pathSide(std::max(0., gap))) < width)
                           limit(gap, speed);
                   }

                   for (int i = cell.person; i >= 0; i = nextTrafficPerson[i])
                       if (people[i].riding < 0)
                           yieldToWalker(people[i].body);
               });

        if (controlledCar < 0)
            yieldToWalker(player);

        // A bus keeps its lane whatever stands in it.
        if (!blocker || c.routePhase != 0 || c.kind == 5)
        {
            c.blocked_since = -1;
            return;
        }

        // A queue at the lights is not an obstacle: keeping eight metres behind its last car
        // halved what a block can hold and left the next car across the junction.
        // Whoever follows a bus to its stop goes round it at once.
        const bool at_stop = blocker->kind == 5 && blocker->bus_until > time;
        const bool queue = 
            !blocker->destroyed && !blocker->driverless && blocker->line < 0 &&
            blocker->routePhase == 0 && blocker->routeFrom == c.routeFrom &&
            blocker->routeTo == c.routeTo && std::cmp_not_equal(blocker->id, controlledCar) &&
            !blocker->personal && !blocker->summoned && !at_stop;

        if (queue)
        {
            c.blocked_since = -1;
            return;
        }

        if (layout.wide() && c.route && blocker->body.v.length2() < 1 && c.route->length() - c.routeDistance >= 8)
        {
            // Leave room to steer while waiting for a gap. Closing bumper-to-bumper
            // during the decision delay strands the queue even if the next lane clears.
            auto road = (nodePoint(c.routeTo) - nodePoint(c.routeFrom)).unit();
            double offset = dot(b.p - nodePoint(c.routeFrom), perpendicular(road));
            if (std::abs(offset - CityLayout::laneOffset(c.laneIndex)) < .7)
                limit(nearest - b.halfH - projectedExtent(blocker->body, dir) - 8, 0);
        }

        if (c.blocked_since < 0)
            c.blocked_since = time;

        if (time < c.next_lane_decision || (time - c.blocked_since < 2 && blocker->line < 0 && !blocker->destroyed && !at_stop))
            return;

        if (random.next() >= .65 || !tryChangeLane(c, 1 - c.laneIndex))
            c.next_lane_decision = time + 1.5;
    }
}
