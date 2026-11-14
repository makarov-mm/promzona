#include "promzona/fleet.hpp"
#include "promzona/world.hpp"
#include <algorithm>
#include <cmath>
#include <print>
#include <filesystem>
#include <stdexcept>
#include <source_location>
#include <format>

using namespace Promzona;
namespace
{
void require(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
constexpr double frame = 1. / 60;

// The player watches on foot from `at`; the car that would stand in a lane is on the beach.
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
// A vehicle on the block that leaves `from` towards `to`, `along` metres into it.
Car &place(World &w, size_t slot, unsigned model, int from, int to, int lane, double along)
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
    c.routeDistance = along;
    const auto [at, heading] = c.route->sample(along);
    c.body.p = at;
    c.body.angle = -heading;
    c.body.height = w.surface(at, 0) + .8;
    return c;
}
// A stop on an ordinary block, with nothing else on the block and nobody near.
int quietStop(World &w, Vec2 from, double least)
{
    for (int n = 0; n < World::nodes * World::nodes; ++n)
    {
        const int stop = w.busStopOfNode[size_t(n)];
        if (stop < 0 || (w.busStops[size_t(stop)] - from).length() < least ||
            w.levelAt(w.busStops[size_t(stop)], 60) || w.layout.degree(n) != 4 ||
            w.layout.degree(n + World::nodes) != 4)
            continue;
        const Vec2 sign = w.busStops[size_t(stop)];
        for (size_t i = 1; i < w.cars.size(); ++i)
            if ((w.cars[i].body.p - sign).length() < 90)
            {
                w.cars[i].destroy();
                w.cars[i].body.p = {World::landEdge - 20, -300. + 8. * double(i % 60)};
            }
        for (auto &p : w.people)
            if ((p.body.p - sign).length() < 40)
                p.body.p += Vec2{0, 90};
        w.indexTraffic();
        return n;
    }
    throw std::runtime_error("no ordinary bus stop");
}

void fleetAndCity()
{
    require(ambulanceModel == 18 && fleet[ambulanceModel].kind == 4 &&
                std::string_view(fleet[18].folder) == "ambulance",
            "the ambulance changed its number: saves hold it");
    require(fleet[busModel].kind == 5 && fleet[gasVanModel].kind == 8 && std::size(fleet) == 21,
            "the bus and the gas van are not at the end of the fleet");
    int buses = 0, vans = 0;
    for (int slot = 0; slot < 1600; ++slot)
    {
        buses += streetModel(slot) == busModel;
        vans += streetModel(slot) == gasVanModel;
    }
    require(streetModel(0) == 0 && buses > 90 && buses <= 100 && vans > 90 && vans <= 100,
            "one slot in sixteen is not a bus, or not a gas van");

    World w;
    w.generate(20261105u);
    int found = 0, service = 0;
    for (size_t i = 1; i < w.trafficPopulation; ++i)
    {
        const auto &c = w.cars[i];
        if (c.kind == 5)
        {
            ++found;
            require(c.model == busModel && streetModel(int(i)) == busModel,
                    "a bus stands in a slot that is not a bus's");
            require(c.laneIndex == 1 && std::abs(c.body.halfH - 4.6) < 1e-9 &&
                        std::abs(c.body.halfW - 1.16) < 1e-9 && c.mass == 10500,
                    "a bus is not a bus, or not at the kerb");
        }
        service += c.kind == 8;
    }
    require(found >= 12 && service >= 12, "the city has no buses or no gas vans");

    // Stops: deterministic, on the west pavement, clear of the road and of everything older.
    require(w.busStops.size() >= 20 && w.busStopOfNode.size() == size_t(World::nodes * World::nodes),
            "the city has no bus stops");
    const double pavement = w.layout.sidewalk(), half = World::spacing * .5;
    size_t listed = 0;
    for (int n = 0; n < World::nodes * World::nodes; ++n)
    {
        const int stop = w.busStopOfNode[size_t(n)];
        if (stop < 0)
            continue;
        ++listed;
        const Vec2 sign = w.busStops[size_t(stop)], node = World::nodePoint(n), centre = World::shelter(sign);
        require(w.layout.road(n % World::nodes, n / World::nodes, 0) &&
                    std::abs(sign.x - (node.x - pavement)) < 1e-9 &&
                    std::abs(sign.z - (node.z + half)) < 1e-9,
                "a stop is not half-way along the block it belongs to");
        require(w.layout.roadDistance(centre) > w.layout.roadHalf() + 1.5 && !w.levelAt(sign, 4) &&
                    !w.insideBuilding(centre, w.terrain.height(centre) + .85, .3) &&
                    w.terrain.height(centre) > CityLayout::waterLevel + .5,
                "a shelter stands in the road, in a house or in the water");
        for (const auto &t : w.trees)
            require(std::abs(t.foot.x - centre.x) > .75 + t.radius ||
                        std::abs(t.foot.z - centre.z) > 1.65 + t.radius,
                    "a shelter was built round a tree");
    }
    require(listed == w.busStops.size(), "the stops and the blocks they serve disagree");
    size_t walls = 0;
    for (const auto &b : w.staticBodies)
        walls += b.id >= 0x52000000u && b.id < 0x53000000u;
    require(walls == w.busStops.size() * 2, "a shelter has no wall or no bench to walk into");
    World again;
    again.generate(20261105u);
    require(again.busStops.size() == w.busStops.size() &&
                std::ranges::equal(again.busStops, w.busStops,
                                   [](Vec2 a, Vec2 b) { return (a - b).length() == 0; }),
            "the stops of a city change from one start to the next");

    // A bus in a save comes back as the same bus.
    const auto file = std::filesystem::temp_directory_path() / "promzona-bus-test.json";
    w.save(file);
    World loaded;
    loaded.load(file);
    std::filesystem::remove(file);
    for (size_t i = 1; i < w.trafficPopulation; ++i)
        require(loaded.cars[i].model == w.cars[i].model && loaded.cars[i].kind == w.cars[i].kind &&
                    loaded.cars[i].laneIndex == w.cars[i].laneIndex &&
                    loaded.cars[i].body.halfH == w.cars[i].body.halfH,
                "a bus was not saved as a bus");
    require(loaded.busStops.size() == w.busStops.size(), "a loaded city has other stops");
}

// The bus pulls up at the shelter, stands and goes on; seen from near and from far.
void stopping(bool near)
{
    World w;
    w.generate(20261105u);
    const Vec2 home = w.position();
    spectate(w, home);
    const int node = quietStop(w, home, 400), north = node + World::nodes;
    const Vec2 sign = w.busStops[size_t(w.busStopOfNode[size_t(node)])];
    if (near)
        spectate(w, sign + Vec2{-6, 8});
    auto &bus = place(w, 9, busModel, node, north, 1, 0);
    require(bus.kind == 5 && bus.routePhase == 0 && bus.body.p.z < sign.z - 6 &&
                std::abs(bus.body.p.x - (sign.x + w.layout.sidewalk() - CityLayout::laneOffset(1))) < .3,
            "the bus does not start in the lane by the stop");
    w.indexTraffic();
    double stoodAt = 0, stood = 0, furthest = 0;
    bool stopped = false;
    for (int i = 0; i < 60 * 40 && bus.routePhase == 0 && bus.routeFrom == node; ++i)
    {
        w.step({}, frame);
        furthest = std::max(furthest, bus.body.v.length());
        if (bus.busUntil > w.time)
        {
            if (!stopped)
                stoodAt = bus.body.p.z;
            stopped = true;
            stood += frame;
            require(bus.body.v.length() < .7, "the bus drives while it stands at its stop");
        }
    }
    require(stopped, "the bus drove past its stop");
    require(std::abs(stoodAt - sign.z) < 1.6 && furthest > 3,
            "the bus stood somewhere else than at the shelter");
    require(stood > 3.9 && stood < 7.2, "the bus stood for the wrong time");
    require(bus.body.p.z > sign.z + 3 && bus.nextBusStop > w.time, "the bus did not leave its stop");
    require(!bus.destroyed && bus.health == 1, "the bus was damaged at its stop");

    // The next stop within the half minute is passed by.
    auto &second = place(w, 9, busModel, node, north, 1, 0);
    second.nextBusStop = w.time + 30;
    w.indexTraffic();
    for (int i = 0; i < 60 * 12 && second.routePhase == 0 && second.routeFrom == node; ++i)
    {
        w.step({}, frame);
        require(second.busUntil <= w.time, "a bus stops at every stop it comes to");
    }
    // In the inner lane, or the other way, the shelter is not this bus's stop.
    auto &inner = place(w, 9, busModel, node, north, 0, 0);
    w.indexTraffic();
    require(w.busStopLimit(inner) == 100, "a bus stops from the middle of the road");
    auto &south = place(w, 9, busModel, north, node, 1, 0);
    w.indexTraffic();
    require(w.busStopLimit(south) == 100, "a bus stops for the shelter across the road");
}

// Whoever comes up behind a bus at its stop makes for the other lane at once; behind a
// queue nobody does. On a block this short there is seldom room to go round before the
// bus leaves, so the car is through when the bus has gone.
void overtaking()
{
    World w;
    w.generate(20261105u);
    const Vec2 home = w.position();
    spectate(w, home);
    const int node = quietStop(w, home, 400), north = node + World::nodes;
    const Vec2 sign = w.busStops[size_t(w.busStopOfNode[size_t(node)])];
    auto &bus = place(w, 9, busModel, node, north, 1, 0);
    bus.routeDistance = sign.z - World::nodePoint(node).z - 9;
    bus.body.p = bus.route->sample(bus.routeDistance).first;
    bus.busUntil = w.time + 6;
    bus.nextBusStop = bus.busUntil + 34;
    auto &follower = place(w, 1, 0, node, north, 1, 0);
    w.indexTraffic();
    require(follower.body.p.z < bus.body.p.z - 8, "the follower does not start behind the bus");
    for (int i = 0; i < 60 * 3; ++i)
        w.step({}, frame);
    require(follower.laneIndex == 0 && follower.blockedSince >= 0,
            "nobody makes for the other lane behind a bus at its stop");
    require(bus.laneIndex == 1 && std::abs(bus.body.p.z - sign.z) < 1.6, "the bus left its stop or its lane");
    for (int i = 0; i < 60 * 25 && follower.routeFrom == node; ++i)
        w.step({}, frame);
    require(follower.routeFrom != node && follower.health == 1 && bus.health == 1 && bus.laneIndex == 1,
            "the car behind the bus did not get through");
    // The same bus waiting at the lights is a queue like any other.
    auto &queued = place(w, 9, busModel, node, north, 1, 0);
    queued.routeDistance = queued.route->length();
    queued.body.p = queued.route->sample(queued.routeDistance).first;
    queued.nextBusStop = w.time + 100;
    auto &behind = place(w, 1, 0, node, north, 1, 0);
    behind.routeDistance = queued.routeDistance - 9.5;
    behind.body.p = behind.route->sample(behind.routeDistance).first;
    w.indexTraffic();
    w.senseTraffic(behind, false);
    require(behind.blockedSince < 0 && behind.trafficLimit < 3 && behind.laneIndex == 1,
            "a bus at the lights is taken for a bus at its stop");
}

// A bus keeps out of streets that end: it cannot turn round in them.
void deadEnds()
{
    World w;
    w.generate(20261105u);
    spectate(w, w.position());
    int tried = 0;
    for (int end = 0; end < World::nodes * World::nodes && tried < 6; ++end)
    {
        if (w.layout.degree(end) != 1)
            continue;
        const int junction = w.neighbours(end).front();
        if (w.layout.degree(junction) < 3 || w.levelAt(World::nodePoint(junction), 60))
            continue;
        for (int from : w.neighbours(junction))
        {
            if (from == end || w.levelEdge(from, junction))
                continue;
            ++tried;
            // Whatever the draw says, the bus plans another way out; a car may take the street.
            bool entered = false;
            for (int draw = 0; draw < 40; ++draw)
            {
                auto &bus = place(w, 9, busModel, from, junction, 1, 0);
                w.planExit(bus);
                require(bus.plannedTo >= 0 && bus.plannedTo != end && bus.plannedTo != from,
                        "a bus turns into a street that ends");
                auto &car = place(w, 1, 0, from, junction, 1, 0);
                w.planExit(car);
                entered = entered || car.plannedTo == end;
            }
            require(entered, "no car ever takes the street that ends");
            break;
        }
    }
    require(tried >= 3, "the city has no streets that end");
    // Off an overpass or out of a tunnel the bus takes the kerb lane again.
    const RoadLevel &level = w.roadLevels.front();
    auto &bus = place(w, 9, busModel, level.leftNode, level.rightNode, 1, 0);
    require(bus.routePhase == 2 && bus.laneIndex == 0, "a bus is not in the single lane of a level road");
    bus.routeDistance = bus.route->length();
    bus.body.p = bus.route->sample(bus.routeDistance).first;
    w.newRoute(bus);
    require(bus.routePhase == 1 && bus.laneIndex == 1,
            "a bus leaves a level road in the middle of the street");
}
} // namespace

int main()
{
    try
    {
        fleetAndCity();
        stopping(false);
        stopping(true);
        overtaking();
        deadEnds();
        std::println("bus tests passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "FAILED: {}", e.what());
        return 1;
    }
}
