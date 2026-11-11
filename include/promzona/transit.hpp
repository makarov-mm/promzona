#pragma once
#include "core.hpp"

namespace pz
{
struct TransitLine
{
    int kind;
    Path track;
    std::vector<double> stops;
    double wrapDistance(double s) const
    {
        s = std::fmod(s, track.length());
        return s < 0 ? s + track.length() : s;
    }
    int nextStopAfter(double along) const;
    auto pose(double s) const { return track.sample(wrapDistance(s)); }
    Vec2 curb(int stop) const
    {
        auto [position, heading] = pose(stops[stop]);
        return position + perpendicular(forward(heading)) * (kind == 9 ? 7.2 : 3.9);
    }
};
} // namespace pz
