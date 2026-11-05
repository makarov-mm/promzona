#pragma once
#include "core.hpp"
#include "layout.hpp"
namespace pz
{
class Terrain;
// Additional surfaces, not a replacement for the terrain/foundation datum.
// Profile and crossfall ported from CityRoadLevel in the managed game.
struct RoadLevel
{
    static constexpr double length = 144, halfWidth = 5.8, covered = 54, clearance = 4.9;
    int id{}, leftNode{}, rightNode{};
    double x{}, z{};
    bool tunnel{};
    std::array<double, 289> floorSamples{};
    double centreZ(double px) const;
    bool contains(Vec2 p, double margin = 0) const;
    bool roof(double px) const;
    bool open(Vec2 p, double margin = 0) const;
    bool reserved(Vec2 p, double radius = 0) const;
    void bake(const Terrain &terrain);
    double floor(const Terrain &terrain, Vec2 p) const;
    Path route(bool east) const;
};
// The base revision-0 generator remains independently comparable with C# fixtures.
// This pass reserves corridors, connects their endpoints and removes unsafe junctions.
std::vector<RoadLevel> generateRoadLevels(CityLayout &layout, std::uint32_t seed);
} // namespace pz
