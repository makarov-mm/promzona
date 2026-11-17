#pragma once
#include "city_mesh.hpp"

namespace Promzona
{
    // Dimensions in metres, adapted from CityArchitecture in the reference game.
    // One plan drives all visual detail levels; it never changes the simulation layout.
    struct FacadeWindow
    {
        Vec3 centre, right, normal;
        std::uint32_t seed{};
        int curtain{}, balcony{-1};
        bool air_conditioner{};
    };

    struct BuildingPlan
    {
        Vec3 origin;
        float half_width{}, half_depth{}, height{};
        bool windows_on_z{};
        BuildingKind kind{BuildingKind::apartment};
        int front{1};
        std::uint32_t seed{};
        std::vector<FacadeWindow> windows;
    };

    BuildingPlan plan_building(const Building &building, double ground);
    void building_core(CityChunk &data, const BuildingPlan &plan, int facade);
    void building_details(CityChunk &data, const BuildingPlan &plan);
    constexpr float building_detail_start = 135, building_detail_end = 185;
}
