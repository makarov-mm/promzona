#include "promzona/city_mesh.hpp"
#include <chrono>
#include <fstream>
#include <iterator>
#include <print>
#include <stdexcept>
#include <source_location>
#include <format>
using namespace Promzona;
namespace
{
int checks{};
void check(bool value, const char *message, std::source_location where = std::source_location::current())
{
    ++checks;
    if (!value)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void pathClear(const World &w, const Path &path, int level = 0)
{
    for (double s = 0; s <= path.length(); s += .35)
    {
        auto p = path.sample(s).first;
        if (level)
        {
            auto road = w.levelById(level);
            check(road && road->contains(p) && std::abs(p.z - road->centre_z(p.x)) < 4.3,
                  "route stays on raised/sunken carriageway");
        }
        else
        {
            if (level)
            {
                auto road = w.levelById(level);
                check(road && road->contains(p) && std::abs(p.z - road->centre_z(p.x)) < 4.3,
                      "route stays on raised/sunken carriageway");
            }
            else
            {
                check(w.layout.roadDistance(p) < w.layout.roadHalf(), "route stays on existing road");
                auto cutting = w.levelAt(p, .3);
                if (cutting && cutting->open(p, .3) && std::abs(p.x - cutting->x) < RoadLevel::length - 12)
                {
                    std::println(stderr, "cut at {},{} level={} center={},{}", p.x, p.z, cutting->id,
                                 cutting->x, cutting->z);
                    check(false, "ordinary traffic never crosses an open cutting");
                }
            }
            auto cutting = w.levelAt(p, .3);
            if (cutting && cutting->open(p, .3) && std::abs(p.x - cutting->x) < RoadLevel::length - 12)
            {
                std::println(stderr, "cut at {},{} level={} center={},{}", p.x, p.z, cutting->id, cutting->x,
                             cutting->z);
                check(false, "ordinary traffic never crosses an open cutting");
            }
        }
        check(w.surface(p, level) > CityLayout::waterLevel, "route does not enter canal or ocean");
    }
}
void distantQueueAndSave()
{
    World w;
    w.generate(20261105);
    // An uphill block away from the spawn in this seed. Keep the full population
    // and normal distant update so this also exercises the on-disk save contract.
    constexpr int from = 17 * World::nodes + 20;
    auto origin = World::nodePoint(from);
    auto &driver = w.cars[1];
    auto &parked = w.cars[2];
    auto place = [&](Car &car, double x)
    {
        car.body.p = origin + Vec2{x, CityLayout::lane};
        car.body.angle = -pi / 2;
        car.body.height = w.surface(car.body.p, 0) + .8;
        car.body.roadLevel = 0;
        car.body.v = {};
    };
    driver.laneIndex = 0;
    driver.routeFrom = from;
    driver.routeTo = from + 1;
    driver.routePhase = 1;
    w.new_route(driver);
    place(driver, 9);
    place(parked, 27);
    w.ownedGarage = w.nearestSite(ServiceKind::garage, {});
    parked.personal = true;
    check((w.position() - driver.body.p).length() > 150, "fixture uses distant simulation");
    bool merged = false, passed = false;
    for (int step = 0; step < 120 * 20 && !passed; ++step)
    {
        w.step({}, 1. / 120);
        const auto &active = w.cars[1];
        if (!merged && active.laneIndex == 1 && active.body.p.z > origin.z + CityLayout::lane + .3)
        {
            check(w.time >= 2 && active.routePhase == 0, "driver waits for the obstruction before merging");
            auto saved = std::filesystem::temp_directory_path() /
                         std::format("promzona-merge-{}.json",
                                     std::chrono::steady_clock::now().time_since_epoch().count());
            auto position = active.body.p;
            double along = active.routeDistance, cooldown = active.next_lane_decision;
            w.save(saved);
            {
                // The keys of a save are its format: they do not follow the names of the members.
                std::ifstream in(saved, std::ios::binary);
                const std::string text{std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>()};
                for (const char *key : {"\"nextLaneDecision\"", "\"blockedSince\"", "\"nextStop\"",
                                        "\"laneIndex\"", "\"routeDistance\""})
                    check(text.contains(key), "a save keeps the keys of its format");
            }
            w.load(saved);
            std::filesystem::remove(saved);
            const auto &copy = w.cars[1];
            check(copy.laneIndex == 1 && copy.next_lane_decision == cooldown && copy.routeDistance == along &&
                      (copy.route->sample(copy.routeDistance).first - position).length() < 1e-8,
                  "mid-merge save retains path, position and decision cooldown");
            merged = true;
        }
        auto delta = w.cars[1].body.p - w.cars[2].body.p;
        if (std::abs(delta.x) < 2)
            check(std::abs(delta.z) > w.cars[1].body.halfW + w.cars[2].body.halfW,
                  "distant driver passes clear of the parked car");
        passed = merged && delta.x > 2.5;
    }
    check(passed, "distant traffic can finish a pass after reloading mid-merge");
}
// Cars that stand in traffic near the player are set on fire: their drivers do not wait.
void burningDrivers()
{
    // A full city: the streets round the player have queues at their lights.
    World base;
    base.generate(20261105u, {}, CityLayout::districtRevision, Density::full);
    const PlayerInput idle{};
    for (int i = 0; i < 600; ++i)
        base.step(idle, 1. / 120);
    const Vec2 focus = base.position();
    // What every car does in the next two seconds when nothing happens to it.
    World calm = base;
    std::vector<double> calmTop(base.cars.size()), top(base.cars.size());
    for (int i = 0; i < 240; ++i)
    {
        calm.step(idle, 1. / 120);
        for (size_t k = 0; k < calm.cars.size(); ++k)
            calmTop[k] = std::max(calmTop[k], calm.cars[k].body.v.length());
    }
    std::vector<size_t> standing;
    for (size_t k = 1; k < base.cars.size(); ++k)
    {
        const Car &c = base.cars[k];
        if (int(k) != base.controlledCar && !c.destroyed && !c.falling && !c.driverless && !c.personal &&
            !c.summoned && !c.responding && c.line < 0 && c.kind <= 2 && (c.body.p - focus).length() < 60 &&
            calmTop[k] < .3)
            standing.push_back(k);
    }
    check(standing.size() >= 2, "cars stand in traffic near the player");
    if (standing.size() < 2)
        return;
    World burning = base;
    // The last of them has nobody at the wheel.
    const size_t deserted = standing.back();
    burning.cars[deserted].driverless = true;
    for (size_t k : standing)
        burning.cars[k].fireTime = .001;
    for (int i = 0; i < 240; ++i)
    {
        burning.step(idle, 1. / 120);
        for (size_t k : standing)
            top[k] = std::max(top[k], burning.cars[k].body.v.length());
    }
    bool away = true;
    for (size_t k : standing)
        away = away && (k == deserted || top[k] > 2);
    check(away, "the driver of a burning car does not wait in traffic");
    check(top[deserted] < .5, "a burning car with nobody in it stays where it is");
}
void laneChecks(World &w)
{
    w.cars.resize(2);
    w.people.clear();
    w.controlledCar = -1;
    w.player.p = {10000, 10000};
    auto &driver = w.cars[0], &other = w.cars[1];
    driver = {};
    other = {};
    driver.routeFrom = 1200;
    driver.routeTo = w.layout.neighbours(1200).front();
    driver.routePhase = 1;
    w.new_route(driver);
    auto [start, heading] = driver.route->sample(0);
    auto direction = forward(heading), right = perpendicular(direction);
    driver.body.p = start;
    driver.body.angle = -heading;
    driver.body.height = other.body.height = 10;
    auto destination = start + right * (CityLayout::laneOffset(1) - CityLayout::laneOffset(0));
    auto change = [&]
    {
        w.index_traffic();
        return w.tryChangeLane(driver, 1);
    };

    other.body.p = destination - direction * 20;
    other.body.angle = -heading;
    other.body.v = direction * 12;
    check(!change(), "fast vehicle closing from behind has priority");
    other.body.p = destination + direction * 4;
    other.body.v = {};
    check(!change(), "merging driver yields to occupied lane");
    other.body.p = start + direction * 5;
    other.routeFrom = driver.routeFrom;
    other.routeTo = driver.routeTo;
    other.laneIndex = 1;
    check(!change(), "two drivers cannot claim the same gap");
    other.body.roadLevel = 1;
    other.body.height += 8;
    w.people.emplace_back().body.p = destination + direction * 5;
    w.people[0].body.height = driver.body.height;
    check(!change(), "walker blocks a lane change");
    w.people[0].riding = 1;
    check(change(), "car on another level and passenger leave target lane free");
    auto begin = driver.route->sample(0), end = driver.route->sample(driver.route->length());
    check((begin.first - start).length() < 1e-8 && dot(forward(begin.second), direction) > .998 &&
              dot(forward(end.second), direction) > .998,
          "merge starts in place and exits parallel to road");
    check(std::abs(dot(end.first - destination, right)) < 1e-8, "merge arrives in the adjacent lane");
    pathClear(w, *driver.route);
    check(!w.tryChangeLane(driver, 0), "cannot immediately change lanes again");
    w.new_route(driver);
    check((driver.route->sample(0).first - end.first).length() < 1e-8, "crossing follows merged lane");

    // Approach every elevated and sunken link from a normal outer lane. Force the
    // route picker to take that link, then compare both ends of the transition.
    int transitions = 0;
    for (const auto &level : w.roadLevels)
        for (int entry : {level.left_node, level.right_node})
        {
            int previous = w.layout.neighbours(entry).front();
            for (int attempt = 0; attempt < 100; ++attempt)
            {
                Car probe;
                probe.routeFrom = previous;
                probe.routeTo = entry;
                probe.laneIndex = 1;
                w.new_route(probe);
                if (!w.levelEdge(probe.routeFrom, probe.routeTo))
                    continue;
                auto corner = probe.route->sample(probe.route->length());
                w.new_route(probe);
                check(probe.routePhase == 2 && probe.laneIndex == 0 &&
                          (corner.first - probe.route->sample(0).first).length() < 1e-8,
                      "outer lane joins the single lane of the deck or tunnel");
                ++transitions;
                break;
            }
        }
    check(transitions == int(w.roadLevels.size()) * 2, "all level entrances exercised");
}
} // namespace
int main()
{
    try
    {
        World w;
        w.generate(20261105);
        // Walk real route transitions over the random graph, including its dead ends.
        int deadEnds = 0;
        for (int n = 0; n < World::nodes * World::nodes; ++n)
        {
            if (w.neighbours(n).size() != 1)
                continue;
            ++deadEnds;
            Car c;
            c.routeFrom = w.neighbours(n)[0];
            c.routeTo = n;
            c.routePhase = 0;
            auto f = (World::nodePoint(n) - World::nodePoint(c.routeFrom)).unit();
            auto start = World::nodePoint(n) - f * stopLine + perpendicular(f) * CityLayout::lane;
            w.new_route(c);
            check(c.routePhase == 1 && c.routeFrom == n, "dead end enters turnaround");
            check((start - c.route->sample(0).first).length() < 1e-8, "turnaround starts on incoming lane");
            pathClear(w, *c.route, c.routeLevel);
            auto end = c.route->sample(c.route->length());
            w.new_route(c);
            auto next = c.route->sample(0);
            check((end.first - next.first).length() < 1e-8,
                  "turnaround joins outgoing lane without teleport");
            check(std::abs(wrap(end.second - next.second)) < .1, "turnaround exits facing correct way");
        }
        check(deadEnds > 0, "seed exercises dead ends");
        Car c;
        c.routeFrom = 1200;
        c.routeTo = w.neighbours(1200)[0];
        c.routePhase = 1;
        w.new_route(c);
        for (int i = 0; i < 1000; ++i)
        {
            pathClear(w, *c.route, c.routeLevel);
            auto end = c.route->sample(c.route->length());
            w.new_route(c);
            auto begin = c.route->sample(0);
            check((begin.first - end.first).length() < 1e-8, "street and crossing endpoints agree");
            check(std::abs(wrap(begin.second - end.second)) < .13, "street and crossing directions agree");
        }
        for (auto &p : w.pickups)
            check(w.terrain.height(p.p) > CityLayout::waterLevel, "pickup spawns on dry land");
        for (auto &p : w.people)
            check(w.terrain.height(p.body.p) > CityLayout::waterLevel, "pedestrian spawns on dry land");

        // Vertex heights on bridge edge and road paint must agree with collision support.
        double riverX = w.layout.coordinate(16) + 18, bridgeZ = w.layout.coordinate(24);
        auto chunk = city_chunk(w, riverX - 72, bridgeZ - 72);
        int deckVertices = 0;
        for (auto &v : chunk[Road].vertices)
        {
            Vec2 p{v.position.x, v.position.z};
            if (std::abs(p.x - riverX) < 6 && std::abs(p.z - bridgeZ) < 7.4 && v.position.y > 0)
            {
                ++deckVertices;
                check(std::abs(v.position.y - w.terrain.height(p)) < 1e-4,
                      "rendered bridge matches support height");
            }
        }
        check(deckVertices > 0, "mesh contains visible bridge deck");
        for (auto &v : chunk[Pavement].vertices)
        {
            Vec2 p{v.position.x, v.position.z};
            if (std::abs(p.x - riverX) < 6 && std::abs(std::abs(p.z - bridgeZ) - 10.55) < 1e-4)
                check(v.position.y > 3, "deck edge never stretches down to river bed");
        }
        for (size_t i = 0; i < chunk[Paint].vertices.size(); i += 4)
        {
            Vec2 centre{};
            for (int j = 0; j < 4; ++j)
            {
                auto p = chunk[Paint].vertices[i + j].position;
                centre += Vec2{p.x, p.z} * .25;
            }
            check(w.layout.roadDistance(centre) < w.layout.roadHalf(), "no paint outside street");
            check(w.terrain.height(centre) > CityLayout::waterLevel, "no paint on water");
        }
        // Signals have a clearance interval with both axes red.
        for (double t = 0; t < 28; t += .1)
            check(!(CityLayout::green(3, 5, 0, t) && CityLayout::green(3, 5, 1, t)),
                  "cross traffic never both green");

        distantQueueAndSave();

        // Keep one car in physical simulation for four simulated minutes.
        // This catches steering/endpoint failures which path geometry checks miss.
        w.cars.resize(1);
        w.people.clear();
        w.pickups.clear();
        w.controlledCar = -1;
        auto &ai = w.cars[0];
        ai.model = 0;
        ai.body.v = {};
        ai.routeFrom = 1200;
        ai.routeTo = w.neighbours(1200)[0];
        ai.routePhase = 1;
        w.new_route(ai);
        auto initial = ai.route->sample(0);
        ai.body.p = initial.first;
        ai.body.angle = -initial.second;
        int transitions = 0;
        double maxError = 0, distance = 0;
        for (int step = 0; step < 240 * 120; ++step)
        {
            w.player.p = ai.body.p + Vec2{30, 30};
            int oldNode = ai.routeFrom, oldPhase = ai.routePhase;
            auto before = ai.body.p;
            w.step({}, 1. / 120);
            w.sounds.clear();
            if (ai.routeFrom != oldNode || ai.routePhase != oldPhase)
                ++transitions;
            distance += (ai.body.p - before).length();
            maxError = std::max(
                maxError, ai.routeLevel
                              ? std::abs(ai.body.p.z - w.levelById(ai.routeLevel)->centre_z(ai.body.p.x))
                              : w.layout.roadDistance(ai.body.p));
            check(std::isfinite(ai.body.p.x) && std::isfinite(ai.body.p.z), "finite local traffic");
        }
        std::println("Local traffic: transitions={} distance={} maxRoadError={}", transitions, distance,
                     maxError);
        check(!ai.destroyed, "autonomous drive survives normal route");
        check(transitions > 30 && distance > 500,
              "local car continues through junctions instead of circling endpoint");
        check(maxError < w.layout.roadHalf(), "local car stays on carriageway");
        laneChecks(w);
        burningDrivers();
        std::println("{} traffic/geometry checks passed", checks);
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "Traffic check {}: {}", checks, e.what());
        return 1;
    }
}
