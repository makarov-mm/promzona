#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <numbers>
namespace pz
{
constexpr double pi = std::numbers::pi;
template <class T> constexpr T clamp(T x, T lo, T hi)
{
    return std::clamp(x, lo, hi);
}
inline double smooth(double t)
{
    t = clamp(t, 0., 1.);
    return t * t * (3 - 2 * t);
}
inline double wrap(double a)
{
    return std::remainder(a, 2 * pi);
}
struct Vec2
{
    double x{}, z{};
    Vec2 operator+(Vec2 b) const { return {x + b.x, z + b.z}; }
    Vec2 operator-(Vec2 b) const { return {x - b.x, z - b.z}; }
    Vec2 operator-() const { return {-x, -z}; }
    Vec2 operator*(double s) const { return {x * s, z * s}; }
    Vec2 operator/(double s) const { return {x / s, z / s}; }
    Vec2 &operator+=(Vec2 b)
    {
        x += b.x;
        z += b.z;
        return *this;
    }
    Vec2 &operator-=(Vec2 b)
    {
        x -= b.x;
        z -= b.z;
        return *this;
    }
    Vec2 &operator*=(double s)
    {
        x *= s;
        z *= s;
        return *this;
    }
    double length2() const { return x * x + z * z; }
    double length() const { return std::sqrt(length2()); }
    Vec2 unit() const { return *this / std::max(1e-12, length()); }
};
inline double dot(Vec2 a, Vec2 b)
{
    return a.x * b.x + a.z * b.z;
}
inline double cross(Vec2 a, Vec2 b)
{
    return a.x * b.z - a.z * b.x;
}
inline Vec2 perpendicular(Vec2 a)
{
    return {-a.z, a.x};
}
inline Vec2 rotate(Vec2 a, double r)
{
    return {a.x * std::cos(r) - a.z * std::sin(r), a.x * std::sin(r) + a.z * std::cos(r)};
}
inline Vec2 forward(double heading)
{
    return {std::sin(heading), std::cos(heading)};
}
struct Vec3
{
    float x{}, y{}, z{};
    Vec3 operator+(Vec3 b) const { return {x + b.x, y + b.y, z + b.z}; }
    Vec3 operator-(Vec3 b) const { return {x - b.x, y - b.y, z - b.z}; }
    Vec3 operator*(float s) const { return {x * s, y * s, z * s}; }
    float length() const { return std::sqrt(x * x + y * y + z * z); }
    Vec3 unit() const { return *this * (1 / std::max(1e-8f, length())); }
};
inline Vec3 cross(Vec3 a, Vec3 b)
{
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}
inline float dot(Vec3 a, Vec3 b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
struct Mat4
{
    std::array<float, 16> m{};
    static Mat4 identity()
    {
        Mat4 r;
        r.m = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
        return r;
    }
    static Mat4 translate(Vec3 p)
    {
        auto r = identity();
        r.m[12] = p.x;
        r.m[13] = p.y;
        r.m[14] = p.z;
        return r;
    }
    static Mat4 scale(Vec3 s)
    {
        auto r = identity();
        r.m[0] = s.x;
        r.m[5] = s.y;
        r.m[10] = s.z;
        return r;
    }
    static Mat4 yaw(float a)
    {
        auto r = identity();
        r.m[0] = r.m[10] = std::cos(a);
        r.m[2] = -std::sin(a);
        r.m[8] = std::sin(a);
        return r;
    }
    static Mat4 perspective(float fov, float aspect, float nearZ, float farZ)
    {
        Mat4 r;
        float f = 1 / std::tan(fov * .5f);
        r.m[0] = f / aspect;
        r.m[5] = f;
        r.m[10] = (farZ + nearZ) / (nearZ - farZ);
        r.m[11] = -1;
        r.m[14] = 2 * farZ * nearZ / (nearZ - farZ);
        return r;
    }
    static Mat4 pitch(float a)
    {
        auto r = identity();
        r.m[5] = r.m[10] = std::cos(a);
        r.m[6] = std::sin(a);
        r.m[9] = -std::sin(a);
        return r;
    }
    static Mat4 roll(float a)
    {
        auto r = identity();
        r.m[0] = r.m[5] = std::cos(a);
        r.m[1] = std::sin(a);
        r.m[4] = -std::sin(a);
        return r;
    }
    static Mat4 lookAt(Vec3 eye, Vec3 target)
    {
        auto f = (target - eye).unit(), s = cross(f, {0, 1, 0}).unit(), u = cross(s, f);
        auto r = identity();
        r.m = {s.x, u.x, -f.x, 0, s.y,          u.y,          -f.y,        0,
               s.z, u.z, -f.z, 0, -dot(s, eye), -dot(u, eye), dot(f, eye), 1};
        return r;
    }
    Mat4 operator*(const Mat4 &b) const
    {
        Mat4 r;
        for (int c = 0; c < 4; c++)
            for (int row = 0; row < 4; row++)
                for (int k = 0; k < 4; k++)
                    r.m[c * 4 + row] += m[k * 4 + row] * b.m[c * 4 + k];
        return r;
    }
};
// Identical sequence to CityTraffic.Random: seeds are portable across runtimes.
struct Random
{
    std::uint32_t state{1};
    explicit Random(std::uint32_t seed = 1) : state(seed | 1u) {}
    double next()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state + .5) / 4294967296.;
    }
    int index(int n) { return static_cast<int>(next() * n); }
};
} // namespace pz
