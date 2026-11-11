#include "promzona/minimap.hpp"
#include "promzona/world.hpp"

namespace pz
{
namespace
{
constexpr std::uint32_t water = 0x2b5565;
}

void Minimap::rebuild(const World &world)
{
    const auto &layout = world.layout;
    extent_ = int(std::ceil(layout.edge() * scale));
    stride_ = 2 * extent_ + 1;
    atlas_.assign(size_t(stride_) * stride_, water);
    auto rect = [&](int x0, int z0, int x1, int z1, std::uint32_t color)
    {
        int left = std::clamp(std::min(x0, x1), 0, stride_);
        int right = std::clamp(std::max(x0, x1) + 1, 0, stride_);
        int top = std::clamp(std::min(z0, z1), 0, stride_);
        int bottom = std::clamp(std::max(z0, z1) + 1, 0, stride_);
        for (int y = top; y < bottom; ++y)
            std::fill_n(atlas_.data() + size_t(y) * stride_ + left, right - left, color);
    };
    auto box = [&](Vec2 a, Vec2 b, std::uint32_t color)
    { rect(coordinate(a.x), coordinate(a.z), coordinate(b.x), coordinate(b.z), color); };
    double beach = layout.edge() - 12, city = layout.coordinate(layout.nodes() - 1) + layout.roadHalf();
    box({-beach, -beach}, {beach, beach}, 0xab976c);
    box({-city, -city}, {city, city}, 0x253a2d);
    if (layout.rivers())
        for (int col : {layout.nodes() / 3, 2 * layout.nodes() / 3})
        {
            double x = layout.coordinate(col) + layout.spacing / 2;
            box({x - layout.bankHalf(), -beach}, {x + layout.bankHalf(), beach}, water);
        }
    for (int j = 0; j < layout.nodes(); ++j)
        for (int i = 0; i < layout.nodes(); ++i)
        {
            auto p = layout.point(i + j * layout.nodes());
            double h = layout.roadHalf();
            if (layout.road(i, j, 0))
                box(p - Vec2{h, h}, p + Vec2{h, layout.spacing + h}, 0x5c5e58);
            if (layout.road(i, j, 1))
                box(p - Vec2{h, h}, p + Vec2{layout.spacing + h, h}, 0x5c5e58);
        }
    for (const auto &b : world.buildings)
        box(b.p - b.half, b.p + b.half, 0xa1a087);
    for (const auto &level : world.roadLevels)
        for (int k = 0; k <= 2 * RoadLevel::length; ++k)
        {
            double x = level.x - RoadLevel::length + k, z = level.centreZ(x);
            box({x, z - 2.8}, {x + 1, z + 2.8}, level.tunnel ? 0x778792 : 0xb1a887);
        }
    for (const auto &s : world.sites)
    {
        int x = coordinate(s.entrance.x), y = coordinate(s.entrance.z);
        if (s.kind == ServiceKind::clinic)
        {
            rect(x - 3, y - 1, x + 3, y + 1, 0x78cdf9);
            rect(x - 1, y - 3, x + 1, y + 3, 0x78cdf9);
        }
        else
            rect(x - 1, y - 1, x + 1, y + 1, s.food() ? 0x6ed386 : 0xc29851);
    }
}

void Minimap::copyView(Vec2 focus, std::span<std::uint32_t, size * size> output) const
{
    int x = coordinate(focus.x) - size / 2, y = coordinate(focus.z) - size / 2;
    int left = std::clamp(-x, 0, size), right = std::clamp(stride_ - x, 0, size);
    for (int row = 0; row < size; ++row)
    {
        auto *dst = output.data() + size * row;
        if (y + row < 0 || y + row >= stride_ || left >= right)
            std::fill_n(dst, size, water);
        else
        {
            std::fill(dst, dst + left, water);
            std::copy_n(atlas_.data() + size_t(y + row) * stride_ + (x + left), right - left, dst + left);
            std::fill(dst + right, dst + size, water);
        }
    }
}
} // namespace pz
