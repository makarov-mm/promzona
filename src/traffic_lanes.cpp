#include "promzona/world.hpp"

namespace pz
{
namespace
{
int cellCoordinate(double x)
{
    return clamp(int(std::floor(x / World::spacing + World::nodes * .5)), 0, World::nodes - 1);
}

template<class Visit> void nearby(const World &w, Vec2 pos, double radius, Visit visit)
{
    // Include travel since the last index refresh; narrow phase uses live positions.
    const double range = radius + 12;
    for (int z = cellCoordinate(pos.z - range); z <= cellCoordinate(pos.z + range); ++z)
        for (int x = cellCoordinate(pos.x - range); x <= cellCoordinate(pos.x + range); ++x)
            visit(w.trafficCells[z * World::nodes + x]);
}

bool sameSurface(const World &world, const Body &a, const Body &b)
{
    if (std::abs(a.height - b.height) < 2.5) return true;
    if (a.roadLevel != b.roadLevel) return false;
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
}

void World::walkersNear(Vec2 pos, double radius, std::vector<int> &out) const
{
    out.clear();
    nearby(*this, pos, radius, [&](const TrafficCell &cell) {
        for (int i = cell.person; i >= 0; i = nextTrafficPerson[i])
            if (people[i].riding < 0 && (people[i].body.p - pos).length2() <= radius * radius)
                out.push_back(i);
    });
}

void World::indexTraffic()
{
    trafficCells.assign(nodes * nodes, {});
    auto cell = [&](Vec2 p) -> TrafficCell & {
        return trafficCells[cellCoordinate(p.z) * nodes + cellCoordinate(p.x)];
    };
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
        if (people[i].riding >= 0) continue;
        int &head = cell(people[i].body.p).person;
        nextTrafficPerson[i] = head;
        head = i;
    }
    trafficIndexAt = time + .18;
}

bool World::tryChangeLane(Car &c, int destination)
{
    if (!layout.wide() || !c.route || c.routePhase != 0 || c.body.roadLevel != 0 ||
        c.line >= 0 || c.responding || c.personal || c.summoned || destination < 0 || destination > 1 ||
        c.laneIndex == destination || time < c.nextLaneDecision || c.destroyed || c.falling)
        return false;
    if (trafficCells.empty()) indexTraffic();
    auto from = nodePoint(c.routeFrom), to = nodePoint(c.routeTo);
    auto dir = (to - from).unit(), side = perpendicular(dir);
    auto end = to - dir * 11 + side * CityLayout::laneOffset(destination);
    auto start = c.body.p;
    double ahead = dot(end - start, dir);
    if (ahead < 8 || ahead > spacing - 9 || dot(forward(c.heading()), dir) < .82 ||
        std::abs(dot(start - from, side) - CityLayout::laneOffset(c.laneIndex)) > 1.1)
        return false;

    bool clear = true;
    const double speed = dot(c.body.v, dir);
    auto personBlocks = [&](const Body &b) {
        return sameSurface(*this, c.body, b) && std::abs(dot(b.p - end, side)) < c.body.halfW + .7 &&
               std::abs(dot(b.p - start, dir)) < 12;
    };
    nearby(*this, start, 45, [&](const TrafficCell &cell) {
        for (int i = cell.car; clear && i >= 0; i = nextTrafficCar[i])
        {
            const auto &other = cars[i];
            if (&other == &c || other.falling || !sameSurface(*this, c.body, other.body)) continue;
            const auto &b = other.body;
            bool reserved = i != controlledCar && !other.personal && !other.destroyed && other.line < 0 &&
                other.routePhase == 0 && other.routeFrom == c.routeFrom && other.routeTo == c.routeTo &&
                other.laneIndex == destination;
            if (!reserved && std::abs(dot(b.p - end, side)) >
                c.body.halfW + projectedExtent(b, side) + .45) continue;
            const double extent = c.body.halfH + projectedExtent(b, dir);
            const double longitudinal = dot(b.p - start, dir), relative = dot(b.v, dir) - speed;
            if (longitudinal > -extent - 4 - 2 * std::max(0., relative) &&
                longitudinal < extent + 6 + 2 * std::max(0., -relative))
                clear = false;
        }
        for (int i = cell.person; clear && i >= 0; i = nextTrafficPerson[i])
            if (people[i].riding < 0 && personBlocks(people[i].body)) clear = false;
    });
    if (!clear || (controlledCar < 0 && personBlocks(player))) return false;

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
    if (trafficCells.empty()) indexTraffic();
    const double interval = local ? .15 : .38;
    c.senseAt = time + interval + (c.id % 7) * .012;
    c.trafficLimit = 100;
    const auto &b = c.body;
    const auto dir = forward(c.heading()), right = perpendicular(dir);
    // During a merge the bonnet still points down the original lane. Check the
    // actual path at each obstacle, including its nearer end, so a stopped car
    // keeps clearance while the following car moves sideways around it.
    auto pathSide = [&](double distance) {
        if (c.routePhase != 0 || c.responding || !c.route) return 0.;
        auto [p, heading] = c.route->sample(c.routeDistance + distance);
        double over = c.routeDistance + distance - c.route->length();
        if (over > 0) p += forward(heading) * over;
        return dot(p - b.p, right);
    };
    auto limit = [&](double gap, double leaderSpeed) {
        const double space = std::max(0., gap - 1.1);
        const double leader = std::max(0., leaderSpeed);
        // Both stopping distance and time headway matter, in particular at a
        // stationary queue. A driver with no clearance must remain stationary.
        c.trafficLimit = std::min({c.trafficLimit, std::sqrt(leader * leader + 5.5 * space),
                                   space / .95 + std::min(leader, space)});
    };
    const Car *blocker = nullptr;
    double nearest = 24;
    auto yieldToWalker = [&](const Body &walker) {
        auto delta = walker.p - b.p;
        double along = dot(delta, dir);
        if (along <= 0 || along > 20 || !sameSurface(*this, b, walker)) return;
        double lateral = dot(delta, right) - pathSide(along);
        if (std::abs(lateral) < b.halfW + walker.radius + .35)
            limit(along - b.halfH - walker.radius, 0);
    };
    nearby(*this, b.p, 42, [&](const TrafficCell &cell) {
        for (int i = cell.car; i >= 0; i = nextTrafficCar[i])
        {
            const auto &other = cars[i];
            if (&other == &c || (c.responding && i == controlledCar)) continue;
            auto delta = other.body.p - b.p;
            double along = dot(delta, dir);
            if (along <= 0 || along > 38 || std::abs(dot(delta, right)) > 9 || !sameSurface(*this, b, other.body)) continue;
            double gap = along - b.halfH - projectedExtent(other.body, dir);
            double lateral = dot(delta, right);
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
            if (people[i].riding < 0) yieldToWalker(people[i].body);
    });
    if (controlledCar < 0 && !c.responding) yieldToWalker(player);

    if (!blocker || c.routePhase != 0 || c.responding)
    {
        c.blockedSince = -1;
        return;
    }
    if (layout.wide() && c.route && blocker->body.v.length2() < 1 && c.route->length()-c.routeDistance >= 8)
    {
        // Leave room to steer while waiting for a gap. Closing bumper-to-bumper
        // during the decision delay strands the queue even if the next lane clears.
        auto road = (nodePoint(c.routeTo) - nodePoint(c.routeFrom)).unit();
        double offset = dot(b.p - nodePoint(c.routeFrom), perpendicular(road));
        if (std::abs(offset - CityLayout::laneOffset(c.laneIndex)) < .7)
            limit(nearest - b.halfH - projectedExtent(blocker->body,dir) - 8, 0);
    }
    if (c.blockedSince < 0) c.blockedSince = time;
    if (time < c.nextLaneDecision || (time - c.blockedSince < 2 && blocker->line < 0 && !blocker->destroyed))
        return;
    if (random.next() >= .65 || !tryChangeLane(c, 1 - c.laneIndex))
        c.nextLaneDecision = time + 1.5;
}
} // namespace pz
