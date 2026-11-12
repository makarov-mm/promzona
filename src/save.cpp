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
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(CivilianCover, hide, peek, threat, vehicleAnchor, searchAt, holdUntil,
                                   vehicle, found, peeking)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Scooter, body, owner, rider, returnAt)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Animal, species, variant, flock, p, goal, velocity, offset, altitude,
                                   heading, distance, fear, thinkAt, deadUntil)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Flock, landing, flying, landedAt)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(Rocket, p, direction, height, slope, travel, ownerCar)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(ReconDrone, active, position, velocity, altitude, heading, pitch, battery,
                                   health, scanTime, readyAt, nextShot, targets, scanned)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(World::StreetHail, actor, until, spot, posted)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(PeaceRoute, sites, completed, enabled, noise, soundAt, interactAt,
                                   previous)
void to_json(json &j, const PassengerRide &ride)
{
    j = {{"car", ride.car},
         {"line", ride.line},
         {"boardBy", ride.boardBy},
         {"away", ride.away},
         {"healthAtLastScare", ride.healthAtLastScare},
         {"crimeAt", ride.crimeAt},
         {"scares", ride.scares}};
}
void from_json(const json &j, PassengerRide &ride)
{
    ride = {j.at("car").get<int>(),     j.at("line").get<int>(),          j.at("boardBy").get<double>(),
            j.at("away").get<double>(), j.value("healthAtLastScare", 1.), j.value("crimeAt", -1.),
            j.value("scares", 0)};
}
void to_json(json &j, const Job &v)
{
    j = {{"kind", v.kind},         {"actor", v.actor},       {"destination", v.destination}, {"fare", v.fare},
         {"acquired", v.acquired}, {"deadline", v.deadline}, {"stopped", v.stopped}};
    if (v.series)
        j["series"] = true;
    if (v.passenger())
        j["ride"] = v.ride;
    if (v.peace())
        j["route"] = v.route;
}
void from_json(const json &j, Job &v)
{
    v = {j.at("kind").get<JobKind>(),  j.at("actor").get<int>(),     j.at("destination").get<int>(),
         j.at("fare").get<int>(),      j.at("acquired").get<bool>(), j.at("deadline").get<double>(),
         j.at("stopped").get<double>()};
    v.series = j.value("series", false);
    if (v.passenger())
        j.at("ride").get_to(v.ride);
    if (v.peace())
        j.at("route").get_to(v.route);
}
void to_json(json &j, const Pickup &p)
{
    j = {{"p", p.p},           {"weapon", p.weapon},   {"health", p.health},  {"wait", p.wait},
         {"energy", p.energy}, {"dropped", p.dropped}, {"level", p.roadLevel}};
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
    if (c.recovering || c.rollRate || c.pitchRate || c.overturnedTime)
        j["attitude"] = {c.rollRate,     c.pitchRate,    c.overturnedTime,
                         c.rightingTime, c.rightingRoll, c.rightingPitch};
    WRITE(braking);
    WRITE(wreckSupported);
    WRITE(health);
    WRITE(fireTime);
    WRITE(lastDamageAt);
    WRITE(safeDrivingTime);
    WRITE(impactCooldown);
    WRITE(responding);
    WRITE(summoned);
    WRITE(personal);
    WRITE(driverless);
    WRITE(paint);
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
    WRITE(laneIndex);
    WRITE(blockedSince);
    WRITE(nextLaneDecision);
    WRITE(line);
    WRITE(along);
    WRITE(nextStop);
    WRITE(dwell);
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
    if (j.contains("attitude"))
    {
        auto a = j.at("attitude").get<std::array<double, 6>>();
        if (!std::ranges::all_of(a, [](double v) { return std::isfinite(v); }) || a[2] < 0 || a[2] > 4.1 ||
            a[3] < 0 || a[3] > 2.2)
            throw std::runtime_error("Invalid vehicle recovery state");
        c.rollRate = a[0];
        c.pitchRate = a[1];
        c.overturnedTime = a[2];
        c.rightingTime = a[3];
        c.rightingRoll = a[4];
        c.rightingPitch = a[5];
    }
    else if (c.recovering)
        c.beginRighting();
    READ(braking);
    READ(wreckSupported);
    READ(health);
    READ(fireTime);
    READ(lastDamageAt);
    READ(safeDrivingTime);
    READ(impactCooldown);
    c.personal = j.value("personal", false);
    c.driverless = j.value("driverless", false);
    c.paint = j.value("paint", 0);
    c.summoned = j.value("summoned", false);
    c.responding = j.value("responding", false);
    c.nextShot = j.value("nextShot", 0.);
    c.firePlayerCaused = j.value("firePlayerCaused", false);
    READ(routeDistance);
    READ(wheelTravel);
    c.turretYaw = j.value("turretYaw", 0.);
    c.turretPitch = j.value("turretPitch", 0.);
    READ(model);
    READ(id);
    READ(routeFrom);
    READ(routeTo);
    READ(previousNode);
    READ(routePhase);
    READ(routeLevel);
    c.laneIndex = j.value("laneIndex", 0);
    c.blockedSince = j.value("blockedSince", -1.);
    c.nextLaneDecision = j.value("nextLaneDecision", 0.);
    c.senseAt = 0;
    c.trafficLimit = 100;
    c.line = j.value("line", -1);
    c.along = j.value("along", 0.);
    c.nextStop = j.value("nextStop", 0);
    c.dwell = j.value("dwell", 0.);
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
void validateBody(const Body &b, bool generatedMass)
{
    if (std::max(std::abs(b.p.x), std::abs(b.p.z)) > World::landEdge + 100 || b.v.length() > 1000 ||
        std::abs(b.w) > 1000 || b.invMass < 0 || b.invI < 0 ||
        (!generatedMass && (b.invMass > 10 || b.invI > 100)) || b.halfW <= 0 || b.halfH <= 0 ||
        b.radius <= 0 || b.verticalExtent <= 0 || b.friction < 0 || b.restitution < 0 || b.sleepTimer < 0)
        throw std::runtime_error("Invalid body in save");
}
void writeSnapshot(const std::filesystem::path &path, const json &j)
{
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
    if (!out)
        throw std::runtime_error("Cannot close save");
    if (std::filesystem::exists(path))
    {
        // Some runtimes refuse to overwrite the previous backup despite the option.
        std::error_code ignored;
        std::filesystem::remove(backup, ignored);
        std::filesystem::copy_file(path, backup, std::filesystem::copy_options::overwrite_existing);
    }
    std::filesystem::rename(temporary, path);
}
} // namespace

