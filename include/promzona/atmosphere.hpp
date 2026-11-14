#pragma once
#include "math.hpp"

namespace Promzona
{
    // The sky and the haze for the weather and the hour: a port of the managed game's
    // CityAtmosphere. An exponential layer stands in for the haze; this is no volumetric
    // weather. The colours are those of the finished picture, not of linear light.
    struct Atmosphere
    {
        float density{.00052f}, heightScale{140};
        Vec3 horizon {.x = .64f, .y = .73f, .z = .80f}, zenith {.x = .19f, .y = .39f, .z = .65f};
        float rain{}, daylight{1}, golden{};

        static Atmosphere create(double rain, double daylight, double sunHeight, double wetness, bool evening = false)
        {
            auto finite = [](double value, double otherwise)
            {
                return std::isfinite(value) ? value : otherwise;
            };

            auto step = [](float low, float high, float value)
            {
                return static_cast<float>(smooth((value - low) / (high - low)));
            };

            auto blend = [](Vec3 a, Vec3 b, float t) { return a + (b - a) * t; };

            const float r = static_cast<float>(clamp(finite(rain, 0), 0., 1.)),
                d = static_cast<float>(clamp(finite(daylight, 1), 0., 1.)),
                w = static_cast<float>(clamp(finite(wetness, 0), 0., 1.)),
                height = static_cast<float>(finite(sunHeight, 1));

            const float clear = (1 - r) * (1 - r);
            Atmosphere air;
            air.golden = step(-.12f, .07f, height) * (1 - step(.15f, .52f, height)) * clear;
            const float twilight = (1 - step(.02f, .18f, std::abs(height + .045f))) * clear;
            air.horizon = blend({.64f, .73f, .80f}, {.39f, .44f, .49f}, r);
            air.horizon = blend(blend({.025f, .036f, .058f}, {.018f, .026f, .038f}, r), air.horizon, d);
            air.horizon = blend(air.horizon, evening ? Vec3{.56f, .31f, .38f} : Vec3{.49f, .42f, .55f}, twilight * .62f);
            air.horizon = blend(air.horizon, evening ? Vec3{.90f, .55f, .38f} : Vec3{.94f, .73f, .54f}, air.golden * .68f);
            air.zenith = blend({.19f, .39f, .65f}, {.24f, .28f, .33f}, r);
            air.zenith = blend({.006f, .012f, .027f}, air.zenith, d);
            air.zenith = blend(air.zenith, {.14f, .21f, .40f}, .46f * twilight);
            air.density = .00052f + .0028f * r + .00018f * w * (1 - d) + .00024f * air.golden;
            air.heightScale = 140 + 100 * r;
            air.rain = r;
            air.daylight = d;
            return air;
        }

        // Share of the light that crosses `distance` metres of haze between two heights.
        double transmittance(double distance, double eyeHeight = 0, double targetHeight = 0) const
        {
            const double a = std::max(0., eyeHeight) / heightScale, b = std::max(0., targetHeight) / heightScale;
            const double layer = std::abs(b - a) < .001 ? std::exp(-(a + b) * .5) : (std::exp(-a) - std::exp(-b)) / (b - a);
            return std::exp(-std::min(80., std::max(0., distance) * density * layer));
        }
    };
}
