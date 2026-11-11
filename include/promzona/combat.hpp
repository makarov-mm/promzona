#pragma once
#include "core.hpp"
namespace pz
{
// World-space horizontal distance, with a vertical slope. The direction must be unit length.
double rayPrism(Vec2 origin, Vec2 direction, double y, double slope, const Body &body, double limit);
double rayPerson(Vec2 origin, Vec2 direction, double y, double slope, const Body &body, double limit);
double wantedPoints(double level);
double wantedFromPoints(double points);
std::span<const int> policeRoster(int level);
struct Rocket
{
    Vec2 p{}, direction{};
    double height{}, slope{}, travel{};
    int ownerCar{-1};
};
enum class HitKind
{
    none,
    scenery,
    water,
    car,
    person,
    player
};
struct AttackHit
{
    HitKind kind{HitKind::none};
    int index{-1};
    double distance{};
};
} // namespace pz
