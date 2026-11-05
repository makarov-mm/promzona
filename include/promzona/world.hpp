#pragma once
#include "physics.hpp"
#include "combat.hpp"
#include "layout.hpp"
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
    bool dead{};
    double fear{}, heading{}, phase{}, distance{}, health{1}, burnUntil{};
    bool burnPlayerCaused{};
    std::uint32_t style{};
};
struct Pickup
{
    Vec2 p;
    WeaponId weapon{};
    bool health{};
    double wait{};
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
    double lookHeading{}, lookPitch{};
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
    Clock clock;
    Inventory inventory;
    std::vector<Building> buildings;
    std::vector<Car> cars;
    std::vector<Person> people;
    std::vector<Pickup> pickups;
    std::vector<Particle> particles;
    std::vector<Rocket> rockets;
    std::vector<Body> staticBodies;
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
    double ground() const { return terrain.height(position()); }
    void enterExit();
    void recover();
    void shoot(double heading, double pitch = 0);
    void explode(Vec2 p, double y, double strength = 1, bool vehicleBlast = false);
    void destroyCar(Car &car);
    void damageCar(Car &car, double amount);
    void vehicleImpact(Car &car, double velocityChange);
    void killPerson(Person &person, Vec2 velocity = {});
    AttackHit traceAttack(Vec2 from, Vec2 direction, double y, double slope, double range, int ignoreCar = -1,
                          bool includePlayer = false) const;
    bool blastVisible(Vec2 from, double y, Vec2 to, double targetY) const;
    void combatStep(double dt);
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
