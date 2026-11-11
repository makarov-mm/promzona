#pragma once
#include "physics.hpp"
#include "surface_effects.hpp"
#include <deque>
#include <set>
#include "combat.hpp"
#include "jobs.hpp"
#include "transit.hpp"
#include "layout.hpp"
#include "road_levels.hpp"
#include "vegetation.hpp"
#include <filesystem>
#include <functional>
namespace pz
{
class Terrain
{
    int size_{};
    double origin_{};
    std::vector<float> heights_;
    int nodes_{33};
    bool wide_{}, rivers_{};
    std::array<bool, 98> bridges_{};
    double riverDistance(double x) const;
    bool bridge(Vec2 p) const;

  public:
    void generate(std::uint32_t seed, int nodes, std::span<const Building> buildings, bool wide = false,
                  const CityLayout *layout = nullptr);
    double bedHeight(double x, double z) const;
    double height(double x, double z) const;
    double height(Vec2 p) const { return height(p.x, p.z); }
    Vec2 gradient(Vec2 p) const
    {
        return {height(p.x + .5, p.z) - height(p.x - .5, p.z), height(p.x, p.z + .5) - height(p.x, p.z - .5)};
    }
};
class Clock
{
    double phase_{};

  public:
    double hour{7}, cycleMinutes{20};
    Clock() { setHour(7); }
    void setHour(double h);
    void step(double dt);
    double sunHeight() const { return std::sin((hour - 6) * pi / 12); }
    double daylight() const { return smooth((sunHeight() + .12) / .38); }
};
struct CivilianCover
{
    Vec2 hide, peek, threat, vehicleAnchor;
    double searchAt{}, holdUntil{};
    int vehicle{-1};
    bool found{}, peeking{};
};
struct Person
{
    Body body;
    Vec2 target;
    int node{}, next{};
    int riding{-1}, seat{}, alight{};
    int ownedScooter{-1};
    bool courier{}, inCompany{};
    Vec2 incident{}, danger{};
    double watchUntil{}, voiceAt{}, idleVoiceAt{}, fleeUntil{};
    double deliveryDistance{}, deliveryUntil{};
    double boardAfter{}, waitUntil{};
    int waitingLine{-1}, waitingStop{-1};
    Vec2 platformTarget{};
    bool dead{};
    double rescueAfter{};
    double fear{}, heading{}, phase{}, distance{}, health{1}, burnUntil{};
    bool burnPlayerCaused{};
    WeaponId sidearm{};
    CivilianCover cover;
    double alertUntil{}, aimSeconds{}, shotAt{};
    int burstLeft{};
    static WeaponId civilianWeapon(std::uint32_t citySeed, std::uint32_t id);
    std::uint32_t style{};
};
struct Scooter
{
    static constexpr int free = -1, playerRider = -2;
    Body body;
    int owner{-1}, rider{free};
    double returnAt{};
};
enum class Species { cat, dog, pigeon, bird };
struct Animal
{
    Species species{};
    int variant{}, flock{-1};
    Vec2 p, goal, velocity, offset;
    double altitude{}, heading{}, distance{}, fear{}, thinkAt{}, deadUntil{};
};
struct Flock
{
    Vec2 landing;
    bool flying{};
    double landedAt{};
};
struct Pickup
{
    Vec2 p;
    WeaponId weapon{};
    bool health{};
    double wait{};
    bool energy{}, dropped{};
    int roadLevel{};
};
struct Particle
{
    Vec3 p, v, color;
    float life{}, size{};
};
struct PlayerInput
{
    Input car;
    Vec2 walking;
    bool running{}, shooting{};
    double collective{};
    double lookHeading{}, lookPitch{};
};
struct ReconDrone
{
    bool active{};
    Vec2 position{}, velocity{}, obstacleFocus{};
    double altitude{}, heading{}, pitch{}, battery{}, health{}, scanTime{}, readyAt{}, nextShot{};
    std::array<size_t, 3> targets{};
    int scanned{};
    std::vector<size_t> obstacles;
};
struct SoundEvent
{
    std::string name;
    Vec2 source{};
    double gain{1};
    bool ui{};
    int speaker{-1};
};
struct World
{
    static constexpr int nodes = 49;
    static constexpr double spacing = 36, landEdge = (nodes - 1) * spacing * .5 + 54;
    std::uint32_t seed{20261105};
    Random random;
    CityLayout layout;
    Terrain terrain;
    std::vector<RoadLevel> roadLevels;
    Clock clock;
    bool dayNightCycle{true};
    double simulationSpeed{1};
    Inventory inventory;
    std::vector<Building> buildings;
    std::vector<std::vector<unsigned>> roofCells;
    void indexRoofs();
    double flightFloor(Vec2 pos) const;
    double precipitationSurface(Vec2 pos) const;
    double highestRoof(Vec2 pos, double floor, double margin) const;
    bool insideBuilding(Vec2 pos, double y, double radius) const;
    std::vector<TreePlant> trees;
    std::vector<Car> cars;
    std::vector<Person> people;
    std::vector<Pickup> pickups;
    std::vector<Scooter> scooters;
    std::vector<Animal> animals;
    std::vector<Flock> flocks;
    std::vector<Vec2> habitat;
    Random streetRandom;
    double animalClock{};
    int playerScooter{-1};
    enum class StreetOffer { none, gift, taxi, patient };
    struct Conversation
    {
        int actor{-1};
        double nextLine{}, pauseUntil{}, offerUntil{};
        StreetOffer offer{};
    } conversation;
    bool talk();
    void generateStreetLife();
    void formCompanies(bool loading);
    void socialIntent(Person &person, Vec2 &direction, double &speed);
    void scarePerson(Person &person, Vec2 origin, double fear);
    void releaseScooter(int index, Vec2 velocity);
    int nearbyScooter() const;
    bool useScooter();
    void riderIntent(Person &person, Vec2 &direction, double &speed);
    void updateScooters(double dt);
    void stepWildlife(double dt);
    void scareWildlife(Vec2 origin, double height, double lethalRadius);
    Vec2 landingSite(Vec2 near, Vec2 away);
    void takeFlight(int flock, Vec2 from, Vec2 away);
    std::vector<TransitLine> lines;
    int playerTransitSeat{-1};
    bool transitExitRequested{};
    double nextPlatformScan{};
    bool transitPassenger() const { return controlledCar >= 0 && playerTransitSeat >= 0; }
    int boardableTransit() const;
    bool rideTransit();
    bool leaveTransitAt(const Car &car, int stop);
    void disembarkPlayer(const Car &car, Vec2 at);
    void updatePlatforms();
    static double placeTransitRider(Body &body, const Car &car, int seat);
    std::optional<Vec2> platformExit(const Car &car, int stop) const;
    int freeTransitSeat(const Car &car) const;
    void walkersNear(Vec2 pos, double radius, std::vector<int> &out) const;
    void generateTransit();
    void runTransit(Car &car, Input controls, bool driving, bool local, double dt);
    void alignTram(Car &car, double dt);
    void exchangePassengers(Car &car, int stop);
    void seatPassenger(Person &person);
    std::vector<ServiceSite> sites;
    Job job;
    struct StreetHail { int actor{-1}; double until{}; };
    std::array<StreetHail, 3> hailers{};
    double nextHail{};
    void updateHailers();
    int hailerInReach() const;
    bool waitingForTaxi(int actor) const;
    ReconDrone drone;
    void toggleDrone(double heading);
    void stopDrone(const wchar_t *message, double delay);
    void stepDrone(const PlayerInput &input, double dt);
    void refreshDroneObstacles(Vec2 centre);
    bool droneClear(Vec2 point, double height, double radius) const;
    int jobsCompleted{}, jobsFailed{}, lastPay{}, seriesStage{}, seriesCompleted{};
    std::int64_t jobEarnings{};
    double energySeconds{}, clinicCredit{}, purchaseAt{}, noticeUntil{};
    std::wstring notice;
    std::vector<Particle> particles;
    std::deque<SurfaceMark> surfaceMarks;
    std::deque<WaterRipple> ripples;
    void splash(Vec2 point, double strength);
    std::vector<TireContact> tireContacts;
    std::vector<RoadPuddle> puddles;
    std::vector<int> puddleCells;
    void generatePuddles();
    bool inRoadPuddle(Vec2 wheel) const;
    void addMark(SurfaceMark mark);
    void updateTireMarks(Vec2 focus, double dt);
    std::vector<Rocket> rockets;
    enum class FixtureKind { bin, hydrant, dumpster, bench, phoneBooth, drinkMachine, mailbox,
                             bag, bottle, packet, can, garbageBag, count };
    struct StreetFixture {
        size_t body{};
        Vec2 home;
        FixtureKind kind{};
        double standingHeight{}, lift{}, tilt{}, spin{}, sprayUntil{};
        bool displaced{};
        double homeAngle{};
    };
    std::vector<StreetFixture> fixtures;
    std::vector<int> fixtureByBody;
    double nextFixtureRestore{};
    std::vector<std::vector<size_t>> fixtureCells;
    std::set<size_t> displacedFixtures;
    void registerFixture(Body &body, FixtureKind kind);
    void generateStreetFurniture();
    void fixturesNear(Vec2 centre, double radius, std::vector<size_t> &out) const;
    void updateFixtures(Vec2 focus, double dt);
    void supportFixture(StreetFixture &fixture);
    std::vector<Body> staticBodies;
    std::vector<size_t> nearbyStaticBodies;
    Vec2 staticQueryCentre{};
    size_t staticQueryCount{size_t(-1)};
    Body player;
    int controlledCar{0}, money{};
    double wanted{};
    double health{1}, time{}, shotCooldown{}, wantedCooldown{}, rain{}, rainTarget{}, weatherClock{120};
    bool firing{}, aiming{};
    double playerBurnUntil{}, fireClock{}, nextFlameSound{};
    double playerDistance{};
    ContactWorld physics;
    std::vector<SoundEvent> sounds;
    void generate(std::uint32_t newSeed, std::function<void(float)> progress = {},
                  int layoutRevision = CityLayout::districtRevision);
    void step(PlayerInput input, double dt);
    Vec2 position() const { return controlledCar >= 0 ? cars[controlledCar].body.p : player.p; }
    double ground() const
    {
        return controlledCar >= 0 ? cars[controlledCar].body.height - .8 : player.height - .85;
    }
    const RoadLevel *levelById(int id) const;
    const RoadLevel *levelAt(Vec2 p, double margin = 0) const;
    const RoadLevel *levelEdge(int from, int to) const;
    double surface(Vec2 p, int level) const;
    int selectLevel(Vec2 p, int previous, double foot) const;
    bool scenerySolid(Vec2 p, double y, double radius = .03) const;
    bool openCut(Vec2 p, double margin = 0) const;
    void supportCar(Car &car, Vec2 previousPosition, double dt);
    void supportPerson(Body &body, Vec2 previousPosition, double dt);
    void appendLevelContacts(std::uint32_t &id);
    int chooseWalkNext(int n, int previous);
    std::optional<Vec2> vehicleExit(const Car &car, std::optional<Vec2> toward = {}) const;
    void enterExit();
    void generateServices();
    void generateShops();
    int marketInReach() const;
    int garageInReach() const;
    int ownedGarage{-1}, openMarket{-1};
    static constexpr int garagePrice = 400, garageCapacity = 3;
    int ownedVehicleCount() const;
    int garageQuote() const;
    bool serviceGarage();
    void useOwnedGarage();
    int shopPrice(ShopItem item) const;
    bool buyAtMarket(ShopItem item);
    int nearestSite(ServiceKind kind, Vec2 from) const;
    int counterInReach(bool parkedAllowed) const;
    bool buyFood();
    void interactJob();
    bool canTakePassengerJob(int actor) const;
    bool startPassengerJob(JobKind kind, int requestedActor = -1);
    void interactPassengerJob();
    void stepPassengerJob(double dt);
    void releasePassenger(const Job &trip, bool delivered);
    Vec2 passengerDoor(const Car &car, Vec2 from) const;
    void resumeWalk(Person &person, Vec2 position, int level);
    bool startPeaceJob(JobKind kind);
    void interactPeaceJob();
    void stepPeaceJob(double dt);
    bool validPeaceJob() const;
    bool startContract(JobKind kind);
    bool startSeries();
    double deliveryHealth() const { return job.series ? (50 + 10 * seriesStage) / 100. : .5; }
    int expectedPay() const;
    void finishJob(bool success);
    Vec2 jobTarget() const;
    void servicesStep(double dt);
    void clinicRespawn(Vec2 fallen);
    void notify(std::wstring text);
    bool validJob() const;
    static constexpr size_t trafficPopulation = 260, maxResponseVehicles = 32, maxSpawnVehicles = 8;
    bool spawnVehicle(unsigned model);
    int pursuitNode{-1};
    std::array<int, nodes * nodes> pursuitDistances{};
    double dispatchAt{};
    void updateDispatch();
    enum class PatrolStage { stopping, approaching, returning, seated };
    struct ArrestPatrol {
        int car{-1}, officer{-1};
        PatrolStage stage{};
        double hold{}, deadline{};
    } patrol;
    Vec2 stationEntrance{};
    int arrests{}, lastFine{};
    double patrolScanAt{};
    void updatePatrol();
    void officerIntent(Vec2 &direction, double &speed) const;
    void arrestStep(double dt);
    std::optional<Vec2> clearEntrance(Vec2 door) const;
    enum class RescueStage { standby, driving, care, returning };
    struct Rescue {
        int car{-1}, medic{-1}, patient{-1}, destination{-1};
        RescueStage stage{};
        double deadline{}, treatment{};
        bool braking{};
        std::vector<int> distances;
    };
    std::array<Rescue, 2> rescues{};
    double nextRescueScan{}, nextRescueDispatch{};
    size_t casualtyCursor{};
    const Rescue *ambulanceCall(int car) const;
    const Rescue *paramedicCall(int actor) const;
    void routeAmbulance(Rescue &call);
    int ambulanceTurn(const Rescue &call, int node, int previous);
    void updateRescues();
    void medicIntent(int actor, Vec2 &direction, double &speed);
    int pursuitNext(int node, int previous);
    bool deployResponder(int kind);
    void responseFire(Car &car);
    void flyPatrol(Car &car, const PlayerInput &input, double dt);
    void recover();
    void shoot(double heading, double pitch = 0);
    void explode(Vec2 p, double y, double strength = 1, bool vehicleBlast = false, bool playerCaused = false);
    void destroyCar(Car &car);
    void damageCar(Car &car, double amount, bool playerCaused = false);
    void vehicleImpact(Car &car, double velocityChange);
    void killPerson(Person &person, Vec2 velocity = {}, bool playerCaused = false);
    AttackHit traceAttack(Vec2 from, Vec2 direction, double y, double slope, double range, int ignoreCar = -1,
                          bool includePlayer = false, const Person *shooter = nullptr) const;
    bool blastVisible(Vec2 from, double y, Vec2 to, double targetY) const;
    void combatStep(double dt);
    bool coverAllowsAim(Person &person, std::span<const Car *const> traffic, Vec2 &direction, double &speed);
    void civilianCombat(Person &person, double dt);
    void alarmWitnesses(Vec2 origin, double height, bool playerCaused);
    void flame(Vec2 direction, double y);
    void addCrime(double severity);
    void reportCrime(double severity, double radius = 45, bool sight = true);
    int wantedLevel() const { return wanted <= .001 ? 0 : int(std::ceil(wanted)); }
    std::function<void()> captureSave(const std::filesystem::path &path) const;
    void save(const std::filesystem::path &path) const;
    void load(const std::filesystem::path &path);
    std::wstring command(const std::string &text);
    static Vec2 nodePoint(int n)
    {
        return {(n % nodes - (nodes - 1) * .5) * spacing, (n / nodes - (nodes - 1) * .5) * spacing};
    }
    std::vector<int> neighbours(int n) const;
    int chooseNext(int n, int previous);
    void newRoute(Car &car);
    bool intersectionClear(const Car &car) const;
    // Actors are binned by city block for nearby drivers' periodic perception.
    struct TrafficCell { int car{-1}, person{-1}; };
    std::vector<TrafficCell> trafficCells;
    std::vector<int> nextTrafficCar, nextTrafficPerson;
    double trafficIndexAt{};
    void indexTraffic();
    void senseTraffic(Car &car, bool local);
    bool tryChangeLane(Car &car, int destination);
    Vec2 pickupSite();
};
} // namespace pz
