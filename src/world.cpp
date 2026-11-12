#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include "json.hpp"
#include <fstream>
#include <bit>
#include <sstream>
#include <charconv>
#include <cctype>
#include <stdexcept>
namespace pz
{
void Clock::setHour(double h)
{
    if (!std::isfinite(h))
        return;
    hour = std::fmod(std::fmod(h, 24) + 24, 24);
    double lo = 0, hi = 24;
    for (int i = 0; i < 48; i++)
    {
        double m = (lo + hi) * .5;
        if (m + 1.2 * std::sin(m * pi / 6) < hour)
            lo = m;
        else
            hi = m;
    }
    phase_ = (lo + hi) * .5;
}
void Clock::step(double dt)
{
    phase_ = std::fmod(
        phase_ + dt * 24 / (60 * clamp(std::isfinite(cycleMinutes) ? cycleMinutes : 10., 2., 30.)), 24.);
    hour = phase_ + 1.2 * std::sin(phase_ * pi / 6);
}
// CityTerrain natural relief and two-stage foundation grading, including the canal branch.
void Terrain::generate(std::uint32_t seed, int nodes, std::span<const Building> buildings, bool wide,
                       const CityLayout *layout)
{
    nodes_ = nodes;
    wide_ = wide;
    rivers_ = nodes >= 49;
    bridges_.fill(false);
    for (int c = 0; c < 2; ++c)
        for (int j = 0; j < nodes; ++j)
            bridges_[c * 49 + j] = j % 6 == 0 && (!layout || layout->road((c + 1) * nodes / 3, j, 1));
    double edge = (nodes - 1) * 36 * .5 + 54;
    origin_ = -std::ceil(edge) - 2;
    size_ = int(-2 * origin_) + 1;
    heights_.resize(size_t(size_) * size_);
    double phase = seed % 10007 * .002;
    const bool revised = layout && layout->revision() == CityLayout::districtRevision;
    auto natural = [&](double x, double z)
    {
        if (rivers_)
        {
            double inland = edge - std::max(std::abs(x), std::abs(z));
            double h = 26 + 14 * std::sin(x * .0045 + phase) * std::cos(z * .004 - phase * .3) +
                       9 * std::sin(z * .003 + phase * .7);
            double shorelineOffset = 0;
            if (revised && inland > -6 && inland < 42)
            {
                const double band = smooth((inland + 6) / 10) * (1 - smooth((inland - 18) / 24));
                shorelineOffset = band * (4.1 * std::sin(x * .029 + z * .018 + phase) +
                                          2.2 * std::sin(z * .055 - x * .036 - phase));
            }
            const double shore = smooth((inland - 12) / 210);
            const double beach = -4.8 + clamp((inland + 2 + shorelineOffset) / 42, 0., 1.) * 7.2;
            h = beach * (1 - shore) + h * shore;
            double bank = riverDistance(x), bankHalf = 18 - (wide ? 7.3 : 4.8) - 2.8;
            if (bank < 150)
            {
                double blend = smooth((bank - 22) / 128);
                h = 2.2 * (1 - blend) + h * blend;
            }
            if (bank < bankHalf)
            {
                double v = smooth((bank - (bankHalf - 3.2)) / 3.2);
                h = -8 + 10.2 * v;
            }
            // The inland canal profile must meet the sea at its mouth. Otherwise its
            // flat banks continue offshore as two rectangular fingers above the water.
            if (revised && bank < 150 && inland < 42)
                h = std::min(h, beach);
            return h;
        }
        double t = smooth((edge - std::max(std::abs(x), std::abs(z))) / 205);
        double h = 20 + 8 * std::sin(x * .009 + phase) * std::cos(z * .007 - phase * .3) +
                   7 * std::sin(z * .0055 + phase * .7) + 4 * std::cos((x + z) * .006 - phase);
        return h * t;
    };
    const double track = revised ? layout->railZ() : 0, end = revised ? layout->railEnd() : 0;
    const double berthHeight = revised ? natural(end - 23, track) : 0;
    auto base = [&](double x, double z)
    {
        const double h = natural(x, z);
        if (!revised || std::abs(z - track) >= 52 || x <= layout->railStart() - 18 || x >= end + 27)
            return h;
        // The loading aprons and their adjacent building foundations sit on the same grade.
        const double across = 1 - smooth((std::abs(z - track) - 20) / 32);
        const double along = smooth((x - layout->railStart() + 18) / 18) * (1 - smooth((x - end - 3) / 24));
        const double berth = smooth((x - end + 210) / 95);
        const double railGrade = natural(x, track) * (1 - berth) + berthHeight * berth;
        return h + (railGrade - h) * across * along;
    };
    for (int j = 0; j < size_; j++)
        for (int i = 0; i < size_; i++)
            heights_[i + size_t(j) * size_] = float(base(origin_ + i, origin_ + j));
    double low = -(nodes - 1) * 18.;
    auto pad = [&](const Building &b)
    {
        double x = low + (std::floor((b.p.x - low) / 36) + .5) * 36,
               z = low + (std::floor((b.p.z - low) / 36) + .5) * 36;
        return base(x, z);
    };
    std::vector<float> grading(heights_.size());
    for (auto &b : buildings)
    {
        if (b.kind == BuildingKind::wagon || b.kind == BuildingKind::buffer)
            continue;
        int x0 = std::max(0, int(std::floor(b.p.x - b.half.x - 4.5 - origin_))),
            x1 = std::min(size_ - 1, int(std::ceil(b.p.x + b.half.x + 4.5 - origin_))),
            z0 = std::max(0, int(std::floor(b.p.z - b.half.z - 4.5 - origin_))),
            z1 = std::min(size_ - 1, int(std::ceil(b.p.z + b.half.z + 4.5 - origin_)));
        double level = pad(b);
        for (int j = z0; j <= z1; j++)
            for (int i = x0; i <= x1; i++)
            {
                double x = origin_ + i, z = origin_ + j,
                       d = std::max(std::abs(x - b.p.x) - b.half.x, std::abs(z - b.p.z) - b.half.z),
                       w = smooth((4.5 - d) / 3);
                double dx = std::abs(x - low - std::round((x - low) / 36) * 36),
                       dz = std::abs(z - low - std::round((z - low) / 36) * 36);
                w *= smooth((std::min(dx, dz) - (wide ? 7.3 : 4)) / 2);
                auto k = i + size_t(j) * size_;
                if (w > grading[k])
                {
                    grading[k] = float(w);
                    heights_[k] = float(base(x, z) * (1 - w) + level * w);
                }
            }
    }
    for (auto &b : buildings)
    {
        if (b.kind == BuildingKind::wagon || b.kind == BuildingKind::buffer)
            continue;
        double level = pad(b);
        int x0 = std::max(0, int(std::floor(b.p.x - b.half.x - .8 - origin_))),
            x1 = std::min(size_ - 1, int(std::ceil(b.p.x + b.half.x + .8 - origin_))),
            z0 = std::max(0, int(std::floor(b.p.z - b.half.z - .8 - origin_))),
            z1 = std::min(size_ - 1, int(std::ceil(b.p.z + b.half.z + .8 - origin_)));
        for (int j = z0; j <= z1; j++)
            for (int i = x0; i <= x1; i++)
                heights_[i + size_t(j) * size_] = float(level);
    }
}
double Terrain::riverDistance(double x) const
{
    double low = -(nodes_ - 1) * 18.;
    return rivers_ ? std::min(std::abs(x - low - (nodes_ / 3) * 36 - 18),
                              std::abs(x - low - (2 * nodes_ / 3) * 36 - 18))
                   : 1e9;
}
bool Terrain::bridge(Vec2 p) const
{
    if (!rivers_ || riverDistance(p.x) > 18 - (wide_ ? 7.3 : 4.8))
        return false;
    double low = -(nodes_ - 1) * 18.;
    int row = int(std::nearbyint((p.z - low) / 36));
    int channel =
        std::abs(p.x - low - (nodes_ / 3) * 36 - 18) < std::abs(p.x - low - (2 * nodes_ / 3) * 36 - 18) ? 0
                                                                                                        : 1;
    return row >= 0 && row < nodes_ && bridges_[channel * 49 + row] &&
           std::abs(p.z - low - row * 36) <= (wide_ ? 10.55 : 8.05) + 1e-9;
}
double Terrain::height(double x, double z) const
{
    if (bridge({x, z}))
    {
        double c = std::cos(pi * std::min(1., riverDistance(x) / (18 - (wide_ ? 7.3 : 4.8))) * .5);
        return 2.2 + 1.5 * c * c;
    }
    return bedHeight(x, z);
}
double Terrain::bedHeight(double x, double z) const
{
    if (heights_.empty())
        return 0;
    double u = clamp(x - origin_, 0., size_ - 1.000001), v = clamp(z - origin_, 0., size_ - 1.000001);
    int i = int(u), j = int(v);
    u -= i;
    v -= j;
    size_t k = i + size_t(j) * size_;
    double a = heights_[k], b = heights_[k + 1], c = heights_[k + size_ + 1], d = heights_[k + size_];
    return u >= v ? a + (b - a) * u + (c - b) * v : a + (c - d) * u + (d - a) * v;
}
std::vector<int> World::neighbours(int n) const
{
    auto out = layout.neighbours(n);
    for (const auto &s : roadLevels)
    {
        if (s.leftNode == n)
            out.push_back(s.rightNode);
        if (s.rightNode == n)
            out.push_back(s.leftNode);
    }
    return out;
}
int World::chooseNext(int n, int previous)
{
    auto options = neighbours(n);
    if (options.size() > 1)
        std::erase(options, previous);
    if (options.empty())
        throw std::logic_error("Disconnected road node");
    return options[random.index(int(options.size()))];
}
void World::newRoute(Car &car)
{
    if (car.routePhase == 1)
    {
        car.plannedTo = -1;
        if (auto s = levelEdge(car.routeFrom, car.routeTo))
        {
            car.laneIndex = 0;
            car.route = s->route(car.routeFrom == s->leftNode);
            car.routePhase = 2;
            car.routeLevel = s->id;
        }
        else
        {
            auto f = (nodePoint(car.routeTo) - nodePoint(car.routeFrom)).unit();
            const auto offset = perpendicular(f) * CityLayout::laneOffset(car.laneIndex);
            car.route.emplace(nodePoint(car.routeFrom) + f * 9 + offset,
                              nodePoint(car.routeTo) - f * stopLine + offset);
            car.routePhase = 0;
            car.routeLevel = 0;
        }
    }
    else
    {
        int from = car.routeTo;
        auto *call = car.kind == 4 ? ambulanceCall(car.id) : nullptr;
        int to = call && call->stage == RescueStage::driving ? ambulanceTurn(*call, from, car.routeFrom)
                 : car.responding                            ? pursuitNext(from, car.routeFrom)
                 : car.plannedTo >= 0 && std::ranges::contains(neighbours(from), car.plannedTo)
                     ? car.plannedTo
                     : chooseNext(from, car.routeFrom);
        car.plannedTo = -1;
        auto centre = nodePoint(from), incoming = (centre - nodePoint(car.routeFrom)).unit(),
             outgoing = (nodePoint(to) - centre).unit();
        const double entryLane = CityLayout::laneOffset(car.laneIndex);
        if (levelEdge(from, to))
            car.laneIndex = 0;
        // Off an overpass or out of a tunnel, which have one lane, a bus is back at the kerb.
        else if (car.kind == 5 && layout.wide())
            car.laneIndex = 1;
        const double exitLane = CityLayout::laneOffset(car.laneIndex);
        if (to == car.routeFrom)
            car.route = Path::turnaround(centre, incoming, entryLane);
        else
        {
            auto start = centre - incoming * stopLine + perpendicular(incoming) * entryLane;
            auto end = centre + outgoing * 9 + perpendicular(outgoing) * exitLane;
            double handle = dot(incoming, outgoing) > .5    ? 6
                            : cross(incoming, outgoing) > 0 ? (9 - exitLane) * .82
                                                            : (9 + exitLane) * .55228475;
            // The curve itself is the one drawn from the old 11 m line; the set-back
            // stop only lengthens its lead-in.
            car.route.emplace(start, start + incoming * (handle + stopLine - 11), end - outgoing * handle,
                              end);
        }
        car.previousNode = car.routeFrom;
        car.routeFrom = from;
        car.routeTo = to;
        car.routePhase = 1;
        car.routeLevel = 0;
    }
    car.routeDistance = 0;
    car.senseAt = 0;
}
Vec2 World::pickupSite()
{
    for (int tries = 0; tries < 200; ++tries)
    {
        auto p = nodePoint(random.index(nodes * nodes)) + Vec2{10, 10};
        if (!layout.urbanSite(p, .4) || levelAt(p, 1))
            continue;
        bool clear = true;
        for (auto &b : buildings)
            if (std::abs(p.x - b.p.x) < b.half.x + .5 && std::abs(p.z - b.p.z) < b.half.z + .5)
            {
                clear = false;
                break;
            }
        if (clear)
            return p;
    }
    return nodePoint(nodes / 2 * nodes + nodes / 2) + Vec2{10, 10};
}
void World::generate(std::uint32_t newSeed, std::function<void(float)> progress, int layoutRevision,
                     Density population)
{
    seed = newSeed;
    density = population;
    trafficPopulation = populationSize(density).cars;
    drone = {};
    conversation = {};
    rescues = {};
    patrol = {};
    arrests = lastFine = 0;
    patrolScanAt = 0;
    nextRescueScan = nextRescueDispatch = 0;
    casualtyCursor = 0;
    random = Random(seed);
    buildings.clear();
    cars.clear();
    trafficCells.clear();
    trafficIndexAt = 0;
    people.clear();
    pickups.clear();
    particles.clear();
    surfaceMarks.clear();
    ripples.clear();
    tireContacts.clear();
    rockets.clear();
    playerBurnUntil = fireClock = nextFlameSound = 0;
    staticBodies.clear();
    fixtures.clear();
    fixtureByBody.clear();
    fixtureCells.assign(nodes * nodes, {});
    displacedFixtures.clear();
    nextFixtureRestore = 0;
    staticQueryCount = size_t(-1);
    nearbyStaticBodies.clear();
    physics.clear();
    clock.setHour(7);
    inventory.clear();
    player = {};
    player.id = 1;
    player.circle = true;
    player.radius = .32;
    player.mass(75);
    player.invI = 0;
    player.verticalExtent = 1.7;
    recoveryReadyAt = 0;
    vehicleEntry = {};
    controlledCar = 0;
    playerTransitSeat = -1;
    transitExitRequested = false;
    nextPlatformScan = 0;
    money = 0;
    health = 1;
    wanted = 0;
    dispatchAt = 0;
    pursuitNode = -1;
    time = 0;
    playerDistance = 0;
    aiming = firing = false;
    rain = rainTarget = 0;
    dayNightCycle = true;
    simulationSpeed = 1;
    weatherClock = 120;
    shotCooldown = wantedCooldown = 0;
    godMode = helicopterLanding = false;
    playerKnockedUntil = 0;
    lastCrimeAt = lastPoliceSightAt = -100;
    policeSightCheckAt = 0;
    pedestrianImpacts = 0;
    nextWaterCheck = nextRenewal = 0;
    renewalCursor = 0;
    renewedPeople = renewedCars = drownings = sunkCars = potholeJolts = clearedJams = 0;
    rolesTaken = rolesLeft = dutiesEnded = 0;
    sounds.clear();
    auto report = [&](float v)
    {
        if (progress)
            progress(v);
    };
    report(.02f);
    layout.generate(
        std::bit_cast<std::int32_t>(seed), nodes, true, [&](float p) { report(.02f + p * .8f); },
        layoutRevision);
    roadLevels = generateRoadLevels(layout, seed);
    buildings = layout.buildings;
    indexRoofs();
    report(.4f);
    terrain.generate(seed, nodes, buildings, true, &layout);
    for (auto &s : roadLevels)
        s.bake(terrain);
    generatePuddles();
    generatePotholes();
    report(.55f);
    std::uint32_t id = 10;
    for (auto &b : buildings)
    {
        Body body;
        body.p = b.p;
        body.halfW = b.half.x;
        body.halfH = b.half.z;
        body.height = terrain.height(b.p) + b.height * .5;
        body.verticalExtent = b.height;
        body.id = id++;
        staticBodies.push_back(body);
    }
    for (auto &b : layout.furniture)
    {
        Body body;
        body.id = id++;
        body.p = b.p;
        body.halfW = b.half.x;
        body.halfH = b.half.z;
        body.height = terrain.height(b.p) + b.height * .5;
        body.verticalExtent = b.height;
        if (b.height <= 3)
        {
            size_t ordinal = fixtures.size() + 1;
            registerFixture(body, ordinal % 9 == 0   ? FixtureKind::dumpster
                                  : ordinal % 3 == 0 ? FixtureKind::hydrant
                                                     : FixtureKind::bin);
        }
        staticBodies.push_back(body);
    }
    // Rail contact shapes use the identical spans/profile as the visible bridge rails.
    for (int col : {nodes / 3, 2 * nodes / 3})
        for (int row = 0; row < nodes; row += 6)
        {
            Vec2 p{layout.coordinate(col) + 18, layout.coordinate(row)};
            if (!layout.bridge(p))
                continue;
            for (int side : {-1, 1})
                for (double x = -layout.deckHalf(); x < layout.deckHalf(); x += 2)
                {
                    double len = std::min(2., layout.deckHalf() - x);
                    Body body;
                    body.id = id++;
                    body.p = p + Vec2{x + len * .5, side * 10.35};
                    body.halfW = len * .5;
                    body.halfH = .08;
                    body.height = terrain.height(body.p) + .5;
                    body.verticalExtent = 1;
                    staticBodies.push_back(body);
                }
        }
    appendLevelContacts(id);
    Random lanes(seed ^ 0x4c414e45u);
    for (int i = 0; i < int(trafficPopulation); i++)
    {
        Car car;
        car.id = std::uint32_t(i);
        car.body.id = id++;
        fitVehicle(car, streetModel(i));
        car.body.friction = .5;
        // Stable lane distribution without changing the seeded city generation sequence.
        car.laneIndex = i && layout.wide() ? lanes.index(2) : 0;
        // A bus keeps to the kerb, where its stops are.
        if (car.kind == 5 && layout.wide())
            car.laneIndex = 1;
        // The low density keeps the exact sequence of every earlier save. Denser cities
        // would start with cars inside one another, so they retry a taken place.
        for (int attempt = 0, attempts = density == Density::low ? 1 : 8; attempt < attempts; ++attempt)
        {
            car.routeLevel = 0;
            car.routeFrom = random.index(nodes * nodes);
            car.routeTo = chooseNext(car.routeFrom, -1);
            auto from = nodePoint(car.routeFrom), to = nodePoint(car.routeTo), f = (to - from).unit();
            car.body.angle = -std::atan2(f.x, f.z);
            car.body.p = from + f * 9 + perpendicular(f) * CityLayout::lane;
            car.routePhase = 1;
            newRoute(car);
            car.routeDistance = random.next() * car.route->length();
            car.body.p = car.route->sample(car.routeDistance).first;
            car.body.roadLevel = car.routeLevel;
            if (std::ranges::none_of(cars,
                                     [&](const Car &other) {
                                         return other.body.roadLevel == car.body.roadLevel &&
                                                (other.body.p - car.body.p).length2() < 100;
                                     }))
                break;
        }
        car.body.height = surface(car.body.p, car.body.roadLevel) + .8;
        cars.push_back(std::move(car));
    }
    auto centre = nodes / 2 * nodes + nodes / 2;
    cars[0].body.p = nodePoint(centre) + Vec2{-1.9, -12};
    cars[0].body.angle = 0;
    cars[0].model = 0;
    cars[0].body.roadLevel = 0;
    cars[0].body.height = terrain.height(cars[0].body.p) + .8;
    for (size_t k = 1; k < cars.size(); ++k)
        if ((cars[k].body.p - cars[0].body.p).length() < 8)
        {
            auto &c = cars[k];
            c.body.p = c.route->sample(0).first;
            if ((c.body.p - cars[0].body.p).length() < 8)
            {
                c.routeLevel = c.body.roadLevel = 0;
                c.routeFrom = 0;
                c.routeTo = chooseNext(0, -1);
                c.routePhase = 1;
                newRoute(c);
                c.body.p = c.route->sample(0).first;
                c.body.angle = -c.route->sample(0).second;
            }
            c.routeDistance = 0;
            c.body.height = surface(c.body.p, c.body.roadLevel) + .8;
        }
    report(.7f);
    for (size_t i = 0; i < populationSize(density).people; i++)
    {
        Person p;
        p.body.id = id++;
        p.body.circle = true;
        p.body.radius = .28;
        p.body.mass(75);
        p.body.invI = 0;
        p.body.verticalExtent = 1.7;
        p.node = random.index(nodes * nodes);
        p.next = chooseWalkNext(p.node, -1);
        // A cut can remove the sidewalk at a node. Start on an actual walkable
        // edge; its reverse also provides an escape route at a dead end.
        for (int attempt = 0; p.next == p.node && attempt < nodes * nodes; ++attempt)
        {
            p.node = (p.node + 1) % (nodes * nodes);
            p.next = chooseWalkNext(p.node, -1);
        }
        if (p.next == p.node)
            throw std::runtime_error("No connected sidewalk for pedestrian spawn");
        p.body.p = nodePoint(p.node) + Vec2{9.1, 9.1};
        p.target = nodePoint(p.next) + Vec2{9.1, 9.1};
        p.body.p += (p.target - p.body.p) * random.next();
        p.style = random.index(14);
        p.sidearm = Person::civilianWeapon(seed, p.body.id);
        p.phase = random.next() * 8;
        p.body.height = terrain.height(p.body.p) + .85;
        people.push_back(p);
    }
    report(.8f);
    // The map keeps mostly medicine and a few weapons; as in CityTraffic.FillMapPickups
    // only two bazookas and two flamethrowers exist at a time. The number of sites and
    // their order are unchanged, so later generators still read the same random stream.
    for (int i = 0, weapons = 0; i < 70; i++)
    {
        auto p = pickupSite();
        const bool pills = i % 4 != 1;
        WeaponId weapon = WeaponId::none;
        if (!pills)
        {
            weapon = weapons < 2        ? WeaponId::bazooka
                     : weapons < 4      ? WeaponId::flamethrower
                     : weapons % 4 == 3 ? WeaponId::automatic
                                        : WeaponId::pistol;
            ++weapons;
        }
        pickups.push_back({p, weapon, pills, 0});
    }
    trees = planTrees(*this);
    // Separate ID range preserves existing actors and v5 saves.
    for (size_t k = 0; k < trees.size(); ++k)
    {
        const auto &t = trees[k];
        Body body;
        body.id = 0x40000000u + std::uint32_t(k);
        body.p = {t.foot.x, t.foot.z};
        body.circle = true;
        body.radius = t.height * .018;
        body.height = t.foot.y + 1.8;
        body.verticalExtent = 3.6;
        staticBodies.push_back(body);
    }
    generateServices();
    generateTransit();
    generateStreetLife();
    formCompanies(false);
    generateStreetFurniture();
    generateShops();
    generateBusStops();
    settleRoles();
    report(1);
}
void World::step(PlayerInput input, double dt)
{
    if (!(dt > 0 && dt <= .025))
        throw std::invalid_argument("World uses fixed steps <=25 ms");
    time += dt;
    if (dayNightCycle)
        clock.step(dt);
    shotCooldown = std::max(0., shotCooldown - dt);
    wantedCooldown -= dt;
    if (wantedCooldown < 0)
        fadeWanted(dt);
    weatherClock -= dt;
    if (weatherClock <= 0)
    {
        rainTarget = 1 - rainTarget;
        weatherClock = rainTarget ? 80 : 180;
    }
    rain += (rainTarget - rain) * (1 - std::exp(-dt * .15));
    firing = false;
    stepEntry(input, dt);
    if (drone.active)
    {
        stepDrone(input, dt);
        input = {};
        input.lookHeading = -player.angle;
    }
    if (playerScooter >= 0 || transitPassenger())
        input.shooting = false;
    const bool playerDown = controlledCar < 0 && playerKnockedUntil > time;
    if (playerDown)
    {
        input.shooting = false;
        input.walking = {};
    }
    aiming = input.shooting;
    bool mounted = controlledCar >= 0 && cars[controlledCar].mountedGun();
    if (mounted)
    {
        auto &c = cars[controlledCar];
        c.turretYaw = wrap(input.lookHeading - c.heading());
        c.turretPitch = c.kind == 11 ? 0. : clamp(input.lookPitch, -.28, .65);
    }
    if (input.shooting)
    {
        bool fixedGun = controlledCar >= 0 && !mounted;
        shoot(fixedGun ? cars[controlledCar].heading() : input.lookHeading,
              fixedGun  ? cars[controlledCar].terrainPitch + cars[controlledCar].pitch
              : mounted ? cars[controlledCar].turretPitch
                        : input.lookPitch);
    }
    combatStep(dt);
    if (time >= dispatchAt)
        updateDispatch();
    if (time >= nextRescueScan)
        updateRescues();
    if (time >= patrolScanAt)
        updatePatrol();
    if (time >= nextWaterCheck)
        waterStep();
    if (time >= nextRenewal)
        renewCity();
    if (time >= nextRoleStep)
        stepRoles();
    auto focus = position();
    if (time >= trafficIndexAt || trafficCells.empty() || nextTrafficCar.size() != cars.size() ||
        nextTrafficPerson.size() != people.size())
        indexTraffic();
    if (time >= nextPlatformScan)
        updatePlatforms();
    std::vector<Vec2> previousCars, previousPeople;
    for (auto &c : cars)
        previousCars.push_back(c.body.p);
    for (auto &p : people)
        previousPeople.push_back(p.body.p);
    auto previousPlayer = player.p;
    std::vector<Body *> bodies;
    bodies.reserve(600);
    std::vector<size_t> localTrams;
    // Static colliders do not move. Keep a margin so moving the camera or player
    // inside it never removes a collider that could enter the exact contact range.
    constexpr double contactRange = 130, cacheMargin = 16;
    if (staticQueryCount != staticBodies.size() ||
        (staticQueryCentre - focus).length2() > cacheMargin * cacheMargin)
    {
        staticQueryCount = staticBodies.size();
        staticQueryCentre = focus;
        nearbyStaticBodies.clear();
        for (size_t i = 0; i < staticBodies.size(); ++i)
            if ((staticBodies[i].p - focus).length2() <=
                (contactRange + cacheMargin) * (contactRange + cacheMargin))
                nearbyStaticBodies.push_back(i);
    }
    for (size_t i : nearbyStaticBodies)
        if ((staticBodies[i].p - focus).length2() < contactRange * contactRange)
            bodies.push_back(&staticBodies[i]);
    for (size_t i = 0; i < cars.size(); i++)
    {
        auto &car = cars[i];
        auto &b = car.body;
        if (car.doorHold > 0 || car.doorOpen > 0)
        {
            car.doorHold = std::max(0., car.doorHold - dt);
            if (vehicleEntry.car != int(i))
                car.doorOpen = clamp(car.doorOpen + (car.doorHold > 0 ? 3. : -2.4) * dt, 0., 1.);
        }
        car.impactCooldown = std::max(0., car.impactCooldown - dt);
        bool local = (b.p - focus).length2() < 110 * 110 || int(i) == controlledCar;
        car.terrainActive = car.lift < .15;
        car.terrainGradient = {
            (surface(b.p + Vec2{.25, 0}, b.roadLevel) - surface(b.p - Vec2{.25, 0}, b.roadLevel)) * 2,
            (surface(b.p + Vec2{0, .25}, b.roadLevel) - surface(b.p - Vec2{0, .25}, b.roadLevel)) * 2};
        auto f = forward(car.heading());
        if (car.kind != 12)
            car.wheelTravel += dot(b.v, f) * dt;
        car.terrainPitch = std::atan(dot(car.terrainGradient, f));
        car.terrainRoll = -std::atan(dot(car.terrainGradient, {f.z, -f.x}));
        if (car.recovering)
        {
            car.settleAttitude(dt, int(i) == controlledCar && !transitPassenger());
            b.height = surface(b.p, b.roadLevel) + .8 + car.lift;
            continue;
        }
        if (car.line >= 0 && !car.destroyed && !car.falling)
        {
            bool piloted = int(i) == controlledCar && !transitPassenger();
            bool parked = !piloted && (car.driverless || vehicleEntry.car == int(i));
            runTransit(car, parked ? Input{0, 0, true, false} : input.car, piloted || parked, local, dt);
            if (local)
            {
                bodies.push_back(&b);
                if (car.kind == 9)
                    localTrams.push_back(i);
            }
            continue;
        }
        if (!car.destroyed && car.kind == 12)
        {
            flyPatrol(car, input, dt);
            if (local && car.lift < .3)
            {
                b.p = previousCars[i];
                bodies.push_back(&b);
            }
            continue;
        }
        const auto *medical = car.kind == 4 ? ambulanceCall(int(i)) : nullptr;
        if (int(i) == controlledCar && !car.destroyed)
        {
            // Nobody leaves a car in neutral on a hill: with no pedal touched a standing
            // car holds itself, as every parked one does.
            Input controls = input.car;
            if (controls.throttle == 0 && !controls.brake && b.v.length2() < .36)
                controls.brake = true;
            stepVehicle(car, controls, dt, 1 - rain * .2);
        }
        else if (car.destroyed)
        {
            stepWreck(car, dt);
        }
        else if ((int(i) == patrol.car && patrol.stage != PatrolStage::seated) ||
                 (medical && medical->braking) || (job.passenger() && int(i) == job.ride.car) ||
                 car.driverless || vehicleEntry.car == int(i) || car.summoned || car.personal ||
                 car.kind == 11 ||
                 ((job.kind == JobKind::vehicle || job.kind == JobKind::broadcast) && int(i) == job.actor))
            stepVehicle(car, {0, 0, true, false}, dt, 1 - rain * .2);
        else if (!local)
        {
            if (!car.route)
                newRoute(car);
            double cruise = car.responding                                      ? 16.
                            : medical && medical->stage == RescueStage::driving ? 11.
                                                                                : 7.;
            if (time >= car.senseAt)
                senseTraffic(car, false);
            cruise = std::min(cruise, car.trafficLimit);
            planExit(car);
            // A long vehicle waits further back, so that its nose stays off the crossing
            // as an ordinary car's does.
            const double setBack = std::max(0., b.halfH - 2.3);
            bool clear =
                car.routeDistance + cruise * dt < car.route->length() - setBack || intersectionClear(car);
            car.routeDistance =
                std::min(car.route->length(),
                         car.routeDistance +
                             (clear ? cruise * dt
                                    : std::min(cruise * dt, std::max(0., car.route->length() - setBack -
                                                                             car.routeDistance))));
            if (car.routeDistance >= car.route->length() && clear)
                newRoute(car);
            auto sample = car.route->sample(car.routeDistance);
            b.p = sample.first;
            b.angle = -sample.second;
            b.v = forward(sample.second) * (car.routeDistance >= car.route->length() ? 0 : cruise);
            b.roadLevel = car.routeLevel;
            b.height = surface(b.p, b.roadLevel) + .8;
            car.lift = car.verticalVelocity = 0;
            previousCars[i] = b.p;
            continue;
        }
        else
        {
            if (!car.route)
                newRoute(car);
            car.routeDistance = car.route->closestDistance(b.p);
            if (time >= car.senseAt)
                senseTraffic(car, true);
            planExit(car);
            bool clear = intersectionClear(car);
            auto end = car.route->sample(car.route->length());
            auto beyond = b.p - end.first;
            bool reached = beyond.length() < 1.1 || (car.routeDistance > car.route->length() - 1.5 &&
                                                     dot(beyond, forward(end.second)) >= 0 &&
                                                     std::abs(cross(beyond, forward(end.second))) < 2);
            if (reached && clear)
            {
                newRoute(car);
                car.routeDistance = car.route->closestDistance(b.p);
                end = car.route->sample(car.route->length());
            }
            double lookAhead = 2.8 + std::abs(dot(b.v, forward(car.heading()))) * .35;
            double aimDistance = car.routeDistance + lookAhead;
            auto aim = car.route->sample(aimDistance).first;
            // Continue the tangent past an endpoint instead of steering back towards a
            // point just behind the front axle after a small physical overshoot.
            if (aimDistance > car.route->length())
                aim += forward(end.second) * (aimDistance - car.route->length());
            double desired = std::atan2(aim.x - b.p.x, aim.z - b.p.z);
            double target =
                car.routePhase == 1
                    ? 4.5
                    : std::min(8., 4.5 + std::max(0., car.route->length() - car.routeDistance - 5) * .4);
            if (!clear)
                target = std::min(target, std::sqrt(2 * 4 *
                                                    std::max(0., car.route->length() - car.routeDistance -
                                                                     .35 - std::max(0., b.halfH - 2.3))));
            if (car.responding)
            {
                target *= 1.65;
                if (time >= car.nextSightCheck)
                {
                    car.nextSightCheck = time + .45;
                    car.seesSuspect = (b.p - focus).length2() < 45 * 45 &&
                                      b.roadLevel == (controlledCar >= 0 ? cars[controlledCar].body.roadLevel
                                                                         : player.roadLevel) &&
                                      !levelAt(b.p, 3) && !levelAt(focus, 3) &&
                                      blastVisible(b.p, b.height, focus, ground() + .85);
                }
                responseFire(car);
            }
            if (car.responding && car.seesSuspect)
            {
                aim = focus;
                desired = std::atan2(aim.x - b.p.x, aim.z - b.p.z);
                target = 12 + wantedLevel();
                // Other responders cover a nearby stop while the lead officer is at the suspect's door.
                if (patrol.stage == PatrolStage::approaching && wantedLevel() < 4 &&
                    (b.p - focus).length2() < 19 * 19 &&
                    (controlledCar < 0 ? player.v.length2() : cars[controlledCar].body.v.length2()) < 1)
                    target = 0;
            }
            auto dir = forward(car.heading());
            target = std::min(target, car.trafficLimit);
            double speed = dot(b.v, dir);
            double steerLimit = .68 / (1 + std::pow(std::abs(speed) / 22, 2));
            double curvature =
                2 * std::sin(wrap(desired - car.heading())) / std::max(1., (aim - b.p).length());
            double steering = std::atan(car.wheelbase * curvature) / steerLimit;
            target = std::min(target, std::sqrt(3.2 / std::max(.001, std::abs(curvature))));
            stepVehicle(car,
                        {clamp((target - speed) * .4, -1., 1.), clamp(steering, -1., 1.), target < .2, false},
                        dt, 1 - rain * .2);
        }
        if (local)
        {
            potholeJolt(car);
            bodies.push_back(&b);
        }
        else
        {
            b.p += b.v * dt;
            b.angle = wrap(b.angle + b.w * dt);
        }
    }
    if (controlledCar < 0)
    {
        auto dir = input.walking.unit();
        if (playerScooter >= 0)
        {
            double targetSpeed = input.car.handbrake ? 0. : 6.8;
            player.v +=
                (dir * targetSpeed - player.v) * (1 - std::exp(-dt * (input.car.handbrake ? 12. : 3.)));
        }
        else if (playerDown)
            player.v *= std::exp(-dt * 4); // Thrown by the impact; no control until back on the feet.
        else
            player.v = dir * (input.walking.length() > .01 ? (input.running ? 5.7 : 2.7) : 0) *
                       (energySeconds > 0 ? 1.65 : 1.);
        playerDistance += player.v.length() * dt;
        if (playerScooter >= 0 && player.v.length2() > .05)
            player.angle = -std::atan2(player.v.x, player.v.z);
        else if (playerScooter < 0 && !playerDown)
            player.angle = -input.lookHeading;
        player.wake();
        bodies.push_back(&player);
    }
    // Only pedestrians within 90 m react to cars, and only within 4 m of a car.
    std::vector<const Car *> carsNearWalkers;
    for (const auto &car : cars)
        if ((car.body.p - focus).length2() < 94 * 94)
            carsNearWalkers.push_back(&car);
    // Where every moving vehicle near the walkers will be during the next two seconds:
    // along its route for traffic, which turns at junctions, straight on for anything else.
    struct Sweep
    {
        const Car *car;
        double reach;
        std::array<Vec2, 5> at, ahead;
    };
    std::vector<Sweep> sweeps;
    for (const Car *nearby : carsNearWalkers)
    {
        const auto &c = *nearby;
        const double pace = c.body.v.length();
        if (pace < 1 || c.destroyed || c.falling)
            continue;
        Sweep s{nearby, c.body.halfH + 2 + pace * 2, {}, {}};
        const bool routed = c.route && c.line < 0 && c.routePhase <= 1 && !c.responding && !c.driverless &&
                            !c.personal && !c.summoned && int(c.id) != controlledCar;
        const Vec2 here = routed ? c.route->sample(c.routeDistance).first : Vec2{};
        for (size_t k = 0; k < s.at.size(); ++k)
        {
            const double t = .5 * double(k);
            s.at[k] = c.body.p + c.body.v * t;
            s.ahead[k] = forward(c.heading());
            if (routed && k)
            {
                const double along = c.routeDistance + pace * t, over = along - c.route->length();
                const auto [point, heading] = c.route->sample(along);
                s.ahead[k] = forward(heading);
                s.at[k] = c.body.p + (point - here) + (over > 0 ? s.ahead[k] * over : Vec2{});
            }
        }
        sweeps.push_back(s);
    }
    // Whoever is on foot near the player: walkers keep out of each other's way.
    std::vector<const Person *> afoot;
    for (const auto &p : people)
        if (p.riding < 0 && !p.dead && p.knockedUntil <= time && (p.body.p - focus).length2() < 92 * 92)
            afoot.push_back(&p);
    for (auto &p : people)
    {
        if (p.riding >= 0)
            continue;
        bool local = (p.body.p - focus).length2() < 90 * 90;
        p.phase += dt;
        p.distance += p.body.v.length() * dt;
        if (p.dead || p.knockedUntil > time)
        {
            // Detached from the contact world while lying: the body slides to rest.
            p.body.v *= std::exp(-dt * (p.dead ? 5 : 3.2));
            p.body.p += p.body.v * dt;
            continue;
        }
        if (p.injuredUntil > 0 && time >= p.injuredUntil)
        {
            p.injuredUntil = 0;
            p.health = std::max(p.health, .75);
        }
        if ((p.body.p - p.target).length() < 1)
        {
            p.node = p.next;
            p.next = chooseWalkNext(p.node, -1);
            p.target = nodePoint(p.next) + Vec2{9.1, 9.1};
        }
        auto dir = (p.target - p.body.p).unit();
        p.fear = std::max(0., p.fear - dt * .5);
        if (local)
            for (const Car *nearby : carsNearWalkers)
            {
                const auto &c = *nearby;
                auto d = p.body.p - c.body.p;
                double distance = d.length();
                if (distance < 4 && std::abs(p.body.height - c.body.height) < 1.8 && c.body.v.length() > 5)
                {
                    const bool playerDriving =
                        controlledCar >= 0 && !transitPassenger() && nearby == &cars[controlledCar];
                    // A car following the carriageway is dangerous only on an actual collision
                    // course. Traffic passing a pavement or somebody waiting at the kerb is not;
                    // to a walker in the road itself any car coming closer is.
                    const Vec2 course = c.body.v.unit();
                    const bool ahead = dot(d, course) > 0;
                    if (layout.roadDistance(c.body.p) > layout.roadHalf() - .25 ||
                        (ahead && std::abs(dot(d, perpendicular(course))) < c.body.halfW + p.body.radius) ||
                        (!playerDriving && ahead && layout.roadDistance(p.body.p) < layout.roadHalf()))
                    {
                        dir = d.unit();
                        scarePerson(p, c.body.p, 2.);
                        if (playerDriving && p.sidearm != WeaponId::none)
                            p.alertUntil = time + 5.;
                    }
                }
                if (p.ownedScooter >= 0 && scooters[p.ownedScooter].rider >= 0 && distance < 1.55 &&
                    std::abs(p.body.height - c.body.height) < 1.5 && c.body.v.length() > 3)
                {
                    releaseScooter(p.ownedScooter, c.body.v * .45);
                    p.fear = 3;
                }
                if (detectImpact(p, c,
                                 controlledCar >= 0 && !transitPassenger() && &c == &cars[controlledCar]))
                    break;
            }
        if (p.dead || p.knockedUntil > time)
            continue;
        double speed = p.fear > 0 ? fleeingPace(p) : walkingPace(p);
        roleIntent(p, dir, speed);
        riderIntent(p, dir, speed);
        socialIntent(p, dir, speed);
        // Ordinary walking: wait for the walk signal, hurry when the lights change halfway.
        if (local && p.fear <= 0 && p.role == Person::Role::none && p.watchUntil <= time && speed > .2)
        {
            // Nobody dawdles in the road, least of all once the cars have green.
            if (kerbWait(p) > 0)
                speed = 0;
            else if (onCrossing(p, false))
                speed *= onCrossing(p, true) ? 1.5 : 1.2;
            else if (p.drunk() && p.uniform == Person::Uniform::none)
                // A drunk keeps the general direction and little else.
                dir = rotate(dir, std::sin(time * .9 + p.body.id) * .3);
        }
        const bool limping = p.injuredUntil > time;
        if (limping && p.fear <= 0)
        {
            // A survivor of a car hit stays about the place where it fell.
            const double phase = time * .22 + p.body.id;
            auto goal = p.injuryHome + Vec2{std::sin(phase), std::cos(phase * .7)} * 2.2;
            if (layout.roadDistance(goal) < layout.roadHalf() + .4)
                goal = p.injuryHome;
            const auto delta = goal - p.body.p;
            dir = delta.unit();
            speed = std::min(.55, delta.length());
        }
        if (p.waitingLine >= 0 && p.fear <= 0 && p.watchUntil <= time && p.alertUntil <= time)
        {
            const auto delta = p.platformTarget - p.body.p;
            dir = delta.unit();
            speed = delta.length2() < .16 ? 0. : std::min(1.4, delta.length() * 2);
        }
        if (p.fear <= 0 && waitingForTaxi(int(&p - people.data())))
            speed = 0;
        if (job.passenger() && !job.acquired && &p == &people[job.actor] && p.fear <= 0)
        {
            if (job.ride.car >= 0)
            {
                auto delta = passengerDoor(cars[job.ride.car], p.body.p) - p.body.p;
                dir = delta.unit();
                speed = std::min(speed, delta.length() * 3);
            }
            else
                speed = 0;
        }
        if (local && p.sidearm != WeaponId::none && (p.alertUntil > time || p.aimSeconds > 0))
        {
            if (coverAllowsAim(p, carsNearWalkers, dir, speed))
                civilianCombat(p, dt);
            else
            {
                p.aimSeconds = 0;
                p.burstLeft = 0;
            }
            if (p.aimSeconds > 0)
                speed = 0;
        }
        else if (p.aimSeconds > 0 || p.cover.searchAt > 0)
        {
            p.aimSeconds = 0;
            p.burstLeft = 0;
            p.cover = {};
        }
        if (int(&p - people.data()) == patrol.officer)
            officerIntent(dir, speed);
        else
            medicIntent(int(&p - people.data()), dir, speed);
        if (limping)
            speed = std::min(speed, p.fear > 0 ? 1.6 : .7);
        // A vehicle standing in the way is walked around, along the side that is met. Before
        // this, people leaned on a parked car, or on the truck that was waiting for them,
        // until it moved.
        if (local && speed > .2)
        {
            const Vec2 step = p.body.p + dir * .9;
            for (const Car *nearby : carsNearWalkers)
            {
                const auto &c = *nearby;
                if (c.body.v.length2() > 1 || std::abs(p.body.height - c.body.height) > 1.8 ||
                    (c.body.p - p.body.p).length2() > (c.body.halfH + 2.5) * (c.body.halfH + 2.5))
                    continue;
                const Vec2 ahead = forward(c.heading()), side = perpendicular(ahead);
                const double margin = p.body.radius + .2;
                const Vec2 probe = step - c.body.p;
                if (std::abs(dot(probe, ahead)) > c.body.halfH + margin ||
                    std::abs(dot(probe, side)) > c.body.halfW + margin)
                    continue;
                const Vec2 from = p.body.p - c.body.p, wish = p.target - p.body.p;
                // Beside the car: along it; before or behind it: across.
                const bool beside =
                    std::abs(dot(from, side)) - c.body.halfW > std::abs(dot(from, ahead)) - c.body.halfH;
                const Vec2 along = beside ? ahead : side;
                // Whoever is on the way round keeps going. Otherwise along the car when the
                // goal lies that way, and round the nearer end when it lies abeam: choosing
                // by the goal alone turned people back at the corner of the bonnet, where it
                // comes to lie behind them, and they stood there with the car waiting.
                const double moving = dot(p.body.v, along), way = dot(wish.unit(), along),
                             place = dot(from, along);
                const double choice = std::abs(moving) > .35 ? moving
                                      : std::abs(way) > .5   ? way
                                      : std::abs(place) > .1 ? place
                                      : p.body.id % 2        ? 1.
                                                             : -1.;
                dir = along * (choice > 0 ? 1. : -1.);
                break;
            }
        }
        if (local && speed > .2 && p.fear <= 0 && !limping && p.waitingLine < 0)
            passWalkers(p, afoot, dir, speed);
        // Nobody walks into a car that is passing in front of them: a look at where every
        // moving vehicle will be during the next two seconds. Whoever is in its way already
        // keeps walking and gets out of it.
        if (local && p.fear <= 0 && speed > .2 && int(&p - people.data()) != patrol.officer)
        {
            // Whoever is faster looks further: somebody on a scooter needs the room to stop.
            const double stride = std::max(.6, speed * .45);
            const Vec2 steps[]{p.body.p, p.body.p + dir * stride, p.body.p + dir * (2 * stride)};
            for (const Sweep &s : sweeps)
            {
                const auto &c = *s.car;
                if ((c.body.p - p.body.p).length2() > s.reach * s.reach ||
                    std::abs(p.body.height - c.body.height) > 1.8)
                    continue;
                auto swept = [&](Vec2 at)
                {
                    for (size_t k = 0; k < s.at.size(); ++k)
                    {
                        const Vec2 d = at - s.at[k];
                        if (std::abs(dot(d, s.ahead[k])) < c.body.halfH + .55 &&
                            std::abs(dot(d, perpendicular(s.ahead[k]))) < c.body.halfW + .55)
                            return true;
                    }
                    return false;
                };
                if (!swept(steps[0]) && (swept(steps[1]) || swept(steps[2])))
                {
                    speed = 0;
                    break;
                }
            }
        }
        // Whoever walks and gets nowhere goes round whatever holds them. A lamp post met
        // almost squarely held a walker by friction for good, and with him the bus whose
        // bonnet pointed at him across the junction.
        if (local && speed > .2 && !p.body.roadLevel)
        {
            if (time < p.roundUntil)
                dir = rotate(dir, p.roundSide * roundTurn);
            else if (dot(p.body.v, dir) >= .2 * speed)
                p.blockedFor = 0;
            else if ((p.blockedFor += dt) > .6)
                goRound(p, dir);
        }
        else
            p.blockedFor = 0;
        if (speed > .01)
            p.heading = std::atan2(dir.x, dir.z);
        auto desired = dir * speed;
        p.body.v += (desired - p.body.v) * (1 - std::exp(-dt * 4));
        p.body.wake();
        if (local)
            bodies.push_back(&p.body);
        else
            p.body.p += p.body.v * dt;
    }
    detectPlayerImpact(carsNearWalkers);
    std::vector<Vec2> beforeContact;
    beforeContact.reserve(cars.size());
    for (auto &c : cars)
        beforeContact.push_back(c.body.v);
    for (auto &s : scooters)
        if (s.rider == Scooter::free && (s.body.p - focus).length2() < 90 * 90)
            bodies.push_back(&s.body);
    physics.step(bodies, dt);
    updateFixtures(focus, dt);
    for (auto index : localTrams)
        alignTram(cars[index], dt);
    for (size_t i = 0; i < cars.size(); ++i)
    {
        auto &c = cars[i];
        if (c.recovering || (c.kind == 12 && !c.destroyed))
            continue;
        supportCar(c, previousCars[i], dt);
        if (int(i) == controlledCar || (c.body.p - focus).length2() < 110 * 110)
            c.settleAttitude(dt, int(i) == controlledCar && !transitPassenger());
    }
    for (size_t i = 0; i < people.size(); ++i)
    {
        if (people[i].riding >= 0)
            seatPassenger(people[i]);
        else
            supportPerson(people[i].body, previousPeople[i], dt);
    }
    if (controlledCar < 0)
        supportPerson(player, previousPlayer, dt);
    else if (transitPassenger())
        player.angle = -placeTransitRider(player, cars[controlledCar], playerTransitSeat);
    updateTireMarks(focus, dt);
    updateScooters(dt);
    stepWildlife(dt);
    std::vector<Vec2> impacts(cars.size());
    for (size_t i = 0; i < cars.size(); i++)
        impacts[i] = cars[i].body.v - beforeContact[i];
    for (size_t i = 0; i < cars.size(); i++)
        vehicleImpact(cars[i], impacts[i].length(), impacts[i]);
    for (auto &pickup : pickups)
    {
        pickup.wait = std::max(0., pickup.wait - dt);
        if (transitPassenger() || pickup.wait > 0 || (pickup.p - position()).length() > 1.7 ||
            std::abs(surface(pickup.p, pickup.roadLevel) - ground()) > 1.7)
            continue;
        if (health <= 0 || (controlledCar >= 0 && cars[controlledCar].destroyed))
            continue;
        if (pickup.energy)
        {
            if (energySeconds > 2)
                continue;
            energySeconds = 25;
            sounds.push_back({"energy", position(), .6, true});
        }
        else if (pickup.health)
        {
            if (health >= 1)
                continue;
            health = std::min(1., health + pillHealing);
            sounds.push_back({"heal", position(), .8, true});
        }
        else if (pickup.weapon != WeaponId::none)
        {
            inventory.give(pickup.weapon);
            sounds.push_back({"weapon_pickup", position(), .8, true});
        }
        else
        {
            money = std::min(999999999, money + 20 + random.index(80));
            sounds.push_back({"cash", position(), .8, true});
            pickup.wait = -1;
            continue;
        }
        if (pickup.dropped)
        {
            pickup.wait = -1;
            continue;
        }
        pickup.p = pickupSite();
        pickup.wait = 2;
    }
    std::erase_if(pickups, [](const Pickup &p) { return p.wait < 0; });
    for (auto &p : particles)
    {
        p.life -= float(dt);
        p.v.y -= float(dt * 6);
        p.p = p.p + p.v * float(dt);
    }
    std::erase_if(particles, [](auto &p) { return p.life <= 0; });
    if (particles.size() > 5000)
        particles.erase(particles.begin(),
                        particles.begin() + static_cast<std::ptrdiff_t>(particles.size() - 5000));
    while (!ripples.empty() && time - ripples.front().born >= WaterRipple::lifetime)
        ripples.pop_front();
    const double dangerDepth = controlledCar >= 0 ? .85 : 1.15;
    bool submerged = ground() < CityLayout::waterLevel &&
                     terrain.bedHeight(position().x, position().z) < CityLayout::waterLevel - dangerDepth;
    if (submerged)
        splash(position(), controlledCar >= 0 ? 2.5 : .8);
    if (health <= 0 || submerged || std::max(std::abs(position().x), std::abs(position().z)) > landEdge ||
        (controlledCar >= 0 && cars[controlledCar].destroyed))
    {
        auto fallen = position();
        if (controlledCar >= 0 && !transitPassenger())
            cars[controlledCar].driverless = true;
        controlledCar = -1;
        playerTransitSeat = -1;
        transitExitRequested = false;
        clinicRespawn(fallen);
        player.roadLevel = 0;
        player.verticalSpeed = 0;
        player.height = terrain.height(player.p) + .85;
        health = 1;
        wanted = 0;
        dispatchAt = 0;
        for (auto &c : cars)
            c.responding = false;
        playerBurnUntil = playerKnockedUntil = 0;
        rockets.clear();
        inventory.clear();
        physics.clear();
    }
    else
    {
        servicesStep(dt);
        arrestStep(dt);
    }
}
namespace
{
template <class T> T consoleNumber(std::string_view arg, T low, T high, const char *usage)
{
    T number{};
    auto [end, error] = std::from_chars(arg.data(), arg.data() + arg.size(), number);
    if (error != std::errc{} || end != arg.data() + arg.size() || !(number >= low && number <= high))
        throw std::invalid_argument(usage);
    return number;
}
bool consoleToggle(std::string_view arg)
{
    if (arg == "on" || arg == "1")
        return true;
    if (arg == "off" || arg == "0")
        return false;
    throw std::invalid_argument("Expected on/off or 1/0");
}
} // namespace

std::wstring World::command(const std::string &text)
{
    if (text.size() > 160)
        throw std::invalid_argument("Command too long");
    std::istringstream in(text);
    std::vector<std::string> args;
    for (std::string token; in >> token;)
    {
        for (char &c : token)
            c = char(std::tolower(static_cast<unsigned char>(c)));
        args.push_back(std::move(token));
    }
    if (args.empty())
        return {};
    const auto &cmd = args[0];
    auto count = [&](size_t n, const char *usage)
    {
        if (args.size() != n)
            throw std::invalid_argument(usage);
    };
    if (cmd == "health" || cmd == "repair")
    {
        count(1, "health | repair");
        if (cmd == "repair" && (controlledCar < 0 || cars[controlledCar].destroyed))
            throw std::invalid_argument("Enter an intact vehicle first");
        if (cmd == "health")
        {
            health = 1;
            playerBurnUntil = 0; // "health" also puts the player out, as in the managed console.
        }
        if (controlledCar >= 0 && !cars[controlledCar].destroyed)
        {
            auto &car = cars[controlledCar];
            car.health = 1;
            car.firePlayerCaused = false;
            car.fireTime = car.safeDrivingTime = 0;
        }
    }
    else if (cmd == "god")
    {
        count(2, "god on/off");
        godMode = consoleToggle(args[1]);
    }
    else if (cmd == "money")
    {
        if (args.size() == 1)
            money = 1000;
        else
        {
            count(2, "money 0..999999999");
            money = consoleNumber(args[1], 0, 999999999, "money 0..999999999");
        }
    }
    else if (cmd == "weapons")
    {
        count(1, "weapons");
        for (auto w : weapons())
            inventory.give(w.id);
    }
    else if (cmd == "weapon")
    {
        if (args.size() != 2 && args.size() != 3)
            throw std::invalid_argument("weapon TYPE [1..9999]");
        auto it = std::ranges::find_if(weapons(),
                                       [&](const Weapon &w) {
                                           return std::ranges::equal(w.name, args[1], [](char a, char b)
                                                                     { return std::tolower(a) == b; });
                                       });
        if (it == weapons().end())
            throw std::invalid_argument("Unknown weapon");
        int ammo = args.size() == 3 ? consoleNumber(args[2], 1, Inventory::ammoLimit, "Ammo: 1..9999")
                                    : it->pickupAmmo;
        if (!inventory.give(it->id, ammo))
            throw std::invalid_argument("Ammunition already full");
    }
    else if (cmd == "hour")
    {
        if (args.size() == 1)
            clock.setHour(7);
        else
        {
            count(2, "hour 0..24");
            clock.setHour(consoleNumber(args[1], 0., 24., "hour 0..24"));
        }
    }
    else if (cmd == "rain" || cmd == "cycle")
    {
        if (cmd == "rain" && args.size() == 1)
            rainTarget = 1;
        else
        {
            count(2, "rain on/off | cycle on/off");
            bool on = consoleToggle(args[1]);
            if (cmd == "rain")
            {
                rainTarget = on ? 1 : 0;
                weatherClock = on ? 80 : 180;
            }
            else
                dayNightCycle = on;
        }
    }
    else if (cmd == "speed")
    {
        count(2, "speed 0.1..3");
        simulationSpeed = consoleNumber(args[1], .1, 3., "speed 0.1..3");
    }
    else if (cmd == "wanted")
    {
        if (args.size() > 2)
            throw std::invalid_argument("wanted 0..6");
        wanted = args.size() == 1 ? 6 : consoleNumber(args[1], 0, 6, "wanted 0..6");
        wantedCooldown = 15;
    }
    else if (cmd == "spawn")
    {
        count(2, "spawn TYPE");
        if (args[1] == "pills" || args[1] == "energy")
        {
            if (!spawnPickup(args[1] == "energy"))
                throw std::runtime_error("No safe space nearby or pickup limit reached");
            return L"OK";
        }
        static constexpr std::pair<std::string_view, unsigned> types[]{
            {"car", 0}, {"van", 14}, {"truck", 16},     {"police", 3}, {"rosguard", 4},
            {"fsb", 5}, {"btr", 6},  {"helicopter", 7}, {"tank", 10},  {"ambulance", ambulanceModel}};
        auto type = std::ranges::find_if(types, [&](auto entry) { return entry.first == args[1]; });
        if (type == std::end(types))
            throw std::invalid_argument("Unknown vehicle");
        if (!spawnVehicle(type->second))
            throw std::runtime_error("No clear street nearby or vehicle limit reached");
    }
    else if (cmd == "recover")
    {
        count(1, "recover");
        if (!recover())
            throw std::runtime_error("Vehicle recovery is currently unavailable");
    }
    else if (cmd == "jobs")
    {
        count(1, "jobs");
        return L"Выполнено " + std::to_wstring(jobsCompleted) + L"   Сорвано " + std::to_wstring(jobsFailed) +
               L"   Заработок $" + std::to_wstring(jobEarnings) + L"   Серий " +
               std::to_wstring(seriesCompleted);
    }
    else
        throw std::invalid_argument("Unknown command. Type help.");
    return L"OK";
}
} // namespace pz
