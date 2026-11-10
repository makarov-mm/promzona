#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include "json.hpp"
#include <fstream>
#include <bit>
#include <sstream>
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
    auto natural = [&](double x, double z)
    {
        if (rivers_)
        {
            double inland = edge - std::max(std::abs(x), std::abs(z));
            double h = 26 + 14 * std::sin(x * .0045 + phase) * std::cos(z * .004 - phase * .3) +
                       9 * std::sin(z * .003 + phase * .7);
            double shore = smooth((inland - 12) / 210), beach = -4.8 + clamp((inland + 2) / 42, 0., 1.) * 7.2;
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
            return h;
        }
        double t = smooth((edge - std::max(std::abs(x), std::abs(z))) / 205);
        double h = 20 + 8 * std::sin(x * .009 + phase) * std::cos(z * .007 - phase * .3) +
                   7 * std::sin(z * .0055 + phase * .7) + 4 * std::cos((x + z) * .006 - phase);
        return h * t;
    };
    for (int j = 0; j < size_; j++)
        for (int i = 0; i < size_; i++)
            heights_[i + size_t(j) * size_] = float(natural(origin_ + i, origin_ + j));
    double low = -(nodes - 1) * 18.;
    auto pad = [&](const Building &b)
    {
        double x = low + (std::floor((b.p.x - low) / 36) + .5) * 36,
               z = low + (std::floor((b.p.z - low) / 36) + .5) * 36;
        return natural(x, z);
    };
    std::vector<float> grading(heights_.size());
    for (auto &b : buildings)
    {
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
                    heights_[k] = float(natural(x, z) * (1 - w) + level * w);
                }
            }
    }
    for (auto &b : buildings)
    {
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
bool World::intersectionClear(const Car &car) const
{
    if (car.routePhase == 1 || car.responding)
        return true;
    int node = car.routeTo;
    auto f = (nodePoint(node) - nodePoint(car.routeFrom)).unit();
    int d = f.z > .5 ? 0 : f.x > .5 ? 1 : f.z < -.5 ? 2 : 3;
    if (neighbours(node).size() >= 3 && !CityLayout::green(node % nodes, node / nodes, d, time))
        return false;
    // One crossing reservation per junction; released when the crossing path ends.
    for (auto &c : cars)
        if (&c != &car && !c.destroyed && c.routePhase == 1 && c.routeFrom == node &&
            std::abs(c.body.height - car.body.height) < 2 && (c.body.p - nodePoint(node)).length() < 15)
            return false;
    return true;
}
void World::newRoute(Car &car)
{
    if (car.routePhase == 1)
    {
        if (auto s = levelEdge(car.routeFrom, car.routeTo))
        {
            car.route = s->route(car.routeFrom == s->leftNode);
            car.routePhase = 2;
            car.routeLevel = s->id;
        }
        else
        {
            auto f = (nodePoint(car.routeTo) - nodePoint(car.routeFrom)).unit();
            car.route.emplace(nodePoint(car.routeFrom) + f * 9 + perpendicular(f) * CityLayout::lane,
                              nodePoint(car.routeTo) - f * 11 + perpendicular(f) * CityLayout::lane);
            car.routePhase = 0;
            car.routeLevel = 0;
        }
    }
    else
    {
        int from = car.routeTo, to = car.responding ? pursuitNext(from, car.routeFrom) : chooseNext(from, car.routeFrom);
        auto centre = nodePoint(from), incoming = (centre - nodePoint(car.routeFrom)).unit(),
             outgoing = (nodePoint(to) - centre).unit();
        if (to == car.routeFrom)
            car.route = Path::turnaround(centre, incoming);
        else
        {
            auto start = centre - incoming * 11 + perpendicular(incoming) * CityLayout::lane;
            auto end = centre + outgoing * 9 + perpendicular(outgoing) * CityLayout::lane;
            double handle = dot(incoming, outgoing) > .5    ? 6
                            : cross(incoming, outgoing) > 0 ? (9 - CityLayout::lane) * .82
                                                            : (9 + CityLayout::lane) * .55228475;
            car.route.emplace(start, start + incoming * handle, end - outgoing * handle, end);
        }
        car.previousNode = car.routeFrom;
        car.routeFrom = from;
        car.routeTo = to;
        car.routePhase = 1;
        car.routeLevel = 0;
    }
    car.routeDistance = 0;
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
void World::generate(std::uint32_t newSeed, std::function<void(float)> progress)
{
    seed = newSeed;
    drone = {};
    random = Random(seed);
    buildings.clear();
    cars.clear();
    people.clear();
    pickups.clear();
    particles.clear();
    rockets.clear();
    playerBurnUntil = fireClock = nextFlameSound = 0;
    staticBodies.clear();
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
    controlledCar = 0;
    money = 0;
    health = 1;
    wanted = 0;
    dispatchAt = 0;
    pursuitNode = -1;
    time = 0;
    playerDistance = 0;
    aiming = firing = false;
    rain = rainTarget = 0;
    weatherClock = 120;
    shotCooldown = wantedCooldown = 0;
    sounds.clear();
    auto report = [&](float v)
    {
        if (progress)
            progress(v);
    };
    report(.02f);
    layout.generate(std::bit_cast<std::int32_t>(seed), nodes, true, [&](float p) { report(.02f + p * .8f); });
    roadLevels = generateRoadLevels(layout, seed);
    buildings = layout.buildings;
    indexRoofs();
    report(.4f);
    terrain.generate(seed, nodes, buildings, true, &layout);
    for (auto &s : roadLevels)
        s.bake(terrain);
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
    for (int i = 0; i < int(trafficPopulation); i++)
    {
        Car car;
        car.id = std::uint32_t(i);
        car.body.id = id++;
        constexpr unsigned streetFleet[]{0, 1, 2, 11, 12, 13, 14, 0, 2, 16, 1, 13, 15, 17, 12, 3, 0};
        fitVehicle(car, streetFleet[i % std::size(streetFleet)]);
        car.body.friction = .5;
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
                c.routePhase = 0;
                c.routeFrom = 0;
                c.routeTo = chooseNext(0, -1);
                auto f = (nodePoint(c.routeTo) - nodePoint(0)).unit();
                c.route.emplace(nodePoint(0) + f * 9 + perpendicular(f) * 1.9,
                                nodePoint(c.routeTo) - f * 11 + perpendicular(f) * 1.9);
                c.body.p = c.route->sample(0).first;
                c.body.angle = -std::atan2(f.x, f.z);
            }
            c.routeDistance = 0;
            c.body.height = surface(c.body.p, c.body.roadLevel) + .8;
        }
    report(.7f);
    for (int i = 0; i < 720; i++)
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
    for (int i = 0; i < 70; i++)
    {
        auto p = pickupSite();
        pickups.push_back(
            {p, i % 3 == 0 ? WeaponId::none : i % 4 == 0 ? WeaponId::bazooka : (i % 2 == 0 ? WeaponId::pistol : WeaponId::automatic),
             i % 3 == 0, 0});
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
    report(1);
}
void World::enterExit()
{
    if (controlledCar >= 0)
    {
        auto &car = cars[controlledCar];
        if (car.body.v.length() > 4)
            return;
        auto clear = vehicleExit(car);
        if (!clear) return;
        player.p = *clear;
        player.v = car.body.v;
        player.roadLevel = selectLevel(player.p, car.body.roadLevel, car.body.height - .8);
        player.height = car.body.height + .05;
        player.verticalSpeed = car.verticalVelocity;
        player.wake();
        controlledCar = -1;
        physics.clear();
    }
    else
    {
        double best = 3.5;
        int index = -1;
        for (size_t i = 0; i < cars.size(); i++)
        {
            double d = (cars[i].body.p - player.p).length();
            if (d < best && !cars[i].destroyed && std::abs(player.height - cars[i].body.height) < 1.8)
            {
                best = d;
                index = int(i);
            }
        }
        if (index >= 0)
        {
            controlledCar = index;
            cars[index].responding = false;
            cars[index].body.wake();
        }
    }
}
void World::recover()
{
    if (drone.active) stopDrone(L"Связь с дроном прервана", 2);
    auto p = nodePoint(nodes / 2 * nodes + nodes / 2) + Vec2{-1.9, -12};
    if (controlledCar >= 0)
    {
        auto &c = cars[controlledCar];
        if (c.line >= 0)
        {
            const auto &line = lines[c.line];
            c.along = line.stops.front();
            p = line.pose(c.along).first;
            c.nextStop = 1 % int(line.stops.size());
        }
        c.body.p = p;
        c.body.roadLevel = 0;
        c.body.height = terrain.height(p) + .8;
        c.body.v = {};
        c.body.w = 0;
        c.body.angle = c.line >= 0 ? -lines[c.line].pose(c.along).second : 0.;
        c.roll = c.pitch = c.lift = c.verticalVelocity = 0;
        c.health = 1;
        c.fireTime = c.safeDrivingTime = 0;
        c.destroyed = c.falling = c.firePlayerCaused = false;
        c.body.wake();
    }
    else
    {
        player.p = p;
        player.roadLevel = 0;
        player.height = terrain.height(p) + .85;
        player.verticalSpeed = 0;
    }
    playerBurnUntil = 0;
}
void World::step(PlayerInput input, double dt)
{
    if (!(dt > 0 && dt <= .025))
        throw std::invalid_argument("World uses fixed steps <=25 ms");
    time += dt;
    clock.step(dt);
    shotCooldown = std::max(0., shotCooldown - dt);
    wantedCooldown -= dt;
    if (wantedCooldown < 0 && wanted > 0)
    {
        wanted = std::max(0., wanted - dt * .025);
    }
    weatherClock -= dt;
    if (weatherClock <= 0)
    {
        rainTarget = 1 - rainTarget;
        weatherClock = rainTarget ? 80 : 180;
    }
    rain += (rainTarget - rain) * (1 - std::exp(-dt * .15));
    firing = false;
    if (drone.active)
    {
        stepDrone(input, dt);
        input = {};
        input.lookHeading = -player.angle;
    }
    aiming = input.shooting;
    bool mounted = controlledCar>=0 && cars[controlledCar].mountedGun();
    if(mounted)
    {
        auto &c=cars[controlledCar];
        c.turretYaw=wrap(input.lookHeading-c.heading());
        c.turretPitch=c.kind==11 ? 0. : clamp(input.lookPitch,-.28,.65);
    }
    if (input.shooting)
    {
        bool fixedGun=controlledCar>=0 && !mounted;
        shoot(fixedGun ? cars[controlledCar].heading() : input.lookHeading,
              fixedGun ? cars[controlledCar].terrainPitch+cars[controlledCar].pitch :
              mounted ? cars[controlledCar].turretPitch : input.lookPitch);
    }
    combatStep(dt);
    if (time >= dispatchAt) updateDispatch();
    auto focus = position();
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
    if (staticQueryCount != staticBodies.size() || (staticQueryCentre - focus).length2() > cacheMargin * cacheMargin)
    {
        staticQueryCount = staticBodies.size();
        staticQueryCentre = focus;
        nearbyStaticBodies.clear();
        for (size_t i = 0; i < staticBodies.size(); ++i)
            if ((staticBodies[i].p - focus).length2() <= (contactRange + cacheMargin) * (contactRange + cacheMargin))
                nearbyStaticBodies.push_back(i);
    }
    for (size_t i : nearbyStaticBodies)
        if ((staticBodies[i].p - focus).length2() < contactRange * contactRange)
            bodies.push_back(&staticBodies[i]);
    for (size_t i = 0; i < cars.size(); i++)
    {
        auto &car = cars[i];
        auto &b = car.body;
        car.impactCooldown = std::max(0., car.impactCooldown - dt);
        bool local = (b.p - focus).length2() < 110 * 110 || int(i) == controlledCar;
        car.terrainActive = car.lift < .15;
        car.terrainGradient = {
            (surface(b.p + Vec2{.25, 0}, b.roadLevel) - surface(b.p - Vec2{.25, 0}, b.roadLevel)) * 2,
            (surface(b.p + Vec2{0, .25}, b.roadLevel) - surface(b.p - Vec2{0, .25}, b.roadLevel)) * 2};
        auto f = forward(car.heading());
        if(car.kind != 12) car.wheelTravel += dot(b.v, f) * dt;
        car.terrainPitch = std::atan(dot(car.terrainGradient, f));
        car.terrainRoll = -std::atan(dot(car.terrainGradient, {f.z, -f.x}));
        if (car.line >= 0 && !car.destroyed && !car.falling)
        {
            runTransit(car, input.car, int(i) == controlledCar, local, dt);
            if (local)
            {
                bodies.push_back(&b);
                if (car.kind == 9) localTrams.push_back(i);
            }
            continue;
        }
        if (!car.destroyed && car.kind == 12)
        {
            flyPatrol(car, input, dt);
            if (local && car.lift < .3)
            {
                b.p=previousCars[i];
                bodies.push_back(&b);
            }
            continue;
        }
        if (int(i) == controlledCar && !car.destroyed)
            stepVehicle(car, input.car, dt, 1 - rain * .2);
        else if (car.destroyed)
        {
            stepWreck(car, dt);
        }
        else if ((job.passenger() && int(i) == job.ride.car) || car.summoned || car.kind == 11 || ((job.kind == JobKind::vehicle || job.kind == JobKind::broadcast) && int(i) == job.actor))
            stepVehicle(car, {0, 0, true, false}, dt, 1 - rain * .2);
        else if (!local)
        {
            if (!car.route)
                newRoute(car);
            double cruise = car.responding ? 16. : 7.;
            bool clear = car.routeDistance + cruise * dt < car.route->length() || intersectionClear(car);
            car.routeDistance = std::min(
                car.route->length(),
                car.routeDistance +
                    (clear ? cruise * dt
                           : std::min(7 * dt, std::max(0., car.route->length() - car.routeDistance))));
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
                target = std::min(
                    target, std::sqrt(2 * 4 * std::max(0., car.route->length() - car.routeDistance - .35)));
            if (car.responding)
            {
                target *= 1.65;
                if (time >= car.nextSightCheck)
                {
                    car.nextSightCheck = time + .45;
                    car.seesSuspect = (b.p - focus).length2() < 45 * 45 && b.roadLevel == (controlledCar >= 0 ? cars[controlledCar].body.roadLevel : player.roadLevel) &&
                        !levelAt(b.p, 3) && !levelAt(focus, 3) && blastVisible(b.p,b.height,focus,ground()+.85);
                }
                responseFire(car);
            }
            if (car.responding && car.seesSuspect)
            {
                aim = focus;
                desired = std::atan2(aim.x - b.p.x, aim.z - b.p.z);
                target = 12 + wantedLevel();
            }
            auto dir = forward(car.heading());
            for (auto &other : cars)
            {
                if (&other == &car || (car.responding && controlledCar >= 0 && &other == &cars[controlledCar]) || std::abs(b.height - other.body.height) > 2)
                    continue;
                auto delta = other.body.p - b.p;
                double along = dot(delta, dir);
                if (along > 0 && along < 8 && std::abs(cross(delta, dir)) < 1.6)
                    target = std::min(target, std::max(0., along - 4) * 1.4);
            }
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
            bodies.push_back(&b);
        else
        {
            b.p += b.v * dt;
            b.angle = wrap(b.angle + b.w * dt);
        }
    }
    if (controlledCar < 0)
    {
        auto dir = input.walking.unit();
        player.v = dir * (input.walking.length() > .01 ? (input.running ? 5.7 : 2.7) : 0) * (energySeconds > 0 ? 1.65 : 1.);
        playerDistance += player.v.length() * dt;
        player.angle = -input.lookHeading;
        player.wake();
        bodies.push_back(&player);
    }
    // Only pedestrians within 90 m react to cars, and only within 4 m of a car.
    std::vector<const Car *> carsNearWalkers;
    for (const auto &car : cars)
        if ((car.body.p - focus).length2() < 94 * 94)
            carsNearWalkers.push_back(&car);
    for (auto &p : people)
    {
        if (p.riding >= 0) continue;
        bool local = (p.body.p - focus).length2() < 90 * 90;
        p.phase += dt;
        p.distance += p.body.v.length() * dt;
        if (p.dead)
        {
            p.body.v *= std::exp(-dt * 5);
            p.body.p += p.body.v * dt;
            continue;
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
                    const bool playerDriving = controlledCar >= 0 && nearby == &cars[controlledCar];
                    // A car following the carriageway is dangerous only on an actual collision course.
                    if (!playerDriving || layout.roadDistance(c.body.p) > layout.roadHalf() - .25 ||
                        (dot(d, c.body.v.unit()) > 0 && std::abs(dot(d, perpendicular(c.body.v.unit()))) < c.body.halfW + p.body.radius))
                    {
                        dir = d.unit();
                        p.fear = 2;
                        if (playerDriving && p.sidearm != WeaponId::none) p.alertUntil = time + 5.;
                    }
                }
                if (distance < 1.1 && std::abs(p.body.height - c.body.height) < 1.8 && c.body.v.length() > 6)
                {
                    if (controlledCar >= 0 && &c == &cars[controlledCar])
                        reportCrime(.45);
                    killPerson(p, c.body.v * .6, controlledCar >= 0 && &c == &cars[controlledCar]);
                    break;
                }
            }
        if (p.dead)
            continue;
        double speed = p.fear > 0 ? 3.5 : 1.2;
        if (job.passenger() && !job.acquired && &p == &people[job.actor] && p.fear <= 0)
        {
            if (job.ride.car >= 0)
            {
                auto delta = passengerDoor(cars[job.ride.car], p.body.p) - p.body.p;
                dir = delta.unit();
                speed = std::min(speed, delta.length() * 3);
            }
            else speed = 0;
        }
        if (local && p.sidearm != WeaponId::none && (p.alertUntil > time || p.aimSeconds > 0))
        {
            civilianCombat(p, dt);
            if (p.aimSeconds > 0) speed = 0;
        }
        else if (p.aimSeconds > 0) { p.aimSeconds = 0; p.burstLeft = 0; }
        if (speed > .01) p.heading = std::atan2(dir.x, dir.z);
        auto desired = dir * speed;
        p.body.v += (desired - p.body.v) * (1 - std::exp(-dt * 4));
        p.body.wake();
        if (local)
            bodies.push_back(&p.body);
        else
            p.body.p += p.body.v * dt;
    }
    std::vector<Vec2> beforeContact;
    beforeContact.reserve(cars.size());
    for (auto &c : cars)
        beforeContact.push_back(c.body.v);
    physics.step(bodies, dt);
    for (auto index : localTrams)
        alignTram(cars[index], dt);
    for (size_t i = 0; i < cars.size(); ++i)
        if (cars[i].kind != 12 || cars[i].destroyed)
            supportCar(cars[i], previousCars[i], dt);
    for (size_t i = 0; i < people.size(); ++i)
    {
        if (people[i].riding >= 0) seatPassenger(people[i]);
        else supportPerson(people[i].body, previousPeople[i], dt);
    }
    if (controlledCar < 0)
        supportPerson(player, previousPlayer, dt);
    std::vector<double> impacts(cars.size());
    for (size_t i = 0; i < cars.size(); i++)
        impacts[i] = (cars[i].body.v - beforeContact[i]).length();
    for (size_t i = 0; i < cars.size(); i++)
        vehicleImpact(cars[i], impacts[i]);
    for (auto &pickup : pickups)
    {
        pickup.wait = std::max(0., pickup.wait - dt);
        if (pickup.wait > 0 || (pickup.p - position()).length() > 1.7 ||
            std::abs(surface(pickup.p, pickup.roadLevel) - ground()) > 1.7)
            continue;
        if (health <= 0 || (controlledCar >= 0 && cars[controlledCar].destroyed))
            continue;
        if (pickup.energy)
        {
            if (energySeconds > 2) continue;
            energySeconds = 25;
            sounds.push_back({"energy", position(), .6, true});
        }
        else if (pickup.health)
        {
            if (health >= 1) continue;
            health = std::min(1., health + .3);
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
    if (health <= 0 || ground() < CityLayout::waterLevel + .3 ||
        std::max(std::abs(position().x), std::abs(position().z)) > landEdge ||
        (controlledCar >= 0 && cars[controlledCar].destroyed))
    {
        auto fallen = position();
        controlledCar = -1;
        clinicRespawn(fallen);
        player.roadLevel = 0;
        player.verticalSpeed = 0;
        player.height = terrain.height(player.p) + .85;
        health = 1;
        wanted = 0;
        dispatchAt = 0;
        for (auto &c : cars) c.responding = false;
        playerBurnUntil = 0;
        rockets.clear();
        inventory.clear();
        physics.clear();
    }
    else
        servicesStep(dt);
}
void World::command(const std::string &text)
{
    std::istringstream in(text);
    std::string command;
    in >> command;
    if (command == "health")
    {
        health = 1;
        if (controlledCar >= 0)
        {
            cars[controlledCar].health = 1;
            cars[controlledCar].firePlayerCaused = false;
            cars[controlledCar].fireTime = cars[controlledCar].safeDrivingTime = 0;
        }
    }
    else if (command == "money")
    {
        int n = 1000;
        in >> n;
        money = clamp(n, 0, 100000000);
    }
    else if (command == "weapons")
    {
        for (auto w : weapons())
            inventory.give(w.id);
    }
    else if (command == "hour")
    {
        double h = 7;
        in >> h;
        clock.setHour(h);
    }
    else if (command == "rain")
    {
        int n = 1;
        in >> n;
        rainTarget = n ? 1 : 0;
    }
    else if (command == "wanted")
    {
        int n = 6;
        in >> n;
        wanted = double(clamp(n, 0, 6));
        wantedCooldown = 15;
    }
    else if (command == "spawn")
    {
        std::string type, extra;
        in >> type;
        unsigned model = type == "tank" ? 10 : type == "helicopter" ? 7 : type == "btr" ? 6 : unsigned(-1);
        if (model >= std::size(fleet) || in >> extra)
            throw std::invalid_argument("spawn tank | helicopter | btr");
        if (!spawnVehicle(model))
            throw std::runtime_error("No clear street nearby or vehicle limit reached");
    }
    else if (command == "recover")
        recover();
    else
        throw std::invalid_argument(
            "Commands: weapons, health, money N, hour N, rain 0/1, wanted 0..6, spawn tank/helicopter/btr, recover");
}
} // namespace pz
