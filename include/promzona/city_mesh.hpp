#pragma once
#include "assets.hpp"
#include "world.hpp"

namespace Promzona
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

    inline void add_city_triangle(MeshData &mesh, Vec3 a, Vec3 b, Vec3 c)
    {
        const auto base = static_cast<std::uint32_t>(mesh.vertices.size());
        const auto normal = cross(b - a, c - a).unit();
        mesh.vertices.push_back({a, normal, {.5f, 1.f}});
        mesh.vertices.push_back({b, normal, {1.f, 0.f}});
        mesh.vertices.push_back({c, normal, {0.f, 0.f}});
        mesh.indices.insert(mesh.indices.end(), {base, base + 1, base + 2});
    }

    // Heights of a rendered, unclipped lawn quad at (west,south), (east,south),
    // (west,north), (east,north). The triangle diagonal runs from NW to SE.
    struct LawnTile
    {
        float x0, z0, x1, z1, sw, se, nw, ne;
        float height(float x, float z) const
        {
            const float u = (x - x0) / (x1 - x0), v = (z - z0) / (z1 - z0);
            return u + v <= 1 ? sw + u * (se - sw) + v * (nw - sw)
                              : ne + (1 - u) * (nw - ne) + (1 - v) * (se - ne);
        }
    };

    CityChunk city_chunk(const World &world, double x0, double z0, double size = 144, std::vector<LawnTile> *lawn = nullptr);
}
