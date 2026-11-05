#pragma once
#include "math.hpp"
#include <filesystem>
#include <span>
#include <string>
#include <vector>
namespace pz
{
struct Vertex
{
    Vec3 position, normal;
    std::array<float, 2> uv{};
    std::array<float, 4> joints{}, weights{1, 0, 0, 0};
};
struct MeshData
{
    std::vector<Vertex> vertices;
    std::vector<std::uint32_t> indices;
    // Material-baked far LODs keep their colour image inside the GLB.
    std::vector<unsigned char> embeddedColor;
};
struct SkinData
{
    MeshData mesh;
    std::vector<float> bones;
    int joints{}, poses{};
};
struct Image
{
    unsigned width{}, height{};
    std::vector<unsigned char> rgba;
};
MeshData loadGlb(const std::filesystem::path &file);
SkinData loadSkin(const std::filesystem::path &file);
Image loadImage(const std::filesystem::path &file);
Image decodeImage(std::span<const unsigned char> encoded);
std::string readText(const std::filesystem::path &file);
} // namespace pz
