#pragma once
#include "math.hpp"
namespace pz
{
// A local orthographic sun map. Its xy origin is snapped in light space, so
// a sub-texel camera motion does not make stationary shadows crawl.
struct SunShadow
{
    static constexpr int resolution = 2048;
    static constexpr float halfSize = 110, depth = 520;
    Mat4 matrix;
    Vec3 centre;
    float strength{};
};
inline SunShadow sunShadow(Vec3 focus, Vec3 sun)
{
    SunShadow s;
    auto light = sun.unit();
    s.strength = float(smooth((light.y - .025) / .10));
    auto right = cross(light * -1, Vec3{0, 1, 0}).unit();
    if (right.length() < .1f)
        right = {1, 0, 0};
    auto up = cross(right, light * -1);
    float texel = 2 * SunShadow::halfSize / SunShadow::resolution;
    auto snap = [texel](float v) { return std::floor(v / texel + .5f) * texel; };
    s.centre = focus + right * (snap(dot(right, focus)) - dot(right, focus)) +
               up * (snap(dot(up, focus)) - dot(up, focus));
    // Explicit basis remains valid with a sun directly above the observer.
    Mat4 view = Mat4::identity();
    auto eye = s.centre + light * (SunShadow::depth * .5f);
    view.m = {right.x, up.x, light.x, 0, right.y,          up.y,          light.y,          0,
              right.z, up.z, light.z, 0, -dot(right, eye), -dot(up, eye), -dot(light, eye), 1};
    Mat4 projection = Mat4::identity();
    projection.m[0] = projection.m[5] = 1 / SunShadow::halfSize;
    projection.m[10] = -2 / SunShadow::depth;
    projection.m[14] = -1;
    s.matrix = projection * view;
    return s;
}
} // namespace pz
