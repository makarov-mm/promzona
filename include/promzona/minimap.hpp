#pragma once
#include "math.hpp"
#include <span>
#include <vector>

namespace pz
{
struct World;
class Minimap
{
    std::vector<std::uint32_t> atlas_;
    int extent_{}, stride_{};
    int coordinate(double value) const { return extent_ - int(std::lround(value * scale)); }

  public:
    static constexpr int size = 190;
    static constexpr double scale = size / 260.;
    // Looking north (+Z), camera right is -X. Both HUD markers and the atlas use this basis.
    static Vec2 offset(Vec2 delta) { return delta * -scale; }
    void rebuild(const World &world);
    void copyView(Vec2 focus, std::span<std::uint32_t, size * size> output) const;
};
} // namespace pz
