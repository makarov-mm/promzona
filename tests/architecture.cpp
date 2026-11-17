#include "promzona/architecture.hpp"
#include "promzona/visibility.hpp"
#include <print>
#include <stdexcept>
#include <source_location>
#include <format>
using namespace Promzona;
namespace
{
int checks{};
void check(bool v, const char *message, std::source_location where = std::source_location::current())
{
    ++checks;
    if (!v)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void geometry(const CityChunk &mesh, const BuildingPlan &p)
{
    for (const auto &m : mesh)
    {
        check(m.indices.size() % 3 == 0, "complete triangles");
        for (auto index : m.indices)
            check(index < m.vertices.size(), "valid mesh index");
        for (const auto &v : m.vertices)
        {
            check(std::isfinite(v.position.x + v.position.y + v.position.z), "finite geometry");
            check(v.position.y >= p.origin.y - .001f, "architecture above its foundation");
            check(std::abs(v.position.x - p.origin.x) <= p.half_width + 1.4f &&
                      std::abs(v.position.z - p.origin.z) <= p.half_depth + 1.4f,
                  "details stay inside reserved overhang");
            check(std::abs(v.normal.length() - 1) < .001f, "unit surface normal");
        }
        for (size_t k = 0; k < m.indices.size(); k += 3)
        {
            auto a = m.vertices[m.indices[k]].position, b = m.vertices[m.indices[k + 1]].position,
                 c = m.vertices[m.indices[k + 2]].position;
            check(cross(b - a, c - a).length() > 1e-7f, "no degenerate triangles");
        }
    }
}
void plans()
{
    for (auto half : {Vec2{7, 4}, Vec2{4, 7}, Vec2{4, 4}})
    {
        Building building{{23, -57}, half, 19, 2};
        auto p = plan_building(building, 13.5);
        auto repeat = plan_building(building, 13.5);
        check(!p.windows.empty(), "inhabited building has windows");
        check(p.windows.size() == repeat.windows.size(), "plan deterministic");
        CityChunk core, detail;
        building_core(core, p, building.style);
        building_details(detail, p);
        geometry(core, p);
        geometry(detail, p);
        for (int slot = Sign; slot < Sign + 3; ++slot)
            for (const auto &vertex : detail[slot].vertices)
            {
                float horizontal =
                    p.windows_on_z ? vertex.position.x - p.origin.x : vertex.position.z - p.origin.z;
                check(std::abs(horizontal) > .65f, "sign does not cover entrance");
                check(vertex.position.y < p.origin.y + 2.3f, "sign does not cover windows");
            }
        check(core[Windows].indices.size() == p.windows.size() * 6, "single opaque quad per window");
        for (int finish = 0; finish < 3; ++finish)
        {
            const auto count = std::ranges::count(p.windows, finish, &FacadeWindow::balcony);
            check(core[Balcony + finish].indices.size() == size_t(count) * 18,
                  "three coarse balcony panels persist past the trim distance");
            check(detail[Balcony + finish].indices.empty(), "near trim does not double the coarse panels");
        }
        for (size_t i = 0; i < p.windows.size(); ++i)
        {
            auto &w = p.windows[i];
            check(w.seed == repeat.windows[i].seed && w.curtain == repeat.windows[i].curtain &&
                      w.balcony == repeat.windows[i].balcony,
                  "window identity does not depend on generation order or LOD");
            check(dot(cross(w.right, Vec3{0, 1, 0}), w.normal) > .999f,
                  "window winding faces outward on both walls");
            check(w.centre.y - p.origin.y > 2.8f && w.centre.y < p.origin.y + p.height - 1,
                  "windows clear entrance and roof");
            check((p.windows_on_z && std::abs(w.normal.z) == 1) ||
                      (!p.windows_on_z && std::abs(w.normal.x) == 1),
                  "short end walls stay blank");
            check(w.curtain >= 0 && w.curtain < 10, "valid curtain style");
            if (w.balcony >= 0)
                check(w.centre.y - p.origin.y - 1 > 3, "balcony clears pedestrians");
            auto &v = core[Windows].vertices[i * 4];
            check(v.joints[0] == float(w.seed % 65521) && v.joints[1] == float(w.curtain),
                  "GPU window identity preserved");
        }
        const auto &wall = core[Facade + 2].vertices;
        for (size_t i = 0; i < wall.size(); i += 4)
        {
            const auto &a = wall[i], &b = wall[i + 1], &d = wall[i + 3];
            check(std::abs(std::abs(b.uv[0] - a.uv[0]) * 3.2f - (b.position - a.position).length()) < .001f,
                  "front opening preserves horizontal texture scale");
            check(std::abs(std::abs(d.uv[1] - a.uv[1]) * 3.2f - (d.position - a.position).length()) < .001f,
                  "front opening preserves vertical texture scale");
        }
        const Vec3 normal = p.windows_on_z ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
        const float depth = p.windows_on_z ? p.half_depth : p.half_width;
        check(!core[Door].vertices.empty(), "entrance stays closed when near trim unloads");
        for (const auto &v : core[Door].vertices)
            check(dot(v.position - p.origin, normal) < depth - .30f, "door is behind the wall plane");
    }
    World world;
    world.generate(20261105);
    size_t windows = 0, triangles = 0;
    for (const auto &b : world.buildings)
    {
        auto p = plan_building(b, world.terrain.height(b.p));
        CityChunk core, detail;
        building_core(core, p, b.style);
        building_details(detail, p);
        geometry(core, p);
        geometry(detail, p);
        windows += p.windows.size();
        for (const auto &m : detail)
            triangles += m.indices.size() / 3;
    }
    check(windows > 10000, "whole city architecture generated");
    std::println("{} buildings, {} windows, {} near-detail triangles", world.buildings.size(), windows,
                 triangles);
    check(triangles < 2000000, "bounded near-detail geometry budget");
}
void visibility()
{
    auto proj = Mat4::perspective(float(pi / 2), 2, 1, 100);
    Frustum f(proj * Mat4::look_at({0, 0, 0}, {0, 0, -1}));
    check(f.sphere({0, 0, -10}, 1), "forward object visible");
    check(!f.sphere({0, 0, 10}, 1), "behind camera rejected");
    check(!f.sphere({0, 0, -102}, 1), "beyond far clip rejected");
    check(!f.sphere({0, 0, 0}, .1f), "before near clip rejected");
    check(f.sphere({0, 0, -1}, .2f), "near plane intersection retained");
    check(f.sphere({19, 0, -10}, .1f), "wide aspect visible at side");
    check(!f.sphere({23, 0, -10}, .1f), "outside horizontal frustum rejected");
    check(!f.sphere({0, 13, -10}, .1f), "outside vertical frustum rejected");
    check(f.sphere({0, 11, -10}, 2), "large object crossing top plane retained");
    Frustum rotated(proj * Mat4::look_at({30, 5, -15}, {31, 5, -15}));
    check(rotated.sphere({40, 5, -15}, 1), "translated rotated view sees forward");
    check(!rotated.sphere({20, 5, -15}, 1), "translated rotated view rejects behind");
}
} // namespace
int main()
{
    try
    {
        plans();
        visibility();
        std::println("{} architecture/culling checks passed", checks);
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
