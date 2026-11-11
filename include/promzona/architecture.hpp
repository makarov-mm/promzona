#pragma once
#include "city_mesh.hpp"
namespace pz
{
// Dimensions in metres, adapted from CityArchitecture in the reference game.
// One plan drives all visual detail levels; it never changes the simulation layout.
struct FacadeWindow
{
    Vec3 centre, right, normal;
    std::uint32_t seed{};
    int curtain{}, balcony{-1};
    bool airConditioner{};
};
struct BuildingPlan
{
    Vec3 origin;
    float halfWidth{}, halfDepth{}, height{};
    bool windowsOnZ{};
    BuildingKind kind{BuildingKind::apartment};
    int front{1};
    std::uint32_t seed{};
    std::vector<FacadeWindow> windows;
};
BuildingPlan planBuilding(const Building &building, double ground);
void buildingCore(CityChunk &data, const BuildingPlan &plan, int facade);
void buildingDetails(CityChunk &data, const BuildingPlan &plan);
constexpr float buildingDetailStart = 135, buildingDetailEnd = 185;
} // namespace pz
