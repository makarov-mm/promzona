#pragma once
#include "assets.hpp"
#include "world.hpp"
namespace pz
{
// Stable material slots consumed by the renderer; geometry itself needs no GL context.
enum CitySurface
{
    Grass = 0,
    Road = 1,
    Pavement = 2,
    Facade = 3,
    Paint = 11,
    Glass = 12,
    Water = 13,
    Sand = 14,
    Metal = 15,
    Lamps = 16,
    Windows = 17,
    Frame = 18,
    Door = 19,
    Balcony = 20, // three paints
    Roof = 23,
    Foundation = 24,
    Sign = 25, // three existing texture signs
    Rust = 28,
    Canvas,
    SeaCanvas,
    Ballast,
    WorkshopSteel,
    StationSign,
    PotholeDecal, // three photographs
    SurfaceCount = PotholeDecal + 3
};
using CityChunk = std::array<MeshData, SurfaceCount>;
inline void addCityTriangle(MeshData &mesh, Vec3 a, Vec3 b, Vec3 c)
{
    const auto base = static_cast<std::uint32_t>(mesh.vertices.size());
    const auto normal = cross(b - a, c - a).unit();
    mesh.vertices.push_back({a, normal, {.5f, 1.f}});
    mesh.vertices.push_back({b, normal, {1.f, 0.f}});
    mesh.vertices.push_back({c, normal, {0.f, 0.f}});
    mesh.indices.insert(mesh.indices.end(), {base, base + 1, base + 2});
}
CityChunk cityChunk(const World &world, double x0, double z0, double size = 144);
} // namespace pz
