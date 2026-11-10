#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include "json.hpp"
#include <fstream>
#include <set>

namespace pz
{
using nlohmann::json;
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Vec2, x, z)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Vec3, x, y, z)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Body, p, v, angle, w, invMass, invI, halfW, halfH, radius, friction,
                                   restitution, height, verticalExtent, circle, sleeping, sleepTimer, id,
                                   roadLevel, verticalSpeed)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Person, body, target, node, next, dead, fear, heading, phase, distance,
                                   style, health, burnUntil, burnPlayerCaused)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Rocket, p, direction, height, slope, travel, ownerCar)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(ReconDrone, active, position, velocity, altitude, heading, pitch, battery,
                                   health, scanTime, readyAt, nextShot, targets, scanned)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(PeaceRoute, sites, completed, enabled, noise, soundAt, interactAt, previous)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(PassengerRide, car, line, boardBy, away)
void to_json(json &j, const Job &v)
{
    j = {{"kind", v.kind}, {"actor", v.actor}, {"destination", v.destination}, {"fare", v.fare},
         {"acquired", v.acquired}, {"deadline", v.deadline}, {"stopped", v.stopped}};
    if (v.passenger()) j["ride"] = v.ride;
    if (v.peace()) j["route"] = v.route;
}
void from_json(const json &j, Job &v)
{
    v = {j.at("kind").get<JobKind>(), j.at("actor").get<int>(), j.at("destination").get<int>(),
         j.at("fare").get<int>(), j.at("acquired").get<bool>(), j.at("deadline").get<double>(),
         j.at("stopped").get<double>()};
    if (v.passenger()) j.at("ride").get_to(v.ride);
    if (v.peace()) j.at("route").get_to(v.route);
}
void to_json(json &j, const Pickup &p)
{
    j = {{"p", p.p}, {"weapon", p.weapon}, {"health", p.health}, {"wait", p.wait}, {"energy", p.energy}, {"dropped",p.dropped}, {"level",p.roadLevel}};
}
void from_json(const json &j, Pickup &p)
{
    j.at("p").get_to(p.p);
    j.at("weapon").get_to(p.weapon);
    j.at("health").get_to(p.health);
    j.at("wait").get_to(p.wait);
    p.energy = j.value("energy", false);
    p.dropped = j.value("dropped", false);
    p.roadLevel = j.value("level", 0);
}
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
    WRITE(responding);
    WRITE(summoned);
    WRITE(nextShot);
    WRITE(firePlayerCaused);
    WRITE(routeDistance);
    WRITE(wheelTravel);
    WRITE(turretYaw);
    WRITE(turretPitch);
    WRITE(model);
    WRITE(id);
    WRITE(routeFrom);
    WRITE(routeTo);
    WRITE(previousNode);
    WRITE(routePhase);
    WRITE(routeLevel);
    WRITE(line); WRITE(along); WRITE(nextStop); WRITE(dwell);
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
    c.summoned = j.value("summoned", false);
    c.responding = j.value("responding", false);
    c.nextShot = j.value("nextShot", 0.);
    c.firePlayerCaused = j.value("firePlayerCaused", false);
    READ(routeDistance);
    READ(wheelTravel);
    c.turretYaw=j.value("turretYaw",0.);
    c.turretPitch=j.value("turretPitch",0.);
    READ(model);
    READ(id);
    READ(routeFrom);
    READ(routeTo);
    READ(previousNode);
    READ(routePhase);
    READ(routeLevel);
    c.line=j.value("line",-1); c.along=j.value("along",0.);
    c.nextStop=j.value("nextStop",0); c.dwell=j.value("dwell",0.);
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
    json j = {{"format", "promzona-cpp-6"}, {"job", job}, {"jobsCompleted", jobsCompleted},
              {"jobsFailed", jobsFailed}, {"lastPay", lastPay}, {"energySeconds", energySeconds},
              {"clinicCredit", clinicCredit}, {"purchaseAt", purchaseAt},
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
    j["drone"] = drone;
    j["riders"]=json::array();
    for(size_t i=0;i<people.size();++i)
    {
        const auto &p=people[i];
        if (!p.dead && p.sidearm != WeaponId::none)
            j["people"][i]["defence"] = {{"weapon",p.sidearm},{"until",p.alertUntil},
                {"aim",p.aimSeconds},{"shot",p.shotAt},{"burst",p.burstLeft}};
        if(p.riding>=0 || p.boardAfter>time)
            j["riders"].push_back({{"person",i},{"car",p.riding},{"seat",p.seat},{"stop",p.alight},{"retry",p.boardAfter}});
    }
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
    const bool legacy = j.at("format") == "promzona-cpp-5";
    if (!legacy && j.at("format") != "promzona-cpp-6")
        throw std::runtime_error("Unsupported save format");
    validateJson(j);
    World next;
    next.generate(j.at("seed").get<std::uint32_t>());
    if (j.at("cars").size() < trafficPopulation || j.at("cars").size() > trafficPopulation + maxResponseVehicles + maxSpawnVehicles || j.at("people").size() != next.people.size())
        throw std::runtime_error("Invalid saved population");
    std::vector<Car> legacyFleet;
    if (!j.at("cars").front().contains("line"))
        for(const auto &c:next.cars) if(c.line>=0) legacyFleet.push_back(c);
    next.cars.resize(j.at("cars").size());
    for (size_t i = 0; i < next.cars.size(); ++i)
        loadCar(j.at("cars")[i], next.cars[i]);
    auto summoned = size_t(std::ranges::count(next.cars, true, &Car::summoned));
    if (summoned > maxSpawnVehicles || next.cars.size() - summoned > trafficPopulation + maxResponseVehicles)
        throw std::runtime_error("Invalid vehicle pool counts");
    j.at("people").get_to(next.people);
    auto savedPickups = j.at("pickups").get<std::vector<Pickup>>();
    if (legacy)
        for (const auto &p : next.pickups)
            if (p.energy) savedPickups.push_back(p);
    next.pickups = std::move(savedPickups);
    j.at("particles").get_to(next.particles);
#define READ(name) j.at(#name).get_to(next.name)
    if (!legacy)
    {
        READ(job); READ(jobsCompleted); READ(jobsFailed); READ(lastPay);
        READ(energySeconds); READ(clinicCredit); READ(purchaseAt);
        if (!next.validJob() || next.jobsCompleted < 0 || next.jobsCompleted > 10000000 ||
            next.jobsFailed < 0 || next.jobsFailed > 10000000 || next.lastPay < 0 || next.lastPay > 100000 ||
            next.energySeconds < 0 || next.energySeconds > 25 || next.clinicCredit < 0 ||
            next.clinicCredit >= 1 || next.purchaseAt < 0)
            throw std::runtime_error("Invalid saved job or service state");
    }
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
        next.health > 1 || next.money < 0 || next.money > 999999999 || next.wanted < 0 || next.wanted > 6 || next.time < 0 ||
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
        if (b.roadLevel != 0 && !next.levelById(b.roadLevel))
            throw std::runtime_error("Invalid support level");
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
        if (!node(c.routeFrom) || !node(c.routeTo) || c.routePhase < 0 || c.routePhase > 2 || c.kind < 0 ||
            c.kind > 14 || c.mass <= 0 || c.mass > 100000 || c.wheelbase <= 0 || c.model >= std::size(fleet) || c.nextShot < 0 ||
            c.health > 1 || c.health < -.5 || c.lift < 0 || c.fireTime < 0 || c.safeDrivingTime < 0 ||
            c.impactCooldown < 0)
            throw std::runtime_error("Invalid saved vehicle");
        if (c.id != size_t(&c-next.cars.data()) || c.line < -1 || c.line >= int(next.lines.size()) ||
            ((c.kind==9 || c.kind==10) != (c.line>=0)))
            throw std::runtime_error("Invalid transit vehicle identity");
        if (c.summoned && (c.id < trafficPopulation || c.responding ||
            (c.kind != 11 && c.kind != 12 && c.kind != 14)))
            throw std::runtime_error("Invalid spawned vehicle");
        if(c.line>=0)
        {
            const auto &line=next.lines[c.line];
            if(c.kind!=line.kind || c.model!=unsigned(c.kind-1) || c.along<0 || c.along>=line.track.length() ||
               c.nextStop<0 || c.nextStop>=int(line.stops.size()) || c.dwell<0 || c.dwell>5)
                throw std::runtime_error("Invalid saved transit line position");
        }
        if (c.routeLevel != 0 && !next.levelById(c.routeLevel))
            throw std::runtime_error("Invalid route level");
        if (c.routePhase == 2 && (!next.levelEdge(c.routeFrom, c.routeTo) ||
                                  next.levelEdge(c.routeFrom, c.routeTo)->id != c.routeLevel))
            throw std::runtime_error("Invalid level route");
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
    for (size_t i = 0; i < next.people.size(); ++i)
    {
        auto &p = next.people[i];
        p.riding = -1;
        p.boardAfter = 0;
        p.sidearm = p.dead ? WeaponId::none : Person::civilianWeapon(next.seed, p.body.id);
        const auto &entry = j.at("people").at(i);
        if (!entry.contains("defence")) continue;
        const auto &defence = entry.at("defence");
        p.sidearm = defence.at("weapon").get<WeaponId>();
        p.alertUntil = defence.at("until");
        p.aimSeconds = defence.at("aim");
        p.shotAt = defence.at("shot");
        p.burstLeft = defence.at("burst");
        if ((p.sidearm != WeaponId::pistol && p.sidearm != WeaponId::automatic) ||
            p.dead || p.alertUntil < 0 || p.aimSeconds < 0 || p.shotAt < 0 ||
            p.burstLeft < 0 || p.burstLeft >= weapon(p.sidearm)->npcBurst)
            throw std::runtime_error("Invalid saved civilian weapon state");
    }
    std::set<size_t> riders;
    std::set<std::pair<int,int>> seats;
    for(const auto &row:j.value("riders",json::array()))
    {
        size_t index=row.at("person");
        int car=row.at("car"), seat=row.at("seat"), stop=row.at("stop");
        double retry=row.at("retry");
        if(index>=next.people.size() || !riders.insert(index).second || retry<0 || car < -1 || car>=int(next.cars.size()))
            throw std::runtime_error("Invalid saved rider");
        auto &p=next.people[index];
        if(car>=0)
        {
            const auto &c=next.cars[car];
            bool hired = next.job.passenger() && next.job.acquired && next.job.actor == int(index) &&
                         car == next.job.ride.car && c.line < 0 && seat == 1 && stop == -1;
            bool transit = c.line >= 0 && seat >= 0 && seat < 8 && stop >= 0 &&
                           stop < int(next.lines[c.line].stops.size());
            if ((!hired && !transit) || p.dead || !seats.emplace(car,seat).second)
                throw std::runtime_error("Invalid passenger seat or destination");
        }
        p.riding=car; p.seat=seat; p.alight=stop; p.boardAfter=retry;
    }
    if (next.job.passenger())
    {
        int riding = next.people[next.job.actor].riding;
        if (riding != (next.job.acquired ? next.job.ride.car : -1))
            throw std::runtime_error("Saved passenger does not match the active trip");
    }
    for (auto &r : next.rockets)
        if (r.ownerCar < -1 || r.ownerCar >= int(next.cars.size()) || r.travel < 0 ||
            r.travel > weapon(WeaponId::bazooka)->range || std::abs(r.direction.length() - 1) > 1e-6 ||
            std::abs(r.slope) > 4 || std::max(std::abs(r.p.x), std::abs(r.p.z)) > landEdge + 200)
            throw std::runtime_error("Invalid saved rocket");
    for (auto &pickup : next.pickups)
        if (int(pickup.weapon) < 0 || int(pickup.weapon) > 4 || pickup.wait < 0 || (pickup.roadLevel && !next.levelById(pickup.roadLevel)))
            throw std::runtime_error("Invalid saved pickup");
    // Introduce public service on old saves without relocating a nearby, occupied or mission vehicle.
    for (const auto &unit : legacyFleet)
    {
        auto &c=next.cars[unit.id];
        if (c.destroyed || c.fireTime>0 || c.responding || int(c.id)==next.controlledCar ||
            ((next.job.kind==JobKind::vehicle || next.job.kind==JobKind::broadcast) && next.job.actor==int(c.id)) ||
            (next.position()-c.body.p).length2()<100*100 || (next.position()-unit.body.p).length2()<100*100) continue;
        c=unit;
    }
    if (j.contains("drone"))
    {
        j.at("drone").get_to(next.drone);
        auto &d = next.drone;
        if (d.scanned < 0 || d.scanned > 3 || d.scanTime < 0 || d.scanTime >= 2 ||
            d.battery < 0 || d.battery > 100 || d.health > 1 || d.readyAt < 0 || d.nextShot < 0)
            throw std::runtime_error("Invalid saved reconnaissance progress");
        if (d.active)
        {
            if (next.controlledCar >= 0 || next.health <= 0 || d.health <= 0 || d.battery <= 0 ||
                d.scanned >= 3 || std::set<size_t>(d.targets.begin(), d.targets.end()).size() != 3 ||
                std::ranges::any_of(d.targets, [&](size_t i) { return i >= next.buildings.size(); }))
                throw std::runtime_error("Invalid saved reconnaissance flight");
            next.refreshDroneObstacles(d.position);
            if (!next.droneClear(d.position, d.altitude, .35))
                throw std::runtime_error("Invalid saved drone position");
        }
    }
    // Contact warm-start impulses and renderer caches are transient and rebuild after loading.
    *this = std::move(next);
}
} // namespace pz
