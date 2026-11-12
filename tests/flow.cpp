#include "promzona/fleet.hpp"
#include "promzona/world.hpp"
#include <algorithm>
#include <cmath>
#include <iostream>
#include <stdexcept>

using namespace pz;
namespace
{
void require(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
}
constexpr double frame = 1. / 60;
// The player watches on foot; the car that would stand in a lane is out on the beach.
void spectate(World &w, Vec2 at)
{
    auto &own = w.cars[0];
    own.body.p = {World::landEdge - 30, 40};
    own.body.height = w.terrain.height(own.body.p) + .8;
    own.driverless = true;
    w.controlledCar = -1;
    w.player.p = at;
    w.player.height = w.terrain.height(at) + .85;
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
}
// A four-way junction of ordinary streets far from the player.
int quietJunction(const World &w)
{
    for (int n = 0; n < World::nodes * World::nodes; ++n)
        if (w.layout.degree(n) == 4 && w.layout.urbanSite(World::nodePoint(n), 40) &&
            !w.levelAt(World::nodePoint(n), 60) && (World::nodePoint(n) - w.position()).length() > 400)
            return n;
    throw std::runtime_error("no ordinary junction");
}
void clearAround(World &w, Vec2 centre)
{
    for (size_t i = 1; i < w.cars.size(); ++i)
        if ((w.cars[i].body.p - centre).length() < 70)
        {
            w.cars[i].destroy();
            w.cars[i].body.p = {World::landEdge - 20, -300. + 8. * double(i % 60)};
        }
    for (auto &p : w.people)
        if ((p.body.p - centre).length() < 40)
            p.body.p += Vec2{0, 80};
    w.indexTraffic();
}
Car &place(World &w, size_t slot, unsigned model, int from, int to, int lane, double back)
{
    auto &c = w.cars.at(slot);
    c = Car{};
    c.id = std::uint32_t(slot);
    c.body.id = 0x7000u + std::uint32_t(slot);
    fitVehicle(c, model);
    c.routeFrom = from;
    c.routeTo = to;
    c.routePhase = 1;
    c.laneIndex = lane;
    w.newRoute(c);
    c.routeDistance = c.route->length() - back;
    const auto [at, heading] = c.route->sample(c.routeDistance);
    c.body.p = at;
    c.body.angle = -heading;
    c.body.height = w.surface(at, 0) + .8;
    return c;
}

// The rules at one junction.
void junction()
{
    World w;
    w.generate(20261105u);
    spectate(w, World::nodePoint(World::nodes * World::nodes / 2) + Vec2{11.5, 11.5});
    const int node = quietJunction(w), west = node - 1, east = node + 1, north = node + World::nodes;
    const Vec2 centre = World::nodePoint(node);
    clearAround(w, centre);
    // Eastbound traffic has green for the first twelve seconds of the programme.
    const int i = node % World::nodes, j = node / World::nodes;
    w.time = 28 * 40 + 2 - (i * 3 + j * 5) % 9;
    require(CityLayout::green(i, j, 1, w.time), "test junction is not green");

    auto &car = place(w, 1, 0, west, node, 0, 0);
    w.indexTraffic();
    require((car.body.p - (centre + Vec2{-stopLine, CityLayout::lane})).length() < 1e-6 ||
                (car.body.p - (centre + Vec2{-stopLine, -CityLayout::lane})).length() < 1e-6,
            "the stop line is not 12.6 m before the junction");
    require(std::abs(car.body.p.x - centre.x) - car.body.halfH > 10.3, "a waiting car stands on the zebra");
    require(w.intersectionClear(car), "an empty junction is closed");
    // Don't block the box: with the way out taken the car stays at the line.
    car.plannedTo = east;
    require(w.intersectionClear(car), "a free way out keeps the car back");
    auto &queued = place(w, 2, 0, node, east, 0, 0);
    queued.routeDistance = 1.5;
    queued.body.p = queued.route->sample(1.5).first;
    w.indexTraffic();
    require(!w.intersectionClear(car), "the car enters a junction it cannot leave");
    queued.body.v = forward(queued.heading()) * 5;
    require(w.intersectionClear(car), "a car driving off ahead keeps the junction closed");
    queued.body.v = {};
    car.plannedTo = north;
    require(w.intersectionClear(car), "a queue on another street keeps the car back");
    // One car crosses at a time, however steep the street is.
    queued.routePhase = 1;
    queued.routeFrom = node;
    queued.routeTo = east;
    queued.body.p = centre + Vec2{2, CityLayout::lane};
    queued.body.height += 3.5;
    w.indexTraffic();
    require(w.junctionHeld(node, &car) && !w.intersectionClear(car),
            "two cars may cross a steep junction at once");
    require(!w.junctionHeld(node, &queued), "a car holds the junction against itself");
    queued.destroy();
    queued.body.p = {World::landEdge - 20, 200};
    // A tram on the junction closes it, and so does one that has asked for it; one that
    // waits at its lights does not.
    auto tram = std::ranges::find(w.cars, 9, &Car::kind);
    require(tram != w.cars.end(), "no tram");
    const Vec2 home = tram->body.p;
    const double keep = tram->body.angle;
    tram->body.p = centre + Vec2{0, -CityLayout::lane + 3.8};
    tram->body.angle = -pi * .5;
    tram->body.height = w.surface(tram->body.p, 0) + .8;
    w.indexTraffic();
    require(!w.intersectionClear(car), "a car drives into a tram on the junction");
    tram->body.p = centre + Vec2{-17, -CityLayout::lane};
    w.indexTraffic();
    require(w.intersectionClear(car), "a tram waiting at its lights closes the junction");
    tram->claimed = node;
    require(!w.intersectionClear(car), "a car takes the junction a tram has asked for");
    tram->claimed = -1;
    tram->body.p = home;
    tram->body.angle = keep;

    // Two trucks side by side at the lights are 21 cm apart; neither is ahead of the other.
    auto &left = place(w, 3, 17, west, node, 0, 0);
    auto &right = place(w, 4, 17, west, node, 1, 0);
    w.indexTraffic();
    w.senseTraffic(left, false);
    w.senseTraffic(right, false);
    require(left.trafficLimit > 5 && right.trafficLimit > 5, "trucks abreast hold each other");
    // A long vehicle waits further back, an ordinary car right behind a queue.
    right.destroy();
    right.body.p = {World::landEdge - 20, 220};
    auto &behind = place(w, 5, 0, west, node, 0, 9);
    w.indexTraffic();
    w.senseTraffic(behind, false);
    require(behind.trafficLimit > 0 && behind.trafficLimit < 3,
            "a queue at the lights is kept at a distance");
    require(behind.blockedSince < 0, "a queue at the lights counts as an obstacle");

    // Out of a turn the car still points across the lane. Whoever stands in front of the
    // bumper is in the way, although the line the car steers for passes them by.
    for (size_t slot : {size_t(3), size_t(5)})
    {
        w.cars[slot].destroy();
        w.cars[slot].body.p = {World::landEdge - 20, 230. + 8. * double(slot)};
    }
    auto &turned = place(w, 6, 0, node, east, 1, 0);
    turned.routeDistance = .4;
    const auto [start, heading] = turned.route->sample(.4);
    turned.body.p = start;
    turned.body.angle = -(heading + .6);
    const Vec2 nose = forward(turned.heading());
    auto walker = std::ranges::find_if(w.people, [](const Person &p) { return p.riding < 0 && !p.dead; });
    require(walker != w.people.end(), "nobody to stand in the way");
    auto stand = [&](Vec2 at)
    {
        walker->body.p = at;
        walker->body.height = w.surface(at, 0) + .85;
        walker->body.roadLevel = 0;
        walker->body.v = {};
        w.indexTraffic();
        w.senseTraffic(turned, true);
        return turned.trafficLimit;
    };
    const Vec2 before = turned.body.p + nose * (turned.body.halfH + 1.2);
    require(std::abs(cross(before - start, forward(heading))) > turned.body.halfW + walker->body.radius + .5,
            "the walker stands in the lane, not beside it");
    require(stand(before) < .3, "a car runs over whoever stands in front of its bumper");
    // Beside the bumper on the side the lane does not lie on, and behind the car.
    const Vec2 aside = perpendicular(nose) * (dot(forward(heading), perpendicular(nose)) > 0 ? -2.5 : 2.5);
    require(stand(before + aside) > 20 && stand(turned.body.p - nose * (turned.body.halfH + 1.2)) > 20,
            "a car waits for somebody who is not in its way");
    stand(centre + Vec2{0, 60});

    // A truck that came out of a left turn still points at the oncoming lane, where another
    // truck waits at the lights. It steers back as it goes: its bonnet is not on its way
    // into the other. Reckoned straight ahead it was, and the junction behind it stayed shut.
    const int south = node - World::nodes;
    auto point = [&](Car &c, Vec2 at, Vec2 facing)
    {
        c.body.p = centre + at;
        c.body.angle = -std::atan2(facing.x, facing.z);
        c.body.height = w.surface(c.body.p, 0) + .8;
    };
    for (size_t slot : {size_t(1), size_t(2), size_t(4), size_t(6)})
    {
        w.cars[slot].destroy();
        w.cars[slot].body.p = {World::landEdge - 20, 230. + 8. * double(slot)};
    }
    auto &out = place(w, 7, 17, node, south, 0, 0);
    out.routeDistance = .19;
    point(out, {2.46, -9.19}, {-.296, -.955});
    auto &oncoming = place(w, 8, 16, south, node, 0, 1.15);
    require(std::abs(oncoming.body.p.x - centre.x + CityLayout::laneOffset(0)) < 1e-6 &&
                std::abs(oncoming.body.p.z - centre.z + 13.75) < 1e-6,
            "the oncoming truck does not wait where it should");
    w.indexTraffic();
    w.senseTraffic(out, true);
    require(out.trafficLimit > 3, "a truck out of a turn waits for the truck in the oncoming lane");
    // What its bumper will meet all the same stops it: a car across the lane right in front.
    auto &across = place(w, 9, 0, node, south, 0, 0);
    across.driverless = true;
    point(across, Vec2{2.46, -9.19} + Vec2{-.296, -.955} * (out.body.halfH + 1.6), {1, 0});
    w.indexTraffic();
    w.senseTraffic(out, true);
    require(out.trafficLimit < .5, "a truck out of a turn runs into the car in front of its bumper");
    for (size_t slot : {size_t(7), size_t(8), size_t(9)})
    {
        w.cars[slot].destroy();
        w.cars[slot].body.p = {World::landEdge - 20, 230. + 8. * double(slot)};
    }

    // A bus that comes round into the kerb lane points its bonnet at the pavement. Whoever
    // stands there is not in its way: the bumper swings round with the turn.
    auto &bus = place(w, 10, busModel, south, node, 1, 0);
    bus.plannedTo = east;
    bus.routeDistance = bus.route->length();
    w.newRoute(bus);
    require(bus.routePhase == 1 && bus.routeTo == east && bus.route->length() > 24, "the bus does not turn");
    bus.routeDistance = 22.83;
    point(bus, {6.78, 4.29}, {.909, .416});
    auto beside = [&](Vec2 at)
    {
        walker->body.p = centre + at;
        walker->body.height = w.surface(walker->body.p, 0) + .85;
        walker->body.roadLevel = 0;
        walker->body.v = {};
        w.indexTraffic();
        w.senseTraffic(bus, true);
        return bus.trafficLimit;
    };
    require(beside({11.65, 8.03}) > 3,
            "a bus in a turn waits for somebody on the pavement beyond its bonnet");
    const Vec2 bonnet = Vec2{6.78, 4.29} + Vec2{.909, .416} * (bus.body.halfH + .7);
    require(beside(bonnet) < .3, "a bus in a turn runs over whoever stands in front of its bumper");
    // In the lane it is turning into, where the bumper will be three metres on.
    require(beside({14.2, 5.25}) < 2.5, "a bus in a turn does not brake for somebody in its lane");
}

// Somebody hurt in the road makes for the kerb; fright does not freeze people by the canal.
void walkers()
{
    World w;
    w.generate(20261105u);
    spectate(w, World::nodePoint(World::nodes * World::nodes / 2) + Vec2{11.5, 11.5});
    const int node = quietJunction(w);
    const Vec2 inRoad = World::nodePoint(node) + Vec2{18, 1.9};
    const Vec2 kerb = w.kerbside(inRoad);
    require(w.layout.roadDistance(inRoad) < 3 && w.layout.roadDistance(kerb) > w.layout.roadHalf() + 1.2 &&
                (kerb - inRoad).length() < 12,
            "no kerb for the injured");
    require((w.kerbside(kerb) - kerb).length() == 0, "the kerb is not a place to stay");
    // The embankment street: the canal is nine metres from the pavement.
    const int column = World::nodes * 2 / 3;
    Person *shy = nullptr;
    for (auto &p : w.people)
        if (!shy && p.riding < 0 && !p.inCompany && p.role == Person::Role::none && !p.dead)
            shy = &p;
    require(shy, "nobody to frighten");
    const Vec2 pavement = Vec2{w.layout.coordinate(column), w.layout.coordinate(20)} + Vec2{9.1, 22};
    require(!w.layout.urbanSite(pavement, .3) && w.layout.riverDistance(pavement.x) < 10,
            "this is not the embankment");
    shy->body.p = pavement;
    shy->body.height = w.terrain.height(pavement) + .85;
    shy->body.roadLevel = 0;
    shy->node = column + 20 * World::nodes;
    shy->next = shy->node + World::nodes;
    shy->target = World::nodePoint(shy->next) + Vec2{9.1, 9.1};
    w.scarePerson(*shy, pavement + Vec2{0, 2}, 2.);
    for (int k = 0; k < 150; ++k)
        w.step({}, frame);
    require((shy->body.p - pavement).length() > 4 && !shy->dead,
            "fright by the canal roots people to the spot");

    // A tram pulling away runs over whoever is in front of it, not whoever brushes its side.
    auto tram = std::ranges::find(w.cars, 9, &Car::kind);
    require(tram != w.cars.end(), "no tram");
    const Vec2 ahead = forward(tram->heading()), side = perpendicular(ahead);
    tram->body.v = ahead * 2;
    auto meet = [&](Vec2 at)
    {
        shy->body.p = at;
        shy->body.height = tram->body.height;
        shy->body.v = {};
        shy->knockedUntil = 0;
        return w.detectImpact(*shy, *tram, false);
    };
    const int before = w.pedestrianImpacts;
    require(!meet(tram->body.p + ahead * (tram->body.halfH - 1.3) + side * (tram->body.halfW + .2)) &&
                !meet(tram->body.p - ahead * (tram->body.halfH + .2)) && w.pedestrianImpacts == before,
            "a tram runs over somebody beside or behind it");
    require(meet(tram->body.p + ahead * (tram->body.halfH + .2)) && w.pedestrianImpacts == before + 1 &&
                !shy->dead && shy->knockedUntil > w.time,
            "a tram does not knock down whoever is in front of it");
}

// The zebra in front of the stop line, where the player can see it.
void zebra()
{
    World w;
    w.generate(20261105u);
    const int node = quietJunction(w), north = node + World::nodes;
    const Vec2 centre = World::nodePoint(node);
    spectate(w, centre + Vec2{-11.5, -11.5});
    clearAround(w, centre);
    const int i = node % World::nodes, j = node / World::nodes;
    auto phase = [&](double seconds) { w.time = 28 * 40 + seconds - (i * 3 + j * 5) % 9; };
    std::vector<Person *> cast;
    for (auto &p : w.people)
        if (cast.size() < 3 && p.riding < 0 && !p.inCompany && p.role == Person::Role::none && !p.dead &&
            p.uniform == Person::Uniform::none && p.ownedScooter < 0 && !p.courier && p.waitingLine < 0 &&
            !p.elder() && !p.drunk() && !p.homeless() && (p.body.p - centre).length() > 200)
            cast.push_back(&p);
    require(cast.size() == 3, "nobody to cross the street");
    auto put = [&](Person &p, Vec2 where, Vec2 pace)
    {
        p.node = node - 1;
        p.next = node;
        p.target = centre + Vec2{9.1, 9.1};
        p.body.p = centre + where;
        p.body.height = w.terrain.height(p.body.p) + .85;
        p.body.roadLevel = 0;
        p.body.v = pace;
        p.fear = 0;
        w.indexTraffic();
    };
    // Southbound at the stop line with a green light: the walked line is right in front.
    phase(15);
    auto &car = place(w, 1, 0, north, node, 1, 0);
    w.indexTraffic();
    require(CityLayout::green(i, j, 2, w.time) && w.intersectionClear(car), "the car has no green light");
    const double lane = car.body.p.x - centre.x;
    put(*cast[0], {lane, 9.1}, {});
    require(!w.intersectionClear(car), "the car starts with somebody in front of it");
    // Whoever walks towards the lane will be in front of the car when it is on the zebra.
    put(*cast[0], {lane - 4, 9.1}, {1.5, 0});
    require(!w.intersectionClear(car), "the car starts across the way of somebody on the zebra");
    put(*cast[0], {lane - 4, 9.1}, {-1.5, 0});
    require(w.intersectionClear(car), "the car waits for somebody who is walking away");
    put(*cast[0], {lane - 4, 9.1}, {});
    require(w.intersectionClear(car), "the car waits for somebody who stands aside");

    // A car that stopped across the walked line all the same, nose to the junction: people
    // walk round the bonnet. They used to turn back at its corner and stand there, the car
    // waiting for them and the junction for the car.
    car.driverless = true;
    car.body.p.z = centre.z + 10;
    phase(26.5);
    for (int k = 0; k < 3; ++k)
        put(*cast[k], {-9. - 1.3 * k, 9.1}, {});
    const double beyond = lane + car.body.halfW + 1.5;
    int steps = 0;
    auto across = [&](const Person *p) { return p->body.p.x - centre.x > beyond; };
    for (; steps < 60 * 22 && !std::ranges::all_of(cast, across); ++steps)
        w.step({}, frame);
    require(std::ranges::all_of(cast, across), "people do not get round a car that stands on the zebra");
    for (const Person *p : cast)
        require(!p->dead && p->knockedUntil <= w.time && p->fear == 0, "the way round the car is dangerous");
    require((car.body.p - (centre + Vec2{lane, 10})).length() < .3, "the car on the zebra was pushed away");
}

// Tall vehicles used to stop at the first pier of an overpass in front of the player.
void overpass()
{
    World w;
    w.generate(20261105u);
    const RoadLevel *bridge = nullptr;
    for (const auto &level : w.roadLevels)
        if (!level.tunnel && !bridge)
            bridge = &level;
    require(bridge, "no overpass");
    // Nothing under a deck reaches up to what drives on it, on the ramps either: the body of
    // a tall vehicle is as deep under its wheels as it is high above its origin.
    const double tallest = w.cars[0].body.verticalExtent;
    {
        Car tall;
        fitVehicle(tall, 17);
        require(tall.body.verticalExtent > 3 && tall.body.verticalExtent >= tallest, "model 17 is not tall");
        int beams = 0;
        for (const auto &level : w.roadLevels)
            for (const auto &beam : w.staticBodies)
            {
                if (level.tunnel || beam.halfH < 7 || std::abs(beam.p.z - level.centreZ(beam.p.x)) > .01 ||
                    std::abs(beam.p.x - level.x) > RoadLevel::length)
                    continue;
                ++beams;
                for (double x = beam.p.x - 5; x <= beam.p.x + 5; x += .25)
                    for (double side : {-2.8, 2.8})
                    {
                        const Vec2 at{x, level.centreZ(x) + side};
                        require(level.floor(w.terrain, at) + .8 - beam.height >
                                    (tall.body.verticalExtent + beam.verticalExtent) * .5,
                                "a beam under the deck reaches a truck on it");
                    }
            }
        require(beams >= 8, "the overpasses have no beams");
    }
    spectate(w, {bridge->x + 30, bridge->z + 40});
    for (unsigned model : {17u, ambulanceModel})
    {
        auto &c = place(w, model == 17 ? 1 : 2, model, bridge->leftNode, bridge->rightNode, 0, 0);
        require(c.routePhase == 2, "no route over the overpass");
        c.routeDistance = 60;
        c.body.p = c.route->sample(60).first;
        c.body.roadLevel = c.routeLevel;
        c.body.height = w.surface(c.body.p, c.body.roadLevel) + .8;
        const double health = c.health;
        for (int k = 0; k < 60 * 14 && c.routePhase == 2 && c.routeDistance < 150; ++k)
            w.step({}, frame);
        require(c.routeDistance >= 150 && c.health == health,
                "a tall vehicle hit the pier under its own wheels");
        c.destroy();
        c.body.p = {World::landEdge - 20, 260};
    }
}

// Twelve minutes of an untouched city: nobody is left standing, and nothing had to be cleared.
void city()
{
    World w;
    w.generate(20261105u);
    spectate(w, World::nodePoint(World::nodes * World::nodes / 2) + Vec2{11.5, 11.5});
    std::vector<Vec2> last(w.cars.size());
    std::vector<double> movedAt(w.cars.size());
    double travelled = 0;
    for (int k = 1; k <= 60 * 60 * 12; ++k)
    {
        w.step({}, frame);
        if (k % 60)
            continue;
        for (size_t c = 1; c < w.trafficPopulation; ++c)
        {
            const double step = k == 60 ? 0. : (w.cars[c].body.p - last[c]).length();
            if (step > .3 || k == 60)
                movedAt[c] = w.time;
            if (step < 30)
                travelled += step;
            last[c] = w.cars[c].body.p;
        }
    }
    int standing = 0, traffic = 0;
    for (size_t c = 1; c < w.trafficPopulation; ++c)
        if (w.cars[c].line < 0 && !w.cars[c].destroyed)
        {
            ++traffic;
            standing += w.time - movedAt[c] > 120;
        }
    require(standing == 0 && w.clearedJams == 0, "traffic comes to a standstill");
    require(travelled / traffic / w.time > 2.4, "traffic crawls");
    require(std::ranges::none_of(w.people, &Person::dead), "people died in an untouched city");
}
} // namespace

int main()
{
    try
    {
        junction();
        walkers();
        zebra();
        overpass();
        city();
        std::cout << "flow tests passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
