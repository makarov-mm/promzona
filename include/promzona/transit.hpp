#pragma once
#include "core.hpp"

namespace Promzona
{
    struct TransitLine
    {
        int kind;
        Path track;
        std::vector<double> stops;

        double wrap_distance(double s) const
        {
            s = std::fmod(s, track.length());
            return s < 0 ? s + track.length() : s;
        }

        int next_stop_after(double along) const;

        auto pose(double s) const
        {
            return track.sample(wrap_distance(s));
        }

        Vec2 curb(const int stop) const
        {
            auto [position, heading] = pose(stops[stop]);
            return position + perpendicular(forward(heading)) * (kind == 9 ? 7.2 : 3.9);
        }
    };
}
