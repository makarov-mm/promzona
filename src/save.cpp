#include "promzona/world.hpp"
#include "json.hpp"
#include <fstream>
#include <set>

namespace pz
{
using nlohmann::json;
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Vec2, x, z)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Vec3, x, y, z)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Body, p, v, angle, w, invMass, invI, halfW, halfH, radius, friction,
                                   restitution, height, verticalExtent, circle, sleeping, sleepTimer, id)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Person, body, target, node, next, dead, fear, heading, phase, distance,
                                   style, health, burnUntil, burnPlayerCaused)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Rocket, p, direction, height, slope, travel, ownerCar)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Pickup, p, weapon, health, wait)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Particle, p, v, color, life, size)

namespace
{
json carState(const Car &c)
{
    json j;
#define WRITE(name) j[#name] = c.name
    WRITE(body);
    WRITE(kind);
    WRITE(mass);
    WRITE(wheelbase);
    WRITE(steering);
    WRITE(lift);
    WRITE(verticalVelocity);
    WRITE(roll);
    WRITE(pitch);
    WRITE(terrainPitch);
    WRITE(terrainRoll);
    WRITE(terrainGradient);
    WRITE(terrainActive);
    WRITE(falling);
    WRITE(destroyed);
    WRITE(recovering);
    WRITE(braking);
    WRITE(wreckSupported);
    WRITE(health);
    WRITE(fireTime);
    WRITE(lastDamageAt);
    WRITE(safeDrivingTime);
    WRITE(impactCooldown);
    WRITE(routeDistance);
    WRITE(wheelTravel);
    WRITE(model);
    WRITE(id);
    WRITE(routeFrom);
    WRITE(routeTo);
    WRITE(previousNode);
    WRITE(routePhase);
#undef WRITE
    j["route"] = c.route ? json(c.route->points()) : json(nullptr);
    return j;
}
void loadCar(const json &j, Car &c)
{
#define READ(name) j.at(#name).get_to(c.name)
    READ(body);
    READ(kind);
    READ(mass);
    READ(wheelbase);
    READ(steering);
    READ(lift);
    READ(verticalVelocity);
    READ(roll);
    READ(pitch);
    READ(terrainPitch);
    READ(terrainRoll);
    READ(terrainGradient);
    READ(terrainActive);
    READ(falling);
    READ(destroyed);
    READ(recovering);
    READ(braking);
    READ(wreckSupported);
    READ(health);
    READ(fireTime);
    READ(lastDamageAt);
    READ(safeDrivingTime);
    READ(impactCooldown);
    READ(routeDistance);
    READ(wheelTravel);
    READ(model);
    READ(id);
    READ(routeFrom);
    READ(routeTo);
    READ(previousNode);
    READ(routePhase);
#undef READ
    c.route.reset();
    if (!j.at("route").is_null())
        c.route.emplace(j.at("route").get<std::vector<Vec2>>());
}
void validateJson(const json &j, int depth = 0)
{
    if (depth > 20)
        throw std::runtime_error("Save nesting limit");
    if (j.is_number() && (!std::isfinite(j.get<double>()) || std::abs(j.get<double>()) > 1e12))
        throw std::runtime_error("Invalid numeric save value");
    if (j.is_array() && j.size() > 10000)
        throw std::runtime_error("Save collection limit");
    if (j.is_structured())
        for (auto &value : j)
            validateJson(value, depth + 1);
}
void validateBody(const Body &b)
{
    if (std::max(std::abs(b.p.x), std::abs(b.p.z)) > World::landEdge + 100 || b.v.length() > 1000 ||
        std::abs(b.w) > 1000 || b.invMass < 0 || b.invMass > 10 || b.invI < 0 || b.invI > 100 ||
        b.halfW <= 0 || b.halfH <= 0 || b.radius <= 0 || b.verticalExtent <= 0 || b.friction < 0 ||
        b.restitution < 0 || b.sleepTimer < 0)
        throw std::runtime_error("Invalid body in save");
}
} // namespace

// Independent versioned format. It never overwrites the managed game's saves.
void World::save(const std::filesystem::path &path) const
{
    json j = {{"format", "promzona-cpp-4"},
              {"seed", seed},
              {"hour", clock.hour},
              {"cycleMinutes", clock.cycleMinutes},
              {"time", time},
              {"health", health},
              {"money", money},
              {"wanted", wanted},
              {"wantedCooldown", wantedCooldown},
              {"shotCooldown", shotCooldown},
              {"controlledCar", controlledCar},
              {"player", player},
              {"playerDistance", playerDistance},
              {"ammo", inventory.capture()},
              {"owned", inventory.ownership()},
              {"selection", inventory.selection() ? int(*inventory.selection()) : -1},
              {"randomState", random.state},
              {"rain", rain},
              {"rainTarget", rainTarget},
              {"weatherClock", weatherClock},
              {"people", people},
              {"pickups", pickups},
              {"particles", particles},
              {"rockets", rockets},
              {"playerBurnUntil", playerBurnUntil},
              {"fireClock", fireClock},
              {"nextFlameSound", nextFlameSound}};
    for (auto &c : cars)
        j["cars"].push_back(carState(c));
    if (!path.parent_path().empty())
        std::filesystem::create_directories(path.parent_path());
    auto temporary = path;
    temporary += ".tmp";
    std::ofstream out(temporary, std::ios::binary | std::ios::trunc);
    if (!out)
        throw std::runtime_error("Cannot create save");
    out << j.dump();
    out.flush();
    if (!out)
        throw std::runtime_error("Cannot write save");
    out.close();
    auto backup = path;
    backup += ".bak";
    std::error_code ec;
    if (std::filesystem::exists(path))
    {
        std::filesystem::remove(backup, ec);
        std::filesystem::rename(path, backup);
    }
    try
    {
        std::filesystem::rename(temporary, path);
    }
    catch (...)
    {
        if (std::filesystem::exists(backup))
            std::filesystem::rename(backup, path);
        throw;
    }
}

