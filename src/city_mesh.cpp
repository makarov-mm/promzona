#include "promzona/city_mesh.hpp"
#include "promzona/architecture.hpp"
namespace pz
{
namespace
{
void quad(MeshData &m, Vec3 a, Vec3 b, Vec3 c, Vec3 d, float uv = 1)
{
    auto normal = cross(b - a, c - a).unit();
    auto base = std::uint32_t(m.vertices.size());
    for (auto [p, t] : {std::pair{a, std::array<float, 2>{0, uv}}, std::pair{b, std::array<float, 2>{uv, uv}},
                        std::pair{c, std::array<float, 2>{uv, 0}}, std::pair{d, std::array<float, 2>{0, 0}}})
    {
        Vertex v;
        v.position = p;
        v.normal = normal;
        v.uv = t;
        m.vertices.push_back(v);
    }
    for (auto k : {0u, 1u, 2u, 0u, 2u, 3u})
        m.indices.push_back(base + k);
}
void box(MeshData &m, Vec3 p, Vec3 half, float uv = 1)
{
    auto a = p - half, b = p + half;
    quad(m, {a.x, a.y, b.z}, {b.x, a.y, b.z}, {b.x, b.y, b.z}, {a.x, b.y, b.z}, uv);
    quad(m, {b.x, a.y, a.z}, {a.x, a.y, a.z}, {a.x, b.y, a.z}, {b.x, b.y, a.z}, uv);
    quad(m, {b.x, a.y, b.z}, {b.x, a.y, a.z}, {b.x, b.y, a.z}, {b.x, b.y, b.z}, uv);
    quad(m, {a.x, a.y, a.z}, {a.x, a.y, b.z}, {a.x, b.y, b.z}, {a.x, b.y, a.z}, uv);
    quad(m, {a.x, b.y, b.z}, {b.x, b.y, b.z}, {b.x, b.y, a.z}, {a.x, b.y, a.z}, uv);
}
} // namespace
CityChunk cityChunk(const World &w, double x0, double z0, double size)
{
    CityChunk data;
    auto &l = w.layout;
    auto owns = [&](Vec2 p) { return p.x >= x0 && p.x < x0 + size && p.z >= z0 && p.z < z0 + size; };
    auto ground = [&](double x, double z, double add = 0, bool bed = false)
    {
        return Vec3{float(x), float((bed ? w.terrain.bedHeight(x, z) : w.terrain.height(x, z)) + add),
                    float(z)};
    };
    // Clip real holes in the terrain, not transparent rectangles above it.
    // The cutting edges are linear inside each metre-wide strip, just like centreZ().
    auto tile = [&](int material, double a, double b, double c, double d, double add = 0, bool bed = false)
    {
        std::vector<std::vector<Vec2>> polygons{{{a, d}, {c, d}, {c, b}, {a, b}}};
        auto clip = [](const std::vector<Vec2> &in, auto distance)
        {
            std::vector<Vec2> out;
            if (in.empty())
                return out;
            auto prev = in.back();
            double dp = distance(prev);
            for (auto cur : in)
            {
                double dc = distance(cur);
                if ((dc >= 0) != (dp >= 0))
                    out.push_back(prev + (cur - prev) * (dp / (dp - dc)));
                if (dc >= 0)
                    out.push_back(cur);
                prev = cur;
                dp = dc;
            }
            return out;
        };
        for (const auto &level : w.roadLevels)
        {
            double mid = (a + c) * .5;
            if (!level.tunnel || std::abs(mid - level.x) >= RoadLevel::length || level.roof(mid))
                continue;
            std::vector<std::vector<Vec2>> remaining;
            for (const auto &poly : polygons)
            {
                auto lower =
                    clip(poly, [&](Vec2 p) { return level.centreZ(p.x) - RoadLevel::halfWidth - p.z; });
                auto upper =
                    clip(poly, [&](Vec2 p) { return p.z - level.centreZ(p.x) - RoadLevel::halfWidth; });
                if (lower.size() >= 3)
                    remaining.push_back(std::move(lower));
                if (upper.size() >= 3)
                    remaining.push_back(std::move(upper));
            }
            polygons = std::move(remaining);
        }
        for (auto &poly : polygons)
        {
            if (poly.size() == 4)
            {
                quad(data[material], ground(poly[0].x, poly[0].z, add, bed),
                     ground(poly[1].x, poly[1].z, add, bed), ground(poly[2].x, poly[2].z, add, bed),
                     ground(poly[3].x, poly[3].z, add, bed));
            }
            else
                for (size_t i = 1; i + 1 < poly.size(); ++i)
                {
                    auto v0 = ground(poly[0].x, poly[0].z, add, bed),
                         v1 = ground(poly[i].x, poly[i].z, add, bed),
                         v2 = ground(poly[i + 1].x, poly[i + 1].z, add, bed);
                    auto normal = cross(v1 - v0, v2 - v0).unit();
                    auto base = std::uint32_t(data[material].vertices.size());
                    for (auto v : {v0, v1, v2})
                    {
                        Vertex vert;
                        vert.position = v;
                        vert.normal = normal;
                        vert.uv = {v.x * .25f, v.z * .25f};
                        data[material].vertices.push_back(vert);
                    }
                    for (auto k : {0u, 1u, 2u})
                        data[material].indices.push_back(base + k);
                }
        }
    };
    // Split at exact street, sidewalk and bridge boundaries; a regular grid alone
    // can leave grass through asphalt or stretch a bank up to a bridge deck.
    auto cuts = [&](double low, bool xAxis)
    {
        std::vector<double> v{low, low + size};
        auto add = [&](double value)
        {
            if (value > low + 1e-6 && value < low + size - 1e-6)
                v.push_back(value);
        };
        for (double q = low + 4; q < low + size; q += 4)
            add(q);
        for (int i = 0; i < l.nodes(); ++i)
        {
            double c = l.coordinate(i);
            for (double o : {-10.55, -10.1, -7.3, 0., 7.3, 10.1, 10.55})
                add(c + o);
            if (xAxis && l.canalColumn(i))
                for (double o : {-l.deckHalf(), -l.bankHalf(), 0., l.bankHalf(), l.deckHalf()})
                    add(c + 18 + o);
        }
        if (xAxis)
            for (auto &level : w.roadLevels)
                if (level.z + 25 >= z0 && level.z - 7 <= z0 + size)
                    for (int k = 0; k <= 288; ++k)
                        add(level.x - RoadLevel::length + k);
        std::ranges::sort(v);
        v.erase(std::unique(v.begin(), v.end(), [](double a, double b) { return std::abs(a - b) < 1e-6; }),
                v.end());
        return v;
    };
    auto xs = cuts(x0, true), zs = cuts(z0, false);
    for (size_t j = 1; j < zs.size(); ++j)
        for (size_t i = 1; i < xs.size(); ++i)
        {
            double a = xs[i - 1], b = zs[j - 1], c = xs[i], d = zs[j];
            Vec2 p{(a + c) * .5, (b + d) * .5};
            double distance = l.roadDistance(p);
            int material = distance < l.roadHalf() ? Road : distance < l.roadHalf() + 2.8 ? Pavement : Grass;
            if (l.riverDistance(p.x) < l.bankHalf() ||
                std::max(std::abs(p.x), std::abs(p.z)) > l.coordinate(l.nodes() - 1) + 12)
                material = Sand;
            // River bed is always drawn independently; deck never stretches down the bank.
            tile(material, a, b, c, d, 0, true);
            if (l.bridge(p))
                tile(distance < l.roadHalf() ? Road : Pavement, a, b, c, d);
        }
    // One water plane per chunk is depth-tested against the real bed/coast/decks.
    // No overlapping alpha planes and no invisible collision on top of a canal.
    double y = CityLayout::waterLevel;
    quad(data[Water], {float(x0), float(y), float(z0 + size)}, {float(x0 + size), float(y), float(z0 + size)},
         {float(x0 + size), float(y), float(z0)}, {float(x0), float(y), float(z0)}, float(size));
    for (int j = 0; j < l.nodes(); ++j)
        for (int i = 0; i < l.nodes(); ++i)
        {
            Vec2 node{l.coordinate(i), l.coordinate(j)};
            if (!owns(node))
                continue;
            for (int dir = 0; dir < 4; ++dir)
                if (l.road(i, j, dir))
                {
                    auto f = CityLayout::direction(dir), r = perpendicular(f);
                    auto paint = [&](double along, double side, double length, double width)
                    {
                        auto a = node + f * along + r * side, b = a + f * length, c = b + r * width,
                             d = a + r * width;
                        auto mid = (a + c) * .5;
                        if (w.openCut(a, .03) || w.openCut(b, .03) || w.openCut(c, .03) ||
                            w.openCut(d, .03) || l.roadDistance(mid) > l.roadHalf() ||
                            (l.riverDistance(mid.x) < l.bankHalf() && !l.bridge(mid)))
                            return;
                        quad(data[Paint], ground(a.x, a.z, .025), ground(b.x, b.z, .025),
                             ground(c.x, c.z, .025), ground(d.x, d.z, .025));
                    };
                    // Draw each inter-junction edge once, stripes/crosswalks on every connected arm.
                    if (dir < 2)
                        for (double a = 12; a < 26; a += 5)
                        {
                            paint(a, -.07, 2, .14);
                            paint(a, -3.7, 2, .12);
                            paint(a, 3.6, 2, .12);
                        }
                    if (l.degree(i + j * l.nodes()) >= 3)
                        for (double s = -6.5; s < 6.5; s += 1.5)
                            paint(8.8, s, 1.5, .75);
                }
        }
    // Bridge edges follow the same profile as the collision surface. No rail across its entrance.
    for (int col : {l.nodes() / 3, 2 * l.nodes() / 3})
        for (int row = 0; row < l.nodes(); row += 6)
        {
            Vec2 p{l.coordinate(col) + 18, l.coordinate(row)};
            if (!l.bridge(p) || !owns(p))
                continue;
            for (int side : {-1, 1})
                for (double t = -l.deckHalf(); t < l.deckHalf(); t += 1)
                {
                    double x = p.x + t, end = std::min(p.x + l.deckHalf(), x + 1), z = p.z + side * 10.35;
                    auto a = ground(x, z), b = ground(end, z);
                    quad(data[Pavement], a, b, b - Vec3{0, .38f, 0}, a - Vec3{0, .38f, 0});
                    quad(data[Metal], a + Vec3{0, .9f, 0}, b + Vec3{0, .9f, 0}, b + Vec3{0, 1, 0},
                         a + Vec3{0, 1, 0});
                    if (int(t + l.deckHalf()) % 2 == 0)
                        box(data[Metal], a + Vec3{0, .5f, 0}, {.045f, .5f, .045f});
                }
        }
    for (const auto &level : w.roadLevels)
    {
        auto point = [&](double x, double side, double offset = 0.)
        {
            Vec2 p{x, level.centreZ(x) + side};
            return Vec3{float(x), float(level.floor(w.terrain, p) + offset), float(p.z)};
        };
        auto strip = [&](int mat, double x, double end, double left, double right, double offset = 0.)
        {
            quad(data[mat], point(x, right, offset), point(end, right, offset), point(end, left, offset),
                 point(x, left, offset));
        };
        for (int k = 0; k < 288; ++k)
        {
            double x = level.x - RoadLevel::length + k, end = x + 1;
            if (!owns({x + .5, level.centreZ(x + .5)}))
                continue;
            strip(Road, x, end, -4.5, 0);
            strip(Road, x, end, 0, 4.5);
            for (int side : {-1, 1})
            {
                strip(Pavement, x, end, side > 0 ? 4.5 : -5.8, side > 0 ? 5.8 : -4.5, .12);
                auto a = point(x, side * 4.5), b = point(end, side * 4.5);
                quad(data[Pavement], a, b, b + Vec3{0, .12f, 0}, a + Vec3{0, .12f, 0});
                strip(Paint, x, end, side * 4.15 - .065, side * 4.15 + .065, .025);
                if (k < 3 || k >= 285)
                    continue;
                double edge = side * RoadLevel::halfWidth;
                auto lo = point(x, edge, level.tunnel ? 0 : -.55),
                     hi = point(end, edge, level.tunnel ? 0 : -.55);
                auto top = point(x, edge, level.tunnel ? RoadLevel::clearance : .85),
                     topEnd = point(end, edge, level.tunnel ? RoadLevel::clearance : .85);
                if (level.tunnel && !level.roof(x + .5))
                {
                    top.y = float(
                        std::max(w.terrain.height(x, top.z) + .6, level.floor(w.terrain, {x, top.z}) + .8));
                    topEnd.y = float(std::max(w.terrain.height(end, topEnd.z) + .6,
                                              level.floor(w.terrain, {end, topEnd.z}) + .8));
                }
                Vec3 outward{0, 0, float(side * .20)};
                quad(data[Pavement], lo, hi, topEnd, top);
                quad(data[Pavement], hi + outward, lo + outward, top + outward, topEnd + outward);
                quad(data[Pavement], top, topEnd, topEnd + outward, top + outward);
            }
            if (k % 6 < 3)
                strip(Paint, x, end, -.07, .07, .026);
            if (!level.tunnel)
            {
                quad(data[Pavement], point(x, -5.8, -.55), point(end, -5.8, -.55), point(end, 5.8, -.55),
                     point(x, 5.8, -.55));
            }
            else if (level.roof(x + .5))
            {
                quad(data[Pavement], point(x, -5.8, RoadLevel::clearance),
                     point(end, -5.8, RoadLevel::clearance), point(end, 5.8, RoadLevel::clearance),
                     point(x, 5.8, RoadLevel::clearance));
                if (k % 9 == 0)
                    for (int side : {-1, 1})
                    {
                        box(data[Metal], point(x + .5, side * 3.8, RoadLevel::clearance - .09),
                            {.5f, .09f, .18f});
                        box(data[Lamps], point(x + .5, side * 3.8, RoadLevel::clearance - .19),
                            {.4f, .02f, .12f});
                    }
            }
        }
        if (level.tunnel)
        {
            for (int side : {-1, 1})
            {
                double x = level.x + side * RoadLevel::covered;
                if (!owns({x, level.centreZ(x)}))
                    continue;
                auto a = point(x, -5.8, RoadLevel::clearance), b = point(x, 5.8, RoadLevel::clearance);
                auto c = b, d = a;
                c.y = float(w.terrain.height(c.x, c.z));
                d.y = float(w.terrain.height(d.x, d.z));
                quad(data[Pavement], a, b, c, d);
            }
        }
        else
            for (double x = level.x - 54; x <= level.x + 54; x += 36)
            {
                if (!owns({x, level.centreZ(x)}))
                    continue;
                for (int side : {-1, 1})
                {
                    auto p = point(x, side * 7.1, -.55);
                    float bottom = float(w.terrain.height(p.x, p.z));
                    if (p.y > bottom)
                        box(data[Pavement], {p.x, (p.y + bottom) * .5f, p.z},
                            {.7f, (p.y - bottom) * .5f, .7f});
                }
                box(data[Pavement], point(x, 0, -.9), {.8f, .35f, 7.8f});
            }
    }
    for (auto &b : w.buildings)
    {
        if (!owns(b.p))
            continue;
        buildingCore(data, planBuilding(b, w.terrain.height(b.p)), b.style);
    }
    for (auto &b : l.furniture)
    {
        if (!owns(b.p))
            continue;
        auto p = ground(b.p.x, b.p.z);
        if (b.height > 3)
        {
            box(data[Metal], p + Vec3{0, float(b.height * .5), 0}, {.07f, float(b.height * .5), .07f});
            box(data[Metal], p + Vec3{.3f, float(b.height), 0}, {.36f, .05f, .08f});
            box(data[Lamps], p + Vec3{.5f, float(b.height - .06), 0}, {.19f, .03f, .13f});
        }
        else
        {
            box(data[Metal], p + Vec3{0, .45f, 0}, {.25f, .45f, .25f});
            box(data[Pavement], p + Vec3{0, .92f, 0}, {.28f, .035f, .28f});
        }
    }
    return data;
}
} // namespace pz