// Independent versioned format. It never overwrites the managed game's saves.
std::function<void()> World::captureSave(const std::filesystem::path &path) const
{
    json j = {{"format", "promzona-cpp-7"},
              {"layoutRevision", layout.revision()},
              {"job", job},
              {"jobsCompleted", jobsCompleted},
              {"hailers", hailers},
              {"nextHail", nextHail},
              {"dayNightCycle", dayNightCycle},
              {"simulationSpeed", simulationSpeed},
              {"seriesStage", seriesStage},
              {"seriesCompleted", seriesCompleted},
              {"jobEarnings", jobEarnings},
              {"ownedGarage", ownedGarage},
              {"jobsFailed", jobsFailed},
              {"lastPay", lastPay},
              {"energySeconds", energySeconds},
              {"clinicCredit", clinicCredit},
              {"purchaseAt", purchaseAt},
              {"seed", seed},
              {"hour", clock.hour},
              {"cycleMinutes", clock.cycleMinutes},
              {"time", time},
              {"recoveryReadyAt", recoveryReadyAt},
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
    j["transitStopLayout"] = 1;
    auto &moved = j["streetFixtures"] = json::array();
    for (size_t i = 0; i < fixtures.size(); ++i)
    {
        const auto &f = fixtures[i];
        if (!f.displaced)
            continue;
        const auto &b = staticBodies.at(f.body);
        moved.push_back({{"fixture", i},
                         {"p", b.p},
                         {"v", b.v},
                         {"angle", b.angle},
                         {"w", b.w},
                         {"level", b.roadLevel},
                         {"vy", b.verticalSpeed},
                         {"lift", f.lift},
                         {"tilt", f.tilt},
                         {"spin", f.spin},
                         {"spray", f.sprayUntil},
                         {"sleeping", b.sleeping}});
    }
    j["streetLife"] = {
        {"scooters", scooters},           {"animals", animals},           {"flocks", flocks},
        {"playerScooter", playerScooter}, {"random", streetRandom.state}, {"clock", animalClock}};

    j["arrests"] = arrests;
    j["lastFine"] = lastFine;
    if (patrol.car >= 0)
        j["arrestPatrol"] = {{"car", patrol.car},
                             {"officer", patrol.officer},
                             {"stage", int(patrol.stage)},
                             {"hold", patrol.hold},
                             {"deadline", patrol.deadline}};
    j["rescues"] = json::array();
    for (const auto &r : rescues)
        if (r.car >= 0)
            j["rescues"].push_back({{"car", r.car},
                                    {"medic", r.medic},
                                    {"patient", r.patient},
                                    {"stage", int(r.stage)},
                                    {"destination", r.destination},
                                    {"deadline", r.deadline},
                                    {"treatment", r.treatment},
                                    {"braking", r.braking}});
    j["rescueClock"] = {nextRescueDispatch, casualtyCursor};
    if (transitPassenger())
        j["transitPassenger"] = {{"seat", playerTransitSeat}, {"requestedExit", transitExitRequested}};
    j["riders"] = json::array();
    // The low density is the population of every earlier save and is left unnamed.
    if (density != Density::low)
        j["density"] = int(density);
    for (size_t i = 0; i < people.size(); ++i)
    {
        const auto &p = people[i];
        if (p.inCompany || p.watchUntil > time || p.voiceAt > time || p.fleeUntil > time)
            j["people"][i]["social"] = {{"company", p.inCompany},     {"incident", p.incident},
                                        {"danger", p.danger},         {"fleeUntil", p.fleeUntil},
                                        {"watchUntil", p.watchUntil}, {"voiceAt", p.voiceAt}};
        if (p.uniform != Person::Uniform::none)
            j["people"][i]["uniform"] = int(p.uniform);
        // The corners of the walk and the kind of leg walked last.
        j["people"][i]["corner"] = {p.sx, p.sz, p.nextSx, p.nextSz, p.lastWalk};
        // A habit ends with its owner's life or freedom; stepRoles drops the rest.
        if (p.role != Person::Role::none && !p.dead && p.uniform == Person::Uniform::none)
            j["people"][i]["role"] = {double(int(p.role)), p.roleHome.x, p.roleHome.z};
        if (p.dead)
        {
            j["people"][i]["rescueAfter"] = p.rescueAfter;
            j["people"][i]["death"] = {p.diedAt, p.drowned};
        }
        if (!p.dead && (p.knockedUntil > time || p.injuredUntil > time))
            j["people"][i]["injury"] = {p.knockedUntil, p.injuredUntil, p.injuryHome};
        if (!p.dead && p.sidearm == WeaponId::none &&
            Person::civilianWeapon(seed, p.body.id) != WeaponId::none)
            j["people"][i]["disarmed"] = true;
        if (p.courier)
            j["people"][i]["delivery"] = {p.deliveryDistance, p.deliveryUntil};
        if (!p.dead && p.sidearm != WeaponId::none)
            j["people"][i]["defence"] = {{"weapon", p.sidearm},
                                         {"until", p.alertUntil},
                                         {"aim", p.aimSeconds},
                                         {"shot", p.shotAt},
                                         {"burst", p.burstLeft}};
        if (!p.dead && p.sidearm != WeaponId::none && p.cover.found)
            j["people"][i]["defence"]["cover"] = p.cover;
        if (p.waitingLine >= 0)
            j["people"][i]["platform"] = {p.waitingLine, p.waitingStop, p.waitUntil};
        if (p.riding >= 0 || p.boardAfter > time)
            j["riders"].push_back({{"person", i},
                                   {"car", p.riding},
                                   {"seat", p.seat},
                                   {"stop", p.alight},
                                   {"retry", p.boardAfter}});
    }
    for (auto &c : cars)
        j["cars"].push_back(carState(c));
    return [path, snapshot = std::move(j)] { writeSnapshot(path, snapshot); };
}

void World::save(const std::filesystem::path &path) const
{
    captureSave(path)();
}

void World::load(const std::filesystem::path &path)
{
    if (std::filesystem::file_size(path) > 16 * 1024 * 1024)
        throw std::runtime_error("Save exceeds size limit");
    std::ifstream in(path, std::ios::binary);
    auto j = json::parse(in);
    const bool legacy = j.at("format") == "promzona-cpp-5";
    const bool street = j.at("format") == "promzona-cpp-7";
    if (!legacy && !street && j.at("format") != "promzona-cpp-6")
        throw std::runtime_error("Unsupported save format");
    validateJson(j);
    // Saves made before the density setting hold the low population and no such field.
    const int population = j.value("density", 0);
    if (population < 0 || population > int(Density::full))
        throw std::runtime_error("Invalid saved density");
    World next;
    next.generate(j.at("seed").get<std::uint32_t>(), {}, j.value("layoutRevision", 0), Density(population));
    if (j.at("cars").size() < next.trafficPopulation ||
        j.at("cars").size() > next.trafficPopulation + maxResponseVehicles + maxSpawnVehicles ||
        j.at("people").size() != next.people.size())
        throw std::runtime_error("Invalid saved population");
    std::vector<Car> legacyFleet;
    if (!j.at("cars").front().contains("line"))
        for (const auto &c : next.cars)
            if (c.line >= 0)
                legacyFleet.push_back(c);
    next.cars.resize(j.at("cars").size());
    for (size_t i = 0; i < next.cars.size(); ++i)
        loadCar(j.at("cars")[i], next.cars[i]);
    next.ownedGarage = j.value("ownedGarage", -1);
    if (next.ownedGarage < -1 || next.ownedGarage >= int(next.sites.size()) ||
        (next.ownedGarage >= 0 && next.sites[next.ownedGarage].kind != ServiceKind::garage) ||
        std::ranges::count(next.cars, true, &Car::personal) > (next.ownedGarage < 0 ? 0 : garageCapacity))
        throw std::runtime_error("Invalid saved garage");
    for (const auto &car : next.cars)
        if (car.paint < 0 || car.paint > 12 || (car.personal && (car.kind > 1 || car.destroyed)))
            throw std::runtime_error("Invalid saved personal vehicle or paint");
    auto summoned = size_t(std::ranges::count(next.cars, true, &Car::summoned));
    if (summoned > maxSpawnVehicles ||
        next.cars.size() - summoned > next.trafficPopulation + maxResponseVehicles)
        throw std::runtime_error("Invalid vehicle pool counts");
    j.at("people").get_to(next.people);
    auto savedPickups = j.at("pickups").get<std::vector<Pickup>>();
    if (legacy)
        for (const auto &p : next.pickups)
            if (p.energy)
                savedPickups.push_back(p);
    next.pickups = std::move(savedPickups);
    j.at("particles").get_to(next.particles);
#define READ(name) j.at(#name).get_to(next.name)
    if (!legacy)
    {
        READ(job);
        READ(jobsCompleted);
        READ(jobsFailed);
        READ(lastPay);
        if (j.contains("hailers"))
        {
            if (j.at("hailers").size() != next.hailers.size())
                throw std::runtime_error("Invalid street hail count");
            j.at("hailers").get_to(next.hailers);
            std::set<int> waiting;
            for (const auto &h : next.hailers)
                if (h.actor < -1 || h.actor >= int(next.people.size()) || h.until < 0 ||
                    (h.actor >= 0 && !waiting.insert(h.actor).second))
                    throw std::runtime_error("Invalid street hailer");
            next.nextHail = j.value("nextHail", 0.);
        }
        next.seriesStage = j.value("seriesStage", 0);
        next.seriesCompleted = j.value("seriesCompleted", 0);
        next.jobEarnings = j.value("jobEarnings", std::int64_t{0});
        if (next.seriesStage < 0 || next.seriesStage > 2 || next.seriesCompleted < 0 ||
            std::int64_t(next.seriesCompleted) * 3 + next.seriesStage > next.jobsCompleted ||
            next.jobEarnings < 0 || next.jobEarnings > std::int64_t(next.jobsCompleted) * 100000)
            throw std::runtime_error("Invalid saved contract career");
        if (next.job.passenger() && next.job.acquired && next.job.ride.car >= 0 &&
            next.job.ride.car < int(next.cars.size()) &&
            !j.at("job").at("ride").contains("healthAtLastScare"))
            next.job.ride.healthAtLastScare = next.cars[next.job.ride.car].health;
        READ(energySeconds);
        READ(clinicCredit);
        READ(purchaseAt);
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
    next.recoveryReadyAt = j.value("recoveryReadyAt", 0.);
    if (!(next.recoveryReadyAt >= 0 && next.recoveryReadyAt <= next.time + 8.01))
        throw std::runtime_error("Invalid vehicle recovery cooldown");
    next.dayNightCycle = j.value("dayNightCycle", true);
    next.simulationSpeed = j.value("simulationSpeed", 1.);
    if (!(next.simulationSpeed >= .1 && next.simulationSpeed <= 3))
        throw std::runtime_error("Invalid simulation speed");
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
        next.health > 1 || next.money < 0 || next.money > 999999999 || next.wanted < 0 || next.wanted > 6 ||
        next.time < 0 || next.rain < 0 || next.rain > 1 || next.rainTarget < 0 || next.rainTarget > 1 ||
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
    auto body = [&](const Body &b, bool generatedMass = false)
    {
        validateBody(b, generatedMass);
        if (b.roadLevel != 0 && !next.levelById(b.roadLevel))
            throw std::runtime_error("Invalid support level");
        if (!ids.insert(b.id).second)
            throw std::runtime_error("Duplicate body identity");
    };
    if (j.contains("streetFixtures"))
    {
        const auto &entries = j.at("streetFixtures");
        if (!entries.is_array() || entries.size() > next.fixtures.size())
            throw std::runtime_error("Invalid street furniture list");
        for (const auto &entry : entries)
        {
            auto &f = next.fixtures.at(entry.at("fixture").get<size_t>());
            if (f.displaced)
                throw std::runtime_error("Duplicate street fixture");
            auto &b = next.staticBodies[f.body];
            entry.at("p").get_to(b.p);
            entry.at("v").get_to(b.v);
            entry.at("angle").get_to(b.angle);
            entry.at("w").get_to(b.w);
            entry.at("level").get_to(b.roadLevel);
            entry.at("vy").get_to(b.verticalSpeed);
            entry.at("sleeping").get_to(b.sleeping);
            entry.at("lift").get_to(f.lift);
            entry.at("tilt").get_to(f.tilt);
            entry.at("spin").get_to(f.spin);
            entry.at("spray").get_to(f.sprayUntil);
            if (f.lift < 0 || f.lift > 200 || std::abs(f.spin) > 100 || std::abs(b.verticalSpeed) > 200 ||
                std::abs(f.tilt) > pi + .001 || f.sprayUntil < 0 || f.sprayUntil > next.time + 16.001 ||
                (f.sprayUntil > 0 && f.kind != FixtureKind::hydrant) ||
                (b.roadLevel && !next.levelById(b.roadLevel)))
                throw std::runtime_error("Invalid street fixture state");
            f.displaced = true;
            next.displacedFixtures.insert(size_t(&f - next.fixtures.data()));
            next.supportFixture(f);
        }
    }
    body(next.player);
    // Scenery mass and inertia are regenerated from the seed even when its pose is restored.
    for (auto &b : next.staticBodies)
        body(b, true);
    for (auto &c : next.cars)
    {
        body(c.body);
        auto node = [](int n) { return n >= 0 && n < World::nodes * World::nodes; };
        if (!node(c.routeFrom) || !node(c.routeTo) || c.routePhase < 0 || c.routePhase > 2 || c.kind < 0 ||
            c.kind > 14 || c.mass <= 0 || c.mass > 100000 || c.wheelbase <= 0 ||
            c.model >= std::size(fleet) || c.nextShot < 0 || c.health > 1 || c.health < -.5 || c.lift < 0 ||
            c.fireTime < 0 || c.safeDrivingTime < 0 || c.impactCooldown < 0 || c.laneIndex < 0 ||
            c.laneIndex > 1 || (c.routePhase == 2 && c.laneIndex != 0) || c.nextLaneDecision < 0)
            throw std::runtime_error("Invalid saved vehicle");
        if (c.id != size_t(&c - next.cars.data()) || c.line < -1 || c.line >= int(next.lines.size()) ||
            ((c.kind == 9 || c.kind == 10) != (c.line >= 0)))
            throw std::runtime_error("Invalid transit vehicle identity");
        if (c.summoned &&
            (c.id < next.trafficPopulation || c.responding || (c.line >= 0 || c.kind == 9 || c.kind == 10)))
            throw std::runtime_error("Invalid spawned vehicle");
        if (c.line >= 0)
        {
            const auto &line = next.lines[c.line];
            if (c.kind != line.kind || c.model != unsigned(c.kind - 1) || c.along < 0 ||
                c.along >= line.track.length() || c.nextStop < 0 || c.nextStop >= int(line.stops.size()) ||
                c.dwell < 0 || c.dwell > 6)
                throw std::runtime_error("Invalid saved transit line position");
            if (!j.contains("transitStopLayout"))
            {
                // Older cities used fixed block midpoints, even on a steep foundation.
                // Keep vehicles in place. Rejoin the revised timetable from their position.
                auto nextStop = std::upper_bound(line.stops.begin(), line.stops.end(), c.along + .65);
                int upcoming = nextStop == line.stops.end() ? 0 : int(nextStop - line.stops.begin());
                int previous = (upcoming + int(line.stops.size()) - 1) % int(line.stops.size());
                if (std::abs(std::remainder(c.along - line.stops[previous], line.track.length())) > .65)
                    c.dwell = 0;
                c.nextStop = upcoming;
            }
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
        p.body.friction = 0;
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
        int uniform = entry.value("uniform", 0);
        if (uniform < 0 || uniform > int(Person::Uniform::army))
            throw std::runtime_error("Invalid crew uniform");
        p.uniform = Person::Uniform(uniform);
        if (entry.contains("corner"))
        {
            const auto walk = entry.at("corner").get<std::array<int, 5>>();
            if (std::abs(walk[0]) != 1 || std::abs(walk[1]) != 1 || std::abs(walk[2]) != 1 ||
                std::abs(walk[3]) != 1 || walk[4] < -1 || walk[4] > 3)
                throw std::runtime_error("Invalid pedestrian corner");
            p.sx = walk[0];
            p.sz = walk[1];
            p.nextSx = walk[2];
            p.nextSz = walk[3];
            p.lastWalk = walk[4];
        }
        else
            // Saved when everybody walked the one pavement north and east of a street: the
            // walk goes on from the nearest corner.
            next.anchorWalk(p);
        if (entry.contains("role"))
        {
            const auto habit = entry.at("role").get<std::array<double, 3>>();
            p.role = Person::Role(int(habit[0]));
            p.roleHome = {habit[1], habit[2]};
            if (habit[0] != std::floor(habit[0]) || habit[0] < 1 || habit[0] > int(Person::Role::vagrant) ||
                !(std::max(std::abs(p.roleHome.x), std::abs(p.roleHome.z)) <= landEdge) || p.dead ||
                p.uniform != Person::Uniform::none)
                throw std::runtime_error("Invalid pedestrian habit");
        }
        if (entry.contains("platform"))
        {
            const auto &queue = entry.at("platform");
            p.waitingLine = queue.at(0);
            p.waitingStop = queue.at(1);
            p.waitUntil = queue.at(2);
            if (p.waitingLine < 0 || p.waitingLine >= int(next.lines.size()) || p.waitingStop < 0 ||
                p.waitingStop >= int(next.lines[p.waitingLine].stops.size()) || p.dead || p.waitUntil < 0)
                throw std::runtime_error("Invalid waiting passenger");
            p.platformTarget = next.lines[p.waitingLine].curb(p.waitingStop);
        }
        p.rescueAfter = entry.value("rescueAfter", 0.);
        if (p.rescueAfter < 0)
            throw std::runtime_error("Invalid rescue delay");
        // Saves made before bodies were cleared away start the wait at load time.
        p.diedAt = next.time;
        p.drowned = false;
        if (entry.contains("death"))
        {
            const auto &death = entry.at("death");
            p.diedAt = death.at(0);
            p.drowned = death.at(1);
            if (!p.dead || !std::isfinite(p.diedAt) || p.diedAt > next.time + 1)
                throw std::runtime_error("Invalid saved death");
        }
        if (entry.contains("injury"))
        {
            const auto &injury = entry.at("injury");
            p.knockedUntil = injury.at(0);
            p.injuredUntil = injury.at(1);
            p.injuryHome = injury.at(2).get<Vec2>();
            if (p.dead || !std::isfinite(p.knockedUntil) || !std::isfinite(p.injuredUntil) ||
                p.knockedUntil < 0 || p.injuredUntil < p.knockedUntil || !std::isfinite(p.injuryHome.x) ||
                !std::isfinite(p.injuryHome.z))
                throw std::runtime_error("Invalid saved injury");
        }
        if (entry.value("disarmed", false))
            p.sidearm = WeaponId::none;
        if (!entry.contains("defence"))
            continue;
        const auto &defence = entry.at("defence");
        p.sidearm = defence.at("weapon").get<WeaponId>();
        p.alertUntil = defence.at("until");
        p.aimSeconds = defence.at("aim");
        p.shotAt = defence.at("shot");
        p.burstLeft = defence.at("burst");
        if ((p.sidearm != WeaponId::pistol && p.sidearm != WeaponId::automatic) || p.dead ||
            p.alertUntil < 0 || p.aimSeconds < 0 || p.shotAt < 0 || p.burstLeft < 0 ||
            p.burstLeft >= weapon(p.sidearm)->npcBurst)
            throw std::runtime_error("Invalid saved civilian weapon state");
        if (defence.contains("cover"))
        {
            const auto cover = defence.at("cover").get<CivilianCover>();
            auto onMap = [](Vec2 at) { return std::max(std::abs(at.x), std::abs(at.z)) <= landEdge + 100; };
            if (!cover.found || cover.searchAt < 0 || cover.holdUntil < 0 || cover.vehicle < -1 ||
                cover.vehicle >= int(next.cars.size()) || !onMap(cover.hide) || !onMap(cover.peek) ||
                !onMap(cover.threat) || !onMap(cover.vehicleAnchor))
                throw std::runtime_error("Invalid saved civilian cover");
            p.cover = cover;
        }
    }
    next.rescues = {};
    size_t rescueCount = 0;
    for (const auto &row : j.value("rescues", json::array()))
    {
        int car = row.at("car"), medic = row.at("medic"), patient = row.at("patient"),
            stage = row.at("stage");
        int goal = row.at("destination");
        if (rescueCount >= next.rescues.size() || car < 0 || car >= int(next.cars.size()) ||
            next.cars[car].kind != 4 || next.ambulanceCall(car) || medic < 0 ||
            medic >= int(next.people.size()) || next.paramedicCall(medic) || stage < 0 || stage > 3 ||
            (stage == 0) != (patient == -1) || patient < -1 || patient >= int(next.people.size()) ||
            patient == medic || goal < 0 || goal >= nodes * nodes ||
            (patient >= 0 &&
             std::ranges::any_of(next.rescues, [&](const Rescue &r) { return r.patient == patient; })))
            throw std::runtime_error("Invalid ambulance assignment");
        auto &r = next.rescues[rescueCount++];
        r.car = car;
        r.medic = medic;
        r.patient = patient;
        r.stage = RescueStage(stage);
        r.destination = goal;
        r.deadline = row.at("deadline");
        r.treatment = row.at("treatment");
        r.braking = row.at("braking");
        if (r.deadline < 0 || r.treatment < 0)
            throw std::runtime_error("Invalid medical response time");
        if (r.stage == RescueStage::driving)
            next.routeAmbulance(r);
    }
    if (j.contains("rescueClock"))
    {
        next.nextRescueDispatch = j.at("rescueClock").at(0);
        next.casualtyCursor = j.at("rescueClock").at(1);
        if (next.nextRescueDispatch < 0 || next.casualtyCursor >= next.people.size())
            throw std::runtime_error("Invalid ambulance dispatch queue");
    }
    next.arrests = j.value("arrests", 0);
    next.lastFine = j.value("lastFine", 0);
    if (next.arrests < 0 || next.lastFine < 0 || next.lastFine > 220)
        throw std::runtime_error("Invalid arrest history");
    if (j.contains("arrestPatrol"))
    {
        auto &a = next.patrol;
        const auto &row = j.at("arrestPatrol");
        a.car = row.at("car");
        a.officer = row.at("officer");
        int stage = row.at("stage");
        a.hold = row.at("hold");
        a.deadline = row.at("deadline");
        if (a.car < 0 || a.car >= int(next.cars.size()) || a.officer < -1 ||
            a.officer >= int(next.people.size()) || stage < 0 || stage > int(PatrolStage::seated) ||
            (stage != int(PatrolStage::stopping) && a.officer < 0) || a.hold < 0 || a.hold > 2.53 ||
            a.deadline < 0 || (a.officer >= 0 && next.paramedicCall(a.officer)))
            throw std::runtime_error("Invalid arrest patrol");
        const auto kind = next.cars[a.car].kind;
        if (kind != 3 && kind != 6 && kind != 7)
            throw std::runtime_error("Invalid arrest vehicle");
        a.stage = PatrolStage(stage);
    }
    std::set<size_t> riders;
    std::set<std::pair<int, int>> seats;
    if (j.contains("transitPassenger"))
    {
        const auto &ride = j.at("transitPassenger");
        next.playerTransitSeat = ride.at("seat");
        next.transitExitRequested = ride.at("requestedExit");
        if (next.controlledCar < 0 || next.cars[next.controlledCar].line < 0 || next.playerTransitSeat < 0 ||
            next.playerTransitSeat >= 8)
            throw std::runtime_error("Invalid player transit seat");
        seats.emplace(next.controlledCar, next.playerTransitSeat);
    }
    for (const auto &row : j.value("riders", json::array()))
    {
        size_t index = row.at("person");
        int car = row.at("car"), seat = row.at("seat"), stop = row.at("stop");
        double retry = row.at("retry");
        if (index >= next.people.size() || !riders.insert(index).second || retry < 0 || car < -1 ||
            car >= int(next.cars.size()))
            throw std::runtime_error("Invalid saved rider");
        auto &p = next.people[index];
        if (car >= 0)
        {
            const auto &c = next.cars[car];
            bool hired = next.job.passenger() && next.job.acquired && next.job.actor == int(index) &&
                         car == next.job.ride.car && c.line < 0 && seat == 1 && stop == -1;
            bool transit = c.line >= 0 && seat >= 0 && seat < 8 && stop >= 0 &&
                           stop < int(next.lines[c.line].stops.size());
            const auto *crew = next.paramedicCall(int(index));
            bool medical = crew && crew->car == car && seat == 0 && stop == -1 &&
                           (crew->stage == RescueStage::standby || crew->stage == RescueStage::driving);
            bool officer =
                next.patrol.officer == int(index) && next.patrol.car == car && seat == 0 && stop == -1 &&
                (next.patrol.stage == PatrolStage::seated || next.patrol.stage == PatrolStage::stopping);
            if ((!hired && !transit && !medical && !officer) || p.dead || p.waitingLine >= 0 ||
                !seats.emplace(car, seat).second)
                throw std::runtime_error("Invalid passenger seat or destination");
        }
        p.riding = car;
        p.seat = seat;
        p.alight = stop;
        p.boardAfter = retry;
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
        if (int(pickup.weapon) < 0 || int(pickup.weapon) > 4 || pickup.wait < 0 ||
            (pickup.roadLevel && !next.levelById(pickup.roadLevel)))
            throw std::runtime_error("Invalid saved pickup");
    // Introduce public service on old saves without relocating a nearby, occupied or mission vehicle.
    for (const auto &unit : legacyFleet)
    {
        auto &c = next.cars[unit.id];
        if (c.destroyed || c.fireTime > 0 || c.responding || int(c.id) == next.controlledCar ||
            ((next.job.kind == JobKind::vehicle || next.job.kind == JobKind::broadcast) &&
             next.job.actor == int(c.id)) ||
            (next.position() - c.body.p).length2() < 100 * 100 ||
            (next.position() - unit.body.p).length2() < 100 * 100)
            continue;
        c = unit;
    }
    if (j.contains("drone"))
    {
        j.at("drone").get_to(next.drone);
        auto &d = next.drone;
        if (d.scanned < 0 || d.scanned > 3 || d.scanTime < 0 || d.scanTime >= 2 || d.battery < 0 ||
            d.battery > 100 || d.health > 1 || d.readyAt < 0 || d.nextShot < 0)
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
    if (!street)
    {
        next.generateStreetLife();
        next.formCompanies(true);
    }
    else
    {
        const auto &life = j.at("streetLife");
        auto &vehicles = life.at("scooters");
        if (vehicles.size() > next.people.size() || life.at("animals").size() != next.animals.size() ||
            life.at("flocks").size() != next.flocks.size())
            throw std::runtime_error("Invalid street population");
        life.at("playerScooter").get_to(next.playerScooter);
        life.at("random").get_to(next.streetRandom.state);
        life.at("clock").get_to(next.animalClock);
        if (next.playerScooter < -1 || next.playerScooter >= int(vehicles.size()) ||
            (next.playerScooter >= 0 && (next.controlledCar >= 0 || next.drone.active)) ||
            !next.streetRandom.state || next.animalClock < 0 || next.animalClock > .101)
            throw std::runtime_error("Invalid player scooter or wildlife clock");
        for (size_t i = 0; i < next.people.size(); ++i)
        {
            auto &p = next.people[i];
            p.ownedScooter = -1;
            p.inCompany = false;
            p.watchUntil = p.voiceAt = p.fleeUntil = 0;
            const auto &entry = j.at("people").at(i);
            if (entry.contains("social"))
            {
                auto &social = entry.at("social");
                social.at("company").get_to(p.inCompany);
                social.at("incident").get_to(p.incident);
                social.at("watchUntil").get_to(p.watchUntil);
                social.at("voiceAt").get_to(p.voiceAt);
                social.at("danger").get_to(p.danger);
                social.at("fleeUntil").get_to(p.fleeUntil);
                if (p.watchUntil < 0 || p.voiceAt < 0 || p.fleeUntil < 0 ||
                    std::max(std::abs(p.danger.x), std::abs(p.danger.z)) > landEdge + 100 ||
                    (p.inCompany && (i % 28 >= 3 || p.riding >= 0 || entry.contains("delivery"))) ||
                    std::max(std::abs(p.incident.x), std::abs(p.incident.z)) > landEdge + 100)
                    throw std::runtime_error("Invalid pedestrian company or incident");
            }
            p.courier = entry.contains("delivery");
            if (p.courier)
            {
                const auto delivery = entry.at("delivery").get<std::array<double, 2>>();
                p.deliveryDistance = delivery[0];
                p.deliveryUntil = delivery[1];
                if (p.deliveryDistance < 0 || p.deliveryUntil < 0 || p.riding >= 0)
                    throw std::runtime_error("Invalid courier state");
            }
        }
        vehicles.get_to(next.scooters);
        for (size_t i = 0; i < next.scooters.size(); ++i)
        {
            auto &s = next.scooters[i];
            body(s.body);
            if (s.owner < 0 || s.owner >= int(next.people.size()) || s.body.id != 0x71000000u + i ||
                s.returnAt < 0 ||
                (s.rider != s.owner && s.rider != Scooter::free && s.rider != Scooter::playerRider) ||
                (s.rider == Scooter::playerRider) != (int(i) == next.playerScooter))
                throw std::runtime_error("Invalid scooter or rider identity");
            auto &owner = next.people[s.owner];
            if (owner.inCompany || owner.ownedScooter >= 0 || owner.riding >= 0 ||
                (owner.dead && s.rider == s.owner))
                throw std::runtime_error("Conflicting scooter ownership");
            owner.ownedScooter = int(i);
        }
        auto bounded = [](Vec2 p) { return std::max(std::abs(p.x), std::abs(p.z)) <= landEdge + 25; };
        life.at("flocks").get_to(next.flocks);
        for (const auto &f : next.flocks)
            if (!bounded(f.landing) || f.landedAt < 0)
                throw std::runtime_error("Invalid flock destination");
        for (size_t i = 0; i < next.animals.size(); ++i)
        {
            auto a = life.at("animals").at(i).get<Animal>();
            const auto &original = next.animals[i];
            if (a.species != original.species || a.flock != original.flock ||
                !(a.species == Species::cat   ? a.variant >= 0 && a.variant <= 1
                  : a.species == Species::dog ? a.variant >= 2 && a.variant <= 3
                                              : a.variant == 4) ||
                !bounded(a.p) || !bounded(a.goal) || a.offset.length2() > 25 || a.distance < 0 ||
                a.velocity.length2() > 100 || a.fear < 0 || a.deadUntil < 0)
                throw std::runtime_error("Invalid saved animal");
            next.animals[i] = a;
        }
    }
    if (next.patrol.officer >= 0)
    {
        const auto &p = next.people[next.patrol.officer];
        bool riding = next.patrol.stage == PatrolStage::seated || next.patrol.stage == PatrolStage::stopping;
        if (p.ownedScooter >= 0 || p.inCompany || p.courier ||
            (!p.dead && p.riding != (riding ? next.patrol.car : -1)))
            throw std::runtime_error("Conflicting arrest officer assignment");
    }
    for (const auto &r : next.rescues)
    {
        if (r.car < 0)
            continue;
        const auto &p = next.people[r.medic];
        const bool inCab = r.stage == RescueStage::standby || r.stage == RescueStage::driving;
        if (p.ownedScooter >= 0 || p.inCompany || p.courier || (!p.dead && p.riding != (inCab ? r.car : -1)))
            throw std::runtime_error("Conflicting paramedic assignment");
    }
    // Contact warm-start impulses and renderer caches are transient and rebuild after loading.
    *this = std::move(next);
}
} // namespace pz