void World::load(const std::filesystem::path &path)
{
    if (std::filesystem::file_size(path) > 16 * 1024 * 1024)
        throw std::runtime_error("Save exceeds size limit");
    std::ifstream in(path, std::ios::binary);
    auto j = json::parse(in);
    if (j.at("format") != "promzona-cpp-4")
        throw std::runtime_error("Unsupported save format");
    validateJson(j);
    World next;
    next.generate(j.at("seed").get<std::uint32_t>());
    if (j.at("cars").size() != next.cars.size() || j.at("people").size() != next.people.size())
        throw std::runtime_error("Invalid saved population");
    for (size_t i = 0; i < next.cars.size(); ++i)
        loadCar(j.at("cars")[i], next.cars[i]);
    j.at("people").get_to(next.people);
    j.at("pickups").get_to(next.pickups);
    j.at("particles").get_to(next.particles);
#define READ(name) j.at(#name).get_to(next.name)
    READ(controlledCar);
    READ(player);
    READ(playerDistance);
    READ(health);
    READ(money);
    READ(wanted);
    READ(time);
    READ(rain);
    READ(rainTarget);
    READ(weatherClock);
    READ(wantedCooldown);
    READ(shotCooldown);
    READ(rockets);
    READ(playerBurnUntil);
    READ(fireClock);
    READ(nextFlameSound);
#undef READ
    if (next.controlledCar < -1 || next.controlledCar >= int(next.cars.size()) || next.health < 0 ||
        next.health > 1 || next.money < 0 || next.wanted < 0 || next.wanted > 6 || next.time < 0 ||
        next.rain < 0 || next.rain > 1 || next.rainTarget < 0 || next.rainTarget > 1 ||
        next.pickups.size() > 5000 || next.particles.size() > 5000 || next.playerDistance < 0 ||
        next.rockets.size() > 16 || next.fireClock < 0 || next.playerBurnUntil < 0)
        throw std::runtime_error("Invalid save state");
    next.clock.setHour(j.at("hour"));
    next.clock.cycleMinutes = j.at("cycleMinutes");
    if (next.clock.cycleMinutes < 2 || next.clock.cycleMinutes > 30)
        throw std::runtime_error("Invalid solar cycle");
    next.inventory.restore(j.at("ammo").get<std::array<int, 5>>(), j.at("owned").get<std::array<bool, 5>>());
    int selection = j.at("selection");
    if (selection < -1 || selection > 4 || (selection >= 0 && !next.inventory.select(WeaponId(selection))))
        throw std::runtime_error("Invalid weapon selection");
    next.random.state = j.at("randomState");
    if (!next.random.state)
        throw std::runtime_error("Invalid random state");
    std::set<std::uint32_t> ids;
    auto body = [&](const Body &b)
    {
        validateBody(b);
        if (!ids.insert(b.id).second)
            throw std::runtime_error("Duplicate body identity");
    };
    body(next.player);
    for (auto &b : next.staticBodies)
        body(b);
    for (auto &c : next.cars)
    {
        body(c.body);
        auto node = [](int n) { return n >= 0 && n < World::nodes * World::nodes; };
        if (!node(c.routeFrom) || !node(c.routeTo) || c.routePhase < 0 || c.routePhase > 1 || c.kind < 0 ||
            c.kind > 11 || c.mass <= 0 || c.mass > 100000 || c.wheelbase <= 0 || c.model > 1000 ||
            c.health > 1 || c.health < -.5 || c.lift < 0 || c.fireTime < 0 || c.safeDrivingTime < 0 ||
            c.impactCooldown < 0)
            throw std::runtime_error("Invalid saved vehicle");
        if (c.route && c.route->points().size() > 512)
            throw std::runtime_error("Invalid saved route");
        auto connected = next.neighbours(c.routeFrom);
        if (std::ranges::find(connected, c.routeTo) == connected.end())
            throw std::runtime_error("Saved route crosses a missing street");
    }
    for (auto &p : next.people)
    {
        body(p.body);
        if (p.node < 0 || p.node >= nodes * nodes || p.next < 0 || p.next >= nodes * nodes ||
            p.distance < 0 || p.health < 0 || p.health > 1 || p.burnUntil < 0 || (p.dead && p.health != 0))
            throw std::runtime_error("Invalid saved pedestrian");
    }
    for (auto &r : next.rockets)
        if (r.ownerCar < -1 || r.ownerCar >= int(next.cars.size()) || r.travel < 0 ||
            r.travel > weapon(WeaponId::bazooka)->range || std::abs(r.direction.length() - 1) > 1e-6 ||
            std::abs(r.slope) > 4 || std::max(std::abs(r.p.x), std::abs(r.p.z)) > landEdge + 200)
            throw std::runtime_error("Invalid saved rocket");
    for (auto &pickup : next.pickups)
        if (int(pickup.weapon) < 0 || int(pickup.weapon) > 4 || pickup.wait < 0)
            throw std::runtime_error("Invalid saved pickup");
    // Contact warm-start impulses and renderer caches are transient and rebuild after loading.
    *this = std::move(next);
}
} // namespace pz
