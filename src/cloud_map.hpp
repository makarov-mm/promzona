#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

namespace Promzona
{
    inline std::vector<std::uint8_t> cloud_map(const int size)
    {
        auto lattice = [](const unsigned x, const unsigned y)
        {
            std::uint32_t h = x * 0x9e3779b1u + y * 0x85ebca77u + 0x165667b1u;
            h ^= h >> 16;
            h *= 0x7feb352du;
            return static_cast<float>((h ^ (h >> 15)) >> 8) / 16777216.f;
        };

        auto octave = [&](const float u, const float v, const int period)
        {
            const float x = u * period;
            const float y = v * period;
            const int i = static_cast<int>(x), j = static_cast<int>(y);
            const float dx = x - i, dy = y - j;
            const float sx = dx * dx * (3 - 2 * dx), sy = dy * dy * (3 - 2 * dy);

            auto at = [&](const int a, const int b)
            {
                return lattice(a % period, b % period);
            };

            return std::lerp(std::lerp(at(i, j), at(i + 1, j), sx), std::lerp(at(i, j + 1), at(i + 1, j + 1), sx), sy);
        };

        std::vector<std::uint8_t> pixels(static_cast<size_t>(size) * size);

        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                const float u = (.5f + x) / size;
                const float v = (.5f + y) / size;
                float density = 0, amplitude = .53f;

                for (const int period : {8, 16, 32, 64})
                {
                    density += amplitude * octave(u, v, period);
                    amplitude *= .48f;
                }

                pixels[static_cast<size_t>(y) * size + x] = static_cast<std::uint8_t>(std::clamp(density, 0.f, 1.f) * 255.f + .5f);
            }
        }

        return pixels;
    }
}
