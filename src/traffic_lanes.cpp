#include "promzona/world.hpp"

namespace pz
{
namespace
{
int cellCoordinate(double x)
{
    return clamp(int(std::floor(x / World::spacing + World::nodes * .5)), 0, World::nodes - 1);
}

// How far ahead in time a turning driver reckons with somebody walking towards the curve.
constexpr double foresight = 1.6;

template <class Visit> void nearby(const World &w, Vec2 pos, double radius, Visit visit)
{
    // Include travel since the last index refresh; narrow phase uses live positions.
    const double range = radius + 12;
    for (int z = cellCoordinate(pos.z - range); z <= cellCoordinate(pos.z + range); ++z)
        for (int x = cellCoordinate(pos.x - range); x <= cellCoordinate(pos.x + range); ++x)
            visit(w.trafficCells[z * World::nodes + x]);
}

bool sameSurface(const World &world, const Body &a, const Body &b)
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
    return std::abs((a.height - world.surface(a.p, a.roadLevel)) -
                    (b.height - world.surface(b.p, b.roadLevel))) < 2.5;
}

double projectedExtent(const Body &b, Vec2 axis)
{
    const auto facing = forward(-b.angle);
    return b.halfH * std::abs(dot(facing, axis)) + b.halfW * std::abs(cross(facing, axis));
}
} // namespace

void World::walkersNear(Vec2 pos, double radius, std::vector<int> &out) const
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

