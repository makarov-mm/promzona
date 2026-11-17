#pragma once
#include "core.hpp"
#include "layout.hpp"

namespace Promzona
{
    class Terrain;

    // Additional surfaces, not a replacement for the terrain/foundation datum.
    // Profile and crossfall ported from CityRoadLevel in the managed game.
    struct RoadLevel
    {
        static constexpr double length = 144, half_width = 5.8, covered = 54, clearance = 4.9;
        int id{}, left_node{}, right_node{};
        double x{}, z{};
        bool tunnel{};
        std::array<double, 289> floor_samples{};
        double centre_z(double px) const;
        // How far the centre line swings to one side of z between the ends of the level.
        static constexpr double bend = 18;
        // Asked for every actor on every step. Most points lie beyond the ends of the level or
        // off the strip its centre line can reach: that is decided here, before the curve is
        // evaluated. The strip is taken a micrometre wider than the curve reaches, so rounding
        // cannot change the answer.
        bool contains(Vec2 p, double margin = 0) const
        {
            const double reach = half_width + margin;
            return std::abs(p.x - x) <= length + margin && p.z >= z - reach - 1e-6 &&
                   p.z <= z + bend + reach + 1e-6 && std::abs(p.z - centre_z(p.x)) <= reach;
        }
        bool roof(double px) const;
        bool open(Vec2 p, double margin = 0) const;
        bool reserved(Vec2 p, double radius = 0) const;
        void bake(const Terrain &terrain);
        double floor(const Terrain &terrain, Vec2 p) const;
        Path route(bool east) const;
    };

    // The base revision-0 generator remains independently comparable with C# fixtures.
    // This pass reserves corridors, connects their endpoints and removes unsafe junctions.
    std::vector<RoadLevel> plan_road_levels(const CityLayout &layout, std::uint32_t seed);
    std::vector<RoadLevel> generate_road_levels(CityLayout &layout, std::uint32_t seed);
}
