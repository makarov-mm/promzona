#include "promzona/city_mesh.hpp"
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
    auto tile = [&](int material, double a, double b, double c, double d, double add = 0, bool bed = false)
    {
        quad(data[material], ground(a, d, add, bed), ground(c, d, add, bed), ground(c, b, add, bed),
             ground(a, b, add, bed));
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
                        if (l.roadDistance(mid) > l.roadHalf() ||
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
    for (auto &b : w.buildings)
    {
        if (!owns(b.p))
            continue;
        float x = float(b.p.x), z = float(b.p.z), y0 = float(w.terrain.height(b.p)), h = float(b.height),
              wx = float(b.half.x), wz = float(b.half.z);
        box(data[Facade + b.style], {x, y0 + h * .5f, z}, {wx, h * .5f, wz}, 4);
        box(data[Pavement], {x, y0 + h + .13f, z}, {wx + .15f, .13f, wz + .15f});
        for (float wy = 2; wy < h; wy += 3)
            for (float xp = -wx + 1.1f; xp < wx - .8f; xp += 2.4f)
            {
                box(data[Glass], {x + xp, y0 + wy, z + wz + .04f}, {.55f, .7f, .03f});
                box(data[Glass], {x + xp, y0 + wy, z - wz - .04f}, {.55f, .7f, .03f});
            }
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
