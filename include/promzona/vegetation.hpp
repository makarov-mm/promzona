#pragma once
#include "assets.hpp"
#include <array>

namespace Promzona
{
    struct World;

    enum class TreeKind
    {
        birch,
        linden,
        spruce
    };

    struct TreePlant
    {
        Vec3 foot;
        float height{}, radius{};
        TreeKind kind{};
        std::uint32_t seed{};
    };

    // Independent seeded stream: adding vegetation never changes traffic or saved actor IDs.
    std::vector<TreePlant> planTrees(const World &world);

    // Bark and cutout foliage. Vertex joints = {wind weight, phase, colour variation, species}.
    using TreeMesh = std::array<MeshData, 2>;
    void append_tree(TreeMesh &mesh, const TreePlant &plant, bool far);
    constexpr float tree_lod_start = 85, tree_lod_end = 115, tree_draw_distance = 620;
}
