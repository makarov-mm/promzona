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
// Special buildings share the footprint and overall height used for contacts.
void districtBuilding(CityChunk &data, const BuildingPlan &p, int style)
{
    const float x = p.halfWidth, z = p.halfDepth, h = p.height, front = float(p.front);
    auto part = [&](int mat, float dx, float y, float dz, float hx, float hy, float hz)
    { box(data[mat], p.origin + Vec3{dx, y, dz}, {hx, hy, hz}); };
    auto point = [&](float dx, float y, float dz) { return p.origin + Vec3{dx, y, dz}; };
    auto disk = [&](float bottom, float top, float lo, float hi, int mat)
    {
        constexpr int segments = 16;
        for (int k = 0; k < segments; ++k)
        {
            double a = 2 * pi * k / segments, b = 2 * pi * (k + 1) / segments;
            auto rim = [&](double t, float y, float r)
            { return point(float(std::cos(t)) * r, y, float(std::sin(t)) * r); };
            auto A = rim(a, top, hi), B = rim(b, top, hi);
            face(data[mat], rim(b, bottom, lo), rim(a, bottom, lo), A, B, 1, 2);
            addCityTriangle(data[mat], point(0, top, 0), B, A);
        }
    };
    auto pitchedRoof = [&](float eave, int material)
    {
        const float ex = x + .16f, ez = z + .16f;
        auto a = point(-ex, eave, -ez), b = point(ex, eave, -ez), c = point(ex, eave, ez),
             d = point(-ex, eave, ez);
        auto l = point(-ex, h, 0), r = point(ex, h, 0);
        face(data[material], a, l, r, b, ex, ez);
        face(data[material], d, c, r, l, ex, ez);
        addCityTriangle(data[material], a, d, l);
        addCityTriangle(data[material], c, b, r);
    };
    using K = BuildingKind;
    switch (p.kind)
    {
    case K::cottage:
    {
        float eave = h - 1.25f;
        part(Facade + style, 0, eave / 2, 0, x, eave / 2, z);
        pitchedRoof(eave, style % 2 ? Rust : Roof);
        part(Facade + 4, -x * .55f, h - .36f, .4f, .19f, .54f, .25f);
        part(Foundation, 0, .14f, 0, x + .035f, .14f, z + .035f);
        part(Door, 0, 1, front * (z + .025f), .42f, 1, .035f);
        auto window = [&](Vec3 centre, Vec3 normal)
        {
            Vec3 right{normal.z, 0, -normal.x};
            panel(data[Frame], centre, right, normal, .59f, .71f);
            panel(data[Windows], centre + normal * .015f, right, normal, .50f, .62f,
                  {float(p.seed % 65521), float(p.seed % 5), 1, 0});
        };
        for (int side : {-1, 1})
            for (int w : {-1, 1})
            {
                window(point(w * x * .63f, 1.75f, side * (z + .05f)), {0, 0, float(side)});
                window(point(side * (x + .05f), 1.75f, w * z * .51f), {float(side), 0, 0});
            }
        break;
    }
    case K::garage:
        part(Facade + 3, 0, h / 2, 0, x, h / 2, z);
        part(Roof, 0, h - .07f, 0, x + .08f, .09f, z + .10f);
        for (int side : {-1, 1})
            part(style % 2 ? WorkshopSteel : Balcony + style % 3, side * x * .44f, 1.12f, front * (z + .04f),
                 x * .43f, 1.08f, .045f);
        part(Frame, -.14f, 1.13f, front * (z + .095f), .07f, .025f, .022f);
        break;
    case K::hall:
    case K::shed:
    case K::pump:
    {
        float eave = h - .65f;
        part(Facade + (p.kind == K::hall ? 4 : 3), 0, eave / 2, 0, x, eave / 2, z);
        pitchedRoof(eave, WorkshopSteel);
        part(Door, 0, eave * .32f, -z - .04f, std::min(x * .65f, 2.4f), eave * .32f, .05f);
        for (int side : {-1, 1})
            for (float bayX = -x + 1.4f; bayX < x - 1; bayX += 2.4f)
                part(Glass, bayX, eave * .80f, side * (z + .03f), .88f, .35f, .035f);
        break;
    }
    case K::chimney:
        for (int i = 0; i < 8; ++i)
        {
            float a = float(i) / 8, b = float(i + 1) / 8;
            disk(h * a, h * b, x * (1 - .38f * a), x * (1 - .38f * b), i >= 4 && i % 2 ? Paint : Rust);
        }
        break;
    case K::tank:
        disk(.06f, h - .3f, x, x, Frame);
        disk(h - .3f, h, x, .2f, Metal);
        for (int i = 1; i <= 3; ++i)
            disk(i * h * .235f, i * h * .235f + .07f, x + .035f, x + .035f, Metal);
        break;
    case K::wagon:
        part(Metal, 0, .87f, 0, x, .14f, z - .12f);
        for (int side : {-1, 1})
        {
            for (int axle : {-1, 1})
            {
                float axleX = side * x * .70f + axle * .85f;
                part(Frame, axleX, .53f, 0, .18f, .15f, 1.02f);
                for (int wheel : {-1, 1})
                {
                    const auto centre = point(axleX, .51f, wheel * .762f);
                    auto rim = [&](int segment, float offset)
                    {
                        double angle = segment * pi / 6;
                        return centre +
                               Vec3{float(.42 * std::cos(angle)), float(.42 * std::sin(angle)), offset};
                    };
                    for (int k = 0; k < 12; ++k)
                    {
                        auto a = rim(k, .09f), b = rim(k + 1, .09f);
                        auto c = rim(k + 1, -.09f), d = rim(k, -.09f);
                        face(data[Metal], d, c, b, a);
                        if (wheel > 0)
                            addCityTriangle(data[Metal], centre + Vec3{0, 0, .09f}, a, b);
                        else
                            addCityTriangle(data[Metal], centre - Vec3{0, 0, .09f}, c, d);
                    }
                }
            }
            part(Rust, 0, (h + 1) * .5f, side * (z - .06f), x, (h - 1) * .5f, .07f);
            part(Rust, side * (x - .06f), (h + 1) * .5f, 0, .07f, (h - 1) * .5f, z);
            for (float rib = -x + .25f; rib < x; rib += 2)
                part(Metal, rib, (h + 1) * .5f, side * z, .043f, (h - 1) * .5f, .055f);
        }
        break;
    case K::buffer:
        for (int side : {-1, 1})
            part(Metal, 0, h * .40f, side * .76f, x * .5f, h * .40f, .14f);
        part(Rust, 0, h * .85f, 0, x, h * .15f, z);
        break;
    case K::stall:
        // Open timber counter with four wrapped parcels and a striped canvas awning.
        part(Balcony + 1, 0, .43f, front * (z - .07f), x, .43f, .06f);
        part(Door, 0, .92f, 0, x, .06f, z);
        for (int a : {-1, 1})
            for (int b : {-1, 1})
                part(Metal, a * (x - .08f), h * .5f, b * (z - .08f), .035f, h * .5f, .035f);
        for (int i = 0; i < 6; ++i)
        {
            float strip = (x + .14f) / 3;
            float dx = -x - .14f + (i + .5f) * strip;
            int canvas = i % 2 ? Paint : Canvas;
            part(canvas, dx, h + .025f, 0, strip/2, .025f, z + .2f);
            part(canvas, dx, h - .075f, front * (z + .18f), strip/2, .075f, .02f);
        }
        for (int n = 0; n < 4; ++n)
        {
            float dx = x * (n - 1.5f) * .44f;
            float halfHeight = n % 2 ? .09f : .13f;
            part(n % 2 ? Balcony + 2 : Facade + 2, dx, .98f + halfHeight, 0, .20f, halfHeight, .16f);
            part(Canvas, dx, .985f + 2 * halfHeight, 0, .035f, .005f, .16f);
        }
        break;
    case K::fence:
    case K::crate:
        part(p.kind == K::fence ? Facade + 3 : Balcony + 1, 0, h / 2, 0, x, h / 2, z);
        break;
    case K::apartment:
        break;
    }
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
    p.kind = b.kind;
    p.front = b.front;
    if (p.kind != BuildingKind::apartment) return p;
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
    if (p.kind != BuildingKind::apartment) { districtBuilding(data,p,facade); return; }
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
    if (p.kind != BuildingKind::apartment) return;
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
