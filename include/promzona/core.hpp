#pragma once
#include "math.hpp"
#include <optional>
#include <span>
#include <string_view>
#include <vector>
namespace pz
{
// Where a waiting car's centre is, metres before the middle of the junction: the nose of
// an ordinary car stays clear of the zebra (8.8 to 10.3 m) and of whoever walks on it.
// The managed game, and the native one before 12.11.2026, stopped on the zebra at 11 m.
inline constexpr double stopLine = 12.6;
class Path
{
    std::vector<Vec2> points_;
    std::vector<double> arc_;

  public:
    Path(Vec2 a, Vec2 b);
    Path(Vec2 a, Vec2 b, Vec2 c, Vec2 d);
    explicit Path(std::vector<Vec2> points);
    static Path turnaround(Vec2 centre, Vec2 direction, double lane = 1.9);
    double length() const { return arc_.back(); }
    std::pair<Vec2, double> sample(double distance) const;
    double closestDistance(Vec2 p) const;
    const auto &points() const { return points_; }
};
enum class WeaponId : int
{
    none,
    pistol,
    automatic,
    bazooka,
    flamethrower
};
struct Weapon
{
    WeaponId id;
    int power;
    std::string_view name;
    int pickupAmmo;
    double interval, range, carDamage, personDamage;
    int npcBurst;
    double npcInterval, npcRest, npcCarDamage;
    std::string_view sound;
};
std::span<const Weapon> weapons();
const Weapon *weapon(WeaponId id);
class Inventory
{
    std::array<int, 5> ammo_{};
    std::array<bool, 5> owned_{};
    std::optional<WeaponId> selected_;

  public:
    static constexpr int ammoLimit = 9999;
    int ammo(WeaponId id) const;
    bool owns(WeaponId id) const { return weapon(id) && owned_[static_cast<int>(id)]; }
    bool hasWeapons() const
    {
        return std::ranges::any_of(owned_, [](bool x) { return x; });
    }
    auto ownership() const { return owned_; }
    auto selection() const { return selected_; }
    bool give(WeaponId id, std::optional<int> amount = {});
    const Weapon *active() const;
    bool select(WeaponId id);
    bool consume(WeaponId id);
    void cycle(int direction);
    void clear()
    {
        ammo_ = {};
        owned_ = {};
        selected_.reset();
    }
    auto capture() const { return ammo_; }
    void restore(const std::array<int, 5> &values, const std::array<bool, 5> &owned);
};
struct Input
{
    double throttle{}, steering{};
    bool brake{}, handbrake{};
    Input sanitized() const;
};
struct Body
{
    Vec2 p{}, v{};
    double angle{}, w{}, invMass{}, invI{}, halfW{.9}, halfH{2.1}, radius{.32};
    double friction{.45}, restitution{.15}, height{}, verticalExtent{1.6};
    int roadLevel{};
    double verticalSpeed{};
    bool circle{}, sleeping{};
    double sleepTimer{};
    std::uint32_t id{};
    bool isStatic() const { return invMass == 0; }
    void wake()
    {
        sleeping = false;
        sleepTimer = 0;
    }
    Vec2 velocityAt(Vec2 point) const { return v + perpendicular(point - p) * w; }
    void impulse(Vec2 impulse, Vec2 offset = {})
    {
        wake();
        v += impulse * invMass;
        w += cross(offset, impulse) * invI;
    }
    void mass(double kilograms)
    {
        invMass = kilograms > 0 ? 1 / kilograms : 0;
        invI = kilograms > 0 ? 3 / (kilograms * (halfW * halfW + halfH * halfH)) : 0;
    }
};
struct Car
{
    Body body;
    int kind{};
    double mass{1060}, wheelbase{2.42}, steering{}, lift{}, verticalVelocity{}, roll{}, pitch{},
        terrainPitch{}, terrainRoll{};
    Vec2 terrainGradient{};
    bool terrainActive{}, falling{}, destroyed{}, recovering{}, braking{}, wreckSupported{};
    double health{1}, fireTime{}, routeDistance{}, wheelTravel{}, turretYaw{}, turretPitch{},
        lastDamageAt{-100}, safeDrivingTime{}, impactCooldown{};
    int paint{};
    bool personal{}, driverless{};
    int doorSide{};
    double doorOpen{}, doorHold{};
    bool responding{}, seesSuspect{}, firePlayerCaused{}, summoned{};
    double nextShot{}, nextSightCheck{};
    std::uint32_t model{}, id{};
    int routeFrom{}, routeTo{}, previousNode{-1}, routePhase{};
    int routeLevel{}, laneIndex{};
    // The junction after routeTo, chosen on the way to it: a driver enters a crossing only
    // when the street beyond has room. Not saved; a loaded car simply chooses again.
    int plannedTo{-1};
    // The junction a tram or a trolleybus is ready to cross: no car enters it first.
    int claimed{-1};
    // A bus stands at its stop until the first and passes the stops by until the second.
    double busUntil{}, nextBusStop{};
    // Where and since when the car has not moved a metre: a driver who has waited two
    // minutes for nothing gives up out of sight (World::renewCity). Not saved.
    Vec2 stallAnchor{};
    double stallSince{};
    // A driver pushed off the route who cannot go on backs up a little and tries again
    // (the managed CityCar.Stuck and ReverseFor). Not saved.
    double wedgedFor{}, reverseFor{};
    // Moved by the distant model in the last step. Not saved.
    bool distant{};
    double blockedSince{-1}, nextLaneDecision{}, senseAt{}, trafficLimit{100};
    int line{-1}, nextStop{};
    double along{}, dwell{}, serviceTarget{7}, checkTrafficAt{};
    std::optional<Path> route;
    double heading() const { return -body.angle; }
    double rollRate{}, pitchRate{}, overturnedTime{}, rightingTime{}, rightingRoll{}, rightingPitch{};
    void beginRighting();
    void applyImpactRotation(Vec2 velocityChange);
    void settleAttitude(double dt, bool playerDriving);
    bool mountedGun() const { return kind == 11 || kind == 14; }
    bool grounded() const { return lift < .12 && std::cos(roll) * std::cos(pitch) > .45; }
    void destroy();
};
void stepVehicle(Car &car, Input input, double dt, double traction = 1);
void stepWreck(Car &car, double dt);
} // namespace pz
