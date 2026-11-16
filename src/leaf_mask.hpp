#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

namespace Promzona
{
// One RGB coverage texture: birch, lime, spruce. Mipmaps integrate a whole
// twig when individual leaves become smaller than a pixel.
inline std::vector<std::uint8_t> leafMask(int size)
{
    std::vector<std::uint8_t> image(size_t(size) * size * 3);
    constexpr float twigAngle = .48f;
    for (int y = 0; y < size; ++y)
        for (int x = 0; x < size; ++x)
            for (int species = 0; species < 3; ++species)
            {
                const float px = 2.f * (x + .5f) / size - 1.f;
                const float py = 2.f * (y + .5f) / size - .2f;
                float coverage = 0;
                for (int twig : {-1, 0, 1})
                {
                    const float c = std::cos(twig * twigAngle), s = std::sin(twig * twigAngle);
                    const float height = s * px + c * py;
                    if (height < 0 || height > 1.55f)
                        continue;
                    const float lateral = std::abs(c * px - s * py);
                    const float width = lateral / (1.f - .55f * height / 1.8f);
                    const float row = height * 4.f;
                    const float dx = (width - (species == 0 ? .17f : .19f)) / (species == 0   ? .123f
                                                                               : species == 1 ? .155f
                                                                                              : .18f);
                    const float dy = (row - std::floor(row) - .5f - width * (species == 0 ? .95f : .80f)) /
                                     (species == 0   ? .37f
                                      : species == 1 ? .42f
                                                     : .15f);
                    const float blade =
                        row < 6.f ? std::clamp((1.f - dx * dx - dy * dy) * 7.f, 0.f, 1.f) : 0.f;
                    const float stem = std::clamp((.025f - lateral) * 76.f, 0.f, 1.f);
                    coverage = std::max({coverage, blade, stem});
                }
                image[(size_t(y) * size + x) * 3 + species] = std::uint8_t(coverage * 255.f + .5f);
            }
    return image;
}
} // namespace Promzona
