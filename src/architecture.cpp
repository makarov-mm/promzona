#include "promzona/architecture.hpp"
namespace Promzona
{
namespace
{
constexpr float windowHalfWidth = .64f, windowHalfHeight = .54f;
constexpr float entranceHalfWidth = .72f, entranceHeight = 2.24f, recess = .34f;
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
            add_city_triangle(data[mat], point(0, top, 0), B, A);
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
        add_city_triangle(data[material], a, d, l);
        add_city_triangle(data[material], c, b, r);
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
                            add_city_triangle(data[Metal], centre + Vec3{0, 0, .09f}, a, b);
                        else
                            add_city_triangle(data[Metal], centre - Vec3{0, 0, .09f}, c, d);
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
            part(canvas, dx, h + .025f, 0, strip / 2, .025f, z + .2f);
            part(canvas, dx, h - .075f, front * (z + .18f), strip / 2, .075f, .02f);
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
    p.windowsOnZ = entranceNormal(b.half).z > 0;
    p.seed = hash(std::uint32_t(std::llround(b.p.x * 10)) ^ hash(std::uint32_t(std::llround(b.p.z * 10))) ^
                  std::uint32_t(b.style));
    p.kind = b.kind;
    p.front = b.front;
    if (p.kind != BuildingKind::apartment)
        return p;
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
    if (p.kind != BuildingKind::apartment)
    {
        districtBuilding(data, p, facade);
        return;
    }
    const Vec3 front = p.windowsOnZ ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
    const Vec3 side = cross({0, 1, 0}, front);
    const float half = p.windowsOnZ ? p.halfWidth : p.halfDepth;
    const float depth = p.windowsOnZ ? p.halfDepth : p.halfWidth;
    auto shell = [&](MeshData &mesh, float height, float outset, float tile)
    {
        const float width = half + outset, length = depth + outset;
        for (Vec3 normal : {front, side, front * -1.f, side * -1.f})
        {
            const Vec3 right = cross({0, 1, 0}, normal);
            const bool longFace = std::abs(dot(normal, front)) > .5f;
            const bool hasDoor = dot(normal, front) > .5f;
            const float span = longFace ? width : length;
            const Vec3 base = p.origin + normal * (longFace ? length : width);
            auto panel = [&](float left, float rightEdge, float bottom, float top)
            {
                const auto first = mesh.vertices.size();
                auto at = [&](float x, float y) { return base + right * x + Vec3{0, y, 0}; };
                face(mesh, at(left, bottom), at(rightEdge, bottom), at(rightEdge, top), at(left, top));
                for (auto &v : std::span(mesh.vertices).subspan(first))
                {
                    const float x = dot(v.position - base, right), y = v.position.y - p.origin.y;
                    v.uv = {(x + span) / tile, (height - y) / tile};
                    v.joints = {x, y, hasDoor ? -span : span, -1};
                }
            };
            if (hasDoor)
            {
                panel(-span, -entranceHalfWidth, 0, height);
                panel(entranceHalfWidth, span, 0, height);
                if (height > entranceHeight)
                    panel(-entranceHalfWidth, entranceHalfWidth, entranceHeight, height);
            }
            else
                panel(-span, span, 0, height);
        }
        auto cap = [&](float left, float right, float back, float near)
        {
            auto at = [&](float x, float z) { return p.origin + side * x + front * z + Vec3{0, height, 0}; };
            face(mesh, at(left, near), at(right, near), at(right, back), at(left, back),
                 (right - left) / tile, (near - back) / tile);
        };
        if (height > entranceHeight)
            cap(-width, width, -length, length);
        else
        {
            cap(-width, width, -length, depth - recess);
            cap(-width, -entranceHalfWidth, depth - recess, length);
            cap(entranceHalfWidth, width, depth - recess, length);
        }
    };
    shell(data[Facade + clamp(facade, 0, 7)], p.height, 0, 3.2f);
    shell(data[Foundation], .60f, .035f, 2.f);
    box(data[Roof], p.origin + Vec3{0, p.height + .12f, 0}, {p.halfWidth + .12f, .12f, p.halfDepth + .12f});
    // What a roof carries, seen from above: the lift machine rooms over the stairwells, vent
    // stacks, and on some houses an aerial mast or, on a tower, a water tank. Low houses keep a
    // bare roof. The plan seed decides, so a building always looks the same.
    if (p.height > 13)
    {
        const float top = p.height + .24f;
        const bool alongX = p.halfWidth >= p.halfDepth;
        const float length = alongX ? p.halfWidth : p.halfDepth, breadth = alongX ? p.halfDepth : p.halfWidth;
        auto onRoof = [&](float along, float across, float y, Vec3 half)
        {
            const Vec3 offset = alongX ? Vec3{along, y, across} : Vec3{across, y, along};
            return p.origin + offset;
        };
        auto spin = [&](Vec3 half) { return alongX ? half : Vec3{half.z, half.y, half.x}; };
        const auto r = hash(p.seed ^ 0x5bd1e995u);
        const int rooms = length > 7 ? 1 + int(r % 2) : 1;
        for (int k = 0; k < rooms; ++k)
        {
            const float along = rooms == 1 ? (float(int(r >> 4) % 5) - 2) * length * .12f
                                           : (k ? 1.f : -1.f) * length * .45f;
            const float tall = .9f + float((r >> (8 + k)) % 3) * .25f;
            const Vec3 half = spin({std::min(2.1f, length * .3f), tall, std::min(1.5f, breadth * .45f)});
            box(data[Foundation], onRoof(along, 0, top + tall, half), half);
            box(data[Roof], onRoof(along, 0, top + 2 * tall + .06f, half), half + Vec3{.1f, -tall + .06f, .1f});
        }
        for (int k = 0; k < 1 + int((r >> 12) % 3); ++k)
        {
            const auto v = hash(r + std::uint32_t(k) * 7919u);
            const float along = (float(v % 1000) / 1000.f * 1.6f - .8f) * length,
                        across = (float((v >> 10) % 1000) / 1000.f * 1.4f - .7f) * breadth;
            box(data[Metal], onRoof(along, across, top + .45f, {}), {.22f, .45f, .22f});
        }
        if ((r >> 20) % 3 == 0)
        {
            const float along = length * .6f * ((r >> 22) % 2 ? 1.f : -1.f);
            box(data[Metal], onRoof(along, 0, top + 2.6f, {}), {.05f, 2.6f, .05f});
            box(data[Metal], onRoof(along, 0, top + 4.4f, {}), spin({1.1f, .03f, .03f}));
        }
        if (p.height > 36 && (r >> 24) % 4 == 0)
        {
            const Vec3 half{1.1f, .8f, 1.1f};
            box(data[Metal], onRoof(-length * .2f, breadth * .1f, top + 2.6f + half.y, {}), half);
            for (int a : {-1, 1})
                for (int b : {-1, 1})
                    box(data[Metal], onRoof(-length * .2f + a * 1.3f, breadth * .1f + b * 1.3f, top + 1.3f, {}),
                        {.08f, 1.3f, .08f});
        }
    }
    auto at = [&](float x, float y, float z)
    { return p.origin + side * x + Vec3{0, y, 0} + front * (depth + z); };
    const float w = entranceHalfWidth, h = entranceHeight;
    face(data[Foundation], at(-w, 0, .036f), at(-w, 0, -recess), at(-w, h, -recess), at(-w, h, .036f));
    face(data[Foundation], at(w, 0, -recess), at(w, 0, .036f), at(w, h, .036f), at(w, h, -recess));
    face(data[Foundation], at(-w, h, -recess), at(w, h, -recess), at(w, h, .036f), at(-w, h, .036f));
    const auto firstDoor = data[Door].vertices.size();
    face(data[Door], at(-w, 0, -recess), at(w, 0, -recess), at(w, h, -recess), at(-w, h, -recess));
    for (auto &v : std::span(data[Door].vertices).subspan(firstDoor))
        v.joints = {dot(v.position - p.origin, side), v.position.y - p.origin.y, -half, -2};
    for (const auto &w : p.windows)
    {
        panel(data[Windows], w.centre, w.right, w.normal, windowHalfWidth + .055f, windowHalfHeight + .055f,
              {float(w.seed % 65521), float(w.curtain), float((w.seed >> 16) % 4), 0});
        if (w.balcony < 0)
            continue;
        const int material = Balcony + w.balcony;
        auto at = [&](float x, float y, float z)
        { return w.centre + w.right * x + Vec3{0, y, 0} + w.normal * z; };
        panel(data[material], at(0, -.22f, 1), w.right, w.normal, .84f, .40f);
        for (int side : {-1, 1})
            face(data[material], at(side * .84f, -.62f, .05f), at(side * .84f, -.62f, 1),
                 at(side * .84f, .18f, 1), at(side * .84f, .18f, .05f));
    }
}
void buildingDetails(CityChunk &data, const BuildingPlan &p)
{
    if (p.kind != BuildingKind::apartment)
        return;
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
    const Vec3 door = entrance - n * (.09f + recess);
    for (float flank : {-1.f, 1.f})
        localBox(Frame, door + r * (flank * .675f) + n * .02f + Vec3{0, 1.1f, 0}, {.042f, 1.1f, .027f});
    localBox(Frame, door + Vec3{0, 2.18f, 0} + n * .023f, {.71f, .06f, .03f});
    localBox(Metal, door + r * .48f + n * .055f + Vec3{0, 1.03f, 0}, {.033f, .14f, .045f});
    localBox(Foundation, entrance + n * .02f + Vec3{0, .059f, 0}, {.80f, .059f, .49f});
    localBox(Foundation, entrance + n * .43f + Vec3{0, 2.43f, 0}, {1.11f, .105f, .60f});
    localBox(Metal, entrance + n * 1.02f + Vec3{0, 2.42f, 0}, {1.13f, .052f, .032f});
    for (float flank : {-1.f, 1.f})
        localBox(Frame, entrance + r * (.87f * flank) + n * .29f + Vec3{0, 2.28f, 0}, {.035f, .05f, .40f});
    localBox(Frame, entrance + n * .12f + Vec3{0, 2.30f, 0}, {.21f, .055f, .12f});
    localBox(Lamps, entrance + n * .12f + Vec3{0, 2.256f, 0}, {.15f, .025f, .09f});
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
        for (int side : {-1, 1})
            appendThinRail(data[ThinRail], at(side * .84f, .21f, .01f), at(side * .84f, .21f, 1.03f), .025f);
        appendThinRail(data[ThinRail], at(-.87f, .21f, 1.0f), at(.87f, .21f, 1.0f), .025f);
        if (w.balcony == 2)
        {
            panel(data[Glass], at(0, .75f, 1.0f), w.right, w.normal, .84f, .50f);
            for (int divider : {-1, 0, 1})
                appendThinRail(data[ThinRail], at(divider * .82f, .23f, 1.02f),
                               at(divider * .82f, 1.27f, 1.02f), .023f);
            box(data[Roof], at(0, 1.29f, .48f), extents(w.right, w.normal, {.88f, .045f, .56f}));
        }
    }
}
} // namespace Promzona
