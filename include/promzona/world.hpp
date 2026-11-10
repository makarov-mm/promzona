#pragma once
#include "physics.hpp"
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
struct Person
{
    Body body;
    Vec2 target;
    int node{}, next{};
    int riding{-1}, seat{}, alight{};
    double boardAfter{};
    bool dead{};
    double fear{}, heading{}, phase{}, distance{}, health{1}, burnUntil{};
    bool burnPlayerCaused{};
    WeaponId sidearm{};
    double alertUntil{}, aimSeconds{}, shotAt{};
    int burstLeft{};
    static WeaponId civilianWeapon(std::uint32_t citySeed, std::uint32_t id);
    std::uint32_t style{};
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
    Inventory inventory;
    std::vector<Building> buildings;
    std::vector<std::vector<unsigned>> roofCells;
    void indexRoofs();
    double flightFloor(Vec2 pos) const;
    bool insideBuilding(Vec2 pos, double y, double radius) const;
    std::vector<TreePlant> trees;
    std::vector<Car> cars;
    std::vector<Person> people;
    std::vector<Pickup> pickups;
    std::vector<TransitLine> lines;
    void generateTransit();
    void runTransit(Car &car, Input controls, bool driving, bool local, double dt);
    void alignTram(Car &car, double dt);
    void exchangePassengers(Car &car, int stop);
    void seatPassenger(Person &person);
    std::vector<ServiceSite> sites;
    Job job;
    ReconDrone drone;
    void toggleDrone(double heading);
    void stopDrone(const wchar_t *message, double delay);
    void stepDrone(const PlayerInput &input, double dt);
    void refreshDroneObstacles(Vec2 centre);
    bool droneClear(Vec2 point, double height, double radius) const;
    int jobsCompleted{}, jobsFailed{}, lastPay{};
    double energySeconds{}, clinicCredit{}, purchaseAt{}, noticeUntil{};
    std::wstring notice;
    std::vector<Particle> particles;
    std::vector<Rocket> rockets;
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
    void generate(std::uint32_t newSeed, std::function<void(float)> progress = {});
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
    std::optional<Vec2> vehicleExit(const Car &car) const;
    void enterExit();
    void generateServices();
    int nearestSite(ServiceKind kind, Vec2 from) const;
    int counterInReach(bool parkedAllowed) const;
    bool buyFood();
    void interactJob();
    bool startPassengerJob(JobKind kind);
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
    void civilianCombat(Person &person, double dt);
    void alarmWitnesses(Vec2 origin, double height, bool playerCaused);
    void flame(Vec2 direction, double y);
    void addCrime(double severity);
    void reportCrime(double severity, double radius = 45, bool sight = true);
    int wantedLevel() const { return wanted <= .001 ? 0 : int(std::ceil(wanted)); }
    void save(const std::filesystem::path &path) const;
    void load(const std::filesystem::path &path);
    void command(const std::string &text);
    static Vec2 nodePoint(int n)
    {
        return {(n % nodes - (nodes - 1) * .5) * spacing, (n / nodes - (nodes - 1) * .5) * spacing};
    }
    std::vector<int> neighbours(int n) const;
    int chooseNext(int n, int previous);
    void newRoute(Car &car);
    bool intersectionClear(const Car &car) const;
    Vec2 pickupSite();
};
} // namespace pz
