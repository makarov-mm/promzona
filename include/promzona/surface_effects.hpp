#pragma once
#include "math.hpp"

namespace pz
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
struct TireContact
{
    Vec2 wheels[2]{};
    int level{};
    MarkKind kind{};
    bool active{};
};

// The same world column keeps its absolute raindrop height while the camera moves.
// Wrap correctly for long sessions, negative world coordinates and an airborne camera.
inline double rainHeight(double phase, double seconds, double eyeY)
{
    constexpr double period = 28.;
    double base = eyeY - 8., cycles = phase - (seconds * 14. + base) / period;
    return base + period * (cycles - std::floor(cycles));
}
} // namespace pz
