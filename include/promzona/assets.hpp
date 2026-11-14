#pragma once
#include "math.hpp"
#include <filesystem>
#include <span>
#include <string>
#include <vector>

namespace Promzona
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

    MeshData load_glb(const std::filesystem::path &file);
    SkinData load_skin(const std::filesystem::path &file);
    Image load_image(const std::filesystem::path &file);
    Image decode_image(std::span<const unsigned char> encoded);
    std::string read_text(const std::filesystem::path &file);
}
