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
    // Port of the managed I, J, Sx, Sz: the corner of the junction `node` a walk starts
    // from and the corner of `next` it leads to -- east or west of the one street, north or
    // south of the other. `lastWalk` is the kind of leg walked last: nobody turns straight
    // back. With nothing to walk, `walkWait` is when to look again.
    int sx{1}, sz{1}, nextSx{1}, nextSz{1}, lastWalk{-1};
    double walkWait{};
    // A company walks its leader's way.
    void walkLike(const Person &lead)
    {
        node = lead.node;
        next = lead.next;
        target = lead.target;
        sx = lead.sx;
        sz = lead.sz;
        nextSx = lead.nextSx;
        nextSz = lead.nextSz;
        lastWalk = lead.lastWalk;
        walkWait = lead.walkWait;
    }
    int riding{-1}, seat{}, alight{};
    int ownedScooter{-1};
    bool courier{}, inCompany{};
    // The response forces of the managed game wear their own colours.
    enum class Uniform
    {
        none,
        medical,
        police,
        rosguard,
        fsb,
        army
    } uniform{};
    bool officer() const { return uniform >= Uniform::police; }
    // A habit that keeps a person about one place: a towel on the beach, a night shift at
    // the kerb, a homeless corner. Port of the managed CityTraffic.Roles.
    enum class Role
    {
        none,
        beach,
        night,
        vagrant
    } role{};
    Vec2 roleHome{};
    Vec2 incident{}, danger{};
    double watchUntil{}, voiceAt{}, idleVoiceAt{}, fleeUntil{};
    double deliveryDistance{}, deliveryUntil{};
    double boardAfter{}, waitUntil{};
    int waitingLine{-1}, waitingStop{-1};
    Vec2 platformTarget{};
    bool dead{};
    double rescueAfter{};
    // A survivor of a car hit lies where it fell, then limps about that spot.
    double knockedUntil{}, injuredUntil{};
    Vec2 injuryHome{};
    // Walking and getting nowhere: for how long, and the way round that follows.
    double blockedFor{}, roundUntil{};
    int roundSide{};
    Vec2 roundFrom{};
    // Time of death; a body nobody treats is replaced by a new passer-by out of view.
    double diedAt{};
    bool drowned{};
    double fear{}, heading{}, phase{}, distance{}, health{1}, burnUntil{};
    bool burnPlayerCaused{};
    WeaponId sidearm{};
    CivilianCover cover;
    double alertUntil{}, aimSeconds{}, shotAt{};
    int burstLeft{};
    static WeaponId civilianWeapon(std::uint32_t citySeed, std::uint32_t id);
    // Fourteen street types; four of them are the managed personas with manners of their own.
    std::uint32_t style{};
    bool elder() const { return style == 2 || style == 8; }
    bool bagElder() const { return style == 8; }
    bool drunk() const { return style == 9; }
    bool homeless() const { return style == 10; }
};
struct Scooter
{
    static constexpr int free = -1, playerRider = -2;
    Body body;
    int owner{-1}, rider{free};
    double returnAt{};
};
enum class Species
{
    cat,
    dog,
    pigeon,
    bird
};
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
// How many cars and passers-by a new city gets. `low` is the population of every save
// made before the setting existed. `high` is the managed game's first fill; `full` is
// what that game then keeps on the streets: half as many passenger cars again and a
// tenth on top of every kind, and people up to its target (CityTraffic.Population.cs).
enum class Density
{
    low,
    medium,
    high,
    full
};
struct PopulationSize
{
    size_t cars, people;
};
constexpr PopulationSize populationSize(Density density)
{
    return density == Density::full     ? PopulationSize{1485, 5265}
           : density == Density::high   ? PopulationSize{1080, 3240}
           : density == Density::medium ? PopulationSize{540, 1800}
                                        : PopulationSize{260, 720};
}
// What an imported managed (C#) save carried over; see World::importManaged.
struct ManagedImport
{
    int format{}, roadRevision{}, weapons{};
    std::uint32_t seed{};
    bool samePlan{}, positionKept{};
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
    enum class StreetOffer
    {
        none,
        gift,
        taxi,
        patient
    };
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
    // Dry land off the canal banks, and the bridges.
    bool dryGround(Vec2 at) const;
    // How far a walker who is held up turns aside, in radians.
    static constexpr double roundTurn = 1.4;
    void goRound(Person &person, Vec2 dir);
    // Walkers keep out of each other's way; `afoot` is everybody walking near the player.
    void passWalkers(const Person &person, const std::vector<const Person *> &afoot, Vec2 &direction,
                     double speed) const;
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
    // When the driver of a trolleybus that has left its wires is told so again.
    double wiresNoticeAt{};
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
    struct StreetHail
    {
        int actor{-1};
        double until{};
        // Port of the managed MaintainHailers: the place at the kerb of a bus stop where
        // the hailer waits. Without one -- no stop with anybody near it, or an older save --
        // they wait where they stood.
        Vec2 spot{};
        bool posted{};
    };
    const StreetHail *hailOf(int actor) const;
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
    // Holes of block (i, j) are potholes[potholeFirst[b] .. potholeFirst[b + 1]), b = j * (nodes - 1) + i.
    std::vector<Pothole> potholes;
    std::vector<std::uint32_t> potholeFirst;
    int potholeJolts{};
    void generatePotholes();
    void potholeJolt(Car &car);
    void addMark(SurfaceMark mark);
    void updateTireMarks(Vec2 focus, double dt);
    std::vector<Rocket> rockets;
    enum class FixtureKind
    {
        bin,
        hydrant,
        dumpster,
        bench,
        phoneBooth,
        drinkMachine,
        mailbox,
        bag,
        bottle,
        packet,
        can,
        garbageBag,
        count
    };
    struct StreetFixture
    {
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
    // Console cheat of the managed game: the player and the driven vehicle take no damage.
    bool godMode{};
    double playerKnockedUntil{};
    double lastCrimeAt{-100}, lastPoliceSightAt{-100}, policeSightCheckAt{};
    int pedestrianImpacts{};
    static constexpr double fatalImpactSpeed = 5.5;
    bool carTouches(const Car &car, Vec2 point, double margin) const;
    void strikePerson(Person &person, const Car &car, double closingSpeed, bool playerDriving);
    // The nearest spot off the carriageway where somebody hurt in the road can wait.
    Vec2 kerbside(Vec2 from) const;
    void strikePlayer(const Car &car, double closingSpeed);
    bool detectImpact(Person &person, const Car &car, bool playerDriving);
    void detectPlayerImpact(std::span<const Car *const> traffic);
    void hurtPlayer(double amount);
    bool policeSees() const;
    void fadeWanted(double dt);
    // Depth of the canal or the sea under a body whose lowest point is at `feet`;
    // zero on dry land and on a bridge deck.
    double waterDepth(Vec2 point, double feet) const;
    bool outOfSight(Vec2 point) const;
    double nextWaterCheck{}, nextRenewal{};
    size_t renewalCursor{};
    int renewedPeople{}, renewedCars{}, drownings{}, sunkCars{}, clearedJams{};
    void waterStep();
    void sinkCar(Car &car);
    void drownPerson(Person &person);
    bool personEngaged(int actor) const;
    bool carEngaged(int car) const;
    bool renewPerson(int actor);
    bool renewCar(int car);
    void renewCity();
    // Unhurried and frightened speed of a walker, metres per second.
    static double walkingPace(const Person &person);
    static double fleeingPace(const Person &person);
    double nextRoleStep{};
    size_t roleCursor{};
    int rolesTaken{}, rolesLeft{}, dutiesEnded{};
    // Its own sequence: habits must not change the seeded traffic or the wildlife.
    Random roleRandom;
    Vec2 towel{};
    int towelGuests{};
    size_t roleQuota(Person::Role role) const;
    bool roleGround(Person::Role role, Vec2 at) const;
    bool roleCandidate(int actor) const;
    bool takeRole(int actor, Person::Role role, bool unseen);
    void leaveRole(Person &person);
    void anchorWalk(Person &person);
    // The pavements run 9.1 m either side of the middle of a street, and people walk from
    // corner to corner of the junctions: across a street, or along the block to the next.
    static constexpr double walkLine = 9.1;
    static Vec2 corner(int node, int sx, int sz)
    {
        return nodePoint(node) + Vec2{sx * walkLine, sz * walkLine};
    }
    // Which legs can be walked at each junction. Bits 0 and 1: across the north-south street
    // on the south and on the north side; 2 and 3: across the east-west street on the west
    // and on the east side; 4 and 5: the block towards +x on the south and north pavement;
    // 6 and 7: the block towards +z on the west and east pavement.
    std::vector<std::uint8_t> walkLegs;
    void buildWalkLegs();
    bool crossingLights(int node, bool alongX, int side) const;
    bool walkSignal(int node, bool alongX, double seconds) const;
    void chooseWalk(Person &person);
    bool placeOnBlock(Person &person, int from, int to, int side, double along);
    Vec2 walkAim(const Person &person) const;
    Vec2 roadward(Vec2 at) const;
    void roleIntent(Person &person, Vec2 &direction, double &speed);
    void settleRoles();
    void stepRoles();
    // Seconds a walker has to wait before stepping off the kerb in front of it; zero
    // away from a kerb, at a corner without lights and while the walk signal lasts.
    double kerbWait(const Person &person) const;
    bool onCrossing(const Person &person, bool caught) const;
    bool firing{}, aiming{};
    double playerBurnUntil{}, fireClock{}, nextFlameSound{};
    double playerDistance{};
    ContactWorld physics;
    std::vector<SoundEvent> sounds;
    Density density{};
    void generate(std::uint32_t newSeed, std::function<void(float)> progress = {},
                  int layoutRevision = CityLayout::districtRevision, Density population = Density::low);
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
    // `source` replaces the city's own sequence where a choice must not disturb it.
    int chooseWalkNext(int n, int previous, Random *source = nullptr);
    std::optional<Vec2> vehicleExit(const Car &car, std::optional<Vec2> toward = {}) const;
    struct VehicleEntry
    {
        int car{-1}, side{};
        double progress{}, deadline{};
    } vehicleEntry;
    static Vec2 driverDoor(const Car &car, int side);
    bool vehicleSpace(Vec2 at, double height, const Car *ignore, bool includePlayer) const;
    bool entryPath(const Car &car, Vec2 door) const;
    bool unloadCrew(Car &car);
    void stepEntry(PlayerInput &input, double dt);
    void enterExit();
    void generateServices();
    void generateShops();
    // Bus shelters, by their roadside signs: on the west pavement of a street that runs
    // north, half-way along the block. The second list gives the stop of the block that
    // leaves a node towards +z, or -1.
    std::vector<Vec2> busStops;
    std::vector<int> busStopOfNode;
    void generateBusStops();
    // The shelter stands behind its sign.
    static constexpr Vec2 shelter(Vec2 sign) { return {sign.x - .8, sign.z}; }
    // A bus at its stop, or pulling up to it: the speed it may still do.
    double busStopLimit(Car &bus);
    // A taxi ride that ends at a bus stop keeps this in place of the number of a transit line.
    static constexpr int busStopRide = -2;
    Vec2 rideEnd(int line, int destination) const;
    // The public places of the peace jobs, as in the managed game: the food kiosks and,
    // numbered on from them, the bus stops.
    bool peacePlace(int index) const
    {
        return index >= 0 && (index < int(sites.size()) ? sites[size_t(index)].food()
                                                        : index < int(sites.size() + busStops.size()));
    }
    Vec2 peacePoint(int index) const
    {
        return index < int(sites.size()) ? sites[size_t(index)].entrance
                                         : busStops[size_t(index) - sites.size()];
    }
    int marketInReach() const;
    int garageInReach() const;
    int ownedGarage{-1}, openMarket{-1};
    static constexpr int garagePrice = 400, garageCapacity = 3;
    static constexpr double pillHealing = .35;
    bool spawnPickup(bool energy);
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
    // Ordinary traffic occupies the first slots of `cars`; response and summoned vehicles follow.
    size_t trafficPopulation = populationSize(Density::low).cars;
    static constexpr size_t maxResponseVehicles = 32, maxSpawnVehicles = 8;
    bool spawnVehicle(unsigned model);
    int pursuitNode{-1};
    std::array<int, nodes * nodes> pursuitDistances{};
    double dispatchAt{};
    void updateDispatch();
    enum class PatrolStage
    {
        stopping,
        approaching,
        returning,
        seated
    };
    struct ArrestPatrol
    {
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
    enum class RescueStage
    {
        standby,
        driving,
        care,
        returning
    };
    struct Rescue
    {
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
    // L in a piloted helicopter: descend and brake until the skids touch; ascending cancels.
    bool helicopterLanding{};
    void toggleHelicopterLanding();
    double recoveryReadyAt{};
    bool recover();
    bool safeRecoveryPose(const Car &car, Vec2 at, double heading) const;
    std::optional<Vec2> playerVehicleExit(const Car &car) const;
    bool safeExitPoint(const Car &car, Vec2 at) const;
    void shoot(double heading, double pitch = 0);
    void explode(Vec2 p, double y, double strength = 1, bool vehicleBlast = false, bool playerCaused = false);
    void destroyCar(Car &car);
    void damageCar(Car &car, double amount, bool playerCaused = false);
    void vehicleImpact(Car &car, double speedChange, Vec2 velocityChange = {});
    // A drowning is silent: no blood, no alarmed witnesses and no ambulance call.
    void killPerson(Person &person, Vec2 velocity = {}, bool playerCaused = false, bool drowned = false);
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
    ManagedImport importManaged(const std::filesystem::path &file, Density population = Density::low);
    std::wstring command(const std::string &text);
    static int nearestNode(Vec2 p);
    static Vec2 nodePoint(int n)
    {
        return {(n % nodes - (nodes - 1) * .5) * spacing, (n / nodes - (nodes - 1) * .5) * spacing};
    }
    std::vector<int> neighbours(int n) const;
    int chooseNext(int n, int previous);
    void newRoute(Car &car);
    bool intersectionClear(const Car &car) const;
    void planExit(Car &car);
    // A car is crossing this junction (the one reservation it has); `except` is not asked.
    bool junctionHeld(int node, const Car *except) const;
    // Whether two bodies stand on one surface: the same street however steep, not the
    // deck above it or the tunnel below.
    bool onSameSurface(const Body &a, const Body &b) const;
    // Actors are binned by city block for nearby drivers' periodic perception.
    struct TrafficCell
    {
        int car{-1}, person{-1};
    };
    std::vector<TrafficCell> trafficCells;
    std::vector<int> nextTrafficCar, nextTrafficPerson;
    double trafficIndexAt{};
    void indexTraffic();
    void senseTraffic(Car &car, bool local);
    bool tryChangeLane(Car &car, int destination);
    Vec2 pickupSite();
};
} // namespace pz
