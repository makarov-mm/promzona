#include "promzona/world.hpp"
#include "promzona/sun_shadow.hpp"
#include "promzona/visibility.hpp"
#include <iostream>
#include <set>
#include <stdexcept>
using namespace pz;
namespace
{
int checks{};
void check(bool ok, const char *message)
{
    ++checks;
    if (!ok)
        throw std::runtime_error(message);
}
Vec3 project(const Mat4 &m, Vec3 p)
{
    return {m.m[0] * p.x + m.m[4] * p.y + m.m[8] * p.z + m.m[12],
            m.m[1] * p.x + m.m[5] * p.y + m.m[9] * p.z + m.m[13],
            m.m[2] * p.x + m.m[6] * p.y + m.m[10] * p.z + m.m[14]};
}
void shadows()
{
    for (auto sun : {Vec3{1, 1, .3f}, Vec3{0, 1, 0}, Vec3{-1, .12f, .3f}, Vec3{1, -1, 0}})
    {
        Vec3 focus{127, 19, -235};
        auto s = sunShadow(focus, sun);
        for (auto x : s.matrix.m)
            check(std::isfinite(x), "finite shadow matrix including zenith");
        auto centre = project(s.matrix, s.centre);
        check(centre.length() < .0001f, "shadow volume centred on focus");
        auto toward = project(s.matrix, s.centre + sun.unit() * 20);
        check(toward.z < centre.z, "occluder toward sun has smaller depth");
        check(std::abs(toward.x) < .0001f && std::abs(toward.y) < .0001f, "sun rays share shadow texel");
        Frustum f(s.matrix);
        check(f.sphere(focus, 1), "focus inside shadow volume");
        check(!f.sphere(focus + sun.unit() * 600, 1), "outside light depth rejected");
        check(sun.y < 0 ? s.strength == 0 : s.strength > 0, "night shadow disabled");
        auto repeat = sunShadow(s.centre + Vec3{.001f, .001f, .001f}, sun);
        check(std::abs(repeat.matrix.m[12] - s.matrix.m[12]) < 1e-6f &&
                  std::abs(repeat.matrix.m[13] - s.matrix.m[13]) < 1e-6f,
              "subtexel motion preserves xy projection");
    }
}
void geometry()
{
    for (auto kind : {TreeKind::birch, TreeKind::linden, TreeKind::spruce})
        for (float height : {5.8f, 10.3f})
        {
            TreePlant tree{{17, 4, -18}, height, height * .28f, kind, 123};
            size_t nearTriangles{};
            for (bool far : {false, true})
            {
                TreeMesh m;
                appendTree(m, tree, far);
                size_t triangles{};
                for (const auto &part : m)
                {
                    check(!part.vertices.empty(), "tree has bark and foliage");
                    check(part.indices.size() % 3 == 0, "complete tree triangles");
                    for (auto index : part.indices)
                        check(index < part.vertices.size(), "valid tree index");
                    for (const auto &v : part.vertices)
                    {
                        check(std::isfinite(v.position.x + v.position.y + v.position.z), "finite vertex");
                        check(std::abs(v.normal.length() - 1) < .001f, "unit tree normal");
                        check(v.position.y >= tree.foot.y - .025f &&
                                  v.position.y < tree.foot.y + height * 1.3f,
                              "bounded tree height");
                        check((v.position - tree.foot).length() < height * 1.35f, "bounded crown");
                    }
                    for (size_t k = 0; k < part.indices.size(); k += 3)
                    {
                        auto a = part.vertices[part.indices[k]].position;
                        auto b = part.vertices[part.indices[k + 1]].position;
                        auto c = part.vertices[part.indices[k + 2]].position;
                        check(cross(b - a, c - a).length() > 1e-7f, "nondegenerate tree faces");
                    }
                    triangles += part.indices.size() / 3;
                }
                if (!far)
                    nearTriangles = triangles;
                else
                    check(triangles < nearTriangles * .5, "far tree halves triangle cost");
            }
        }
}
void placement()
{
    for (auto seed : {20261105u, 123u, 878787u})
    {
        World world;
        world.generate(seed);
        auto repeat = planTrees(world);
        check(world.trees.size() > 50, "city contains vegetation");
        check(world.trees.size() == repeat.size(), "deterministic tree count");
        std::set<unsigned> ids;
        for (const auto &b : world.staticBodies)
            check(ids.insert(b.id).second, "unique static body IDs");
        std::array<int, 3> kinds{};
        for (size_t k = 0; k < world.trees.size(); ++k)
        {
            const auto &t = world.trees[k];
            Vec2 p{t.foot.x, t.foot.z};
            ++kinds[size_t(t.kind)];
            check((t.foot - repeat[k].foot).length() < 1e-5f && t.seed == repeat[k].seed,
                  "stable tree identity");
            check(world.layout.urbanSite(p, t.radius - .001), "tree away from coast and river");
            check(world.layout.roadDistance(p) >= world.layout.roadHalf() + 3.79,
                  "sidewalk and road clearance");
            check(std::abs(t.foot.y - world.terrain.height(p) + .06) < .002, "trunk planted on terrain");
            for (const auto &b : world.buildings)
                check(std::abs(p.x - b.p.x) >= b.half.x + t.radius + 1.39 ||
                          std::abs(p.z - b.p.z) >= b.half.z + t.radius + 1.39,
                      "crown clears buildings");
            for (const auto &s : world.roadLevels)
                check(!s.reserved(p, t.radius + 1.99), "bridge and tunnel approaches clear");
            for (const auto &b : world.layout.furniture)
                check((p - b.p).length() >= 1.799, "tree clears street furniture");
            check(ids.contains(0x40000000u + unsigned(k)), "trunk has fixed collider");
            const auto &body = world.staticBodies[world.staticBodies.size() - world.trees.size() + k];
            check(body.circle && body.invMass == 0 && (body.p - p).length() < .001, "collider matches trunk");
        }
        for (int count : kinds)
            check(count > 0, "all tree species represented");
        std::cout << "seed " << seed << ": " << world.trees.size() << " trees\n";
    }
}
} // namespace
int main()
{
    try
    {
        shadows();
        geometry();
        placement();
        std::cout << checks << " environment checks passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
