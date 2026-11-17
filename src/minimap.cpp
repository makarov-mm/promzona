#include <utility>

#include "promzona/minimap.hpp"
#include "promzona/world.hpp"

namespace Promzona
{
    namespace
    {
        constexpr std::uint32_t water = 0x2b5565;
    }

    void Minimap::rebuild(const World &world)
    {
        const auto &layout = world.layout;
        extent_ = static_cast<int>(std::ceil(layout.edge() * scale));
        stride_ = 2 * extent_ + 1;
        atlas_.assign(static_cast<size_t>(stride_) * stride_, water);

        auto rect = [&](int x0, int z0, int x1, int z1, std::uint32_t color)
        {
            int left = std::clamp(std::min(x0, x1), 0, stride_);
            int right = std::clamp(std::max(x0, x1) + 1, 0, stride_);
            int top = std::clamp(std::min(z0, z1), 0, stride_);
            int bottom = std::clamp(std::max(z0, z1) + 1, 0, stride_);

            for (int y = top; y < bottom; ++y)
                std::fill_n(atlas_.data() + static_cast<size_t>(y) * stride_ + left, right - left, color);
        };

        auto box = [&](Vec2 a, Vec2 b, std::uint32_t color)
        {
            rect(coordinate(a.x), coordinate(a.z), coordinate(b.x), coordinate(b.z), color);
        };

        double beach = layout.edge() - 12, city = layout.coordinate(layout.nodes() - 1) + layout.roadHalf();
        box({-beach, -beach}, {beach, beach}, 0xab976c);
        box({-city, -city}, {city, city}, 0x253a2d);

        if (layout.rivers())
            for (int col : {layout.nodes() / 3, 2 * layout.nodes() / 3})
            {
                double x = layout.coordinate(col) + layout.spacing / 2;
                box({x - layout.bankHalf(), -beach}, {x + layout.bankHalf(), beach}, water);
            }

        if (layout.districts())
        {
            // Only the coastal strip needs terrain sampling. Use the same water datum as the mesh.
            const int inner = static_cast<int>(std::floor(city * scale));

            for (int row = 0; row < stride_; ++row)
            {
                const double z = (extent_ - row) / scale;

                auto paintShore = [&](int first, int last)
                {
                    for (int col = first; col < last; ++col)
                    {
                        const double x = (extent_ - col) / scale;

                        atlas_[static_cast<size_t>(row) * stride_ + col] =
                            world.terrain.bed_height(x, z) > CityLayout::waterLevel ? 0xab976c : water;
                    }
                };

                if (std::abs(row - extent_) > inner)
                {
                    paintShore(0, stride_);
                }
                else
                {
                    paintShore(0, extent_ - inner);
                    paintShore(extent_ + inner + 1, stride_);
                }
            }
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

        if (layout.districts())
            box({layout.railStart(), layout.railZ() - 1}, {layout.railEnd(), layout.railZ() + 1}, 0x786a54);

        for (const auto &b : world.buildings)
        {
            const int color = b.kind == BuildingKind::apartment
                            ? 0xa1a087
                            : b.kind == BuildingKind::cottage
                            ? 0xa99177
                            : b.kind == BuildingKind::garage
                            ? 0x85877a
                            : 0x928071;

            box(b.p - b.half, b.p + b.half, color);
        }

        for (const auto &level : world.roadLevels)
            for (int k = 0; k <= 2 * RoadLevel::length; ++k)
            {
                double x = level.x - RoadLevel::length + k, z = level.centreZ(x);
                box({x, z - 2.8}, {x + 1, z + 2.8}, level.tunnel ? 0x778792 : 0xb1a887);
            }

        for (const TransitLine &line : world.lines)
            for (int stop = 0; std::cmp_less(stop, line.stops.size()); ++stop)
            {
                auto p = line.curb(stop);
                int x = coordinate(p.x), z = coordinate(p.z);
                rect(x - 2, z - 2, x + 2, z + 2, 0x2d575d);
                rect(x - 1, z - 1, x + 1, z + 1, 0x8ce1d3);
            }
        for (const auto &s : world.sites)
        {
            const int x = coordinate(s.entrance.x);
            const int y = coordinate(s.entrance.z);

            if (s.kind == ServiceKind::clinic)
            {
                rect(x - 3, y - 1, x + 3, y + 1, 0x78cdf9);
                rect(x - 1, y - 3, x + 1, y + 3, 0x78cdf9);
            }
            else
                rect(x - 1, y - 1, x + 1, y + 1,
                     s.kind == ServiceKind::market ? 0xc987d8
                     : s.food()                    ? 0x6ed386
                                                   : 0xc29851);
        }
    }

    void Minimap::copy_view(Vec2 focus, std::span<std::uint32_t, size * size> output) const
    {
        const int x = coordinate(focus.x) - size / 2;
        const int y = coordinate(focus.z) - size / 2;
        const int left = std::clamp(-x, 0, size);
        const int right = std::clamp(stride_ - x, 0, size);

        for (int row = 0; row < size; ++row)
        {
            uint32_t *dst = output.data() + size * row;

            if (y + row < 0 || y + row >= stride_ || left >= right)
            {
                std::fill_n(dst, size, water);
            }
            else
            {
                std::fill_n(dst, left, water);
                std::copy_n(atlas_.data() + static_cast<size_t>(y + row) * stride_ + (x + left), right - left, dst + left);
                std::fill(dst + right, dst + size, water);
            }
        }
    }
}
