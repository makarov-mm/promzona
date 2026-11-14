#pragma once
#include "math.hpp"

namespace Promzona
{
    enum class ServiceKind
    {
        clinic,
        kiosk,
        vending,
        garage,
        market
    };

    enum class ShopItem
    {
        pistol,
        automatic,
        pills,
        energy
    };

    struct ServiceSite
    {
        ServiceKind kind{};
        Vec2 centre, entrance;
        double halfWidth{}, halfDepth{}, height{};
        bool existingBuilding{};

        bool food() const
        {
            return kind == ServiceKind::kiosk || kind == ServiceKind::vending;
        }

        int price() const
        {
            return kind == ServiceKind::kiosk ? 15 : 5;
        }
    };

    enum class JobKind
    {
        none,
        courier,
        vehicle,
        elimination,
        leaflets,
        broadcast,
        taxi,
        patient
    };

    // How near the address a ride ends: ArriveRadius of the managed game.
    constexpr double arrive_radius(JobKind kind)
    {
        return kind == JobKind::patient ? 14 : 11;
    }

    struct PeaceRoute
    {
        std::array<int, 4> sites{-1, -1, -1, -1};
        // The free point of pavement beside each place, by its number on the rings of
        // World::peaceOffset; -1 is the place itself, as in saves from before the search.
        std::array<int, 4> spots{-1, -1, -1, -1};
        int completed{};
        bool enabled{};
        double noise{}, sound_at{}, interact_at{};
        Vec2 previous;
    };

    struct PassengerRide
    {
        int car{-1}, line{-1};
        double board_by{}, away{};
        double health_at_last_scare{1}, crime_at{-1};
        int scares{};
    };

    struct Job
    {
        JobKind kind{};
        int actor{-1}, destination{-1}, fare{};
        bool acquired{};
        double deadline{}, stopped{};
        PeaceRoute route{};
        PassengerRide ride{};
        bool series{};
        bool passenger() const { return kind == JobKind::taxi || kind == JobKind::patient; }
        bool peace() const { return kind == JobKind::leaflets || kind == JobKind::broadcast; }
        int stop_count() const { return kind == JobKind::leaflets ? 4 : 3; }
        bool escaping() const { return peace() && route.completed == stop_count(); }
    };
}
