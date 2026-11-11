#include "promzona/world.hpp"
#include <bit>
namespace pz
{
double RoadLevel::centreZ(double px) const
{
    // Piecewise linear at metre intervals: renderer, paths and support share the same curve.
    double u = clamp(px - x + length, 0., 288.);
    int k = std::min(287, int(u));
    auto at = [&](int n) { return z + 18 * smooth((length - std::abs(n - length) - 11) / 25); };
    return at(k) + (at(k + 1) - at(k)) * (u - k);
}
bool RoadLevel::contains(Vec2 p, double margin) const
{
    return std::abs(p.x - x) <= length + margin && std::abs(p.z - centreZ(p.x)) <= halfWidth + margin;
}
bool RoadLevel::roof(double px) const
{
    return tunnel && std::abs(px - x) <= covered;
}
bool RoadLevel::open(Vec2 p, double margin) const
{
    return tunnel && contains(p, margin) && !roof(p.x);
}
bool RoadLevel::reserved(Vec2 p, double radius) const
{
    return contains(p, radius + 2) && (!tunnel || std::abs(p.x - x) + radius > covered - 2);
}
void RoadLevel::bake(const Terrain &t)
{
    double left = t.height(x - length, z), right = t.height(x + length, z);
    double slope = (right - left) / (2 * length), depth = 8;
    if (tunnel)
        for (int k = 90; k <= 198; ++k)
        {
            double px = x - length + k;
            // Reserve two metres above the tunnel roof for the district/foundations.
            for (double side : {-halfWidth, 0., halfWidth})
                depth = std::max(depth, left + k * slope - t.height(px, centreZ(px) + side) + clearance + 2);
        }
    for (int k = 0; k <= 288; ++k)
    {
        double px = x - length + k, ramp = smooth((length - std::abs(px - x) - 12) / 78);
        floorSamples[k] = tunnel ? t.height(px, centreZ(px)) * (1 - ramp) + (left + k * slope - depth) * ramp
                                 : t.height(px, centreZ(px)) + 8 * ramp;
    }
}
double RoadLevel::floor(const Terrain &t, Vec2 p) const
{
    double u = clamp(p.x - x + length, 0., 288.);
    int k = std::min(287, int(u));
    double y = floorSamples[k] + (floorSamples[k + 1] - floorSamples[k]) * (u - k);
    double crossfall = smooth((std::abs(p.x - x) - (length - 30)) / 18);
    return y + crossfall * (t.height(p) - t.height(p.x, centreZ(p.x)));
}
Path RoadLevel::route(bool east) const
{
    std::vector<Vec2> points;
    // Match the street/junction path endpoints (9 m after and 11 m before each node).
    for (int n = 9; n <= 277; ++n)
    {
        double px = x + (east ? -length + n : length - n);
        points.push_back({px, centreZ(px) + (east ? CityLayout::lane : -CityLayout::lane)});
    }
    return Path(std::move(points));
}
std::vector<RoadLevel> planRoadLevels(const CityLayout &l, std::uint32_t seed)
{
    std::vector<RoadLevel> levels;
    if (l.nodes() != 49)
        return levels;
    const bool districts = l.revision() == CityLayout::districtRevision;
    LayoutRandom random(std::bit_cast<std::int32_t>(seed ^ (districts ? 0x4c371u : 0x64726177u)));
    auto add = [&](int col, int row) {
        row += random.index(3) - 1;
        RoadLevel s;
        s.id = int(levels.size()) + 1;
        s.tunnel = levels.size() % 2 == 1;
        s.x = l.coordinate(col);
        s.z = l.coordinate(row);
        s.leftNode = col - 4 + row * l.nodes();
        s.rightNode = col + 4 + row * l.nodes();
        levels.push_back(s);
    };
    if (districts)
        for (int col : {8, 24, 40})
            for (int row : {8, 23, 38})
                add(col, row);
    else
        for (auto [col,row] : std::array<std::pair<int,int>,4>{{{8,8},{24,16},{40,32},{8,40}}})
            add(col,row);
    return levels;
}

std::vector<RoadLevel> generateRoadLevels(CityLayout &l, std::uint32_t seed)
{
    auto levels = planRoadLevels(l, seed);
    for (const auto &s : levels)
    {
        int row = s.leftNode / l.nodes(), col = s.leftNode % l.nodes() + 4;
        // An entrance is a branch of the road graph, never an unmarked barrier across traffic.
        // Remove streets which would enter the side of an open cutting/merge ramp.
        for (int i = col - 4; i < col + 4; ++i)
        {
            if (std::abs(i + .5 - col) > 1.5)
                l.setRoad(i, row, 1, false);
            if (std::abs(i - col) > 1.5 && i > col - 4)
                l.setRoad(i, row, 0, false);
        }
        // Keep safe, continuous bypass streets along both sides of every reserved corridor.
        for (int i = col - 4; i <= col + 4; ++i)
        {
            l.setRoad(i, row - 1, 0, true);
            l.setRoad(i, row + 1, 0, true);
            if (i < col + 4)
            {
                l.setRoad(i, row - 1, 1, true);
                l.setRoad(i, row + 1, 1, true);
                l.setRoad(i, row + 2, 1, true);
            }
        }
    }
    auto reserved = [&](const Building &b)
    {
        // Conservative footprint reservation also covers beams and lateral grading.
        for (const auto &s : levels)
            if (s.reserved(b.p, b.half.length()))
                return true;
        return false;
    };
    std::erase_if(l.buildings, reserved);
    std::erase_if(l.furniture, reserved);
    return levels;
}
} // namespace pz
