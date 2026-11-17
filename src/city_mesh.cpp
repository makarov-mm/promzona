#include "promzona/city_mesh.hpp"
#include "promzona/architecture.hpp"
#include <set>
#include <ranges>

namespace Promzona
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
CityChunk city_chunk(const World &w, double x0, double z0, double size, std::vector<LawnTile> *lawn)
{
    CityChunk data;
    auto &l = w.layout;
    auto owns = [&](Vec2 p) { return p.x >= x0 && p.x < x0 + size && p.z >= z0 && p.z < z0 + size; };
    auto ground = [&](double x, double z, double add = 0, bool bed = false) {
        return Vec3{float(x), float((bed ? w.terrain.bed_height(x, z) : w.terrain.height(x, z)) + add),
                    float(z)};
    };
    // Rails and contact wires follow the very same polyline that constrains the tram.
    std::set<std::array<int, 5>> segments;
    std::set<std::pair<int, int>> poles;
    std::set<std::pair<int, int>> platforms;
    for (const auto &line : w.lines)
    {
        for (int stop = 0; stop < int(line.stops.size()); ++stop)
        {
            auto direction = forward(line.pose(line.stops[stop]).second);
            auto post = line.curb(stop) + direction * 1.6 + perpendicular(direction) * .50;
            if (!owns(post) || !l.urbanSite(post, .35) ||
                !platforms.emplace(int(std::round(post.x)), int(std::round(post.z))).second)
                continue;
            Vec3 foot = ground(post.x, post.z, .06);
            box(data[Metal], foot + Vec3{0, 1.26f, 0}, {.042f, 1.26f, .042f});
            const bool eastWest = std::abs(direction.x) > .5;
            auto plate = [&](int surface, float width, float halfHeight, float depth, float height)
            {
                box(data[surface], foot + Vec3{0, height, 0},
                    eastWest ? Vec3{width, halfHeight, depth} : Vec3{depth, halfHeight, width});
            };
            plate(SeaCanvas, .32f, .39f, .048f, 2.38f);
            plate(Metal, .23f, .14f, .054f, 2.43f);
            plate(Frame, .17f, .048f, .061f, 2.46f);
            plate(Metal, .17f, .019f, .054f, 2.14f);
        }
        const auto &points = line.track.points();
        for (size_t i = 1; i < points.size(); ++i)
        {
            auto a = points[i - 1], b = points[i], mid = (a + b) * .5;
            if (!owns(mid))
                continue;
            auto f = (b - a).unit(), r = perpendicular(f);
            auto quant = [](double x) { return int(std::round(x * 100)); };
            if (!segments.insert({quant(a.x), quant(a.z), quant(b.x), quant(b.z), line.kind}).second)
                continue;
            auto previous = i > 1 ? points[i - 2] : points[points.size() - 2];
            auto next = i + 1 < points.size() ? points[i + 1] : points[1];
            auto ra = perpendicular((b - previous).unit()), rb = perpendicular((next - a).unit());
            auto strip = [&](double offset, double width, double height, int material)
            {
                auto corner = [&](Vec2 p) { return ground(p.x, p.z, height); };
                quad(data[material], corner(a + ra * (offset - width)), corner(a + ra * (offset + width)),
                     corner(b + rb * (offset + width)), corner(b + rb * (offset - width)));
            };
            if (line.kind == 9)
                for (double rail : {-.762, .762})
                    strip(rail, .045, .042, Metal);
            for (int n = 0; n < (line.kind == 9 ? 1 : 2); ++n)
            {
                const double wire = line.kind == 9 ? 0. : (n ? .28 : -.28);
                auto u = a + ra * wire, v = b + rb * wire;
                auto base = ground(u.x, u.z, 5.75), end = ground(v.x, v.z, 5.75);
                appendThinRail(data[ThinRail], base, end, .028f);
            }
            double along = std::abs(f.x) > .999 ? a.x : std::abs(f.z) > .999 ? a.z : 1e10;
            // Stops occupy the block midpoint. Keep the overhead-wire mast clear of the boarding area.
            if (along == 1e10 || std::abs(std::remainder(along - l.coordinate(0) - 12., 36.)) > .05)
                continue;
            auto post = a + r * (line.kind == 9 ? 7.2 : 3.9);
            if (!l.urbanSite(post, .25) || l.roadDistance(post) < l.roadHalf() + .4 ||
                !poles.emplace(quant(post.x), quant(post.z)).second)
                continue;
            auto foot = ground(post.x, post.z);
            box(data[Metal], foot + Vec3{0, 2.93f, 0}, {.065f, 2.93f, .065f});
            auto end = ground(a.x, a.z, 5.79), root = foot + Vec3{0, 5.84f, 0},
                 edge = Vec3{float(f.x * .035), 0, float(f.z * .035)};
            quad(data[Metal], root - edge, root + edge, end + edge, end - edge);
            quad(data[Metal], end - edge, end + edge, root + edge, root - edge);
        }
    }
    // Clip real holes in the terrain, not transparent rectangles above it.
    // The cutting edges are linear inside each metre-wide strip, just like centreZ().
    auto clippedTile = [&](int material, double a, double b, double c, double d, double add = 0, bool bed = false)
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
        const auto repeated =
            std::ranges::unique(v, [](double a, double b) { return std::abs(a - b) < 1e-6; });
        v.erase(repeated.begin(), repeated.end());
        return v;
    };
    auto xs = cuts(x0, true), zs = cuts(z0, false);
    struct CutBounds { double low{1e20}, high{-1e20}; };
    std::vector<CutBounds> cutsByColumn(xs.size());
    for (size_t i = 1; i < xs.size(); ++i)
    {
        const double mid = (xs[i - 1] + xs[i]) * .5;
        for (const auto &level : w.roadLevels)
        {
            if (!level.tunnel || std::abs(mid - level.x) >= RoadLevel::length || level.roof(mid))
                continue;
            const auto left = level.centreZ(xs[i - 1]), right = level.centreZ(xs[i]);
            auto &cut = cutsByColumn[i];
            cut.low = std::min(cut.low, std::min(left, right) - RoadLevel::halfWidth);
            cut.high = std::max(cut.high, std::max(left, right) + RoadLevel::halfWidth);
        }
    }
    // Adjacent tiles share their bed samples. Most tiles need no polygon clipping.
    std::vector<Vec3> bedGrid;
    bedGrid.reserve(xs.size() * zs.size());
    for (auto z : zs)
        for (auto x : xs)
            bedGrid.push_back(ground(x, z, 0, true));
    for (size_t j = 1; j < zs.size(); ++j)
        for (size_t i = 1; i < xs.size(); ++i)
        {
            double a = xs[i - 1], b = zs[j - 1], c = xs[i], d = zs[j];
            Vec2 p{(a + c) * .5, (b + d) * .5};
            double distance = l.roadDistance(p);
            int material = distance < l.roadHalf() ? Road : distance < l.roadHalf() + 2.8 ? Pavement : Grass;
            if (l.sandyGround(p, distance))
                material = Sand;
            else if (material == Grass && l.districts())
            {
                const int blockX = int(std::floor((p.x - l.coordinate(0)) / l.spacing));
                const int blockZ = int(std::floor((p.z - l.coordinate(0)) / l.spacing));
                if (l.industrial(blockX, blockZ))
                    material = Ballast;
                else if (blockX >= 0 && blockZ >= 0 && blockX < l.nodes() - 1 && blockZ < l.nodes() - 1 &&
                         l.district(blockX, blockZ) == District::garages && !l.park(blockX, blockZ))
                    material = Ballast;
            }
            // River bed is always drawn independently; deck never stretches down the bank.
            const bool clip = d >= cutsByColumn[i].low && b <= cutsByColumn[i].high;
            if (clip)
                clippedTile(material, a, b, c, d, 0, true);
            else
            {
                const size_t k = j * xs.size() + i;
                if (lawn && material == Grass)
                    lawn->push_back({float(a), float(b), float(c), float(d), bedGrid[k - xs.size() - 1].y,
                                     bedGrid[k - xs.size()].y, bedGrid[k - 1].y, bedGrid[k].y});
                quad(data[material], bedGrid[k - 1], bedGrid[k], bedGrid[k - xs.size()],
                     bedGrid[k - xs.size() - 1]);
                if (material == Grass && l.courtyard(p))
                    for (auto &v : std::span(data[Grass].vertices).last(4))
                        v.joints[0] = 1;
            }
            if (l.bridge(p))
            {
                const int deck = distance < l.roadHalf() ? Road : Pavement;
                if (clip)
                    clippedTile(deck, a, b, c, d);
                else
                    quad(data[deck], ground(a, d), ground(c, d), ground(c, b), ground(a, b));
            }
        }
    // Each stone belongs to the chunk containing its midpoint. Its bevel follows the terrain slope.
    const int firstX = int(std::floor((x0 - l.coordinate(0)) / l.spacing));
    const int firstZ = int(std::floor((z0 - l.coordinate(0)) / l.spacing));
    for (int j = firstZ; l.coordinate(j) < z0 + size; ++j)
        for (int i = firstX; l.coordinate(i) < x0 + size; ++i)
            if (const auto *yard = l.courtyard(i, j))
                for (const auto &stone : yard->curbs)
                {
                    if (!owns((stone.a + stone.b) * .5))
                        continue;
                    const auto side = perpendicular((stone.b - stone.a).unit());
                    auto point = [&](Vec2 end, double width, double h)
                    {
                        auto p = end + side * width;
                        return ground(p.x, p.z, h, true);
                    };
                    // A slightly battered shoulder catches grazing sunlight.
                    constexpr std::array<Vec2, 6> profile{{{-.092, -.06},
                                                           {-.092, .10},
                                                           {-.068, .133},
                                                           {.068, .133},
                                                           {.092, .10},
                                                           {.092, -.06}}};
                    auto &mesh = data[Foundation];
                    const auto base = std::uint32_t(mesh.vertices.size());
                    const std::array<Vec2, 6> normals{
                        {{-1, 0}, {-1, .2}, {-.3, 1}, {.3, 1}, {1, .2}, {1, 0}}};
                    for (auto end : {stone.a, stone.b})
                        for (const auto &[offset, normal] : std::views::zip(profile, normals))
                        {
                            Vertex v;
                            v.position = point(end, offset.x, offset.z);
                            v.normal =
                                Vec3{float(side.x * normal.x), float(normal.z), float(side.z * normal.x)}
                                    .unit();
                            mesh.vertices.push_back(v);
                        }
                    for (unsigned k = 0; k < 5; ++k)
                        mesh.indices.insert(mesh.indices.end(), {base + k, base + k + 1, base + k + 7,
                                                                 base + k, base + k + 7, base + k + 6});
                    // The ends have independent flat normals on the narrow expansion joints.
                    for (int end = 0; end < 2; ++end)
                    {
                        const auto cap = std::uint32_t(mesh.vertices.size());
                        const auto direction = (stone.b - stone.a).unit() * (end ? 1. : -1.);
                        for (unsigned k = 0; k < 6; ++k)
                        {
                            Vertex v;
                            v.position = mesh.vertices[base + end * 6 + k].position;
                            v.normal = {float(direction.x), 0, float(direction.z)};
                            mesh.vertices.push_back(v);
                        }
                        for (unsigned k = 1; k < 5; ++k)
                            if (end)
                                mesh.indices.insert(mesh.indices.end(), {cap, cap + k, cap + k + 1});
                            else
                                mesh.indices.insert(mesh.indices.end(), {cap, cap + k + 1, cap + k});
                    }
                }
    // Signed bed depth and exposure to the open sea are interpolated over wet cells.
    // Shared positions are sampled in world coordinates, including sector boundaries.
    auto &water = data[Water];
    const int divisions = std::max(1, int(std::ceil(size / 3.)));
    const double step = size / divisions;
    struct WaterSample
    {
        Vertex vertex;
        std::optional<std::uint32_t> meshIndex;
    };
    std::array<std::vector<WaterSample>, 2> waterRows;
    for (auto &samples : waterRows)
        samples.resize(divisions + 1);
    auto indexOf = [&](WaterSample &sample)
    {
        if (!sample.meshIndex)
        {
            sample.meshIndex = static_cast<std::uint32_t>(water.vertices.size());
            water.vertices.push_back(sample.vertex);
        }
        return *sample.meshIndex;
    };
    for (int iz = 0; iz <= divisions; ++iz)
    {
        auto &current = waterRows[iz % 2];
        auto &previous = waterRows[(iz + 1) % 2];
        for (int ix = 0; ix <= divisions; ++ix)
        {
            const double x = x0 + ix * step, z = z0 + iz * step;
            const double inland = l.edge() - std::max(std::abs(x), std::abs(z));
            const float depth = float(CityLayout::waterLevel - w.terrain.bed_height(x, z));
            current[ix] = {.vertex = {.position = {float(x), float(CityLayout::waterLevel), float(z)},
                                      .normal = {0, 1, 0},
                                      .uv = {depth, float(1. - smooth((inland - 12.) / 42.))}},
                           .meshIndex = std::nullopt};
            if (!iz || !ix)
                continue;
            auto &a = current[ix - 1], &b = current[ix], &c = previous[ix], &d = previous[ix - 1];
            if (std::max({a.vertex.uv[0], b.vertex.uv[0], c.vertex.uv[0], d.vertex.uv[0]}) < -.5f)
                continue;
            const auto ia = indexOf(a), ib = indexOf(b), ic = indexOf(c), id = indexOf(d);
            water.indices.insert(water.indices.end(), {ia, ib, ic, ia, ic, id});
        }
    }
    if (l.districts())
    {
        const double z = l.railZ();
        if (z >= z0 && z < z0 + size)
            for (double x = l.railStart(); x < l.railEnd(); x += 1.)
            {
                if (!owns({x + .5, z}))
                    continue;
                auto band = [&](int mat, double x1, double x2, double offset, double halfWidth, double height)
                {
                    quad(data[mat], ground(x1, z + offset + halfWidth, height),
                         ground(x2, z + offset + halfWidth, height),
                         ground(x2, z + offset - halfWidth, height),
                         ground(x1, z + offset - halfWidth, height));
                };
                bool crossing = l.roadDistance({x + .5, z}) < l.roadHalf();
                if (!crossing)
                {
                    band(Ballast, x, x + 1, 0, 1.65, .025);
                    band(Rust, x + .36, x + .61, 0, 1.2, .050);
                }
                for (double offset : {-.762, .762})
                    band(Metal, x, x + 1, offset, .055, crossing ? .045 : .094);
            }

        for (int side = 0; side < 4; ++side)
            for (int i = 0; i < l.nodes() - 1; ++i)
            {
                if ((i + side) % 3 != 1)
                    continue;
                const double along = l.coordinate(i) + l.spacing * .5;
                for (int k = -1; k <= 1; ++k)
                {
                    const double shore = l.edge() - 38 + (k % 2) * 3.;
                    Vec2 p = CityLayout::direction(side) * shore +
                             perpendicular(CityLayout::direction(side)) * (along + k * 4.);
                    if (!owns(p) || l.riverDistance(p.x) < l.deckHalf() + 2 ||
                        !l.sandyGround(p, l.roadDistance(p)) ||
                        w.terrain.bed_height(p.x, p.z) < CityLayout::waterLevel + .65)
                        continue;
                    auto at = ground(p.x, p.z);
                    box(data[Metal], at + Vec3{0, 1.07f, 0}, {.032f, 1.07f, .032f});
                    int cloth = (i + side + k) % 2 ? Canvas : SeaCanvas;
                    quad(data[cloth], ground(p.x - .65, p.z + 1.1, .028), ground(p.x + .65, p.z + 1.1, .028),
                         ground(p.x + .65, p.z - 1.1, .028), ground(p.x - .65, p.z - 1.1, .028));
                    auto rim = [&](int n)
                    {
                        double a = n * pi / 6;
                        return at + Vec3{float(std::cos(a) * 1.4), 1.85f, float(std::sin(a) * 1.4)};
                    };
                    auto top = at + Vec3{0, 2.2f, 0};
                    for (int n = 0; n < 12; ++n)
                        add_city_triangle(data[n % 2 ? Paint : cloth], top, rim(n + 1), rim(n));
                }
            }
    }
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
                        // A stripe has area. At the coast a legal centre alone can still put its tip on sand.
                        if (l.revision() &&
                            std::max(std::abs(mid.x), std::abs(mid.z)) >= l.coordinate(l.nodes() - 1) - 12 &&
                            std::ranges::any_of(
                                std::array{a, b, c, d},
                                [&](Vec2 v) { return !l.bridge(v) && l.sandyGround(v, l.roadDistance(v)); }))
                            return;
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
                    double x = p.x + t, end = std::min(p.x + l.deckHalf(), x + 1),
                           z = p.z + side * (l.roadHalf() + 3.05);
                    auto a = ground(x, z), b = ground(end, z);
                    quad(data[Pavement], a, b, b - Vec3{0, l.canalDeckThickness, 0},
                         a - Vec3{0, l.canalDeckThickness, 0});
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
        auto plan = plan_building(b, w.terrain.height(b.p));
        building_core(data, plan, b.style);
        if ((b.p + Vec2{0, b.half.z + 1.25} - w.stationEntrance).length2() < .01)
        {
            const Vec3 centre = plan.origin + Vec3{0, 4.f, plan.half_depth + .18f};
            box(data[Metal], centre, {4.f, .65f, .08f});
            Vec3 face = centre + Vec3{0, 0, .085f};
            quad(data[StationSign], face + Vec3{-3.95f, -.60f, 0}, face + Vec3{3.95f, -.60f, 0},
                 face + Vec3{3.95f, .60f, 0}, face + Vec3{-3.95f, .60f, 0});
        }
    }
    for (const auto &site : w.sites)
    {
        if (site.existing_building || !owns(site.centre))
            continue;
        auto p = ground(site.centre.x, site.centre.z);
        if (site.kind == ServiceKind::garage)
        {
            float d = float(site.half_depth), h = float(site.height), width = float(site.half_width);
            box(data[Foundation], p + Vec3{0, h / 2, 0}, {width, h / 2, d});
            box(data[Roof], p + Vec3{0, h, 0}, {width + .13f, .11f, d + .15f});
            box(data[Metal], p + Vec3{0, 1.16f, -d - .04f}, {width - .22f, 1.14f, .04f});
            for (int i = 1; i < 9; ++i)
                box(data[Frame], p + Vec3{0, i * .25f, -d - .085f}, {width - .26f, .017f, .014f});
        }
        else if (site.kind == ServiceKind::market)
        {
            Building stall{site.centre, {site.half_width, site.half_depth}, site.height, 1, BuildingKind::stall,
                           -1};
            building_core(data, plan_building(stall, p.y), stall.style);
        }
        else if (site.kind == ServiceKind::clinic)
        {
            auto sign = p + Vec3{0, 3.35f, float(site.half_depth) + .24f};
            box(data[Metal], sign, {.84f, .84f, .055f});
            box(data[Lamps], sign + Vec3{0, 0, .08f}, {.60f, .17f, .035f});
            box(data[Lamps], sign + Vec3{0, 0, .08f}, {.17f, .60f, .04f});
        }
    }
    for (const auto &b : l.furniture)
    {
        if (b.height <= 3 || !owns(b.p))
            continue;
        auto p = ground(b.p.x, b.p.z);
        box(data[Metal], p + Vec3{0, float(b.height * .5), 0}, {.07f, float(b.height * .5), .07f});
        box(data[Metal], p + Vec3{.3f, float(b.height), 0}, {.36f, .05f, .08f});
        box(data[Lamps], p + Vec3{.5f, float(b.height - .06), 0}, {.19f, .03f, .13f});
    }
    // Pothole photographs lie just above the lane paint; the shader dissolves their border.
    for (const auto &hole : w.potholes)
    {
        if (!owns(hole.p))
            continue;
        const double x = hole.p.x, z = hole.p.z, s = hole.size;
        quad(data[PotholeDecal + hole.style], ground(x - s, z - s, .03), ground(x - s, z + s, .03),
             ground(x + s, z + s, .03), ground(x + s, z - s, .03));
    }
    return data;
}
} // namespace Promzona
