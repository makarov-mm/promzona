#include "promzona/vegetation.hpp"
#include "promzona/world.hpp"
namespace pz
{
std::vector<TreePlant> planTrees(const World &w)
{
    std::vector<TreePlant> trees;
    const auto &l = w.layout;
    for (int j = 0; j < l.nodes() - 1; ++j)
        for (int i = 0; i < l.nodes() - 1; ++i)
        {
            Random r(w.seed ^ (std::uint32_t(i + j * l.nodes()) * 747796405u + 2891336453u));
            bool park = l.park(i, j);
            if (CityLayout::clinic(i, j) || CityLayout::station(i, j))
                continue;
            for (int k = 0; k < (park ? 6 : 2); ++k)
            {
                // Leave the continuous sidewalk clear; use courtyards and park interiors.
                Vec2 p{l.coordinate(i) + 12 + r.next() * 12, l.coordinate(j) + 12 + r.next() * 12};
                float h = float(5.8 + r.next() * 4.5);
                TreeKind kind = TreeKind(r.index(3));
                float crown = h * (kind == TreeKind::linden ? .28f : kind == TreeKind::birch ? .24f : .22f);
                if (!l.urbanSite(p, crown) || l.roadDistance(p) < l.roadHalf() + 3.8 ||
                    w.terrain.height(p) < CityLayout::waterLevel + 2)
                    continue;
                bool blocked = false;
                for (const auto &b : w.buildings)
                    if (std::abs(p.x - b.p.x) < b.half.x + crown + 1.4 &&
                        std::abs(p.z - b.p.z) < b.half.z + crown + 1.4)
                    {
                        blocked = true;
                        break;
                    }
                for (const auto &s : w.roadLevels)
                    if (s.reserved(p, crown + 2))
                        blocked = true;
                for (const auto &b : l.furniture)
                    if ((p - b.p).length() < 1.8)
                    {
                        blocked = true;
                        break;
                    }
                // Only nearby plants can overlap; the map has fixed 36m cells.
                for (auto it = trees.rbegin(); it != trees.rend(); ++it)
                {
                    if (it->foot.z < l.coordinate(j) - 36)
                        break;
                    if ((p - Vec2{it->foot.x, it->foot.z}).length() < (crown + it->radius) * .85)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked)
                    trees.push_back({{float(p.x), float(w.terrain.height(p) - .06), float(p.z)},
                                     h,
                                     crown,
                                     kind,
                                     r.state});
            }
        }
    return trees;
}
namespace
{
void face(MeshData &m, std::array<Vec3, 4> p, Vec3 normal, std::array<float, 4> data, float u = 1,
          float v = 1)
{
    auto base = std::uint32_t(m.vertices.size());
    constexpr std::array<std::array<float, 2>, 4> uv{{{0, 0}, {1, 0}, {1, 1}, {0, 1}}};
    for (int k = 0; k < 4; ++k)
    {
        Vertex x;
        x.position = p[k];
        x.normal = normal;
        x.uv = {uv[k][0] * u, uv[k][1] * v};
        x.joints = data;
        m.vertices.push_back(x);
    }
    for (auto k : {0u, 1u, 2u, 0u, 2u, 3u})
        m.indices.push_back(base + k);
}
void stem(MeshData &mesh, Vec3 a, Vec3 b, float ra, float rb, int sides, std::array<float, 4> data)
{
    auto axis = (b - a).unit(),
         u = cross(axis, std::abs(axis.y) > .9f ? Vec3{1, 0, 0} : Vec3{0, 1, 0}).unit(), v = cross(axis, u);
    for (int k = 0; k < sides; ++k)
    {
        float x = float(k * 2 * pi / sides), y = float((k + 1) * 2 * pi / sides);
        auto r = u * std::cos(x) + v * std::sin(x), s = u * std::cos(y) + v * std::sin(y);
        face(mesh, {a + r * ra, a + s * ra, b + s * rb, b + r * rb}, (r + s).unit(), data, ra * 6,
             (b - a).length());
    }
}
} // namespace
void appendTree(TreeMesh &out, const TreePlant &t, bool far)
{
    const float h = t.height, phase = float(t.seed % 1000) * .01f, kind = float(t.kind);
    const bool pine = t.kind == TreeKind::spruce;
    auto trunk = [&](float u)
    {
        return t.foot +
               Vec3{h * .018f * std::sin(phase + u * 2) * u, h * u, h * .014f * std::cos(phase + u) * u};
    };
    for (int k = 0; k < 4; ++k)
        stem(out[0], trunk(k * .25f), trunk((k + 1) * .25f), h * (.018f - k * .0038f),
             h * (.018f - (k + 1) * .0038f), far ? 5 : 8, {0, phase, 0, kind});
    int branches = pine ? 18 : 14;
    for (int j = 0; j < branches; ++j)
    {
        Random random(t.seed ^ (std::uint32_t(j + 1) * 2654435761u));
        float u = (pine ? .18f : .29f) + (pine ? .72f : .62f) * (j + .5f) / branches,
              angle = phase + j * 2.399963f;
        Vec3 dir{std::cos(angle), 0, std::sin(angle)};
        float reach =
            t.radius * (pine ? (1.12f - u) : (.30f + .60f * std::sin(float(pi) * (j + 1) / (branches + 1))));
        auto root = trunk(u), end = root + dir * reach + Vec3{0, pine ? -.25f : h * .05f, 0};
        stem(out[0], root, end, h * .006f, .008f, far ? 3 : 5, {0, phase, 0, kind});
        int leaves = far ? 7 : 26;
        for (int k = 0; k < leaves; ++k)
        {
            float theta = float(random.next() * 2 * pi), y = float(random.next() * 2 - 1),
                  rr = std::sqrt(1 - y * y);
            Vec3 radial{rr * std::cos(theta), y, rr * std::sin(theta)};
            // Each card carries a twig with many small leaves, not one enormous leaf.
            float size = h * (far ? .11f : .055f) * float(.75 + random.next() * .5);
            auto p =
                end + Vec3{radial.x * h * .095f, radial.y * h * (pine ? .045f : .11f), radial.z * h * .095f};
            auto n = (radial + Vec3{0, .45f, 0}).unit();
            auto right = cross(n, std::abs(n.y) > .95f ? Vec3{1, 0, 0} : Vec3{0, 1, 0}).unit() * size;
            auto up = cross(n, right.unit()) * size * (pine ? .64f : 1.f);
            float weight = clamp((p.y - t.foot.y) / h, 0.f, 1.f);
            face(out[1], {p - right - up, p + right - up, p + right + up, p - right + up}, n,
                 {weight, phase, float(random.next()), kind});
        }
    }
}
} // namespace pz
