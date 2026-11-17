#pragma once
#include "core.hpp"

namespace Promzona
{
    // World-space horizontal distance, with a vertical slope. The direction must be unit length.
    double ray_prism(Vec2 origin, Vec2 direction, double y, double slope, const Body &body, double limit);
    double ray_person(Vec2 origin, Vec2 direction, double y, double slope, const Body &body, double limit);
    double wanted_points(double level);
    double wanted_from_points(double points);
    std::span<const int> police_roster(int level);

    struct Rocket
    {
        Vec2 p{}, direction{};
        double height{}, slope{}, travel{};
        int owner_car{-1};
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
}
