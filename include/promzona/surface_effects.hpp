#pragma once
#include "math.hpp"
#include <cstdint>
#include <vector>

namespace Promzona
{
    enum class MarkKind
    {
        rubber,
        wet,
        sand,
        blood
    };

    struct SurfaceMark
    {
        Vec2 from, to;
        double born{}, duration{};
        int level{};
        MarkKind kind{};
        std::array<Vec3, 4> corners{};
    };

    // What a collision leaves behind for a minute and a half, as in the managed game: dark
    // strips along the flanks of the car, or a patch on the wall it struck.
    struct Scuff
    {
        static constexpr double onCar = 100, onWall = 90;
        // On a wall: the middle of the patch and whether the wall faces along x. Otherwise
        // the id of the car.
        bool wall{};
        std::uint32_t car{};
        Vec3 at;
        bool facesX{};
        double born{};
        double duration() const { return wall ? onWall : onCar; }
    };

    struct WaterRipple
    {
        static constexpr double lifetime = 3.;
        Vec2 centre;
        double born{}, strength{};
    };

    // A hole in the asphalt: a decal of half-width `size` that jolts a car driving over it.
    struct Pothole
    {
        Vec2 p;
        double size{};
        int style{};
    };

    // Holes of the block north-east of node (i, j), relative to that node, exactly as the
    // managed CityRoadDetail.Holes yields them. The flags say whether a street leaves the
    // node toward +Z and toward +X.
    std::vector<Pothole> blockPotholes(std::uint32_t seed, int i, int j, bool roadNorth, bool roadEast);

    struct RoadPuddle
    {
        static constexpr double radiusX = 1.48, radiusZ = .96;
        Vec2 centre;
        std::array<Vec3, 4> corners;
    };

    struct TireContact
    {
        Vec2 wheels[2]{};
        int level{};
        MarkKind kind{};
        bool active{};
        double wetUntil{}, nextSqueal{}, nextSplash{}, nextPuff{};
        bool skidding{}, inPuddle{};
    };

    // The same world column keeps its absolute raindrop height while the camera moves.
    // Wrap correctly for long sessions, negative world coordinates and an airborne camera.
    inline double rain_height(double phase, double seconds, double eyeY)
    {
        constexpr double period = 28.;
        double base = eyeY - 8., cycles = phase - (seconds * 14. + base) / period;
        return base + period * (cycles - std::floor(cycles));
    }
}
