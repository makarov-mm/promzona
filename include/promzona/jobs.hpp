#pragma once
#include "math.hpp"

namespace pz
{
enum class ServiceKind
{
    clinic,
    kiosk,
    vending,
    garage,
    market
};
enum class ShopItem { pistol, automatic, pills, energy };
struct ServiceSite
{
    ServiceKind kind;
    Vec2 centre, entrance;
    double halfWidth{}, halfDepth{}, height{};
    bool existingBuilding{};
    bool food() const { return kind == ServiceKind::kiosk || kind == ServiceKind::vending; }
    int price() const { return kind == ServiceKind::kiosk ? 15 : 5; }
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
struct PeaceRoute
{
    std::array<int, 4> sites{-1, -1, -1, -1};
    int completed{};
    bool enabled{};
    double noise{}, soundAt{}, interactAt{};
    Vec2 previous;
};
struct PassengerRide
{
    int car{-1}, line{-1};
    double boardBy{}, away{};
    double healthAtLastScare{1}, crimeAt{-1};
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
    int stopCount() const { return kind == JobKind::leaflets ? 4 : 3; }
    bool escaping() const { return peace() && route.completed == stopCount(); }
};
} // namespace pz
