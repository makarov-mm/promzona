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
    SurfaceCount = 28
};
using CityChunk = std::array<MeshData, SurfaceCount>;
CityChunk cityChunk(const World &world, double x0, double z0, double size = 144);
} // namespace pz
