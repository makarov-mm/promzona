#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include <queue>
#include <stdexcept>

namespace pz
{
namespace
{
constexpr int N = World::nodes;
using Graph = std::array<std::vector<int>, N * N>;
constexpr int node(int x, int z)
{
    return z * N + x;
}

Graph serviceStreets(const World &w)
{
    Graph edges;
    for (int a = 0; a < N * N; ++a)
        for (int b : w.layout.neighbours(a))
        {
            if (b <= a)
                continue;
            Vec2 p = World::nodePoint(a), f = (World::nodePoint(b) - p).unit();
            bool clear = true;
            for (int d = 0; d <= 36 && clear; d += 3)
            {
                auto at = p + f * d, outer = perpendicular(f) * 5.2;
                clear = !w.levelAt(at, 14) && w.terrain.height(at + outer) > CityLayout::waterLevel + .05 &&
                        w.terrain.height(at - outer) > CityLayout::waterLevel + .05;
            }
            if (clear)
            {
                edges[a].push_back(b);
                edges[b].push_back(a);
            }
        }
    return edges;
}

std::vector<int> circuit(const Graph &graph, const std::array<int, 4> &targets)
{
    std::array<bool, N * N> reserved{};
    for (int t : targets)
        reserved[t] = true;
    std::vector<int> cycle{targets.front()};
    for (int leg = 0; leg < 4; ++leg)
    {
        const int start = targets[leg], goal = targets[(leg + 1) % 4];
        std::array<int, N * N> parent;
        std::array<double, N * N> costs;
        costs.fill(1e30);
        parent.fill(-1);
        costs[start] = 0;
        using Entry = std::pair<double, int>;
        std::priority_queue<Entry, std::vector<Entry>, std::greater<>> queue;
        queue.emplace(0, start);
        while (!queue.empty())
        {
            int at = queue.top().second;
            queue.pop();
            if (at == goal)
                break;
            for (int next : graph[at])
            {
                int x = next % N, z = next / N;
                if (x < 1 || x >= N - 1 || z < 1 || z >= N - 1 || (reserved[next] && next != goal))
                    continue;
                int off = start % N == goal % N ? std::abs(x - start % N) : std::abs(z - start / N);
                double cost = costs[at] + 1 + off * .07;
                if (cost >= costs[next])
                    continue;
                costs[next] = cost;
                parent[next] = at;
                queue.emplace(cost + std::abs(x - goal % N) + std::abs(z - goal / N), next);
            }
        }
        if (parent[goal] < 0)
            return {};
        std::vector<int> legNodes;
        for (int n = goal; n != start; n = parent[n])
        {
            legNodes.push_back(n);
            reserved[n] = true;
        }
        cycle.insert(cycle.end(), legNodes.rbegin(), legNodes.rend());
    }
    cycle.pop_back();
    return cycle;
}

double platformRoughness(const World &world, Vec2 road, Vec2 direction, int kind)
{
    const auto right = perpendicular(direction);
    const double width = kind == 9 ? 7.2 : 3.9;
    const auto curb = road + right * width;
    if (!world.layout.urbanSite(curb,.6) || world.insideBuilding(curb,world.terrain.height(curb)+1,1))
        return 100.;
    double worst = 0;
    // A flat foundation can end in a steep cut between the vehicle and the pavement.
    // Check the walking surface across the whole door and waiting area.
    for (double along : {-2., 0., 2.})
    {
        const auto side = road + direction * along;
        for (double across = 1.; across <= width + .6; across += .5)
            worst = std::max(worst, world.terrain.gradient(side + right * across).length());
    }
    return worst;
}

TransitLine makeLine(const World &world, std::span<const int> nodes, int kind)
{
    std::vector<Vec2> corners, points;
    for (size_t i = 0; i < nodes.size(); ++i)
        if (nodes[i] - nodes[(i + nodes.size() - 1) % nodes.size()] !=
            nodes[(i + 1) % nodes.size()] - nodes[i])
            corners.push_back(World::nodePoint(nodes[i]));
    double length = 0, lane = kind == 9 ? 1.9 : 5.2;
    std::vector<double> stops, midpoints;
    auto add = [&](Vec2 p)
    {
        if (!points.empty())
            length += (p - points.back()).length();
        points.push_back(p);
    };
    for (size_t i = 0; i < corners.size(); ++i)
    {
        auto a = corners[i], b = corners[(i + 1) % corners.size()], c = corners[(i + 2) % corners.size()];
        auto forward = (b - a).unit(), next = (c - b).unit();
        auto start = a + forward * 12 + perpendicular(forward) * lane;
        auto end = b - forward * 12 + perpendicular(forward) * lane;
        auto leave = b + next * 12 + perpendicular(next) * lane;
        if (i == 0)
            add(start);
        double straight = (end - start).length();
        // Stops at block midpoints, with space for the full vehicle before either junction.
        for (double d = 6; d < straight - 3; d += 36)
        {
            midpoints.push_back(length + d);
            if (stops.empty() || length + d - stops.back() >= 180)
                stops.push_back(length + d);
        }
        for (double d = 3; d < straight; d += 3)
            add(start + forward * d);
        add(end);
        double handle = std::abs(dot(leave - end, forward)) * .55228475;
        for (int k = 1; k <= 12; ++k)
        {
            double u = k / 12., v = 1 - u;
            add(end * (v * v * v) + (end + forward * handle) * (3 * v * v * u) +
                (leave - next * handle) * (3 * v * u * u) + leave * (u * u * u));
        }
    }
    TransitLine line{kind, Path(std::move(points)), std::move(stops)};
    for (auto &stop : line.stops)
    {
        const double scheduled = stop;
        auto [point, heading] = line.pose(scheduled);
        double best = platformRoughness(world, point, forward(heading), kind);
        if (best <= .25) continue;
        // Keep the original stop count/order and only consider straight streets nearby.
        for (auto it = std::lower_bound(midpoints.begin(),midpoints.end(),scheduled-72.);
             it != midpoints.end() && *it <= scheduled+72.; ++it)
            for (double shift : {0., -4., 4.})
            {
                const double candidate = *it + shift;
                auto [at, angle] = line.pose(candidate);
                double score = platformRoughness(world, at, forward(angle), kind)
                             + std::abs(candidate-scheduled) * .001;
                if (score < best)
                {
                    best = score;
                    stop = candidate;
                }
            }
    }
    return line;
}

} // namespace

int TransitLine::nextStopAfter(double along) const
{
    auto it = std::upper_bound(stops.begin(), stops.end(), wrapDistance(along));
    return it == stops.end() ? 0 : int(it - stops.begin());
}

void World::generateTransit()
{
    lines.clear();
    auto graph = serviceStreets(*this);
    const std::array<std::array<int, 4>, 4> districts{
        {{4, 4, 44, 26}, {6, 4, 26, 44}, {4, 22, 44, 44}, {22, 4, 42, 44}}};
    for (int k = 0; k < 4; ++k)
    {
        auto [left, top, right, bottom] = districts[k];
        for (int attempt = 0; attempt < 32; ++attempt)
        {
            int s = attempt == 0 ? 0 : (attempt % 2 == 0 ? 1 : -1) * ((attempt % 8 + 1) / 2);
            int dz = attempt < 8 ? 0 : ((attempt / 8) % 2 == 0 ? 2 : -2);
            int x0 = clamp(left + s, 2, N - 8), x1 = clamp(right - s, x0 + 5, N - 3);
            int z0 = clamp(top + s + dz, 2, N - 8), z1 = clamp(bottom - s + dz, z0 + 5, N - 3);
            std::array<int, 4> targets{node(x0, z0), node(x0, z1), node(x1, z1), node(x1, z0)};
            if (attempt >= 16)
                std::reverse(targets.begin(), targets.end());
            auto cycle = circuit(graph, targets);
            if (cycle.empty())
                continue;
            auto line = makeLine(*this, cycle, k % 2 == 0 ? 9 : 10);
            if (line.stops.empty() ||
                std::ranges::any_of(line.track.points(),
                                    [&](Vec2 p)
                                    {
                                        return terrain.height(p) <= CityLayout::waterLevel + .05 ||
                                               layout.roadDistance(p) > layout.roadHalf() || levelAt(p, 7);
                                    }))
                continue;
            lines.push_back(std::move(line));
            break;
        }
    }

    size_t slot = cars.size(), rider = 0;
    for (size_t i = 0; i < lines.size(); ++i)
    {
        const auto &line = lines[i];
        int fleetSize = clamp(int(std::ceil(line.track.length() / 420)), 4, 16);
        for (int n = 0; n < fleetSize && slot > 1; ++n)
        {
            auto &c = cars[--slot];
            c.line = int(i);
            fitVehicle(c, line.kind == 9 ? 8 : 9);
            c.along = (n + .25) * line.track.length() / fleetSize;
            for (int trySlot = 0; trySlot < 30; ++trySlot)
            {
                auto pos = line.pose(c.along).first;
                if (!std::ranges::any_of(cars, [&](const Car &other)
                                         { return &other != &c && (other.body.p - pos).length2() < 125; }))
                    break;
                c.along = line.wrapDistance(c.along + 13);
            }
            c.nextStop = line.nextStopAfter(c.along);
            auto [p, heading] = line.pose(c.along);
            c.body.p = p;
            c.body.angle = -heading;
            c.body.v = forward(heading) * 4.;
            c.body.roadLevel = 0;
            c.body.height = terrain.height(p) + .8;
            for (int seat = 0; seat < 2 && rider < people.size(); ++seat)
            {
                auto &person = people[rider++];
                person.riding = int(slot);
                person.seat = seat;
                person.alight = (c.nextStop + seat + 1) % int(line.stops.size());
                seatPassenger(person);
            }
        }
    }
}

void World::seatPassenger(Person &p)
{
    p.waitingLine = p.waitingStop = -1;
    auto &c = cars[p.riding];
    if (c.destroyed || c.falling)
    {
        p.riding = -1;
        killPerson(p, c.body.v, c.firePlayerCaused);
        return;
    }
    if (c.line < 0)
    {
        p.body.p = c.body.p + rotate({c.body.halfW * .45, c.body.halfH * .12}, c.body.angle);
        // Align the seated pose's feet with the front footwell. The vehicle body's
        // origin is 0.8 m above its base and a pedestrian's is 0.85 m above its feet.
        p.body.height = c.body.height - .8 + .16 + .85;
        p.body.v = {};
        p.heading = c.heading();
        p.body.roadLevel = c.body.roadLevel;
        return;
    }
    p.heading = placeTransitRider(p.body, c, p.seat);
}

void World::resumeWalk(Person &p, Vec2 waiting, int level)
{
    p.riding = -1;
    p.boardAfter = time + 60;
    p.waitingLine = p.waitingStop = -1;
    p.body.p = waiting;
    p.body.v = {};
    p.body.roadLevel = level;
    p.body.height = surface(waiting, level) + .85;
    // Pedestrians use the northeast corner of each junction as their path anchor.
    int x = clamp(int(std::round((waiting.x - 9.1 - layout.coordinate(0)) / spacing)), 0, nodes - 1);
    int z = clamp(int(std::round((waiting.z - 9.1 - layout.coordinate(0)) / spacing)), 0, nodes - 1);
    p.node = p.next = node(x, z);
    p.target = nodePoint(p.node) + Vec2{9.1, 9.1};
    p.body.wake();
}

namespace
{
int platformStop(const TransitLine &line, const Car &car)
{
    return (car.nextStop + int(line.stops.size()) - 1) % int(line.stops.size());
}

bool doorsOpen(const Car &car)
{
    return car.line >= 0 && !car.driverless && car.dwell > 0 && !car.destroyed && !car.falling && car.fireTime <= 0 &&
           car.body.v.length2() <= .8 * .8;
}
}

double World::placeTransitRider(Body &body, const Car &car, int seat)
{
    const bool tram = car.kind == 9;
    const double side = seat % 2 ? 1. : -1.;
    Vec2 local{side * (tram ? .83 : .74), (tram ? -2.25 : -3.05) + (seat / 2) * (tram ? 1.5 : 1.65)};
    body.p = car.body.p + rotate(local, car.body.angle);
    body.height = car.body.height + (tram ? .81 : .85);
    body.roadLevel = car.body.roadLevel;
    body.v = {};
    body.verticalSpeed = 0;
    return car.heading() + (tram ? -side * pi * .5 : 0.);
}

int World::freeTransitSeat(const Car &car) const
{
    std::array<bool, 8> used{};
    if (transitPassenger() && controlledCar == int(car.id)) used[playerTransitSeat] = true;
    for (const auto &person : people)
        if (person.riding == int(car.id)) used[person.seat] = true;
    auto seat = std::ranges::find(used, false);
    return seat == used.end() ? -1 : int(seat - used.begin());
}

int World::boardableTransit() const
{
    if (controlledCar >= 0 || playerScooter >= 0 || drone.active || player.roadLevel != 0 || health <= 0)
        return -1;
    int nearest = -1;
    double best = 100;
    for (const auto &car : cars)
    {
        if (!doorsOpen(car) || (car.body.p-player.p).length2() > 100 ||
            std::abs(player.height - car.body.height) > 1.8) continue;
        const auto &line = lines[car.line];
        auto curb = line.curb(platformStop(line,car));
        if ((curb-car.body.p).length2() > 100) continue;
        auto door = passengerDoor(car,curb);
        double distance = std::min((player.p-door).length2(), (player.p-curb).length2());
        if (distance < 2.5 * 2.5 && distance < best && freeTransitSeat(car) >= 0 &&
            blastVisible(player.p,player.height,door,car.body.height+.05))
        {
            nearest = int(car.id);
            best = distance;
        }
    }
    return nearest;
}

bool World::rideTransit()
{
    if (transitPassenger())
    {
        const auto &car = cars[controlledCar];
        if (doorsOpen(car) && leaveTransitAt(car,platformStop(lines[car.line],car))) return true;
        transitExitRequested = true;
        return true;
    }
    const int carIndex = boardableTransit();
    if (carIndex < 0) return false;
    auto &car = cars[carIndex];
    playerTransitSeat = freeTransitSeat(car);
    controlledCar = carIndex;
    transitExitRequested = false;
    conversation = {};
    car.dwell = std::max(6., car.dwell);
    player.angle = -placeTransitRider(player, car, playerTransitSeat);
    physics.clear();
    return true;
}

// Disembark on the pavement, including at tram stops set back from the rails.
// Try alternate standing places if a pedestrian, car or street fixture blocks the first.
std::optional<Vec2> World::platformExit(const Car &car, int stop) const
{
    const auto &line = lines[car.line];
    const auto centre = line.curb(stop);
    if ((centre-car.body.p).length2() > 100) return {};
    const auto tangent = forward(line.pose(line.stops[stop]).second);
    for (double offset : {0., .9, -.9, 1.8, -1.8, 2.7, -2.7})
    {
        auto p = centre + tangent * offset;
        double y = terrain.height(p) + .85;
        if (!layout.urbanSite(p,.4) || scenerySolid(p,y,.35) || insideBuilding(p,y,.35)) continue;
        auto overlaps = [&](const Body &b) {
            if (std::abs(b.height-y) > b.verticalExtent * .5 + .8) return false;
            auto d = rotate(p-b.p,-b.angle);
            return b.circle ? d.length2() < (b.radius+.4)*(b.radius+.4) :
                   std::abs(d.x)<b.halfW+.4 && std::abs(d.z)<b.halfH+.4;
        };
        if (std::ranges::any_of(people,[&](const Person &v){return v.riding<0 && !v.dead && overlaps(v.body);}) ||
            (controlledCar < 0 && overlaps(player)) ||
            std::ranges::any_of(cars,[&](const Car &v){return overlaps(v.body);}) ||
            std::ranges::any_of(staticBodies,[&](const Body &v){return v.invMass<1 && overlaps(v);})) continue;
        return p;
    }
    return {};
}

bool World::leaveTransitAt(const Car &car, int stop)
{
    auto exit = platformExit(car,stop);
    if (!exit) return false;
    disembarkPlayer(car, *exit);
    return true;
}

void World::disembarkPlayer(const Car &car, Vec2 at)
{
    player.p = at;
    player.v = {};
    player.verticalSpeed = 0;
    player.height = surface(at, car.body.roadLevel) + .85;
    player.roadLevel = car.body.roadLevel;
    player.angle = car.body.angle;
    player.wake();
    controlledCar = playerTransitSeat = -1;
    transitExitRequested = false;
    physics.clear();
}

void World::exchangePassengers(Car &car, int stop)
{
    if (transitPassenger() && controlledCar == int(car.id) && transitExitRequested)
        leaveTransitAt(car,stop);
    for (auto &p : people)
        if (p.riding == int(car.id) && p.alight == stop)
            if (auto at = platformExit(car,stop)) resumeWalk(p,*at,0);
}

void World::updatePlatforms()
{
    nextPlatformScan = time + .5;
    if (trafficCells.empty()) indexTraffic();
    for (const auto &car : cars)
    {
        if (car.line < 0 || (!car.driverless && car.fireTime <= 0) || car.destroyed || car.falling || car.body.v.length2() > .8*.8) continue;
        if (transitPassenger() && int(car.id) == controlledCar)
            if (auto exit = vehicleExit(car)) disembarkPlayer(car, *exit);
        for (auto &p : people)
            if (p.riding == int(car.id))
                if (auto exit = vehicleExit(car))
                {
                    resumeWalk(p, *exit, car.body.roadLevel);
                    if (car.fireTime > 0) scarePerson(p, car.body.p, 1.);
                }
    }
    auto eligible = [&](int i) {
        const auto &p = people[i];
        return !p.dead && p.riding < 0 && !p.body.roadLevel && p.ownedScooter < 0 && !p.inCompany &&
               !p.courier && p.fear < .15 && p.health > 0 && p.burnUntil <= time && p.alertUntil <= time &&
               p.watchUntil <= time && p.boardAfter <= time && !waitingForTaxi(i) && !paramedicCall(i) &&
               i != patrol.officer && !((job.passenger() || job.kind == JobKind::elimination) && job.actor == i);
    };
    for (int i = 0; i < int(people.size()); ++i)
    {
        auto &p = people[i];
        if (p.waitingLine >= 0 && (!eligible(i) || time >= p.waitUntil))
        {
            p.waitingLine = p.waitingStop = -1;
            p.boardAfter = time + 60;
        }
    }
    std::vector<int> candidates;
    for (int l = 0; l < int(lines.size()); ++l)
    {
        const auto &line = lines[l];
        std::vector<Car *> arrivals(line.stops.size());
        for (auto &c : cars)
            if (c.line == l && doorsOpen(c) && (int(c.id) != controlledCar || transitPassenger()))
                arrivals[platformStop(line,c)] = &c;
        int waiting = int(std::ranges::count(people,l,&Person::waitingLine));
        for (int s = 0; s < int(line.stops.size()); ++s)
        {
            const auto curb = line.curb(s);
            walkersNear(curb,14,candidates);
            const auto tangent = forward(line.pose(line.stops[s]).second);
            Car *car = arrivals[s];
            if (car && (car->body.p-curb).length2()>100) car = nullptr;
            if (car) exchangePassengers(*car,s);
            const Vec2 door = car ? passengerDoor(*car,curb) : curb;
            int queued = 0, first = -1;
            for (int i : candidates)
            {
                auto &p = people[i];
                if (p.waitingLine == l && p.waitingStop == s)
                {
                    ++queued;
                    p.platformTarget = curb + tangent * (double(int(p.body.id % 3)-1) * .85);
                    if (!car) continue;
                    int seat = freeTransitSeat(*car);
                    if (seat < 0) continue;
                    p.platformTarget = door;
                    if ((p.body.p-door).length2() > .8*.8 ||
                        !blastVisible(p.body.p,p.body.height,door,car->body.height+.05)) continue;
                    p.waitingLine = p.waitingStop = -1;
                    p.riding = int(car->id);
                    p.seat = seat;
                    p.alight = (s+1+int(p.body.id%3)) % int(line.stops.size());
                    seatPassenger(p);
                    --waiting;
                }
                else if (first < 0 && waiting < 16 && p.waitingLine < 0 && eligible(i) &&
                         (p.body.p-curb).length2() < 144 &&
                         std::abs(dot(p.body.p-curb,perpendicular(tangent))) < 3 &&
                         blastVisible(p.body.p,p.body.height,curb,terrain.height(curb)+.85))
                    first = i;
            }
            if (first >= 0 && queued < 3 && layout.urbanSite(curb,.4) &&
                !insideBuilding(curb,terrain.height(curb)+.85,.4))
            {
                auto &p = people[first];
                p.waitingLine = l;
                p.waitingStop = s;
                p.platformTarget = curb;
                p.waitUntil = time + 100;
                ++waiting;
            }
        }
    }
}

void World::runTransit(Car &c, Input controls, bool driving, bool local, double dt)
{
    const auto &line = lines[c.line];
    auto &b = c.body;
    // A road-going trolley can be steered off its wire. Reacquire the route when handed back.
    if (driving)
    {
        c.dwell = 0;
        c.nextStop = line.nextStopAfter(c.along + .7);
    }
    if (c.kind == 10 && driving && (local || c.driverless))
    {
        stepVehicle(c, controls, dt, 1 - rain * .2);
        c.checkTrafficAt = 0;
        return;
    }
    if (local && c.kind == 10 && time >= c.checkTrafficAt)
    {
        const double projected = line.track.closestDistance(b.p);
        if (std::abs(std::remainder(projected - c.along, line.track.length())) > 12.)
            c.nextStop = line.nextStopAfter(projected);
        c.along = projected;
    }
    auto [point, heading] = line.pose(c.along);
    auto f = forward(heading);
    double speed = dot(b.v, f);
    if (!driving)
    {
        c.dwell = std::max(0., c.dwell - dt);
        double toStop = line.wrapDistance(line.stops[c.nextStop] - c.along);
        if (toStop < .65 && std::abs(speed) < 1.4 && c.dwell <= 0)
        {
            c.dwell = 5;
            exchangePassengers(c, c.nextStop);
            c.nextStop = (c.nextStop + 1) % int(line.stops.size());
        }
        if (time >= c.checkTrafficAt)
        {
            c.checkTrafficAt = time + .3;
            c.serviceTarget = 7;
            auto slow = [&](double clearance)
            { c.serviceTarget = std::min(c.serviceTarget, std::sqrt(3.6 * std::max(0., clearance))); };
            if (local)
            {
                for (auto &other : cars)
                {
                    auto d = other.body.p - b.p;
                    if (&c == &other || std::abs(other.body.height - b.height) > 2 || dot(d, f) <= 0 ||
                        dot(d, f) > 28 || std::abs(cross(d, f)) > b.halfW + other.body.halfW)
                        continue;
                    slow(dot(d, f) - b.halfH - other.body.halfH - 1.);
                }
                for (auto &p : people)
                {
                    auto d = p.body.p - b.p;
                    if (p.riding < 0 && !p.dead && std::abs(p.body.height - b.height) < 2 && dot(d, f) > 0 &&
                        dot(d, f) < 18 && std::abs(cross(d, f)) < b.halfW + .4)
                        slow(dot(d, f) - b.halfH - 1.);
                }
            }
            int dir = std::abs(f.x) > std::abs(f.z) ? (f.x > 0 ? 1 : 3) : (f.z > 0 ? 0 : 2);
            for (double d = b.halfH + 1; d < 24; d += 2)
            {
                auto ahead = line.pose(c.along + d).first;
                int x = clamp(int(std::round((ahead.x - layout.coordinate(0)) / spacing)), 0, N - 1);
                int z = clamp(int(std::round((ahead.z - layout.coordinate(0)) / spacing)), 0, N - 1);
                auto junction = nodePoint(node(x, z));
                auto delta = ahead - junction, now = b.p - junction;
                if (std::max(std::abs(delta.x), std::abs(delta.z)) < layout.roadHalf() &&
                    std::max(std::abs(now.x), std::abs(now.z)) > layout.roadHalf() + b.halfH &&
                    layout.degree(node(x, z)) >= 3 && !CityLayout::green(x, z, dir, time))
                {
                    slow(d - b.halfH - .9);
                    break;
                }
            }
        }
        double target =
            (c.dwell > 0 || c.fireTime > 0) ? 0. : std::min(c.serviceTarget, std::sqrt(3.6 * std::max(0., toStop - .3)));
        controls = {clamp((target - speed) * .7, -1., 1.), 0, target < .1, false};
        if (local && c.kind == 10)
        {
            auto aim = line.pose(c.along + std::max(5., speed * .8)).first - b.p;
            double angle = wrap(std::atan2(aim.x, aim.z) - c.heading());
            controls.steering = clamp(
                std::atan2(2 * c.wheelbase * std::sin(angle), std::max(1., aim.length())) / .62, -1., 1.);
            stepVehicle(c, controls, dt, 1 - rain * .2);
            c.along = line.wrapDistance(c.along + speed * dt);
            return;
        }
    }
    bool braking = controls.handbrake || controls.brake || controls.throttle * speed < -.12;
    speed = braking ? speed - clamp(speed, -3.6 * dt, 3.6 * dt)
                    : speed + (controls.throttle * 1.25 - speed * .025) * dt;
    speed = clamp(speed, driving ? -4.5 : 0., 14.);
    c.braking = braking;
    b.v = f * speed;
    b.angle = -heading;
    b.w = 0;
    if (std::abs(speed) > .001)
        b.wake();
    if (!local)
    {
        c.along = line.wrapDistance(c.along + speed * dt);
        auto [pos, yaw] = line.pose(c.along);
        b.p = pos;
        b.angle = -yaw;
        b.height = terrain.height(pos) + .8;
        b.roadLevel = 0;
    }
}

void World::alignTram(Car &c, double dt)
{
    const auto &line = lines[c.line];
    auto [point, heading] = line.pose(c.along);
    auto f = forward(heading);
    c.along = line.wrapDistance(c.along + clamp(dot(c.body.p - point, f), -40 * dt, 40 * dt));
    auto [p, newHeading] = line.pose(c.along);
    c.body.p = p;
    c.body.angle = -newHeading;
    c.body.w = 0;
    c.body.v = forward(newHeading) * dot(c.body.v, f);
}
} // namespace pz