void World::indexTraffic()
{
    trafficCells.assign(nodes * nodes, {});
    auto cell = [&](Vec2 p) -> TrafficCell &
    { return trafficCells[cellCoordinate(p.z) * nodes + cellCoordinate(p.x)]; };
    nextTrafficCar.resize(cars.size());
    for (int i = 0; i < int(cars.size()); ++i)
    {
        int &head = cell(cars[i].body.p).car;
        nextTrafficCar[i] = head;
        head = i;
    }
    nextTrafficPerson.resize(people.size());
    for (int i = 0; i < int(people.size()); ++i)
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
void World::planExit(Car &car)
{
    if (car.plannedTo >= 0 || car.routePhase == 1 || car.responding || car.line >= 0 || !car.route)
        return;
    const auto *call = car.kind == 4 ? ambulanceCall(int(car.id)) : nullptr;
    if ((call && call->stage == RescueStage::driving) || neighbours(car.routeTo).empty())
        return;
    car.plannedTo = chooseNext(car.routeTo, car.routeFrom);
    // A bus does not turn into a street it could leave only by turning round. It is too
    // long for the turn at the end, and stood across the street there.
    if (car.kind == 5 && neighbours(car.plannedTo).size() < 2)
    {
        auto ways = neighbours(car.routeTo);
        std::erase_if(ways, [&](int n) { return n == car.routeFrom || neighbours(n).size() < 2; });
        if (!ways.empty())
            car.plannedTo = ways[size_t(car.id + car.routeTo) % ways.size()];
    }
}

namespace
{
// Every car near a point until `visit` says no; the cell index when there is one.
template <class Visit> bool eachCarNear(const World &w, Vec2 centre, double radius, Visit visit)
{
    if (w.trafficCells.empty() || w.nextTrafficCar.size() != w.cars.size())
    {
        for (const auto &c : w.cars)
            if (!visit(c))
                return false;
        return true;
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
} // namespace

bool World::onSameSurface(const Body &a, const Body &b) const
{
    return sameSurface(*this, a, b);
}

// One crossing reservation per junction; released when the crossing path ends. Whoever
// crosses this junction is on its surface: the height test that stood here let a second
// car in on steep streets, where the one in the middle may stand metres higher.
bool World::junctionHeld(int node, const Car *except) const
{
    const Vec2 centre = nodePoint(node);
    return !eachCarNear(*this, centre, 15,
                        [&](const Car &c)
                        {
                            return &c == except || c.destroyed || c.routePhase != 1 || c.routeFrom != node ||
                                   c.line >= 0 || (c.body.p - centre).length() >= 15;
                        });
}

bool World::intersectionClear(const Car &car) const
{
    if (car.routePhase == 1 || car.responding)
        return true;
    const int node = car.routeTo;
    const Vec2 centre = nodePoint(node);
    auto f = (centre - nodePoint(car.routeFrom)).unit();
    int d = f.z > .5 ? 0 : f.x > .5 ? 1 : f.z < -.5 ? 2 : 3;
    if (neighbours(node).size() >= 3 && !CityLayout::green(node % nodes, node / nodes, d, time))
        return false;
    auto each = [&](double radius, auto visit) { return eachCarNear(*this, centre, radius, visit); };
    if (junctionHeld(node, &car))
        return false;
    // A tram or a trolleybus on the junction, or about to be: they do not take reservations,
    // and a car that turned across one stood nose to nose with it until the end of the game.
    if (!each(26,
              [&](const Car &c)
              {
                  if (c.line < 0 || c.destroyed || !sameSurface(*this, c.body, car.body))
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
                       if (people[i].riding >= 0 || !sameSurface(*this, car.body, walker))
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
    if (car.plannedTo < 0)
        return true;
    // Do not block the box: beyond the crossing of the street the car leaves by there
    // must be room for all of it, or the queue ahead keeps it in the middle of the
    // junction and the cross traffic with it. A city of such junctions stops for good.
    const Vec2 out = (nodePoint(car.plannedTo) - centre).unit(), side = perpendicular(out);
    const double lane = CityLayout::laneOffset(levelEdge(node, car.plannedTo) ? 0 : car.laneIndex);
    const double need = 9.5 + 2 * car.body.halfH + 1.1;
    return each(need + 8,
                [&](const Car &c)
                {
                    if (&c == &car || !sameSurface(*this, c.body, car.body))
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
    if (bus.busUntil > time)
        return 0;
    if (time < bus.nextBusStop || bus.routePhase != 0 || bus.routeTo != bus.routeFrom + nodes ||
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
        bus.busUntil = time + 4 + bus.id % 4;
        bus.nextBusStop = bus.busUntil + 34;
        return 0;
    }
    return std::max(.5, std::sqrt(5.5 * std::max(0., ahead)));
}

bool World::tryChangeLane(Car &c, int destination)
{
    if (!layout.wide() || !c.route || c.routePhase != 0 || c.body.roadLevel != 0 || c.line >= 0 ||
        c.responding || c.personal || c.summoned || destination < 0 || destination > 1 ||
        c.laneIndex == destination || time < c.nextLaneDecision || c.destroyed || c.falling)
        return false;
    if (trafficCells.empty())
        indexTraffic();
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
        return sameSurface(*this, c.body, b) && std::abs(dot(b.p - end, side)) < c.body.halfW + .7 &&
               std::abs(dot(b.p - start, dir)) < 12;
    };
    nearby(*this, start, 45,
           [&](const TrafficCell &cell)
           {
               for (int i = cell.car; clear && i >= 0; i = nextTrafficCar[i])
               {
                   const auto &other = cars[i];
                   if (&other == &c || other.falling || !sameSurface(*this, c.body, other.body))
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
    c.nextLaneDecision = time + 8;
    c.blockedSince = -1;
    return true;
}

void World::senseTraffic(Car &c, bool local)
{
    if (trafficCells.empty())
        indexTraffic();
    const double interval = local ? .15 : .38;
    c.senseAt = time + interval + (c.id % 7) * .012;
    c.trafficLimit = 100;
    const auto &b = c.body;
    // Traffic on a route looks along the route. Out of a turn the bonnet still points across
    // the lanes: looking along it, a car took the tram that waited in the oncoming lane for
    // an obstacle and stopped, the tram waited for the junction behind the car to clear, and
    // the truck in the junction waited for the car.
    const auto nose = forward(c.heading());
    const bool routed = c.route && c.routePhase != 1 && !c.responding;
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
        if (c.routePhase == 1 || c.responding || !c.route)
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
        c.trafficLimit = std::min({c.trafficLimit, std::sqrt(leader * leader + 5.5 * space),
                                   space / .95 + std::min(leader, space)});
    };
    if (c.kind == 5)
        c.trafficLimit = std::min(c.trafficLimit, busStopLimit(c));
    const Car *blocker = nullptr;
    double nearest = 24;
    // In a turn the bonnet does not point where the car is going. Whoever is on the
    // crossing of the street the car turns into is watched along the curve, as the
    // managed Enter checks the crossing paths before a car may go.
    std::array<Vec2, 8> curve{}, curveAhead{};
    const bool turning = c.routePhase == 1 && c.route && !c.responding;
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
            if (!sameSurface(*this, b, walker))
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
        if (along <= 0 || along > 20 || !sameSurface(*this, b, walker))
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
                   if (&other == &c || (c.responding && i == controlledCar))
                       continue;
                   auto delta = other.body.p - b.p;
                   if (turning)
                   {
                       // In a turn the bonnet sweeps across lanes the car never enters: a truck
                       // waiting at the opposite stop line looked like an obstacle and the two
                       // stood there for good. What counts is the spine of the other vehicle
                       // against the bumper and against the curve ahead.
                       if (delta.length2() > 28 * 28 || !sameSurface(*this, b, other.body))
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
                       auto away = [&](Vec2 at)
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
                           if (1.5 * double(k + 1) > b.halfH && away(curve[k]) < room)
                               conflict = 1.5 * double(k + 1) - b.halfH - .4;
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
                       const auto [lane, heading] = other.route->sample(other.routeDistance);
                       if ((other.body.p - lane).length2() < 1 &&
                           dot(forward(other.heading()), forward(heading)) > .85)
                           continue;
                   }
                   if (askew && delta.length2() < 14 * 14 && sameSurface(*this, b, other.body))
                   {
                       // The spine of the other vehicle against the first metres the bumper
                       // travels: its length is not its width, however the two are turned.
                       const Vec2 spine =
                           forward(other.heading()) * std::max(0., other.body.halfH - other.body.halfW);
                       const double room = b.halfW + other.body.halfW + .15;
                       for (size_t k = 0; k < bumper.size(); ++k)
                       {
                           const Vec2 from = bumper[k] - other.body.p;
                           const double t = spine.length2() > 1e-9
                                                ? clamp(dot(from, spine) / spine.length2(), -1., 1.)
                                                : 0.;
                           if ((from - spine * t).length2() < room * room)
                           {
                               limit(.5 * double(k), std::max(0., dot(other.body.v, nose)));
                               break;
                           }
                       }
                   }
                   double along = dot(delta, dir);
                   if (along <= 0 || along > 38 || std::abs(dot(delta, right)) > 9 ||
                       !sameSurface(*this, b, other.body))
                       continue;
                   double gap = along - b.halfH - projectedExtent(other.body, dir);
                   double lateral = dot(delta, right);
                   // Whoever is alongside is not ahead: two trucks in neighbouring lanes are 21 cm
                   // apart and used to hold each other at the lights for ever.
                   if (gap < -1 && std::abs(lateral) > b.halfW + .3)
                       continue;
                   double width = b.halfW + projectedExtent(other.body, right) + .20;
                   double speed = dot(other.body.v, dir);
                   if (std::abs(lateral) < width && along < nearest &&
                       (other.destroyed || other.body.v.length2() < .5 ||
                        (dot(dir, forward(other.heading())) > .8 &&
                         (speed < 4.4 || (other.line >= 0 && other.dwell > 0)))))
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
    if (controlledCar < 0 && !c.responding)
        yieldToWalker(player);

    // A bus keeps its lane whatever stands in it.
    if (!blocker || c.routePhase != 0 || c.responding || c.kind == 5)
    {
        c.blockedSince = -1;
        return;
    }
    // A queue at the lights is not an obstacle: keeping eight metres behind its last car
    // halved what a block can hold and left the next car across the junction.
    // Whoever follows a bus to its stop goes round it at once.
    const bool atStop = blocker->kind == 5 && blocker->busUntil > time;
    const bool queue = !blocker->destroyed && !blocker->driverless && blocker->line < 0 &&
                       blocker->routePhase == 0 && blocker->routeFrom == c.routeFrom &&
                       blocker->routeTo == c.routeTo && int(blocker->id) != controlledCar &&
                       !blocker->personal && !blocker->summoned && !atStop;
    if (queue)
    {
        c.blockedSince = -1;
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
    if (c.blockedSince < 0)
        c.blockedSince = time;
    if (time < c.nextLaneDecision ||
        (time - c.blockedSince < 2 && blocker->line < 0 && !blocker->destroyed && !atStop))
        return;
    if (random.next() >= .65 || !tryChangeLane(c, 1 - c.laneIndex))
        c.nextLaneDecision = time + 1.5;
}
} // namespace pz
