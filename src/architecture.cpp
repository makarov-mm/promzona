#include "promzona/architecture.hpp"
namespace pz
{
namespace
{
constexpr float windowHalfWidth = .64f, windowHalfHeight = .54f;
constexpr float storey = 2.8f, bay = 2.45f, firstWindow = 2.9f;
std::uint32_t hash(std::uint32_t n)
{
    n ^= n >> 16;
    n *= 0x7feb352du;
    n ^= n >> 15;
    n *= 0x846ca68bu;
    return n ^ (n >> 16);
}
void face(MeshData &m, Vec3 a, Vec3 b, Vec3 c, Vec3 d, float u = 1, float v = 1,
          std::array<float, 4> detail = {})
{
    auto base = std::uint32_t(m.vertices.size());
    auto n = cross(b - a, c - a).unit();
    for (auto [p, uv] : {std::pair{a, std::array<float, 2>{0, v}}, std::pair{b, std::array<float, 2>{u, v}},
                         std::pair{c, std::array<float, 2>{u, 0}}, std::pair{d, std::array<float, 2>{0, 0}}})
    {
        Vertex x;
        x.position = p;
        x.normal = n;
        x.uv = uv;
        // The static shader uses the otherwise unused joint channel for window identity.
        x.joints = detail;
        m.vertices.push_back(x);
    }
    for (auto i : {0u, 1u, 2u, 0u, 2u, 3u})
        m.indices.push_back(base + i);
}
void box(MeshData &m, Vec3 p, Vec3 half, float metresPerTile = 2)
{
    auto a = p - half, b = p + half;
    float ux = 2 * half.x / metresPerTile, uz = 2 * half.z / metresPerTile, vy = 2 * half.y / metresPerTile;
    face(m, {a.x, a.y, b.z}, {b.x, a.y, b.z}, {b.x, b.y, b.z}, {a.x, b.y, b.z}, ux, vy);
    face(m, {b.x, a.y, a.z}, {a.x, a.y, a.z}, {a.x, b.y, a.z}, {b.x, b.y, a.z}, ux, vy);
    face(m, {b.x, a.y, b.z}, {b.x, a.y, a.z}, {b.x, b.y, a.z}, {b.x, b.y, b.z}, uz, vy);
    face(m, {a.x, a.y, a.z}, {a.x, a.y, b.z}, {a.x, b.y, b.z}, {a.x, b.y, a.z}, uz, vy);
    face(m, {a.x, b.y, b.z}, {b.x, b.y, b.z}, {b.x, b.y, a.z}, {a.x, b.y, a.z}, ux, uz);
    face(m, {a.x, a.y, a.z}, {b.x, a.y, a.z}, {b.x, a.y, b.z}, {a.x, a.y, b.z}, ux, uz);
}
void panel(MeshData &m, Vec3 p, Vec3 right, Vec3 normal, float hw, float hh, std::array<float, 4> detail = {})
{
    auto r = right * hw, up = Vec3{0, hh, 0};
    face(m, p - r - up, p + r - up, p + r + up, p - r + up, 1, 1, detail);
    (void)normal; // right is cross(up, normal), so winding faces outside.
}
Vec3 extents(Vec3 right, Vec3 n, Vec3 half)
{
    return {std::abs(right.x) * half.x + std::abs(n.x) * half.z, half.y,
            std::abs(right.z) * half.x + std::abs(n.z) * half.z};
}
// Thin rails use crossed sheets instead of six box faces.
void rail(MeshData &mesh, Vec3 a, Vec3 b, float radius)
{
    auto axis = (b - a).unit();
    auto u = cross(axis, std::abs(axis.y) > .9f ? Vec3{1, 0, 0} : Vec3{0, 1, 0}).unit() * radius;
    auto v = cross(axis, u).unit() * radius;
    face(mesh, a - u, b - u, b + u, a + u);
    face(mesh, a - v, b - v, b + v, a + v);
}
} // namespace
BuildingPlan planBuilding(const Building &b, double ground)
{
    BuildingPlan p;
    p.origin = {float(b.p.x), float(ground), float(b.p.z)};
    p.halfWidth = float(b.half.x);
    p.halfDepth = float(b.half.z);
    p.height = float(b.height);
    p.windowsOnZ = b.half.x >= b.half.z;
    p.seed = hash(std::uint32_t(std::llround(b.p.x * 10)) ^ hash(std::uint32_t(std::llround(b.p.z * 10))) ^
                  std::uint32_t(b.style));
    float half = p.windowsOnZ ? p.halfWidth : p.halfDepth;
    int columns = std::max(1, int((2 * half - 1.2f) / bay));
    for (int side : {-1, 1})
    {
        Vec3 normal = p.windowsOnZ ? Vec3{0, 0, float(side)} : Vec3{float(side), 0, 0};
        auto right = cross({0, 1, 0}, normal);
        float depth = p.windowsOnZ ? p.halfDepth : p.halfWidth;
        int floor = 0;
        for (float y = firstWindow; y + windowHalfHeight + .5f < p.height; y += storey, ++floor)
            for (int col = 0; col < columns; ++col)
            {
                auto id = hash(p.seed ^ hash(std::uint32_t(col + floor * 37 + (side + 1) * 7919)));
                FacadeWindow w;
                w.centre = p.origin + normal * (depth + .045f) + right * ((col - (columns - 1) * .5f) * bay) +
                           Vec3{0, y, 0};
                w.right = right;
                w.normal = normal;
                w.seed = id;
                w.curtain = int(id % 10);
                // Continuous vertical stacks: balconies are architecture, not random speckles.
                if (floor > 0 && col % 3 == int(p.seed % 3))
                    w.balcony = int(hash(p.seed + col) % 3);
                w.airConditioner = w.balcony < 0 && id % 7 == 0;
                p.windows.push_back(w);
            }
    }
    return p;
}
void buildingCore(CityChunk &data, const BuildingPlan &p, int facade)
{
    auto centre = p.origin + Vec3{0, p.height * .5f, 0};
    box(data[Facade + clamp(facade, 0, 7)], centre, {p.halfWidth, p.height * .5f, p.halfDepth}, 3.2f);
    box(data[Roof], p.origin + Vec3{0, p.height + .12f, 0}, {p.halfWidth + .12f, .12f, p.halfDepth + .12f});
    box(data[Foundation], p.origin + Vec3{0, .30f, 0}, {p.halfWidth + .035f, .3f, p.halfDepth + .035f});
    for (const auto &w : p.windows)
        panel(data[Windows], w.centre, w.right, w.normal, windowHalfWidth + .055f, windowHalfHeight + .055f,
              {float(w.seed % 65521), float(w.curtain), float((w.seed >> 16) % 4), 0});
}
void buildingDetails(CityChunk &data, const BuildingPlan &p)
{
    // Parapet, drainpipe, entrance canopy and roof ventilation: metre-sized silhouettes.
    for (int side : {-1, 1})
    {
        box(data[Roof], p.origin + Vec3{0, p.height + .35f, side * (p.halfDepth - .06f)},
            {p.halfWidth, .15f, .09f});
        box(data[Roof], p.origin + Vec3{side * (p.halfWidth - .06f), p.height + .35f, 0},
            {.09f, .15f, p.halfDepth});
    }
    box(data[Roof], p.origin + Vec3{p.halfWidth * .32f, p.height + .65f, -p.halfDepth * .3f},
        {.65f, .4f, .5f});
    Vec3 n = p.windowsOnZ ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
    Vec3 r = cross({0, 1, 0}, n);
    float depth = p.windowsOnZ ? p.halfDepth : p.halfWidth;
    float half = p.windowsOnZ ? p.halfWidth : p.halfDepth;
    auto entrance = p.origin + n * (depth + .09f);
    auto localBox = [&](int material, Vec3 c, Vec3 size) { box(data[material], c, extents(r, n, size)); };
    localBox(Door, entrance + Vec3{0, 1.05f, 0}, {.62f, 1.05f, .045f});
    localBox(Frame, entrance + r * .43f + n * .06f + Vec3{0, 1.0f, 0}, {.025f, .14f, .025f});
    localBox(Roof, entrance + n * .35f + Vec3{0, 2.22f, 0}, {.96f, .085f, .46f});
    localBox(Lamps, entrance + n * .1f + Vec3{0, 2.08f, 0}, {.18f, .055f, .04f});
    localBox(Metal, entrance - r * (half - .25f) + Vec3{0, p.height * .5f, 0}, {.05f, p.height * .5f, .05f});
    // Reuse the original advertisement art. 2:1 signs fit the fascia without stretching.
    if (p.seed % 3 == 0 && half >= 3.8f)
        panel(data[Sign + int((p.seed >> 8) % 3)],
              entrance + r * std::min(2.8f, half - 1.8f) + n * .07f + Vec3{0, 1.6f, 0}, r, n, 1.25f, .625f);
    for (const auto &w : p.windows)
    {
        auto at = [&](float x, float y, float z)
        { return w.centre + w.right * x + Vec3{0, y, 0} + w.normal * z; };
        // Sill is a folded sheet, not a six-faced box for every window.
        face(data[Frame], at(-.73f, -.60f, .21f), at(.73f, -.60f, .21f), at(.73f, -.55f, 0),
             at(-.73f, -.55f, 0));
        face(data[Frame], at(-.73f, -.63f, .21f), at(.73f, -.63f, .21f), at(.73f, -.60f, .21f),
             at(-.73f, -.60f, .21f));
        if (w.airConditioner)
        {
            box(data[Frame], at(.8f, -.94f, .22f), extents(w.right, w.normal, {.39f, .24f, .22f}));
            panel(data[Metal], at(.8f, -.94f, .45f), w.right, w.normal, .27f, .15f);
        }
        if (w.balcony < 0)
            continue;
        // Low cost slabs/panels, with railings only in the near batch. They are all above pedestrian head
        // height.
        box(data[Roof], at(0, -.68f, .45f), extents(w.right, w.normal, {.88f, .065f, .54f}));
        int mat = Balcony + w.balcony;
        panel(data[mat], at(0, -.22f, 1.0f), w.right, w.normal, .84f, .40f);
        for (int side : {-1, 1})
        {
            face(data[mat], at(side * .84f, -.62f, .05f), at(side * .84f, -.62f, 1.0f),
                 at(side * .84f, .18f, 1.0f), at(side * .84f, .18f, .05f));
            rail(data[Metal], at(side * .84f, .21f, .01f), at(side * .84f, .21f, 1.03f), .025f);
        }
        rail(data[Metal], at(-.87f, .21f, 1.0f), at(.87f, .21f, 1.0f), .025f);
        if (w.balcony == 2)
        {
            panel(data[Glass], at(0, .75f, 1.0f), w.right, w.normal, .84f, .50f);
            for (int divider : {-1, 0, 1})
                rail(data[Frame], at(divider * .82f, .23f, 1.02f), at(divider * .82f, 1.27f, 1.02f), .023f);
            box(data[Roof], at(0, 1.29f, .48f), extents(w.right, w.normal, {.88f, .045f, .56f}));
        }
    }
}
} // namespace pz
