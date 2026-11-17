#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include <deque>
#include <limits>

namespace Promzona
{
namespace
{
int gridIndex(double value)
{
    return clamp(int(std::lround(value / World::spacing)) + World::nodes / 2, 0, World::nodes - 1);
}
unsigned responderModel(int kind)
{
    switch (kind)
    {
    case 7:
        return 4;
    case 13:
        return 5;
    case 14:
        return 6;
    case 12:
        return 7;
    default:
        return 3;
    }
}
} // namespace
int World::nearestNode(Vec2 pos)
{
    return gridIndex(pos.x) + gridIndex(pos.z) * World::nodes;
}

void World::indexRoofs()
{
    roofCells.clear();
    roofCells.resize(nodes * nodes);
    for (unsigned i = 0; i < buildings.size(); ++i)
    {
        const auto &b = buildings[i];
        const auto lower = b.p - b.half - Vec2{2, 2}, upper = b.p + b.half + Vec2{2, 2};
        for (int j = gridIndex(lower.z); j <= gridIndex(upper.z); ++j)
            for (int x = gridIndex(lower.x); x <= gridIndex(upper.x); ++x)
                roofCells[x + j * nodes].push_back(i);
    }
}
bool World::insideBuilding(Vec2 pos, double y, double radius) const
{
    if (roofCells.empty())
        return false;
    for (auto i : roofCells[nearestNode(pos)])
    {
        const auto &b = buildings[i];
        if (std::abs(pos.x - b.p.x) >= b.half.x + radius || std::abs(pos.z - b.p.z) >= b.half.z + radius)
            continue;
        double base = terrain.height(b.p);
        if (y > base - radius && y < base + b.height + radius)
            return true;
    }
    return false;
}
double World::flightFloor(Vec2 pos) const
{
    double floor = terrain.height(pos);
    if (auto level = levelAt(pos); level && !level->tunnel)
        floor = std::max(floor, level->floor(terrain, pos));
    return highestRoof(pos, floor, 1.4);
}

double World::highestRoof(Vec2 pos, double floor, double margin) const
{
    if (!roofCells.empty())
        for (auto i : roofCells[nearestNode(pos)])
        {
            const auto &b = buildings[i];
            if (std::abs(pos.x - b.p.x) < b.half.x + margin && std::abs(pos.z - b.p.z) < b.half.z + margin)
                floor = std::max(floor, terrain.height(b.p) + b.height);
        }
    return floor;
}

void World::updateDispatch()
{
    dispatchAt = time + .55;
    std::array<int, 15> required{};
    for (int k : police_roster(wantedLevel()))
        ++required[k];
    for (size_t i = 0; i < cars.size(); ++i)
    {
        auto &c = cars[i];
        if (!c.responding)
            continue;
        if (int(i) == controlledCar || c.destroyed || c.falling || !required[c.kind])
            standDown(c);
        else
            --required[c.kind];
    }
    if (!wantedLevel())
    {
        pursuitNode = -1;
        return;
    }

    int target = nearestNode(position());
    // All ground units follow the same reverse breadth-first tree. Rebuild only
    // when the suspect crosses into another intersection's catchment area.
    if (pursuitNode != target)
    {
        pursuitNode = target;
        pursuitDistances.fill(-1);
        std::deque<int> queue{target};
        pursuitDistances[target] = 0;
        while (!queue.empty())
        {
            int n = queue.front();
            queue.pop_front();
            for (int next : neighbours(n))
                if (pursuitDistances[next] < 0)
                {
                    pursuitDistances[next] = pursuitDistances[n] + 1;
                    queue.push_back(next);
                }
        }
    }
    for (int k : police_roster(wantedLevel()))
        if (required[k] && deployResponder(k))
            break;
}

// Where a crew may drive in a straight line (the managed ClearWalk): not through a house,
// a fence or a kiosk, not into a cut or deep water, not off the land.
bool World::chaseGround(Vec2 at) const
{
    const double ground = terrain.height(at);
    return std::max(std::abs(at.x), std::abs(at.z)) < landEdge - 3 && !openCut(at) &&
           CityLayout::waterLevel - terrain.bed_height(at.x, at.z) <= .65 &&
           !insideBuilding(at, ground + .8, .4) && !scenerySolid(at, ground + .8, .3);
}

// After CityTraffic.RejoinRoad: a unit whose chase is over goes back to the street graph by
// itself. The managed car makes for the nearest stop line in a straight line, whichever way
// that lane goes; five units left in the junction where the suspect had stood all took the
// stop line of the same street, from the wrong side, and stood in the box for good. Here
// the unit takes the lane that goes its way and lies nearest to it, within two junctions,
// and curves into it from where the bonnet points, to that lane's stop line.
bool World::rejoinRoad(Car &c)
{
    if (c.body.roadLevel || !c.grounded())
        return false;
    struct Way
    {
        double score, ahead;
        int from, to;
        Vec2 stop, along;
    };
    const Vec2 at = c.body.p, nose = forward(c.heading());
    const int ci = gridIndex(at.x), cj = gridIndex(at.z);
    std::vector<Way> ways;
    for (int j = std::max(0, cj - 2); j <= std::min(nodes - 1, cj + 2); ++j)
        for (int i = std::max(0, ci - 2); i <= std::min(nodes - 1, ci + 2); ++i)
        {
            const int node = i + j * nodes;
            for (int back : layout.neighbours(node))
            {
                const Vec2 along = (nodePoint(node) - nodePoint(back)).unit();
                const Vec2 stop =
                    nodePoint(node) - along * stopLine + perpendicular(along) * CityLayout::laneOffset(0);
                // The stop line has to be ahead in the lane, with room to come into it, and
                // the junction before it not further back than its own far side.
                const double ahead = dot(stop - at, along);
                if (ahead < 4 || ahead > spacing + 9)
                    continue;
                ways.push_back({std::abs(cross(stop - at, along)) + 10 * (1 - dot(nose, along)) + .05 * ahead,
                                ahead, back, node, stop, along});
            }
        }
    std::ranges::sort(ways, {}, &Way::score);
    for (const auto &way : ways)
    {
        const double handle = clamp(way.ahead * .4, 2., 8.);
        Path path(at, at + nose * handle, way.stop - way.along * handle, way.stop);
        // Nothing solid on the way: the curve itself and 1.1 m to either side of it.
        bool open = true;
        for (double s = 0; s <= path.length() && open; s += 1)
        {
            const auto [point, heading] = path.sample(s);
            const Vec2 side = perpendicular(forward(heading)) * 1.1;
            open = chaseGround(point) && chaseGround(point + side) && chaseGround(point - side);
        }
        if (!open)
            continue;
        // The stop line is in the first lane, as the crossing that follows expects.
        c.laneIndex = 0;
        c.routeFrom = way.from;
        c.routeTo = way.to;
        c.previousNode = c.planned_to = -1;
        c.route = std::move(path);
        c.routePhase = c.routeLevel = 0;
        c.routeDistance = c.sense_at = 0;
        c.rejoining = true;
        return true;
    }
    return false;
}

// The end of a police mission (CityTraffic.ServicesStep and PoliceDispatchStep): the unit
// is traffic again. One that left the street after the suspect, or stands across it, goes
// back to the street by RejoinRoad instead of steering for the segment it left.
void World::standDown(Car &c)
{
    const bool pursued = c.responding;
    c.responding = c.seesSuspect = false;
    c.avoid = c.wedged_for = c.reverse_for = 0;
    if (!pursued || c.destroyed || c.falling || c.driverless || c.kind == 12 || c.line >= 0 || !c.route ||
        int(&c - cars.data()) == controlledCar)
        return;
    const auto [onRoute, heading] = c.route->sample(c.route->closestDistance(c.body.p));
    if ((onRoute - c.body.p).length() > 1.5 || dot(forward(c.heading()), forward(heading)) < .9)
        rejoinRoad(c);
}

int World::pursuitNext(int node, int previous)
{
    if (pursuitNode < 0 || node == pursuitNode)
        return choose_next(node, previous);
    auto options = neighbours(node);
    int best = -1;
    for (int n : options)
        if (pursuitDistances[n] >= 0 && (best < 0 || pursuitDistances[n] < pursuitDistances[best]))
            best = n;
    return best < 0 ? choose_next(node, previous) : best;
}

bool World::deployResponder(int kind)
{
    const auto focus = position();
    for (size_t i = trafficPopulation; i < cars.size(); ++i)
    {
        auto &unit = cars[i];
        if (!unit.driverless && !unit.summoned && !unit.responding && int(i) != controlledCar &&
            unit.kind == kind && !unit.destroyed && !unit.falling && unit.fireTime == 0 &&
            (unit.body.p - focus).length2() < 340 * 340)
        {
            unit.responding = true;
            return true;
        }
    }
    // Reuse an off-duty or wrecked unit only after the player has left it behind.
    // Stable slots are also used in saves and ray-hit results.
    size_t slot = trafficPopulation;
    for (; slot < cars.size(); ++slot)
        if (int(slot) != patrol.car && !cars[slot].summoned && !cars[slot].personal &&
            int(slot) != controlledCar && !cars[slot].responding &&
            (cars[slot].body.p - focus).length2() > 340 * 340)
            break;
    if (slot == cars.size() && cars.size() - std::ranges::count(cars, true, &Car::summoned) >=
                                   trafficPopulation + maxResponseVehicles)
        return false;
    for (int attempt = 0; attempt < 48; ++attempt)
    {
        double angle = random.next() * 2 * pi;
        int from = nearestNode(focus + forward(angle) * (150 + random.next() * 95));
        if (pursuitDistances[from] <= 1)
            continue;
        int to = pursuitNext(from, -1);
        if (levelEdge(from, to))
            continue;
        Car car;
        car.id = static_cast<unsigned>(slot);
        car.body.id = 0x60000000u + car.id;
        fit_vehicle(car, responderModel(kind));
        car.kind = kind;
        car.responding = true;
        car.routeFrom = from;
        car.routeTo = to;
        car.routePhase = 1;
        new_route(car);
        auto [pos, heading] = car.route->sample(0);
        if ((focus - pos).length2() < 120 * 120 || !layout.urbanSite(pos, .5))
            continue;
        if (std::ranges::any_of(cars, [&](const Car &c) { return (c.body.p - pos).length2() < 64; }))
            continue;
        if (std::ranges::any_of(people, [&](const Person &p) { return (p.body.p - pos).length2() < 16; }))
            continue;
        car.body.p = pos;
        car.body.angle = -heading;
        car.body.height = terrain.height(pos) + .8;
        car.body.v = forward(heading) * 8;
        if (kind == 12)
        {
            car.body.height = flightFloor(pos) + 25;
            car.lift = car.body.height - .8 - terrain.height(pos);
        }
        if (slot == cars.size())
            cars.push_back(std::move(car));
        else
            cars[slot] = std::move(car);
        return true;
    }
    return false;
}

bool World::spawnVehicle(unsigned model)
{
    if (model >= std::size(fleet) || fleet[model].kind == 9 || fleet[model].kind == 10)
        return false;
    const Vec2 focus = position();
    size_t slot = cars.size();
    for (size_t i = trafficPopulation; i < cars.size(); ++i)
        if (cars[i].summoned && cars[i].destroyed && (cars[i].body.p - focus).length2() > 340 * 340 &&
            int(i) != controlledCar)
        {
            slot = i;
            break;
        }
    if (slot == cars.size() && size_t(std::ranges::count(cars, true, &Car::summoned)) >= maxSpawnVehicles)
        return false;
    Car candidate;
    fit_vehicle(candidate, model);
    double best = 60 * 60;
    int start = nearestNode(focus);
    for (int z = std::max(0, start / nodes - 2); z <= std::min(nodes - 1, start / nodes + 2); ++z)
        for (int x = std::max(0, start % nodes - 2); x <= std::min(nodes - 1, start % nodes + 2); ++x)
        {
            int from = z * nodes + x;
            for (int to : neighbours(from))
            {
                if (levelEdge(from, to))
                    continue;
                auto dir = (nodePoint(to) - nodePoint(from)).unit();
                auto pos = (nodePoint(from) + nodePoint(to)) * .5 + perpendicular(dir) * CityLayout::lane;
                double distance = (pos - focus).length2();
                if (distance < 8 * 8 || distance >= best || levelAt(pos, 6) || !layout.urbanSite(pos, .5))
                    continue;
                double clearance = std::hypot(candidate.body.halfW, candidate.body.halfH) + .4;
                auto occupied = [&](const Car &c)
                {
                    if (c.lift > 5)
                        return false;
                    double r = clearance + std::hypot(c.body.halfW, c.body.halfH);
                    return (c.body.p - pos).length2() < r * r;
                };
                if (std::ranges::any_of(cars, occupied) ||
                    std::ranges::any_of(people, [&](const Person &p)
                                        { return (p.body.p - pos).length() < clearance + .45; }))
                    continue;
                double base = terrain.height(pos);
                auto hitsStatic = [&](const Body &b)
                {
                    if (std::abs(b.height - base - .8) >
                        (b.verticalExtent + candidate.body.verticalExtent) * .5)
                        return false;
                    auto offset = rotate(pos - b.p, -b.angle);
                    double dx = std::max(0., std::abs(offset.x) - b.halfW);
                    double dz = std::max(0., std::abs(offset.z) - b.halfH);
                    return dx * dx + dz * dz < clearance * clearance;
                };
                if (insideBuilding(pos, base + .8, clearance) ||
                    std::ranges::any_of(staticBodies, hitsStatic))
                    continue;
                best = distance;
                candidate.routeFrom = from;
                candidate.routeTo = to;
                candidate.body.p = pos;
                candidate.body.angle = -std::atan2(dir.x, dir.z);
                candidate.body.height = base + .8;
            }
        }
    if (best == 60 * 60)
        return false;
    candidate.routePhase = 1;
    new_route(candidate);
    candidate.routeDistance = candidate.route->closestDistance(candidate.body.p);
    candidate.summoned = true;
    candidate.id = static_cast<unsigned>(slot);
    candidate.body.id = 0x60000000u + candidate.id;
    if (slot == cars.size())
        cars.push_back(std::move(candidate));
    else
        cars[slot] = std::move(candidate);
    return true;
}

void World::responseFire(Car &c)
{
    if (!c.responding || (c.kind != 12 && c.kind != 13 && c.kind != 14) || c.nextShot > time)
        return;
    auto delta = position() - c.body.p;
    double range = delta.length();
    if (range < 1 || range > (c.kind == 12 ? 83. : 57.))
        return;
    double y = c.body.height + (c.kind == 14 ? 1.45 : .12);
    double targetY = controlledCar >= 0 ? cars[controlledCar].body.height : player.height;
    auto dir = delta / range;
    if (c.kind == 14)
    {
        c.turretYaw = wrap(std::atan2(dir.x, dir.z) - c.heading());
        c.turretPitch = clamp(std::atan2(targetY - y, range), -.4, 1.);
    }
    int index = int(&c - cars.data());
    auto hit = traceAttack(c.body.p, dir, y, (targetY - y) / range, range + 1, index, true);
    c.nextShot = time + .42;
    if (!((hit.kind == HitKind::car && hit.index == controlledCar) || hit.kind == HitKind::player))
        return;
    c.nextShot = time + (c.kind == 12 ? 2.3 : c.kind == 14 ? .65 : .9);
    sounds.push_back({c.kind == 12 ? "shot" : "automatic", c.body.p, .75});
    if (hit.kind == HitKind::player)
        wound_player(.055);
    else
        damageCar(cars[hit.index], .095);
    bulletSparks(c.body.p, dir, y, (targetY - y) / range, hit);
    for (double t = 2; t < hit.distance && particles.size() < 5000; t += 2.3)
        particles.push_back({{float(c.body.p.x + dir.x * t), float(y + (targetY - y) * t / range),
                              float(c.body.p.z + dir.z * t)},
                             {},
                             {1, .78f, .38f},
                             .09f,
                             .065f});
}

void World::flyPatrol(Car &c, const PlayerInput &input, double dt)
{
    auto &b = c.body;
    bool piloted = controlledCar >= 0 && &c == &cars[controlledCar];
    double vertical = 0;
    Vec2 desired{};
    if (piloted)
    {
        if (helicopterLanding && (input.collective > .1 || c.lift < .05))
            helicopterLanding = false;
        if (helicopterLanding)
            vertical = -2.2; // Assisted descent of the managed game; horizontal motion is braked.
        else
        {
            b.angle = wrap(b.angle - input.car.steering * dt * 1.25);
            desired = forward(c.heading()) * (input.car.throttle * 23);
            vertical = clamp(input.collective, -1., 1.) * 7;
        }
    }
    else if (c.responding)
    {
        auto orbit = position() + forward(time * .12) * (27. + c.id % 5);
        auto delta = orbit - b.p;
        desired = delta.unit() * std::min(24., delta.length() * .75);
        auto aim = position() - b.p;
        b.angle = wrap(b.angle + clamp(wrap(-std::atan2(aim.x, aim.z) - b.angle), -dt * 1.5, dt * 1.5));
        double floor = std::max(flightFloor(b.p), flightFloor(b.p + b.v * 1.5));
        vertical = clamp((floor + 24 - b.height) * 1.3, -6., 10.);
        responseFire(c);
    }
    else
        vertical = -3;
    if (piloted || c.responding || c.lift > .3)
        c.wheelTravel = std::fmod(c.wheelTravel + dt * 29, 2 * pi);
    b.v += (desired - b.v) * (1 - std::exp(-dt * 1.35));
    auto next = b.p + b.v * dt;
    double floor = flightFloor(b.p);
    if (flightFloor(next) > std::max(floor + .3, b.height - .75))
    {
        b.v = {};
        next = b.p;
        if (c.responding)
            vertical = 10;
    }
    c.verticalVelocity += (vertical - c.verticalVelocity) * (1 - std::exp(-dt * 2.8));
    b.p = next;
    double landing = flightFloor(next) + .8;
    b.height = clamp(b.height + c.verticalVelocity * dt, landing, std::max(landing, 160.));
    if (b.height <= landing + .001 && c.verticalVelocity < 0)
        c.verticalVelocity = 0;
    c.lift = std::max(0., b.height - terrain.height(b.p) - .8);
    c.terrainPitch = c.terrainRoll = 0;
}
void World::toggleHelicopterLanding()
{
    if (controlledCar < 0 || transitPassenger())
        return;
    const auto &c = cars[controlledCar];
    if (c.kind == 12 && !c.destroyed && !c.recovering && c.lift >= .05)
        helicopterLanding = !helicopterLanding;
}
} // namespace Promzona
