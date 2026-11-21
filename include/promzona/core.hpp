#pragma once
#include "math.hpp"
#include <optional>
#include <span>
#include <string_view>
#include <vector>
#include <utility>

namespace Promzona
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
        double closest_distance(Vec2 p) const;
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
        int pickup_ammo;
        double interval, range, car_damage, person_damage;
        int npc_burst;
        double npc_interval, npc_rest, npc_car_damage;
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
        static constexpr int ammo_limit = 9999;
        int ammo(WeaponId id) const;

        bool owns(const WeaponId id) const
        {
            return weapon(id) && owned_[std::to_underlying(id)];
        }

        bool has_weapons() const
        {
            return std::ranges::any_of(owned_, [](const bool x) { return x; });
        }

        auto ownership() const
        {
            return owned_;
        }

        auto selection() const
        {
            return selected_;
        }

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

        auto capture() const
        {
            return ammo_;
        }

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

        bool is_static() const
        {
            return invMass == 0;
        }

        void wake()
        {
            sleeping = false;
            sleepTimer = 0;
        }

        Vec2 velocity_at(const Vec2& point) const
        {
            return v + perpendicular(point - p) * w;
        }

        void impulse(const Vec2& impulse, const Vec2& offset = {})
        {
            wake();
            v += impulse * invMass;
            w += cross(offset, impulse) * invI;
        }

        void mass(const double kilograms)
        {
            invMass = kilograms > 0 ? 1 / kilograms : 0;
            invI = kilograms > 0 ? 3 / (kilograms * (halfW * halfW + halfH * halfH)) : 0;
        }
    };

    struct Car
    {
        Body body;
        int kind{};
        double mass{1060}, wheelbase{2.42}, steering{}, lift{}, verticalVelocity{}, roll{}, pitch{}, terrainPitch{}, terrainRoll{};
        Vec2 terrainGradient{};
        bool terrainActive{}, falling{}, destroyed{}, recovering{}, braking{}, wreckSupported{};
        double health{1}, fireTime{}, routeDistance{}, wheelTravel{}, turretYaw{}, turretPitch{},lastDamageAt{-100}, safeDrivingTime{}, impactCooldown{};
        int paint{};
        bool personal{}, driverless{};
        int doorSide{};
        double doorOpen{}, doorHold{};
        bool responding{}, seesSuspect{}, firePlayerCaused{}, summoned{};
        // Crew members who left the vehicle for good (killed, or left behind on the street).
        // A crew is finite: a patrol car cannot send out more officers than it carries.
        int crewLost{};
        double nextShot{}, nextSightCheck{};
        std::uint32_t model{}, id{};
        int routeFrom{}, routeTo{}, previousNode{-1}, routePhase{};
        int routeLevel{}, laneIndex{};

        // The junction after routeTo, chosen on the way to it: a driver enters a crossing only
        // when the street beyond has room. Not saved; a loaded car simply chooses again.
        int planned_to{-1};

        // The junction a tram or a trolleybus is ready to cross: no car enters it first.
        int claimed{-1};

        // A bus stands at its stop until the first and passes the stops by until the second.
        double bus_until{}, next_bus_stop{};

        // Where and since when the car has not moved a metre: a driver who has waited two
        // minutes for nothing gives up out of sight (World::renewCity). Not saved.
        Vec2 stall_anchor{};
        double stall_since{};

        // A driver pushed off the route who cannot go on backs up a little and tries again
        // (the managed CityCar.Stuck and ReverseFor). Saved while rejoining traffic.
        double wedged_for{}, reverse_for{};

        // A crew on a call goes round whoever is ahead: by this much steering when it makes
        // straight for the suspect (the managed PolicePursuit), and pulled out of its lane by
        // `overtake` metres, on the way to `pullOut`, when it follows the street (the managed
        // OvertakeOffset). Not saved.
        double avoid{}, overtake{}, pullOut{};

        // On its way back to the street after a call, until it is in a lane (World::rejoinRoad).
        bool rejoining{};

        // Moved by the distant model in the last step. Not saved.
        bool distant{};
        double blocked_since{-1}, next_lane_decision{}, sense_at{}, traffic_limit{100};
        int line{-1}, next_stop{};
        double along{}, dwell{}, service_target{7}, check_traffic_at{};
        std::optional<Path> route;
        double heading() const { return -body.angle; }
        double roll_rate{}, pitch_rate{}, overturned_time{}, righting_time{}, righting_roll{}, righting_pitch{};
        void begin_righting();
        void apply_impact_rotation(Vec2 velocity_change);
        void settle_attitude(double dt, bool player_driving);
        bool mounted_gun() const { return kind == 11 || kind == 14; }
        bool grounded() const { return lift < .12 && std::cos(roll) * std::cos(pitch) > .45; }
        void destroy();
    };

    void step_vehicle(Car &car, Input input, double dt, double traction = 1);
    // The pedals and the wheel under the driver of a burning car, who is out of his mind:
    // full throttle towards wherever he happens to turn. `time` is that of the world.
    Input panic_drive(Car &car, double time, double dt);
    void step_wreck(Car &car, double dt);
}
