#include "promzona/renderer.hpp"
#include "frame_trace.hpp"
#include "promzona/atmosphere.hpp"
#include "promzona/audio.hpp"
#include "promzona/fleet.hpp"
#include "json.hpp"
#include "effect_batch.hpp"
#include "street_lighting.hpp"
#include "cloud_map.hpp"
#include "leaf_mask.hpp"
#include "post_processing.hpp"
#include "promzona/minimap.hpp"
#include "promzona/reflection_probe.hpp"
#include "promzona/sector_stream.hpp"
#include "promzona/names.hpp"
#include "promzona/control_hints.hpp"
#include "promzona/hud_damage.hpp"
#include "promzona/gl.hpp"
#include "promzona/animation.hpp"
#include "promzona/appearance.hpp"
#include "promzona/city_mesh.hpp"
#include "promzona/architecture.hpp"
#include "promzona/visibility.hpp"
#include "promzona/sun_shadow.hpp"
#include "promzona/detail_stream.hpp"
#include "promzona/text.hpp"
#include "ui_canvas.hpp"
#include <optional>
#include <chrono>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <iomanip>
#include <map>
#include <sstream>
#include <unordered_map>
#include <format>
#include <ranges>

namespace Promzona
{
using namespace gl;
namespace
{
GLuint program(const std::string &vertex, const std::string &fragment)
{
    auto compile = [](GLenum type, const std::string &source)
    {
        GLuint id = CreateShader(type);
        auto p = source.c_str();
        ShaderSource(id, 1, &p, nullptr);
        CompileShader(id);
        GLint ok = 0;
        GetShaderiv(id, COMPILE_STATUS, &ok);
        if (!ok)
        {
            char log[8192]{};
            GetShaderInfoLog(id, sizeof log, nullptr, log);
            DeleteShader(id);
            throw std::runtime_error(std::string("GLSL: ") + log);
        }
        return id;
    };
    GLuint v = compile(VERTEX_SHADER, vertex), f = 0, id = 0;
    try
    {
        f = compile(FRAGMENT_SHADER, fragment);
        id = CreateProgram();
        AttachShader(id, v);
        AttachShader(id, f);
        LinkProgram(id);
        GLint ok = 0;
        GetProgramiv(id, LINK_STATUS, &ok);
        if (!ok)
        {
            char log[8192]{};
            GetProgramInfoLog(id, sizeof log, nullptr, log);
            throw std::runtime_error(std::string("GLSL link: ") + log);
        }
        DeleteShader(v);
        DeleteShader(f);
        return id;
    }
    catch (...)
    {
        DeleteShader(v);
        if (f)
            DeleteShader(f);
        if (id)
            DeleteProgram(id);
        throw;
    }
}
// GLSL has no include. A line that begins with #include "name" is replaced by that
// file from the folder of the shader, so that the scene, the effects and the sky share
// one description of the haze and the reflections.
std::string readShader(const std::filesystem::path &file, int depth = 0)
{
    static constexpr std::string_view directive = "#include \"";
    std::istringstream source(read_text(file));
    std::string text, line;
    while (std::getline(source, line))
    {
        if (line.starts_with(directive))
        {
            const auto end = line.find('"', directive.size());
            if (end == std::string::npos || depth > 3)
                throw std::runtime_error("GLSL include: " + file.string());
            text += readShader(file.parent_path() / line.substr(directive.size(), end - directive.size()),
                               depth + 1);
        }
        else
            text += line;
        text += '\n';
    }
    return text;
}
struct Mesh
{
    GLuint vao{}, vbo{}, ibo{}, instanceVbo{};
    GLsizei count{};
    GLenum indexType{GL_UNSIGNED_INT};
    Mesh() = default;
    Mesh(const Mesh &) = delete;
    Mesh &operator=(const Mesh &) = delete;
    Mesh(Mesh &&o) noexcept
    {
        std::swap(vao, o.vao);
        std::swap(vbo, o.vbo);
        std::swap(ibo, o.ibo);
        std::swap(instanceVbo, o.instanceVbo);
        std::swap(count, o.count);
        std::swap(indexType, o.indexType);
    }
    Mesh &operator=(Mesh &&o) noexcept
    {
        if (this != &o)
        {
            release();
            std::swap(vao, o.vao);
            std::swap(vbo, o.vbo);
            std::swap(ibo, o.ibo);
            std::swap(instanceVbo, o.instanceVbo);
            std::swap(count, o.count);
            std::swap(indexType, o.indexType);
        }
        return *this;
    }
    ~Mesh() { release(); }
    void release()
    {
        if (instanceVbo)
            DeleteBuffers(1, &instanceVbo);
        if (ibo)
            DeleteBuffers(1, &ibo);
        if (vbo)
            DeleteBuffers(1, &vbo);
        if (vao)
            DeleteVertexArrays(1, &vao);
        vao = vbo = ibo = instanceVbo = 0;
    }
    void upload(const MeshData &d)
    {
        if (!vao)
        {
            GenVertexArrays(1, &vao);
            GenBuffers(1, &vbo);
            GenBuffers(1, &ibo);
        }
        count = static_cast<GLsizei>(d.indices.size());
        indexType = GL_UNSIGNED_INT;
        BindVertexArray(vao);
        BindBuffer(ARRAY_BUFFER, vbo);
        BufferData(ARRAY_BUFFER, std::ptrdiff_t(d.vertices.size() * sizeof(Vertex)), d.vertices.data(),
                   STATIC_DRAW);
        BindBuffer(ELEMENT_ARRAY_BUFFER, ibo);
        BufferData(ELEMENT_ARRAY_BUFFER, std::ptrdiff_t(d.indices.size() * 4), d.indices.data(), STATIC_DRAW);
        int sizes[] = {3, 3, 2, 4, 4};
        std::uintptr_t offsets[] = {0, 12, 24, 32, 48};
        for (GLuint i = 0; i < 5; i++)
        {
            EnableVertexAttribArray(i);
            VertexAttribPointer(i, sizes[i], GL_FLOAT, GL_FALSE, sizeof(Vertex),
                                reinterpret_cast<const void *>(offsets[i]));
        }
        BindVertexArray(0);
    }
    static constexpr size_t staticVertex = SectorStream::Packet::stride;
    void uploadStatic(const MeshData &d)
    {
        static_assert(sizeof(Vertex) == 64 && offsetof(Vertex, joints) == 32 &&
                      offsetof(Vertex, weights) == staticVertex);
        if (!vao)
        {
            GenVertexArrays(1, &vao);
            GenBuffers(1, &vbo);
            GenBuffers(1, &ibo);
        }
        count = static_cast<GLsizei>(d.indices.size());
        // The shader reads the fourth attribute of scenery as its detail values (the seed
        // and the curtains of a window); the weights after it are never read without a rig.
        static std::vector<unsigned char> packed;
        packed.resize(d.vertices.size() * staticVertex);
        for (size_t i = 0; i < d.vertices.size(); ++i)
            std::memcpy(packed.data() + i * staticVertex, &d.vertices[i], staticVertex);
        BindVertexArray(vao);
        BindBuffer(ARRAY_BUFFER, vbo);
        BufferData(ARRAY_BUFFER, std::ptrdiff_t(packed.size()), packed.data(), STATIC_DRAW);
        BindBuffer(ELEMENT_ARRAY_BUFFER, ibo);
        if (d.vertices.size() <= 65536)
        {
            static std::vector<std::uint16_t> narrow;
            narrow.resize(d.indices.size());
            std::ranges::transform(d.indices, narrow.begin(), [](auto index) { return std::uint16_t(index); });
            BufferData(ELEMENT_ARRAY_BUFFER, std::ptrdiff_t(narrow.size() * 2), narrow.data(), STATIC_DRAW);
            indexType = GL_UNSIGNED_SHORT;
        }
        else
        {
            BufferData(ELEMENT_ARRAY_BUFFER, std::ptrdiff_t(d.indices.size() * 4), d.indices.data(),
                       STATIC_DRAW);
            indexType = GL_UNSIGNED_INT;
        }
        int sizes[] = {3, 3, 2, 4};
        std::uintptr_t offsets[] = {0, 12, 24, 32};
        for (GLuint i = 0; i < 4; i++)
        {
            EnableVertexAttribArray(i);
            VertexAttribPointer(i, sizes[i], GL_FLOAT, GL_FALSE, GLsizei(staticVertex),
                                reinterpret_cast<const void *>(offsets[i]));
        }
        BindVertexArray(0);
    }
    void draw() const
    {
        BindVertexArray(vao);
        glDrawElements(GL_TRIANGLES, count, indexType, nullptr);
    }
    void drawInstances(const std::vector<std::array<float, 8>> &instances, bool upload = true)
    {
        BindVertexArray(vao);
        if (!instanceVbo)
        {
            GenBuffers(1, &instanceVbo);
            BindBuffer(ARRAY_BUFFER, instanceVbo);
            for (GLuint i = 0; i < 2; ++i)
            {
                EnableVertexAttribArray(5 + i);
                VertexAttribPointer(5 + i, 4, GL_FLOAT, GL_FALSE, 8 * sizeof(float),
                                    reinterpret_cast<const void *>(std::uintptr_t(i * 4 * sizeof(float))));
                VertexAttribDivisor(5 + i, 1);
            }
        }
        BindBuffer(ARRAY_BUFFER, instanceVbo);
        // Orphan the stream so a new camera/shadow pass need not wait for the previous draw.
        if (upload)
            BufferData(ARRAY_BUFFER, std::ptrdiff_t(instances.size() * sizeof(instances[0])), instances.data(),
                       DYNAMIC_DRAW);
        DrawElementsInstanced(GL_TRIANGLES, count, indexType, nullptr, GLsizei(instances.size()));
    }
};
void quad(MeshData &m, Vec3 a, Vec3 b, Vec3 c, Vec3 d, float u = 1, float v = 1)
{
    auto n = cross(b - a, c - a).unit();
    auto base = std::uint32_t(m.vertices.size());
    for (auto [p, uv] : {std::pair{a, std::array<float, 2>{0, v}}, std::pair{b, std::array<float, 2>{u, v}},
                         std::pair{c, std::array<float, 2>{u, 0}}, std::pair{d, std::array<float, 2>{0, 0}}})
    {
        Vertex x;
        x.position = p;
        x.normal = n;
        x.uv = uv;
        m.vertices.push_back(x);
    }
    for (auto i : {0u, 1u, 2u, 0u, 2u, 3u})
        m.indices.push_back(base + i);
}
void box(MeshData &m, Vec3 p, Vec3 half, float uv = 1)
{
    auto a = p - half, b = p + half;
    quad(m, {a.x, a.y, b.z}, {b.x, a.y, b.z}, {b.x, b.y, b.z}, {a.x, b.y, b.z}, uv, uv);
    quad(m, {b.x, a.y, a.z}, {a.x, a.y, a.z}, {a.x, b.y, a.z}, {b.x, b.y, a.z}, uv, uv);
    quad(m, {b.x, a.y, b.z}, {b.x, a.y, a.z}, {b.x, b.y, a.z}, {b.x, b.y, b.z}, uv, uv);
    quad(m, {a.x, a.y, a.z}, {a.x, a.y, b.z}, {a.x, b.y, b.z}, {a.x, b.y, a.z}, uv, uv);
    quad(m, {a.x, b.y, b.z}, {b.x, b.y, b.z}, {b.x, b.y, a.z}, {a.x, b.y, a.z}, uv, uv);
    quad(m, {a.x, a.y, a.z}, {b.x, a.y, a.z}, {b.x, a.y, b.z}, {a.x, a.y, b.z}, uv, uv);
}
// A shared patch of curved ribbons. Roots spread through the patch and the
// wider second section twists into a narrow tip, so the profile works side-on.
MeshData grassTuftMesh()
{
    Random rng(0x742ead3);
    MeshData mesh;
    for (int blade = 0; blade < 28; ++blade)
    {
        const float angle = float(rng.next() * 2 * pi), radius = .28f * std::sqrt(float(rng.next()));
        const Vec3 root{radius * std::cos(angle), -.012f, radius * std::sin(angle)};
        const float lean = float(rng.next() * 2 * pi), height = .055f + float(rng.next()) * .095f;
        const Vec3 dir{std::cos(lean), 0, std::sin(lean)}, side{-dir.z, 0, dir.x};
        const float width = .0028f + float(rng.next()) * .004f;
        const Vec3 mid = root + Vec3{0, height * .63f, 0} + dir * (height * .12f);
        const Vec3 tip = root + Vec3{0, height, 0} + dir * (height * .68f);
        const auto first = std::uint32_t(mesh.vertices.size());
        for (auto [point, t] : {std::pair{root - side * width, 0.f}, {root + side * width, 0.f},
                                {mid - side * width * .68f, .63f}, {mid + side * width * .68f, .63f},
                                {tip, 1.f}})
            mesh.vertices.push_back({point, {0, 1, 0}, {0, t}});
        for (auto i : {0u, 1u, 2u, 2u, 1u, 3u, 2u, 3u, 4u})
            mesh.indices.push_back(first + i);
    }
    return mesh;
}
void cylinder(MeshData &mesh, Vec3 a, Vec3 b, float radius, int sides)
{
    Vec3 axis = (b - a).unit();
    Vec3 tangent = cross(axis, std::abs(axis.y) > .8f ? Vec3{1, 0, 0} : Vec3{0, 1, 0}).unit();
    Vec3 bitangent = cross(axis, tangent);
    auto rim = [&](int i)
    {
        float angle = float(2 * pi * i / sides);
        return (tangent * std::cos(angle) + bitangent * std::sin(angle)) * radius;
    };
    for (int i = 0; i < sides; ++i)
    {
        auto r = rim(i), s = rim(i + 1);
        quad(mesh, a + r, a + s, b + s, b + r);
        quad(mesh, a + s, a + r, a, a);
        quad(mesh, b + r, b + s, b, b);
    }
}
// Model the cap in metres on the standing pose, then bind it to the same head
// joint as the hair. The two human rigs have different joint-space origins.
MeshData policeCap(const SkinData &skin)
{
    constexpr int head = 7;
    if (skin.joints <= head)
        throw std::runtime_error("Human rig has no head joint");
    const float *bone = skin.bones.data() + head * 16;
    const Vec3 x{bone[0], bone[1], bone[2]}, y{bone[4], bone[5], bone[6]}, z{bone[8], bone[9], bone[10]};
    const Vec3 origin{bone[12], bone[13], bone[14]};
    MeshData mesh;
    cylinder(mesh, {0, 1.68f, .025f}, {0, 1.747f, .025f}, .103f, 12);
    box(mesh, {0, 1.688f, .121f}, {.077f, .009f, .052f});
    for (auto &vertex : mesh.vertices)
    {
        const auto delta = vertex.position - origin;
        vertex.position = {dot(delta, x) / dot(x, x), dot(delta, y) / dot(y, y), dot(delta, z) / dot(z, z)};
        const auto n = vertex.normal;
        vertex.normal = Vec3{dot(n, x), dot(n, y), dot(n, z)}.unit();
        vertex.joints = {float(head), 0, 0, 0};
        vertex.weights = {1, 0, 0, 0};
    }
    return mesh;
}
GLuint bonePalette(const SkinData &s)
{
    GLuint t;
    glGenTextures(1, &t);
    glBindTexture(GL_TEXTURE_2D, t);
    glTexImage2D(GL_TEXTURE_2D, 0, RGBA32F, s.joints * 4, s.poses, 0, GL_RGBA, GL_FLOAT, s.bones.data());
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    return t;
}
} // namespace
struct Renderer::Impl
{
    std::filesystem::path root;
    GLint staticLightMatrix{};
    GLuint staticDepth{}, scene{}, ui{}, white{}, bones[2]{}, rug{};
    std::array<GLuint, 3> shadowTexture{}, shadowFbo{};
    Mesh uiQuad, cube, marker, skin[2], patrolCap[2];
    // Beyond the skinned people and the full vehicle models, the city keeps its crowd and
    // traffic as instanced stand-ins: a blocky figure per passer-by and the coarsest model
    // of each vehicle, grouped by clothing colour and by model and paint.
    Mesh farPerson;
    static constexpr int crowdColours = 8;
    std::array<std::vector<std::array<float, 8>>, crowdColours> crowdInstances;
    std::vector<std::vector<std::array<float, 8>>> trafficInstances;
    struct
    {
        GLint right{}, up{}, forward{}, sun{}, fog{}, zenith{}, air{}, day{}, rain{}, time{}, probePass{};
    } skyUniforms;
    struct
    {
        GLint eye{}, time{}, sun{}, fog{}, zenith{}, air{}, day{}, rain{}, probeReady{}, probeCentre{},
            probeRoofed{}, waterEdge{}, lamps{}, colors{}, axes{}, clip{}, softDepth{};
    } effectUniforms;
    GLint reflectPuddlesUniform{};
    GLint uiSample{};
    GLuint effectShader{};
    EffectBatch effects;
    bool weaponSight{};
    // The sky, and the local reflection probe: the cube in use and the one being taken.
    static constexpr int probeUnit = 7;
    GLuint sky{}, skyVao{}, probeFbo{}, probeDepth{};
    std::array<GLuint, 2> probeCubes{};
    int probeFront{};
    ReflectionProbe probe;
    // Batches and triangles of the last face rendered for the probe.
    size_t probeBatches{}, probeTriangles{};
    // The sky fills what the scenery left open: one triangle at the far plane. `forward`
    // and `up` are those of the view, `tangent` that of half its vertical angle.
    void drawSky(const Atmosphere &air, Vec3 sun, Vec3 forward, Vec3 up, float tangent, float aspect,
                 float time, bool probePass, Vec3 eye)
    {
        const Vec3 ahead = forward.unit(), right = cross(ahead, up).unit(), top = cross(right, ahead);
        const Vec3 across = right * (tangent * aspect), rise = top * tangent;
        UseProgram(sky);
        Uniform3f(cloudEye, eye.x, eye.y, eye.z);
        Uniform3f(skyUniforms.right, across.x, across.y, across.z);
        Uniform3f(skyUniforms.up, rise.x, rise.y, rise.z);
        Uniform3f(skyUniforms.forward, ahead.x, ahead.y, ahead.z);
        Uniform3f(skyUniforms.sun, sun.x, sun.y, sun.z);
        Uniform3f(skyUniforms.fog, air.horizon.x, air.horizon.y, air.horizon.z);
        Uniform3f(skyUniforms.zenith, air.zenith.x, air.zenith.y, air.zenith.z);
        Uniform4f(skyUniforms.air, air.density, air.heightScale, air.golden, 0);
        Uniform1f(skyUniforms.day, air.daylight);
        Uniform1f(skyUniforms.rain, air.rain);
        Uniform1f(skyUniforms.time, time);
        Uniform1i(skyUniforms.probePass, probePass ? 1 : 0);
        glDepthMask(GL_FALSE);
        BindVertexArray(skyVao);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        BindVertexArray(0);
        glDepthMask(GL_TRUE);
        UseProgram(scene);
    }
    SceneMode mode{SceneMode::enhanced};
    GraphicsPreferences graphics;
    Language language{Language::russian};
    // The name on the van of the gas service in every language, and the batch that shows it.
    std::array<GLuint, 3> serviceLettering{};
    std::array<size_t, 2> serviceLetters{};
    GLuint menuArt{};
    // Height of every street-lamp head, one texel per road node (see scene.frag).
    GLuint lampMap{}, yardMask{};
    void buildYardMask(const CityLayout &layout)
    {
        // One 48 x 48 patch spans the inner 24 metres of each block, with a clear border.
        // The road mesh stays coarse; trail distance and antialiasing live in a mipmapped R8 atlas.
        constexpr int tile = 48;
        const int blocks = layout.nodes() - 1, atlasWidth = blocks * tile;
        std::vector<unsigned char> coverage(size_t(atlasWidth) * atlasWidth);
        for (int j = 0; j < blocks; ++j)
            for (int i = 0; i < blocks; ++i)
                if (const auto *yard = layout.courtyard(i, j))
                    for (const auto &path : yard->paths)
                    {
                        Vec2 origin{layout.coordinate(i) + 6, layout.coordinate(j) + 6};
                        Vec2 lo = path.points.front(), hi = lo;
                        for (auto p : path.points)
                        {
                            lo = {std::min(lo.x, p.x), std::min(lo.z, p.z)};
                            hi = {std::max(hi.x, p.x), std::max(hi.z, p.z)};
                        }
                        double pad = path.halfWidth + .9;
                        auto first = [&](double v) { return clamp(int(std::floor(v * 2)), 0, tile - 1); };
                        for (int z = first(lo.z - origin.z - pad); z <= first(hi.z - origin.z + pad); ++z)
                            for (int x = first(lo.x - origin.x - pad); x <= first(hi.x - origin.x + pad); ++x)
                            {
                                auto p = origin + Vec2{(x + .5) * .5, (z + .5) * .5};
                                auto &byte = coverage[size_t(j * tile + z) * atlasWidth + i * tile + x];
                                auto value = std::lround(255 * clamp(.40 - path.edgeDistance(p), 0., 1.));
                                byte = std::max(byte, static_cast<unsigned char>(value));
                            }
                    }
        if (!yardMask)
            glGenTextures(1, &yardMask);
        glBindTexture(GL_TEXTURE_2D, yardMask);
        constexpr GLint r8 = 0x8229; // GL_R8, not defined by the Windows GL 1.1 header
        glTexImage2D(GL_TEXTURE_2D, 0, r8, atlasWidth, atlasWidth, 0, GL_RED, GL_UNSIGNED_BYTE, coverage.data());
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
        GenerateMipmap(GL_TEXTURE_2D);
    }
    int lampNodes{};
    float lampLow{}, lampSide{};
    void buildLampMap(const World &world)
    {
        const auto &layout = world.layout;
        lampNodes = layout.nodes();
        lampLow = float(layout.coordinate(0));
        lampSide = layout.wide() ? 8.2f : 5.15f;
        std::vector<float> heads(size_t(lampNodes) * lampNodes * 4, -1000.f);
        for (const auto &lampPost : layout.furniture)
        {
            if (lampPost.height < 4)
                continue; // bins and park lamps
            const int i = int(std::lround((lampPost.p.x - lampLow) / CityLayout::spacing));
            const int j = int(std::lround((lampPost.p.z - lampLow) / CityLayout::spacing));
            if (i < 0 || j < 0 || i >= lampNodes || j >= lampNodes)
                continue;
            const double dx = lampPost.p.x - layout.coordinate(i), dz = lampPost.p.z - layout.coordinate(j);
            int channel = -1;
            if (std::abs(dz - 12) < .2 && std::abs(std::abs(dx) - lampSide) < .2)
                channel = dx < 0 ? 0 : 1;
            else if (std::abs(dx - 12) < .2 && std::abs(std::abs(dz) - lampSide) < .2)
                channel = dz < 0 ? 2 : 3;
            if (channel >= 0)
                heads[(size_t(j) * lampNodes + i) * 4 + channel] =
                    float(world.terrain.height(lampPost.p) + lampPost.height - .06);
        }
        if (!lampMap)
            glGenTextures(1, &lampMap);
        glBindTexture(GL_TEXTURE_2D, lampMap);
        glTexImage2D(GL_TEXTURE_2D, 0, RGBA32F, lampNodes, lampNodes, 0, GL_RGBA, GL_FLOAT, heads.data());
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
    }
    float menuAspect{};
    PostProcessing post;
    SceneTimer timer;
    HeadlightSelection headlights;
    StreetLighting street;
    struct StreetUniforms
    {
        GLint pair, blockers, centres, sizes, axes, covers, roofs, spans;
        void locate(GLuint program)
        {
            auto get = [&](const char *name) { return GetUniformLocation(program, name); };
            *this = {get("uBlockedPair"), get("uBlockerCount"), get("uBoxCentre"),   get("uBoxHalf"),
                     get("uBoxForward"),  get("uCoverCount"),   get("uCoverCentre"), get("uCoverHalf")};
        }
        void upload(const StreetLighting &data) const
        {
            std::array<float, 36> values;
            auto send = [&](GLint location, const auto &items, auto member, int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    const Vec3 v = items[i].*member;
                    values[i * 3] = v.x;
                    values[i * 3 + 1] = v.y;
                    values[i * 3 + 2] = v.z;
                }
                if (count)
                    Uniform3fv(location, count, values.data());
            };
            Uniform1i(pair, data.lampIndex);
            Uniform1i(blockers, data.blockerCount);
            send(centres, data.blockers, &LightBlocker::centre, data.blockerCount);
            send(sizes, data.blockers, &LightBlocker::halfSize, data.blockerCount);
            send(axes, data.blockers, &LightBlocker::forward, data.blockerCount);
            Uniform1i(covers, data.coverCount);
            send(roofs, data.covers, &RainCover::centre, data.coverCount);
            send(spans, data.covers, &RainCover::halfSize, data.coverCount);
        }
    } sceneStreet, effectStreet;
    FarShadowCache farShadowCache;
    int poses[2]{};
    std::map<std::uint32_t, ActorMotion> actorMotion;
    std::map<std::filesystem::path, GLuint> textures;
    struct Batch
    {
        Mesh mesh;
        GLuint sectorVao{};
        GLint baseVertex{};
        size_t indexOffset{};
        GLuint color{}, normal{}, orm{};
        Vec3 tint{1, 1, 1};
        float emissive{};
        Vec3 centre{};
        float radius{100000};
        int part{}, surface{};
        // 1 = dedicated paint material, 2 = paint shares an atlas with trim.
        int paintMask{};
        float alpha{1}, roughness{.7f}, metallic{};
        bool detail{};
        // The surface of the city this batch holds (CitySurface), or -1.
        int slot{-1};
    };
    struct Model
    {
        struct Wheel
        {
            Vec3 pivot;
            float radius;
            bool front;
        };
        std::vector<std::vector<Batch>> lods;
        std::vector<Wheel> wheels;
        std::array<Vec3, 2> hinges{};
        void readHinges(const nlohmann::json &manifest)
        {
            if (!manifest.contains("hinges"))
                return;
            for (size_t i = 0; i < hinges.size(); ++i)
            {
                const auto &p = manifest.at("hinges").at(i);
                hinges[i] = {p.at(0), p.at(1), p.at(2)};
            }
        }
    };
    // The standing city by sector. A sector that is not in memory has no batches.
    struct SectorMeshes
    {
        Mesh storage;
        std::vector<LawnTile> lawn;
        std::vector<Batch> batches;
        // Where each surface of the city is among the batches, or -1.
        std::array<std::int16_t, SurfaceCount> at;
        size_t bytes{};
        bool installed{};
        SectorMeshes() { at.fill(-1); }
    };
    std::vector<SectorMeshes> sectors;
    // The sea beyond the island reaches the horizon whatever the draw distance. The
    // outer sectors carry it; this plane stands in for those that are not in memory.
    std::optional<Batch> openSea;
    std::vector<const Batch *> visibleRails;
    GLuint leafCoverage{}, cloudDensity{};
    GLint cloudEye{};
    std::unique_ptr<SectorStream> sectorStream;
    // In memory or being uploaded, and the orders the worker still has to deliver.
    std::vector<bool> sectorResident;
    std::vector<SectorOrder> sectorOrders;
    std::optional<Vec3> sectorAnchor;
    Vec3 sectorEye{};
    float sectorRange{-1};
    bool sectorIdle{};
    std::unique_ptr<SectorStream::Packet> pendingSector;

    size_t pendingSectorBytes{}, sectorBytes{}, peakSectorBytes{}, residentSectors{}, sectorUploads{},
        sectorEvictions{}, sectorBatches{};
    double maxSectorUploadMs{};
    std::array<GLuint, 10> walls{}, normals{}, orm{};
    std::array<GLuint, 3> signs{};
    GLuint stationSign{}, kerbConcrete{}, lawnColor{}, lawnNormal{}, asphaltColor{}, asphaltNormal{};
    float anisotropy{1};
    Mesh grassTuft;
    std::vector<std::array<float, 8>> grassInstances;
    std::optional<std::pair<int, int>> grassCell;
    std::array<size_t, 4> grassSectors{};
    bool grassBufferDirty{};
    GLuint potholeArt[3]{};
    std::unique_ptr<DetailStream> detailStream;
    std::unique_ptr<DetailResidency> detailResidency;
    std::map<size_t, std::vector<Batch>> detailMeshes;
    std::optional<Vec3> detailAnchor;
    std::vector<size_t> detailCandidates, measuredDetailBytes;
    bool detailIdle{};
    std::unique_ptr<DetailStream::Packet> pendingDetail;
    std::vector<Batch> pendingBatches;
    int pendingSurface{};
    size_t pendingBytes{}, detailUploads{}, detailEvictions{}, peakDetailBytes{};
    double maxDetailUploadMs{};

    // Three species, three silhouettes, two LODs; all plants share these meshes.
    std::array<std::array<Mesh, 2>, 18> treeModels;
    std::array<std::vector<std::array<float, 8>>, 18> treeInstances;
    size_t sceneryBytes{}, submittedBatches{}, submittedTriangles{}, submittedDetails{}, shadowBatches{},
        treeCount{};
    std::array<Model, std::size(fleet)> cars;
    std::array<Model, 2> foodModels;
    Model busShelter;
    struct AnimalRig
    {
        Model model;
        GLuint palette{};
        std::array<double, 4> cycle{};
    };
    std::array<AnimalRig, 5> animalRigs;
    Mesh scooterFrame, scooterRubber, scooterTrim;
    static constexpr size_t fixtureKinds = size_t(World::FixtureKind::count);
    std::array<std::array<Mesh, 2>, fixtureKinds> fixtureModels;
    std::array<std::vector<std::array<float, 8>>, fixtureKinds> fixtureInstances;
    std::vector<size_t> visibleFixtures;
    GLuint dumpsterTexture{};
    GLuint peopleTexture[2]{};
    // Weapon badges of the managed HUD: fist, pistol, rifle, launcher, flame, cannon, mounted gun.
    std::array<Image, 7> weaponArt;
    Minimap minimap;
    HudDamage hudDamage;
    std::array<GLuint, HudDamage::texture_count> hudTextures{};
    std::size_t hudTextureSlot{};
    GLuint hudUploadBuffer{};
    struct HudRegion
    {
        int x, y, width, height;
        std::size_t offset;
    };
    std::vector<HudRegion> hudRegions;
    std::vector<std::uint32_t> hudStaging;
    size_t hudPixelsUploaded{};
    std::array<uint32_t, Minimap::size * Minimap::size> mapPixels{};
    int width{}, height{};
    HudCanvas hud;
    std::optional<Font> font, large, letteringFont, huge;
    // Every caller names its uniform with a string literal, so the address of the name
    // finds its location without comparing text.
    std::unordered_map<const char *, GLint> locations;
    GLint uniform(const char *n)
    {
        auto it = locations.find(n);
        if (it != locations.end())
            return it->second;
        auto location = GetUniformLocation(scene, n);
        locations.emplace(n, location);
        return location;
    }
    explicit Impl(const std::filesystem::path &contentRoot) : root(contentRoot)
    {
        gl::load();
        GLint extensions{};
        glGetIntegerv(0x821D /* GL_NUM_EXTENSIONS */, &extensions);
        for (GLint i = 0; i < extensions; ++i)
        {
            std::string_view ext(reinterpret_cast<const char *>(GetStringi(GL_EXTENSIONS, GLuint(i))));
            if (ext == "GL_EXT_texture_filter_anisotropic" || ext == "GL_ARB_texture_filter_anisotropic")
            {
                glGetFloatv(0x84FF /* GL_MAX_TEXTURE_MAX_ANISOTROPY */, &anisotropy);
                anisotropy = std::min(8.f, anisotropy);
                break;
            }
        }
        scene = program(readShader(root / "Shaders/Native/scene.vert"),
                        readShader(root / "Shaders/Native/scene.frag"));
        staticDepth = program(readShader(root / "Shaders/Native/static_depth.vert"),
                              readShader(root / "Shaders/Native/static_depth.frag"));
        staticLightMatrix = GetUniformLocation(staticDepth, "lightMatrix");
        ui = program(read_text(root / "Shaders/Native/ui.vert"), read_text(root / "Shaders/Native/ui.frag"));
        effectShader = program(readShader(root / "Shaders/Native/effects.vert"),
                               readShader(root / "Shaders/Native/effects.frag"));
        effects.initialize(effectShader);
        sky =
            program(read_text(root / "Shaders/Native/sky.vert"), readShader(root / "Shaders/Native/sky.frag"));
        skyUniforms = {
            GetUniformLocation(sky, "uRight"),
            GetUniformLocation(sky, "uUp"),
            GetUniformLocation(sky, "uForward"),
            GetUniformLocation(sky, "uSun"),
            GetUniformLocation(sky, "uFog"),
            GetUniformLocation(sky, "uZenith"),
            GetUniformLocation(sky, "uAir"),
            GetUniformLocation(sky, "uDay"),
            GetUniformLocation(sky, "uRain"),
            GetUniformLocation(sky, "uTime"),
            GetUniformLocation(sky, "uProbePass")};
        UseProgram(sky);
        cloudEye = GetUniformLocation(sky, "uCloudEye");
        Uniform1i(GetUniformLocation(sky, "uCloudMap"), 12);
        UseProgram(scene);
        Uniform1i(GetUniformLocation(scene, "uCloudMap"), 12);
        sceneStreet.locate(scene);
        effectStreet.locate(effectShader);
        UseProgram(effectShader);
        Uniform1i(GetUniformLocation(effectShader, "uSceneColor"), 11);
        Uniform1i(GetUniformLocation(effectShader, "uCloudMap"), 12);
        reflectPuddlesUniform = GetUniformLocation(effectShader, "uReflectPuddles");
        effectUniforms = {GetUniformLocation(effectShader, "uEye"),
                          GetUniformLocation(effectShader, "uTime"),
                          GetUniformLocation(effectShader, "uSun"),
                          GetUniformLocation(effectShader, "uFog"),
                          GetUniformLocation(effectShader, "uZenith"),
                          GetUniformLocation(effectShader, "uAir"),
                          GetUniformLocation(effectShader, "uDay"),
                          GetUniformLocation(effectShader, "uRain"),
                          GetUniformLocation(effectShader, "uProbeReady"),
                          GetUniformLocation(effectShader, "uProbeCentre"),
                          GetUniformLocation(effectShader, "uProbeRoofed"),
                          GetUniformLocation(effectShader, "uWaterEdge"),
                          GetUniformLocation(effectShader, "uHeadPosition"),
                          GetUniformLocation(effectShader, "uHeadColor"),
                          GetUniformLocation(effectShader, "uHeadAxis"),
                          GetUniformLocation(effectShader, "uClip"),
                          GetUniformLocation(effectShader, "uSoftDepth")};
        UseProgram(effectShader);
        Uniform1i(GetUniformLocation(effectShader, "uSceneDepth"), 9);
        UseProgram(ui);
        Uniform1i(GetUniformLocation(ui, "uColor"), 0);
        uiSample = GetUniformLocation(ui, "uSample");
        GenVertexArrays(1, &skyVao);
        // The probe sampler must never share the unit of a 2D sampler, not even before the
        // first frame sets the others.
        for (const GLuint mirrored : {scene, effectShader})
        {
            UseProgram(mirrored);
            Uniform1i(GetUniformLocation(mirrored, "uProbe"), probeUnit);
        }
        UseProgram(0);
        static constexpr const char *constantNames[] = {"uHasNormal", "uHasOrm",   "uDetail",   "uTreeLod",
                                                        "uInstanced", "uSurface",  "uEmissive", "uAlpha",
                                                        "uRoughness", "uMetallic", "uTint"};
        static_assert(std::size(constantNames) == size_t(Constant::count));
        for (auto &&[constant, name] : std::views::zip(constants, constantNames))
            constant.location = GetUniformLocation(scene, name);
        modelLocation = GetUniformLocation(scene, "uModel");
        glGenTextures(2, probeCubes.data());
        for (const GLuint faces : probeCubes)
        {
            glBindTexture(TEXTURE_CUBE_MAP, faces);
            for (int face = 0; face < 6; ++face)
                glTexImage2D(TEXTURE_CUBE_MAP_POSITIVE_X + face, 0, RGBA16F, ReflectionProbe::size,
                             ReflectionProbe::size, 0, GL_RGBA, GL_FLOAT, nullptr);
            glTexParameteri(TEXTURE_CUBE_MAP, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
            glTexParameteri(TEXTURE_CUBE_MAP, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
            glTexParameteri(TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
            glTexParameteri(TEXTURE_CUBE_MAP, TEXTURE_WRAP_R, CLAMP_TO_EDGE);
            GenerateMipmap(TEXTURE_CUBE_MAP);
        }
        glBindTexture(TEXTURE_CUBE_MAP, 0);
        // A blurred reflection reads the small mip levels, where the edges of a cube show.
        glEnable(TEXTURE_CUBE_MAP_SEAMLESS);
        glGenTextures(1, &probeDepth);
        glBindTexture(GL_TEXTURE_2D, probeDepth);
        glTexImage2D(GL_TEXTURE_2D, 0, DEPTH_COMPONENT24, ReflectionProbe::size, ReflectionProbe::size, 0,
                     GL_DEPTH_COMPONENT, GL_UNSIGNED_INT, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        GenFramebuffers(1, &probeFbo);
        BindFramebuffer(FRAMEBUFFER, probeFbo);
        FramebufferTexture2D(FRAMEBUFFER, DEPTH_ATTACHMENT, GL_TEXTURE_2D, probeDepth, 0);
        FramebufferTexture2D(FRAMEBUFFER, COLOR_ATTACHMENT0, TEXTURE_CUBE_MAP_POSITIVE_X, probeCubes[1], 0);
        if (CheckFramebufferStatus(FRAMEBUFFER) != FRAMEBUFFER_COMPLETE)
            throw std::runtime_error("Reflection probe framebuffer incomplete");
        BindFramebuffer(FRAMEBUFFER, 0);
        const auto fullscreen = read_text(root / "Shaders/Native/fullscreen.vert");
        post.initialize(program(fullscreen, read_text(root / "Shaders/Native/glow.frag")),
                        program(fullscreen, read_text(root / "Shaders/Native/resolve.frag")),
                        program(fullscreen, read_text(root / "Shaders/Native/occlusion.frag")));
        glGenTextures(GLsizei(shadowTexture.size()), shadowTexture.data());
        GenFramebuffers(GLsizei(shadowFbo.size()), shadowFbo.data());
        for (size_t i = 0; i < shadowFbo.size(); ++i)
        {
            const int size = i == 0 ? SunShadow::resolution : SunShadow::farResolution;
            glBindTexture(GL_TEXTURE_2D, shadowTexture[i]);
            glTexImage2D(GL_TEXTURE_2D, 0, DEPTH_COMPONENT24, size, size, 0, GL_DEPTH_COMPONENT,
                         GL_UNSIGNED_INT, nullptr);
            glTexParameteri(GL_TEXTURE_2D, TEXTURE_COMPARE_MODE, COMPARE_REF_TO_TEXTURE);
            glTexParameteri(GL_TEXTURE_2D, TEXTURE_COMPARE_FUNC, GL_LEQUAL);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
            BindFramebuffer(FRAMEBUFFER, shadowFbo[i]);
            FramebufferTexture2D(FRAMEBUFFER, DEPTH_ATTACHMENT, GL_TEXTURE_2D, shadowTexture[i], 0);
            glDrawBuffer(GL_NONE);
            glReadBuffer(GL_NONE);
            if (CheckFramebufferStatus(FRAMEBUFFER) != FRAMEBUFFER_COMPLETE)
                throw std::runtime_error("Sun shadow framebuffer incomplete");
        }
        BindFramebuffer(FRAMEBUFFER, 0);
        const auto density = cloud_map(512);
        glGenTextures(1, &cloudDensity);
        glBindTexture(GL_TEXTURE_2D, cloudDensity);
        glTexImage2D(GL_TEXTURE_2D, 0, R8, 512, 512, 0, GL_RED, GL_UNSIGNED_BYTE, density.data());
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        GenerateMipmap(GL_TEXTURE_2D);
        const auto leaves = leafMask(256);
        glGenTextures(1, &leafCoverage);
        glBindTexture(GL_TEXTURE_2D, leafCoverage);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB8, 256, 256, 0, GL_RGB, GL_UNSIGNED_BYTE, leaves.data());
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
        GenerateMipmap(GL_TEXTURE_2D);
        glGenTextures(1, &white);
        glBindTexture(GL_TEXTURE_2D, white);
        unsigned char rgba[] = {255, 255, 255, 255};
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 1, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, rgba);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        const auto menuPath = root / "Textures/menu-city.png";
        const auto art = load_image(menuPath);
        menuAspect = float(art.width) / art.height;
        menuArt = uploadTexture(menuPath, art);
        glGenTextures(GLsizei(hudTextures.size()), hudTextures.data());
        GenBuffers(1, &hudUploadBuffer);
        MeshData q;
        quad(q, {-1, -1, 0}, {1, -1, 0}, {1, 1, 0}, {-1, 1, 0});
        uiQuad.upload(q);
        MeshData c;
        box(c, {}, {.5f, .5f, .5f});
        cube.upload(c);
        MeshData figure;
        box(figure, {0, .44f, 0}, {.15f, .44f, .1f});
        box(figure, {0, 1.17f, 0}, {.21f, .31f, .13f});
        box(figure, {0, 1.6f, 0}, {.11f, .12f, .11f});
        farPerson.upload(figure);
        MeshData arrow;
        const Vec3 rim[] = {{-.43f, .9f, -.43f}, {.43f, .9f, -.43f}, {.43f, .9f, .43f}, {-.43f, .9f, .43f}};
        for (int i = 0; i < 4; ++i)
            quad(arrow, rim[i], rim[(i + 1) % 4], {}, {});
        quad(arrow, rim[3], rim[2], rim[1], rim[0]);
        marker.upload(arrow);
        font.emplace(ui_font_file(root, false), 21);
        large.emplace(ui_font_file(root, true), 55);
        huge.emplace(ui_font_file(root, true), 104);
        for (int i = 0; i < 2; i++)
        {
            auto name = i ? "woman" : "man";
            auto s = load_skin(root / (std::string("Models/PeopleSkinned/") + name + ".cityskin"));
            mark_standing_pose(s);
            skin[i].upload(s.mesh);
            patrolCap[i].upload(policeCap(s));
            poses[i] = s.poses;
            bones[i] = bonePalette(s);
            peopleTexture[i] = texture(root / (std::string("Textures/People/") + name + "-palette.png"));
        }
        constexpr const char *badges[]{"fist", "pistol", "rifle", "launcher", "flame", "cannon", "mg"};
        for (auto &&[picture, badge] : std::views::zip(weaponArt, badges))
        {
            // The badge is decoration: without the file the panel shows the name and rounds only.
            try
            {
                picture = load_image(root / "Textures/Hud" / std::format("{}.png", badge));
            }
            catch (const std::exception &)
            {
                picture = {};
            }
        }
        const char *animalNames[] = {"cat-ginger", "cat-calico", "labrador", "puppy", "pigeon"};
        for (auto &&[rig, name] : std::views::zip(animalRigs, animalNames))
            loadModel(rig.model, root / "Models/AnimalsSkinned" / name, &rig);
        buildFixtureModels();
        MeshData frame, rubber, trim;
        box(frame, {0, .19f, -.06f}, {.13f, .035f, .49f});
        box(rubber, {0, .229f, -.08f}, {.115f, .008f, .37f});
        cylinder(frame, {0, .16f, .48f}, {0, 1.23f, .43f}, .027f, 8);
        cylinder(frame, {-.25f, 1.23f, .43f}, {.25f, 1.23f, .43f}, .024f, 8);
        box(trim, {0, .72f, .46f}, {.03f, .16f, .027f});
        for (float sign : {-1.f, 1.f})
        {
            cylinder(rubber, {-.036f, .13f, sign * .49f}, {.036f, .13f, sign * .49f}, .12f, 16);
            cylinder(frame, {-.042f, .13f, sign * .49f}, {.042f, .13f, sign * .49f}, .048f, 12);
            cylinder(rubber, {sign * .12f, 1.23f, .43f}, {sign * .255f, 1.23f, .43f}, .032f, 8);
        }
        scooterFrame.upload(frame);
        scooterRubber.upload(rubber);
        scooterTrim.upload(trim);
        rug = texture(root / "Textures/Interiors/rug_red.png");
        for (size_t k = 0; k < cars.size(); k++)
        {
            if (k == ambulanceModel)
            {
                loadAmbulance(cars[k], root / "Models/Articulated");
                continue;
            }
            if (!*fleet[k].folder)
            {
                const bool bus = k == busModel;
                if (bus)
                    buildBus(cars[k]);
                else
                    buildServiceVan(cars[k]);
                buildNearServiceVehicle(cars[k], bus);
                continue;
            }
            auto path = root / "Models/Imported" / fleet[k].folder;
            if (!std::filesystem::exists(path / "manifest.json"))
            {
                if (k == 3)
                    path = root / "Models/Imported/1970_soviet_car_vaz_2101";
                else
                    throw std::runtime_error("Missing vehicle manifest");
            }
            loadModel(cars[k], path);
        }
        loadModel(foodModels[0], root / "Models/Imported/soviet_kiosk__3d_model");
        loadModel(foodModels[1], root / "Models/Imported/soviet_soda_machine");
        loadModel(busShelter, root / "Models/Imported/outdated_soviet_bus_stop");
    }
    // The original box models remain the distant LOD. Wheels start at part 3 in both LODs.
    Batch &boxed(Model &model, const MeshData &data, Vec3 tint, float roughness = .5f, int part = 0)
    {
        if (model.lods.empty())
            model.lods.emplace_back();
        auto &b = model.lods.front().emplace_back();
        b.mesh.upload(data);
        b.color = white;
        b.tint = tint;
        b.roughness = roughness;
        b.part = part;
        return b;
    }
    void wheels(Model &model, float side, float axleHeight, float axle, float radius, float capRadius)
    {
        for (int x = -1; x <= 1; x += 2)
            for (int z = 1; z >= -1; z -= 2)
            {
                const Vec3 hub{float(x) * side, axleHeight, float(z) * axle}, across{1, 0, 0};
                const int part = 3 + int(model.wheels.size());
                model.wheels.push_back({hub, radius, z > 0});
                MeshData tyre, plate;
                cylinder(tyre, hub - across * .12f, hub + across * .12f, radius, 14);
                cylinder(plate, hub + across * (float(x) * .12f), hub + across * (float(x) * .15f), capRadius,
                         10);
                boxed(model, tyre, {.02f, .02f, .022f}, .9f, part);
                boxed(model, plate, {.55f, .57f, .58f}, .35f, part).metallic = .8f;
            }
    }
    void lamps(Model &model, float side, float lampHeight, float noseZ, float tailZ, Vec3 half)
    {
        MeshData head, tail;
        for (int x = -1; x <= 1; x += 2)
        {
            box(head, {float(x) * side, lampHeight, noseZ}, half);
            box(tail, {float(x) * side, lampHeight, tailZ}, {half.x * .8f, half.y, half.z});
        }
        boxed(model, head, {1, .95f, .75f}).emissive = .9f;
        boxed(model, tail, {.9f, .06f, .04f}).emissive = .5f;
    }
    void buildBus(Model &model)
    {
        MeshData body, roof, skirt, glass, doors;
        box(body, {0, 1.55f, 0}, {1.09f, 1.08f, 4.48f});
        box(roof, {0, 2.7f, 0}, {1.02f, .21f, 4.27f});
        box(skirt, {0, .86f, 0}, {1.12f, .15f, 4.45f});
        for (int side = -1; side <= 1; side += 2)
            for (int w = 0; w < 7; ++w)
                box(glass, {float(side) * 1.105f, 2.03f, -3.65f + float(w) * 1.17f}, {.012f, .46f, .48f});
        box(glass, {0, 2.04f, 4.49f}, {.98f, .51f, .015f});
        box(glass, {0, 2.04f, -4.49f}, {.98f, .48f, .015f});
        // Two doors on the kerb side.
        for (float z : {3.5f, -.6f})
        {
            box(doors, {-1.12f, 1.47f, z}, {.02f, .92f, .48f});
            box(glass, {-1.145f, 1.90f, z}, {.015f, .42f, .40f});
        }
        boxed(model, body, {.71f, .66f, .42f});
        boxed(model, roof, {.81f, .78f, .61f});
        boxed(model, skirt, {.38f, .21f, .14f});
        boxed(model, doors, {.03f, .03f, .035f}, .8f);
        boxed(model, glass, {.10f, .19f, .22f}, .12f);
        wheels(model, 1.1f, .49f, 2.8f, .47f, .26f);
        lamps(model, .85f, .93f, 4.51f, -4.51f, {.16f, .11f, .02f});
    }
    void buildServiceVan(Model &model)
    {
        MeshData body, roof, glass, trim, bumper, letters;
        box(body, {0, 1.1f, 0}, {.95f, .75f, 2.05f});
        box(roof, {0, 1.86f, -.05f}, {.85f, .14f, 1.89f});
        for (int side = -1; side <= 1; side += 2)
        {
            box(glass, {float(side) * .47f, 1.48f, 2.06f}, {.40f, .30f, .014f});
            for (int w = 0; w < 3; ++w)
                box(glass, {float(side) * .956f, 1.48f, 1.25f - float(w) * 1.1f}, {.014f, .29f, .39f});
            // The name of the service along either flank, reading from the nose on one side
            // and from the tail on the other.
            const float x = float(side) * .962f, from = float(side) * 1.7f - .2f,
                        to = float(-side) * 1.7f - .2f;
            quad(letters, {x, .78f, from}, {x, .78f, to}, {x, 1.14f, to}, {x, 1.14f, from});
        }
        box(trim, {0, .75f, 2.10f}, {.30f, .16f, .025f});
        box(bumper, {0, .43f, 2.13f}, {.94f, .07f, .08f});
        boxed(model, body, {.95f, .68f, .08f}).paintMask = 1;
        boxed(model, roof, {.58f, .62f, .46f});
        boxed(model, glass, {.08f, .19f, .24f}, .12f);
        boxed(model, trim, {.03f, .03f, .035f}, .8f);
        boxed(model, bumper, {.55f, .57f, .58f}, .35f).metallic = .8f;
        // The name is game text like any other: the managed game translates it as well.
        for (size_t k = 0; k < serviceLettering.size(); ++k)
            serviceLettering[k] = lettering(localize(L"ГАЗОВАЯ СЛУЖБА", Language(k)), 3.4f / .36f);
        serviceLetters[1] = model.lods.front().size();
        boxed(model, letters, {.07f, .14f, .27f}, .7f).color = serviceLettering[std::to_underlying(language)];
        wheels(model, .96f, .36f, 1.35f, .35f, .19f);
        lamps(model, .67f, .84f, 2.11f, -2.06f, {.14f, .12f, .02f});
    }
    void buildNearServiceVehicle(Model &model, bool bus)
    {
        auto distant = std::move(model.lods.front());
        model.lods.front().clear();
        model.wheels.clear();

        const float halfWidth = bus ? 1.09f : .95f, length = bus ? 4.48f : 2.05f;
        const float sill = bus ? 1.53f : 1.23f, lintel = bus ? 2.56f : 1.85f;
        const float axle = bus ? 2.8f : 1.35f, hub = bus ? .49f : .36f;
        const float radius = bus ? .47f : .35f, arch = radius + .13f;
        const float base = bus ? .48f : .38f;
        MeshData paint, roof, seals, seats, glass, stripe, letteringPanel;

        // A ring follows the body perimeter. The clipped corners and the narrower upper
        // ring shape the nose, raked windscreen and roof without internal box faces.
        auto ring = [](float w, float l, float y)
        {
            constexpr float bevel = .14f;
            return std::array<Vec3, 8>{{{-w + bevel, y, l},
                                        {w - bevel, y, l},
                                        {w, y, l - bevel},
                                        {w, y, -l + bevel},
                                        {w - bevel, y, -l},
                                        {-w + bevel, y, -l},
                                        {-w, y, -l + bevel},
                                        {-w, y, l - bevel}}};
        };
        const auto belt = ring(halfWidth, length, sill);
        const auto upper = ring(halfWidth - .045f, length - .16f, lintel);
        const auto crown = ring(halfWidth - .12f, length - .24f, lintel + (bus ? .24f : .17f));
        for (size_t i = 0; i < belt.size(); ++i)
        {
            const auto j = (i + 1) % belt.size();
            const bool side = i == 2 || i == 6;
            if (!side)
                quad(paint, {belt[i].x, base, belt[i].z}, {belt[j].x, base, belt[j].z}, belt[j], belt[i]);
            auto &panel = i % 2 == 0 ? glass : paint;
            quad(panel, belt[i], belt[j], upper[j], upper[i]);
            quad(roof, upper[i], upper[j], crown[j], crown[i]);
            // Both ends of the crown have a short bevel; fan from its centre.
            const float capHeight = crown[i].y;
            const auto first = static_cast<std::uint32_t>(roof.vertices.size());
            for (auto p : {Vec3{0, capHeight, 0}, crown[i], crown[j]})
                roof.vertices.push_back({.position = p, .normal = {0, 1, 0}});
            roof.indices.insert(roof.indices.end(), {first, first + 1, first + 2});
        }

        for (const float side : {-1.f, 1.f})
        {
            // Cut the lower flank around both tyres; keep the floor between the arches.
            const float x = side * halfWidth;
            auto flank = [&](float z0, float y0, float z1, float y1)
            {
                if (side < 0)
                    quad(paint, {x, y0, z0}, {x, y1, z1}, {x, sill, z1}, {x, sill, z0});
                else
                    quad(paint, {x, y1, z1}, {x, y0, z0}, {x, sill, z0}, {x, sill, z1});
            };
            float last = -length + .14f;
            for (float centre : {-axle, axle})
            {
                flank(last, base, centre - arch, base);
                for (int k = 0; k < 10; ++k)
                {
                    auto lip = [&](int sample)
                    {
                        const float angle = float(pi) * sample / 10.f;
                        return std::pair{centre - arch * std::cos(angle),
                                         std::max(base, hub + arch * std::sin(angle))};
                    };
                    const auto [z0, y0] = lip(k);
                    const auto [z1, y1] = lip(k + 1);
                    flank(z0, y0, z1, y1);
                }
                last = centre + arch;
            }
            flank(last, base, length - .14f, base);

            const int bays = bus ? 7 : 3;
            for (int i = 1; i < bays; ++i)
            {
                const float z = std::lerp(-length + .18f, length - .18f, float(i) / bays);
                box(seals, {side * (halfWidth - .036f), (sill + lintel) * .5f, z},
                    {.041f, (lintel - sill) * .5f, .028f});
                box(seats, {side * halfWidth * .59f, sill - .14f, z}, {.23f, .09f, .22f});
                box(seats, {side * halfWidth * .59f, sill + .08f, z - .18f}, {.23f, .24f, .055f});
            }
            if (bus)
                box(stripe, {x, 1.18f, 0}, {.009f, .056f, length - .15f});
            else
            {
                // The service van carries tools behind the cab.
                box(paint, {side * (halfWidth - .026f), (sill + lintel) * .5f, -length * .72f},
                    {.028f, (lintel - sill) * .5f, length * .19f});
            }
            const float mirrorZ = length - .38f;
            box(seals, {side * (halfWidth + .07f), lintel - .22f, mirrorZ}, {.11f, .018f, .018f});
            box(seals, {side * (halfWidth + .16f), lintel - .29f, mirrorZ}, {.045f, .10f, .065f});
            if (bus && side < 0)
                for (float z : {3.5f, -.6f})
                    for (float offset : {-.43f, 0.f, .43f})
                        box(seals, {x, (base + lintel) * .5f, z + offset},
                            {.012f, (lintel - base) * .5f, .014f});
            if (!bus)
            {
                const float front = side * 1.46f, back = -front;
                quad(letteringPanel, {x * 1.003f, .90f, front}, {x * 1.003f, .90f, back},
                     {x * 1.003f, 1.19f, back}, {x * 1.003f, 1.19f, front});
            }
        }
        if (bus)
        {
            boxed(model, stripe, {.38f, .21f, .14f});
            box(roof, {0, lintel + .26f, -.6f}, {.44f, .035f, .55f});
        }
        for (int slat = 0; slat < 4; ++slat)
            box(seals, {0, base + .29f + slat * .045f, length + .007f}, {.33f, .012f, .012f});
        box(seals, {0, sill - .22f, 0}, {halfWidth - .17f, .07f, length - .20f});
        box(seals, {0, sill + .018f, length - .34f}, {halfWidth - .16f, .045f, .17f});
        box(seals, {0, base + .05f, length}, {halfWidth - .10f, .065f, .06f});
        box(seals, {0, base + .05f, -length}, {halfWidth - .10f, .065f, .06f});
        boxed(model, paint, bus ? Vec3{.72f, .64f, .39f} : Vec3{.95f, .68f, .08f}).paintMask = bus ? 0 : 1;
        boxed(model, roof, bus ? Vec3{.81f, .78f, .61f} : Vec3{.58f, .62f, .46f});
        boxed(model, seats, {.24f, .24f, .18f}, .84f);
        boxed(model, seals, {.085f, .086f, .080f}, .76f);
        boxed(model, glass, {.19f, .31f, .34f}, .15f).alpha = .35f;
        if (!bus)
        {
            serviceLetters[0] = model.lods.front().size();
            boxed(model, letteringPanel, {.07f, .14f, .27f}).color =
                serviceLettering[std::to_underlying(language)];
        }
        wheels(model, bus ? 1.1f : .96f, hub, axle, radius, bus ? .26f : .19f);
        lamps(model, bus ? .85f : .67f, bus ? .93f : .84f, length + .01f, -length - .01f,
              {.14f, .10f, .025f});
        model.lods.push_back(std::move(distant));
    }
    // White letters on nothing; the batch that shows them gives the colour. The canvas
    // has the proportions of the panel, so that a short name is not drawn out along it.
    GLuint lettering(const std::wstring &text, float aspect)
    {
        if (!letteringFont)
            letteringFont.emplace(lettering_font_file(root), 56);
        const int h = letteringFont->height() + 8,
                  w = (std::max(letteringFont->width(text) + 16, int(float(h) * aspect)) + 3) / 4 * 4;
        HudCanvas canvas;
        canvas.resize(w, h);
        canvas.text_box(*letteringFont, 0, 0, w, h, text, rgb(255, 255, 255), true, false);
        Image image{unsigned(w), unsigned(h), std::vector<unsigned char>(size_t(w) * size_t(h) * 4, 255)};
        const auto *drawn = canvas.data();
        for (size_t i = 0; i < size_t(w) * size_t(h); ++i)
            image.rgba[i * 4 + 3] = static_cast<unsigned char>((drawn[i] >> 8) & 0xFF);
        return uploadTexture(std::filesystem::path("lettering") / utf8_path(utf8(text)), image);
    }
    void loadAmbulance(Model &model, const std::filesystem::path &dir)
    {
        auto meta = nlohmann::json::parse(read_text(dir / "manifest.json")).at("ambulance");
        model.readHinges(meta);
        for (const auto &w : meta.at("wheels"))
        {
            auto p = w.at("pivot");
            model.wheels.push_back({{p[0], p[1], p[2]}, w.at("radius"), w.at("front")});
        }
        auto &lod = model.lods.emplace_back();
        for (const char *name : {"body", "left", "right"})
        {
            auto file = dir / (std::string("ambulance-") + name + ".glb");
            auto mesh = load_glb(file);
            auto image = uploadTexture(file, decode_image(mesh.embedded_color));
            // The articulated exporter stores wheel membership in the palette V coordinate.
            std::vector<MeshData> groups(model.wheels.size() + 1);
            for (auto index : mesh.indices)
            {
                auto v = mesh.vertices[index];
                int wheel = int(std::floor(v.uv[1])) - 2;
                size_t group = wheel >= 0 && wheel < int(model.wheels.size()) ? size_t(wheel + 1) : 0;
                v.uv[1] = .5f;
                auto &g = groups[group];
                g.indices.push_back(std::uint32_t(g.vertices.size()));
                g.vertices.push_back(v);
            }
            for (size_t i = 0; i < groups.size(); ++i)
            {
                if (groups[i].indices.empty())
                    continue;
                auto &b = lod.emplace_back();
                b.part = i                                   ? int(i + 2)
                         : name == std::string_view("left")  ? 1
                         : name == std::string_view("right") ? 2
                                                             : 0;
                b.color = image;
                b.surface = 10; // Articulated vehicle palette; alpha holds the material ID.
                b.mesh.upload(groups[i]);
            }
        }
    }
    void loadModel(Model &model, const std::filesystem::path &path, AnimalRig *rig = nullptr)
    {
        auto j = nlohmann::json::parse(read_text(path / "manifest.json"));
        if (rig)
        {
            auto &clips = j.at("clips");
            rig->cycle = {clips.at("idle").at("seconds"), clips.at("walk").at("stride"),
                          clips.at("run").at("stride"), clips.at("fly").at("seconds")};
        }
        model.readHinges(j);
        for (auto &wheel : j.value("wheels", nlohmann::json::array()))
        {
            auto p = wheel.at("pivot");
            model.wheels.push_back({{p[0], p[1], p[2]}, wheel.at("radius"), wheel.at("front")});
        }
        for (auto &lod : j.at("lods"))
        {
            std::vector<Batch> batches;
            for (auto &part : lod)
            {
                Batch b;
                auto file = path / part.at("file").get<std::string>();
                MeshData data;
                if (rig)
                {
                    auto skinData = load_skin(file);
                    if (skinData.poses < 256)
                        throw std::runtime_error("Incomplete wildlife animation");
                    if (!rig->palette)
                        rig->palette = bonePalette(skinData);
                    data = std::move(skinData.mesh);
                }
                else
                    data = load_glb(file);
                b.mesh.upload(data);
                b.part = part.value("part", 0);
                int mat = part.at("material");
                b.color =
                    data.embedded_color.empty() ? white : uploadTexture(file, decode_image(data.embedded_color));
                if (mat < 0 && j.value("farSurfaceAlpha", false))
                {
                    b.surface = 14;
                    b.paintMask = j.value("kind", std::string{}) == "vehicle" ? 2 : 0;
                }
                if (mat >= 0)
                {
                    auto &m = j.at("materials").at(mat);
                    auto name = m.value("name", std::string{});
                    if (name == "paint" || name == "Car_Paint" || name == "CarBody" ||
                        name == "Ukrainer79Custom_bodymat" || name == "olive" || name == "Cabin")
                        b.paintMask = 1;
                    else if (!m.value("glass", false) && b.part < 3 &&
                             (j.at("materials").size() <= 3 || name == "standard" || name == "standard1"))
                        b.paintMask = 2;
                    if (m.contains("colorMap"))
                        b.color = texture(path / m.at("colorMap").get<std::string>());
                    if (m.contains("normalMap"))
                        b.normal = texture(path / m.at("normalMap").get<std::string>());
                    if (m.contains("color"))
                    {
                        auto color = m.at("color");
                        b.tint = {color[0], color[1], color[2]};
                    }
                    b.alpha = m.value("alpha", 1.f);
                    b.roughness = m.value("roughness", .7f);
                    b.metallic = m.value("metal", 0.f);
                }
                batches.push_back(std::move(b));
            }
            model.lods.push_back(std::move(batches));
        }
        if (model.lods.empty())
            throw std::runtime_error("Empty imported model: " + path.string());
    }
    ~Impl()
    {
        if (probeFbo)
            DeleteFramebuffers(1, &probeFbo);
        glDeleteTextures(2, probeCubes.data());
        glDeleteTextures(1, &probeDepth);
        glDeleteTextures(1, &yardMask);
        if (skyVao)
            DeleteVertexArrays(1, &skyVao);
        if (sky)
            DeleteProgram(sky);
        DeleteFramebuffers(GLsizei(shadowFbo.size()), shadowFbo.data());
        glDeleteTextures(GLsizei(shadowTexture.size()), shadowTexture.data());
        sectorStream.reset();
        sectors.clear();
        openSea.reset();
        for (auto &v : cars)
            v.lods.clear();
        for (auto &[path, t] : textures)
            glDeleteTextures(1, &t);
        glDeleteTextures(1, &cloudDensity);
        glDeleteTextures(1, &leafCoverage);
        glDeleteTextures(1, &white);
        glDeleteTextures(GLsizei(hudTextures.size()), hudTextures.data());
        DeleteBuffers(1, &hudUploadBuffer);
        glDeleteTextures(2, bones);
        for (auto &rig : animalRigs)
            glDeleteTextures(1, &rig.palette);
        if (staticDepth) DeleteProgram(staticDepth);
        if (scene)
            DeleteProgram(scene);
        if (ui)
            DeleteProgram(ui);
        if (effectShader)
            DeleteProgram(effectShader);
    }
    void buildFixtureModels()
    {
        std::array<MeshData, fixtureKinds> enamel, iron;
        cylinder(enamel[0], {0, -.33f, 0}, {0, .32f, 0}, .255f, 12);
        cylinder(enamel[0], {0, .30f, 0}, {0, .36f, 0}, .28f, 12);
        cylinder(iron[0], {0, .36f, 0}, {0, .363f, 0}, .244f, 12);
        for (float side : {-.28f, .28f})
            box(iron[0], {side, -.10f, 0}, {.021f, .325f, .035f});
        cylinder(iron[1], {0, -.41f, 0}, {0, -.34f, 0}, .22f, 12);
        cylinder(enamel[1], {0, -.35f, 0}, {0, .24f, 0}, .15f, 12);
        cylinder(enamel[1], {0, .21f, 0}, {0, .29f, 0}, .18f, 12);
        cylinder(enamel[1], {0, .27f, 0}, {0, .36f, 0}, .115f, 10);
        cylinder(iron[1], {0, .36f, 0}, {0, .41f, 0}, .045f, 6);
        cylinder(enamel[1], {-.23f, .025f, 0}, {.23f, .025f, 0}, .091f, 10);
        for (float side : {-1.f, 1.f})
            cylinder(iron[1], {side * .21f, .025f, 0}, {side * .25f, .025f, 0}, .104f, 8);
        box(enamel[2], {0, .02f, 0}, {.72f, .47f, .53f});
        for (float side : {-1.f, 1.f})
        {
            box(iron[2], {side * .37f, .53f, 0}, {.365f, .045f, .55f});
            box(iron[2], {side * .73f, .28f, 0}, {.02f, .033f, .25f});
            for (float z : {-.38f, .38f})
                cylinder(iron[2], {side * .58f, -.48f, z}, {side * .66f, -.48f, z}, .095f, 8);
        }
        using K = World::FixtureKind;
        auto parts = [&](K kind) -> std::pair<MeshData &, MeshData &>
        { return {enamel[std::to_underlying(kind)], iron[std::to_underlying(kind)]}; };
        auto [slats, legs] = parts(K::bench);
        for (int n = 0; n < 4; ++n)
            box(slats, {0, -.02f, -.18f + n * .12f}, {1, .035f, .050f});
        for (int n = 0; n < 3; ++n)
            box(slats, {0, .17f + n * .14f, -.24f}, {1, .05f, .03f});
        for (float x : {-.78f, .78f})
        {
            cylinder(legs, {x, -.50f, -.18f}, {x, .47f, -.245f}, .034f, 8);
            cylinder(legs, {x, -.50f, .19f}, {x, -.065f, .15f}, .034f, 8);
            box(legs, {x, -.075f, 0}, {.036f, .032f, .24f});
        }
        auto [frame, phone] = parts(K::phoneBooth);
        box(frame, {0, 1.045f, 0}, {.65f, .105f, .65f});
        for (float x : {-.56f, .56f})
        {
            for (float z : {-.56f, .56f})
                box(frame, {x, -.10f, z}, {.035f, 1.05f, .035f});
            box(frame, {x, -.49f, 0}, {.025f, .025f, .54f});
            box(frame, {x, -.79f, 0}, {.02f, .27f, .54f});
        }
        box(frame, {0, -.08f, -.56f}, {.55f, 1.05f, .025f});
        box(phone, {0, .15f, -.43f}, {.23f, .32f, .12f});
        box(frame, {.065f, .23f, -.30f}, {.075f, .10f, .012f});
        cylinder(frame, {.055f, .14f, -.301f}, {.055f, .14f, -.282f}, .08f, 12);
        for (int hole = 0; hole < 10; ++hole)
        {
            float a = float(hole * 2 * pi / 10);
            float x = .055f + .055f * std::cos(a), y = .14f + .055f * std::sin(a);
            cylinder(phone, {x, y, -.282f}, {x, y, -.278f}, .009f, 6);
        }
        box(frame, {.055f, .36f, -.300f}, {.071f, .014f, .012f});
        cylinder(phone, {-.14f, .10f, -.26f}, {-.14f, .42f, -.26f}, .042f, 8);
        cylinder(phone, {-.14f, .12f, -.26f}, {-.12f, -.24f, -.32f}, .011f, 6);
        for (float y : {.10f, .41f})
            cylinder(phone, {-.14f, y, -.29f}, {-.14f, y, -.22f}, .062f, 8);

        auto [mail, pedestal] = parts(K::mailbox);
        cylinder(pedestal, {0, -.73f, 0}, {0, .04f, 0}, .064f, 10);
        box(mail, {0, .35f, 0}, {.32f, .38f, .21f});
        box(pedestal, {0, .54f, .214f}, {.23f, .022f, .009f});
        box(mail, {0, .57f, .229f}, {.25f, .014f, .025f});
        box(pedestal, {0, .23f, .213f}, {.108f, .037f, .009f});

        auto [plastic, handles] = parts(K::bag);
        for (int n = 0; n < 6; ++n)
        {
            float x = -.155f + n * .052f, next = x + .052f;
            float fold = .03f + .017f * std::sin(n * 2.1f),
                  nextFold = .03f + .017f * std::sin((n + 1) * 2.1f);
            for (float side : {-1.f, 1.f})
                quad(plastic, {x * .8f, -.17f, side * .03f}, {next * .8f, -.17f, side * .03f},
                     {next, .065f, side * nextFold}, {x, .065f, side * fold});
        }
        for (float x : {-.076f, .076f})
            for (int n = 0; n < 8; ++n)
            {
                auto loop = [&](int i)
                {
                    float a = float(pi) * i / 8;
                    return Vec3{x + .045f * std::cos(a), .065f + .105f * std::sin(a), .024f};
                };
                cylinder(handles, loop(n), loop(n + 1), .007f, 5);
            }

        auto [glass, cap] = parts(K::bottle);
        cylinder(glass, {-.12f, 0, 0}, {.045f, 0, 0}, .045f, 10);
        cylinder(glass, {.044f, 0, 0}, {.065f, 0, 0}, .035f, 10);
        cylinder(glass, {.064f, 0, 0}, {.12f, 0, 0}, .019f, 8);
        cylinder(cap, {.116f, 0, 0}, {.13f, 0, 0}, .023f, 8);
        auto [paper, band] = parts(K::packet);
        box(paper, {0, -.002f, 0}, {.055f, .035f, .085f});
        box(band, {0, .034f, .04f}, {.053f, .003f, .036f});
        auto [label, alu] = parts(K::can);
        cylinder(alu, {-.055f, 0, 0}, {.055f, 0, 0}, .034f, 10);
        cylinder(label, {-.041f, 0, 0}, {.041f, 0, 0}, .035f, 10);
        auto [sack, knot] = parts(K::garbageBag);
        auto ring = [](int n, float y, float r)
        {
            float a = float(2 * pi) * n / 12, pleat = 1.f + .065f * std::cos(5 * a);
            return Vec3{r * pleat * std::cos(a), y, r * .84f * pleat * std::sin(a)};
        };
        constexpr float ys[]{-.28f, -.21f, -.045f, .11f, .19f, .24f};
        constexpr float rs[]{.15f, .25f, .265f, .19f, .060f, .035f};
        for (int n = 0; n < 12; ++n)
            for (int j = 1; j < 6; ++j)
                quad(sack, ring(n, ys[j - 1], rs[j - 1]), ring(n, ys[j], rs[j]), ring(n + 1, ys[j], rs[j]),
                     ring(n + 1, ys[j - 1], rs[j - 1]));
        cylinder(knot, {0, .20f, 0}, {.053f, .27f, .02f}, .026f, 7);

        for (size_t k = 0; k < fixtureModels.size(); ++k)
        {
            if (k == size_t(K::drinkMachine))
                continue; // Uses the textured service cabinet from Content.
            fixtureModels[k][0].upload(enamel[k]);
            fixtureModels[k][1].upload(iron[k]);
        }
        dumpsterTexture = texture(root / "Textures/Props/dumpster.png");
    }
    GLuint texture(const std::filesystem::path &file)
    {
        auto it = textures.find(file);
        if (it != textures.end())
            return it->second;
        return uploadTexture(file, load_image(file));
    }
    GLuint uploadTexture(const std::filesystem::path &key, const Image &im)
    {
        if (auto found = textures.find(key); found != textures.end())
            return found->second;
        GLuint t;
        glGenTextures(1, &t);
        glBindTexture(GL_TEXTURE_2D, t);
        glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, im.width, im.height, 0, GL_RGBA, GL_UNSIGNED_BYTE,
                     im.rgba.data());
        GenerateMipmap(GL_TEXTURE_2D);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
        // Atlases with material IDs (including one-row car palettes) are data.
        // Filter world-space materials along the long axis of a grazing footprint.
        if (anisotropy > 1 && im.height > 1 &&
            (key.parent_path().filename() == "Facades" || key.parent_path().filename() == "PBR" ||
             key.parent_path().filename() == "Ground"))
            glTexParameterf(GL_TEXTURE_2D, 0x84FE /* GL_TEXTURE_MAX_ANISOTROPY */, anisotropy);
        textures.emplace(key, t);
        return t;
    }
    Batch uploadSurface(const MeshData &data, int i, bool detail)
    {
        auto b = surfaceMaterial(i, detail);
        b.mesh.uploadStatic(data);
        Vec3 low{1e9f, 1e9f, 1e9f}, high{-1e9f, -1e9f, -1e9f};
        for (const auto &vertex : data.vertices)
        {
            const auto &p = vertex.position;
            low = {std::min(low.x, p.x), std::min(low.y, p.y), std::min(low.z, p.z)};
            high = {std::max(high.x, p.x), std::max(high.y, p.y), std::max(high.z, p.z)};
        }
        b.centre = (low + high) * .5f;
        b.radius = (high - low).length() * .5f;
        return b;
    }
    Batch surfaceMaterial(int i, bool detail)
    {
        Batch b;
        b.detail = detail;
        b.slot = i;
        if (i >= Facade && i < Paint)
        {
            b.color = walls[i - Facade];
            b.normal = normals[i - Facade];
            b.orm = orm[i - Facade];
        }
        else
            b.color = white;
        if (i == Grass)
        {
            b.color = lawnColor;
            b.normal = lawnNormal;
            b.tint = {.72f, .92f, .67f};
            b.roughness = .96f;
            b.surface = 1;
        }
        if (i == Road)
        {
            b.color = asphaltColor;
            b.normal = asphaltNormal;
            b.tint = {.57f, .60f, .62f};
            b.surface = 2;
            b.roughness = .86f;
        }
        if (i == Pavement)
        {
            b.color = walls[3];
            b.tint = {.82f, .87f, .90f};
            b.surface = 3;
        }
        if (i == Paint)
            b.tint = {.9f, .86f, .68f};
        if (i == Glass)
        {
            b.tint = {.23f, .34f, .40f};
            b.roughness = .15f;
            b.metallic = .35f;
        }
        if (i == Water)
        {
            b.tint = {.12f, .30f, .34f};
            b.surface = 4;
            b.roughness = .12f;
        }
        if (i == Sand)
        {
            b.tint = {.62f, .53f, .36f};
            b.surface = 5;
        }
        if (i == Metal)
        {
            b.tint = {.22f, .28f, .25f};
            b.metallic = .65f;
        }
        if (i == Lamps)
        {
            b.tint = {.95f, .79f, .48f};
            b.emissive = 1.8f;
        }
        if (i == Windows)
            b.surface = 6;
        if (i == Frame)
            b.tint = {.65f, .66f, .60f};
        if (i == Door)
        {
            b.tint = {.19f, .28f, .25f};
            b.metallic = .25f;
        }
        if (i == Roof)
        {
            b.color = asphaltColor;
            b.tint = {.67f, .68f, .64f};
            b.surface = 15;
        }
        if (i == Foundation)
        {
            b.color = kerbConcrete;
            b.tint = {1.20f, 1.23f, 1.20f};
            b.surface = 15;
        }
        if (i >= Balcony && i < Roof)
        {
            constexpr Vec3 paints[] = {{.27f, .39f, .33f}, {.48f, .39f, .28f}, {.51f, .54f, .49f}};
            b.tint = paints[i - Balcony];
            b.surface = 7;
            b.metallic = .15f;
        }
        if (i == Rust || i == WorkshopSteel)
        {
            const int slot = i == Rust ? 8 : 9;
            b.color = walls[slot];
            b.normal = normals[slot];
            b.orm = orm[slot];
        }
        if (i == Canvas)
            b.tint = {.96f, .53f, .20f};
        if (i == SeaCanvas)
            b.tint = {.12f, .63f, .66f};
        if (i == Ballast)
        {
            b.tint = {.40f, .36f, .30f};
            b.surface = 2;
        }
        if (i == ThinRail)
            b.surface = 16;
        if (i == StationSign)
            b.color = stationSign;
        if (i >= Sign && i < Sign + 3)
            b.color = signs[i - Sign];
        if (i >= PotholeDecal && i < PotholeDecal + 3)
        {
            b.color = potholeArt[i - PotholeDecal];
            b.surface = 11;
            b.roughness = .9f;
        }
        return b;
    }
    void drawGrass(const World &world, Vec3 eye)
    {
        // Cache a four-metre camera cell plus a margin for motion within it.
        // A 52 m square intersects at most four of the 144 m terrain sectors.
        constexpr int cellSize = 4;
        constexpr float extent = 26, spacing = .49f;
        const std::pair cell{int(std::floor(eye.x / cellSize)), int(std::floor(eye.z / cellSize))};
        const float cx = float(cell.first * cellSize) + 2, cz = float(cell.second * cellSize) + 2;
        std::array<size_t, 4> nearby{};
        size_t count = 0;
        auto all = sectorStream->sectors();
        for (size_t id = 0; id < all.size(); ++id)
            if (sectors[id].installed && all[id].x < cx + extent && all[id].x + Sector::size > cx - extent &&
                all[id].z < cz + extent && all[id].z + Sector::size > cz - extent)
                nearby[count++] = id + 1;
        if (cell != grassCell || nearby != grassSectors)
        {
            grassCell = cell;
            grassSectors = nearby;
            grassBufferDirty = true;
            grassInstances.clear();
            for (size_t s = 0; s < count; ++s)
                for (const auto &tile : sectors[nearby[s] - 1].lawn)
                {
                    const float x0 = std::max(tile.x0, cx - extent), x1 = std::min(tile.x1, cx + extent);
                    const float z0 = std::max(tile.z0, cz - extent), z1 = std::min(tile.z1, cz + extent);
                    if (x0 >= x1 || z0 >= z1)
                        continue;
                    const auto *yard =
                        world.layout.courtyard(Vec2{(tile.x0 + tile.x1) * .5, (tile.z0 + tile.z1) * .5});
                    for (int j = int(std::floor(z0 / spacing)); j * spacing < z1; ++j)
                        for (int i = int(std::floor(x0 / spacing)); i * spacing < x1; ++i)
                        {
                            Random rng(world.seed ^ (std::uint32_t(i) * 0x85ebca6bu) ^
                                       (std::uint32_t(j) * 0xc2b2ae35u));
                            const float x = (i + float(rng.next())) * spacing;
                            const float z = (j + float(rng.next())) * spacing;
                            if (x < x0 || x >= x1 || z < z0 || z >= z1 ||
                                (x - cx) * (x - cx) + (z - cz) * (z - cz) > extent * extent)
                                continue;
                            const auto y = tile.height(x, z);
                            const Vec2 at{x, z};
                            if (y < CityLayout::waterLevel + .12f || world.insideBuilding(at, y + .1, .25) ||
                                (world.layout.districts() &&
                                 x >= world.layout.railStart() && x <= world.layout.railEnd() &&
                                 std::abs(z - world.layout.railZ()) < 1.95))
                                continue;
                            if (yard && yard->edge_distance(at) < -.12 + .46 * rng.next())
                                continue;
                            const float yaw = float(rng.next() * 2 * pi);
                            grassInstances.push_back({x, y, z, .62f + .54f * float(rng.next()),
                                                      std::sin(yaw), std::cos(yaw), float(rng.next() * 6),
                                                      float(rng.next())});
                        }
                }
        }
        if (grassInstances.empty() || std::abs(eye.y - world.terrain.height(eye.x, eye.z)) > extent + 12)
            return;
        setModel(Mat4::identity());
        material(lawnColor, 0, {.72f, .92f, .67f});
        setFlag(Constant::instanced, 1);
        setFlag(Constant::surface, 12);
        grassTuft.drawInstances(grassInstances, grassBufferDirty);
        grassBufferDirty = false;
        ++submittedBatches;
        submittedTriangles += size_t(grassTuft.count) / 3 * grassInstances.size();
    }
    void releaseSector(size_t id)
    {
        auto &sector = sectors[id];
        if (pendingSector && pendingSector->id == id)
        {
            pendingSector.reset();
            pendingSectorBytes = 0;
        }
        if (sector.installed)
        {
            sectorBytes -= sector.bytes;
            sectorBatches -= sector.batches.size();
            --residentSectors;
            ++sectorEvictions;
        }
        sector.batches.clear();
        std::vector<LawnTile>().swap(sector.lawn);
        sector.at.fill(-1);
        sector.bytes = 0;
        sector.installed = false;
        // Drop the backing stores, but retain the VAO and buffer names for the next visit.
        BindVertexArray(sector.storage.vao);
        BindBuffer(ARRAY_BUFFER, sector.storage.vbo);
        BufferData(ARRAY_BUFFER, 0, nullptr, STATIC_DRAW);
        BufferData(ELEMENT_ARRAY_BUFFER, 0, nullptr, STATIC_DRAW);
        sectorResident[id] = false;
    }
    // The worker has packed all materials into two contiguous buffers. Keep transfers
    // bounded even when a single terrain material is larger than the frame allowance.
    bool uploadSector(SectorStream::Packet &packet, size_t &offset, bool atOnce)
    {
        const auto start = std::chrono::steady_clock::now();
        auto &sector = sectors[packet.id];
        auto &storage = sector.storage;
        BindVertexArray(storage.vao);
        BindBuffer(ARRAY_BUFFER, storage.vbo);
        if (!offset)
        {
            BufferData(ARRAY_BUFFER, std::ptrdiff_t(packet.vertices.size()), nullptr, STATIC_DRAW);
            BufferData(ELEMENT_ARRAY_BUFFER, std::ptrdiff_t(packet.indices.size()), nullptr, STATIC_DRAW);
        }
        size_t end = atOnce ? packet.bytes : std::min(packet.bytes, offset + 2 * 1024 * 1024);
        auto transfer = [&](GLenum target, const auto &data, size_t base)
        {
            const auto first = std::max(offset, base), last = std::min(end, base + data.size());
            if (first < last)
                BufferSubData(target, std::ptrdiff_t(first - base), std::ptrdiff_t(last - first),
                              data.data() + (first - base));
        };
        transfer(ARRAY_BUFFER, packet.vertices, 0);
        transfer(ELEMENT_ARRAY_BUFFER, packet.indices, packet.vertices.size());
        offset = end;
        const size_t otherPartial = pendingSector && pendingSector.get() != &packet && pendingSectorBytes
                                        ? pendingSector->bytes : 0;
        peakSectorBytes = std::max(peakSectorBytes, sectorBytes + packet.bytes + otherPartial);
        if (offset == packet.bytes)
        {
            for (int i = 0; i < SurfaceCount; ++i)
            {
                const auto &s = packet.surfaces[i];
                if (!s.count)
                    continue;
                auto batch = surfaceMaterial(i, false);
                batch.sectorVao = storage.vao;
                batch.baseVertex = GLint(s.firstVertex);
                batch.indexOffset = s.firstIndexByte;
                batch.mesh.count = GLsizei(s.count);
                batch.mesh.indexType = s.narrow ? GL_UNSIGNED_SHORT : GL_UNSIGNED_INT;
                batch.centre = s.centre;
                batch.radius = s.radius;
                sector.at[i] = std::int16_t(sector.batches.size());
                sector.batches.push_back(std::move(batch));
            }
            sector.lawn = std::move(packet.lawn);
            sector.bytes = packet.bytes;
            sector.installed = true;
            sectorBytes += sector.bytes;
            sectorBatches += sector.batches.size();
            ++residentSectors;
        }
        if (!atOnce)
            maxSectorUploadMs = std::max(maxSectorUploadMs,
                std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count());
        return offset == packet.bytes;
    }
    void installSector(size_t id)
    {
        auto packet = sectorStream->build(id);
        size_t offset{};
        sectorResident[id] = true;
        uploadSector(packet, offset, true);
    }
    // District streaming of the managed game: the standing city in memory follows the
    // eye and the draw distance. A worker builds the sectors that come into range, the
    // nearest first; one is uploaded at a time within a budget; those left behind go.
    void updateSectors(Vec3 eye)
    {
        if (!sectorStream)
            return;
        const auto all = sectorStream->sectors();
        const float range = float(graphics.distance);
        sectorEye = eye;
        // Turning the head does not change the neighbourhood: plan again only after four
        // metres or a new draw distance.
        if (!sectorAnchor || (*sectorAnchor - eye).length() >= 4 || sectorRange != range)
        {
            sectorAnchor = eye;
            sectorRange = range;
            auto plan = planSectors(all, sectorResident, eye, range);
            for (const auto id : plan.evict)
                releaseSector(id);
            // A jump can make an already-partially-uploaded sector the new floor.
            // It is reserved in the residency mask and therefore absent from plan.load.
            if (pendingSector && all[pendingSector->id].distance(eye) < sectorUrgent)
            {
                uploadSector(*pendingSector, pendingSectorBytes, true);
                pendingSector.reset();
                pendingSectorBytes = 0;
                ++sectorUploads;
            }
            sectorOrders = std::move(plan.load);
            // A sector at the eye cannot wait for the worker: after a jump the ground
            // under the camera is built at once.
            while (!sectorOrders.empty() && sectorOrders.front().distance < sectorUrgent)
            {
                installSector(sectorOrders.front().id);
                ++sectorUploads;
                sectorOrders.erase(sectorOrders.begin());
            }
            sectorStream->request(sectorOrders);
            sectorIdle = false;
        }
        else if (sectorIdle)
            return;
        if (!pendingSector)
        {
            pendingSector = sectorStream->poll();
            if (pendingSector)
            {
                if (sectorResident[pendingSector->id])
                    pendingSector.reset(); // built on the spot in the meantime
                else
                {
                    sectorResident[pendingSector->id] = true;
                    pendingSectorBytes = 0;
                }
            }
        }
        if (!pendingSector)
        {
            sectorIdle = sectorOrders.empty();
            return;
        }
        if (uploadSector(*pendingSector, pendingSectorBytes, false))
        {
            pendingSectorBytes = 0;
            std::erase_if(sectorOrders,
                          [&](const SectorOrder &order) { return order.id == pendingSector->id; });
            pendingSector.reset();
            ++sectorUploads;
            // The queue of the worker is short; let it take the next ones.
            sectorStream->request(sectorOrders);
        }
    }
    void updateDetails(Vec3 eye)
    {
        if (!detailStream)
            return;
        const auto cells = detailStream->cells();
        auto evict = [&](size_t reservation)
        {
            for (auto id : detailResidency->trim(cells, eye, reservation))
            {
                detailMeshes.erase(id); // GL buffers die on the render thread.
                ++detailEvictions;
            }
        };
        // Rotation does not change the neighbourhood. Avoid a city-wide sort or worker
        // wakeup on every stationary frame (prefetch has 75 m of headroom over the fade).
        if (!detailAnchor || (*detailAnchor - eye).length() >= 4)
        {
            detailAnchor = eye;
            detailCandidates = detail_order(cells, eye, detail_prefetch_distance);
            detailIdle = false;
            evict(0);
        }
        else if (detailIdle)
            return;
        if (pendingDetail && cells[pendingDetail->id].distance(eye) > detail_keep_distance)
        {
            pendingDetail.reset();
            pendingBatches.clear();
            pendingBytes = 0;
        }
        std::vector<size_t> wanted;
        size_t workingSet{};
        for (auto id : detailCandidates)
        {
            workingSet += measuredDetailBytes[id];
            if (workingSet > detail_gpu_budget)
                break; // Do not continually replace nearer cells with farther ones at the budget limit.
            if (!detailResidency->contains(id) && (!pendingDetail || pendingDetail->id != id))
                wanted.push_back(id);
        }
        detailStream->request(wanted);
        detailIdle = wanted.empty() && !pendingDetail;
        if (!pendingDetail)
        {
            pendingDetail = detailStream->poll();
            if (pendingDetail)
            {
                measuredDetailBytes[pendingDetail->id] = pendingDetail->bytes;
                pendingSurface = 0;
                pendingBytes = 0;
                evict(pendingDetail->bytes);
            }
        }
        if (!pendingDetail)
            return;
        // One material upload is indivisible. Stop between batches after 2 MiB or 1.5 ms.
        // The first batch is always allowed, otherwise a large material could starve forever.
        auto start = std::chrono::steady_clock::now();
        size_t uploaded{};
        while (pendingSurface < SurfaceCount)
        {
            auto &mesh = pendingDetail->geometry[pendingSurface];
            size_t bytes = mesh_bytes(mesh);
            double elapsed =
                std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
            if (uploaded && (uploaded + bytes > 2 * 1024 * 1024 || elapsed >= 1.5))
                break;
            if (!mesh.indices.empty())
            {
                pendingBatches.push_back(uploadSurface(mesh, pendingSurface, true));
                uploaded += bytes;
                pendingBytes += bytes;
            }
            mesh = {}; // Release CPU geometry as soon as this material is uploaded.
            ++pendingSurface;
        }
        maxDetailUploadMs = std::max(
            maxDetailUploadMs,
            std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count());
        peakDetailBytes = std::max(peakDetailBytes, detailResidency->bytes() + pendingBytes);
        if (pendingSurface == SurfaceCount)
        {
            detailResidency->add(pendingDetail->id, pendingDetail->bytes);
            detailMeshes.emplace(pendingDetail->id, std::move(pendingBatches));
            pendingBatches.clear();
            pendingDetail.reset();
            pendingBytes = 0;
            ++detailUploads;
        }
    }
    // The constants of the scene program that change from one draw to the next, the
    // textures of a material and the model matrix. A wall follows a wall and a wheel a
    // wheel: a value already in place costs no call into the driver. Everything that
    // sets them goes through here, and the memory is dropped at the start of every pass,
    // when other code has had the program and the texture units.
    enum class Constant
    {
        hasNormal,
        hasOrm,
        detail,
        treeLod,
        instanced,
        surface,
        emissive,
        alpha,
        roughness,
        metallic,
        tint,
        count
    };
    struct ConstantSlot
    {
        GLint location{-1};
        std::array<float, 3> value{};
        bool known{};
    };
    std::array<ConstantSlot, size_t(Constant::count)> constants;
    std::array<GLuint, 4> unitTexture{};
    std::array<bool, 4> unitKnown{};
    GLint modelLocation{-1};
    Mat4 modelMatrix;
    bool modelKnown{};
    void forgetConstants()
    {
        for (auto &slot : constants)
            slot.known = false;
        unitKnown.fill(false);
        modelKnown = false;
    }
    void setFlag(Constant which, int value)
    {
        auto &slot = constants[std::to_underlying(which)];
        if (slot.known && slot.value[0] == float(value))
            return;
        Uniform1i(slot.location, value);
        slot.value[0] = float(value);
        slot.known = true;
    }
    void setScalar(Constant which, float value)
    {
        auto &slot = constants[std::to_underlying(which)];
        if (slot.known && slot.value[0] == value)
            return;
        Uniform1f(slot.location, value);
        slot.value[0] = value;
        slot.known = true;
    }
    void setColour(Constant which, Vec3 value)
    {
        auto &slot = constants[std::to_underlying(which)];
        if (slot.known && slot.value[0] == value.x && slot.value[1] == value.y && slot.value[2] == value.z)
            return;
        Uniform3f(slot.location, value.x, value.y, value.z);
        slot.value = {value.x, value.y, value.z};
        slot.known = true;
    }
    void bindUnit(unsigned unit, GLuint id)
    {
        if (unitKnown[unit] && unitTexture[unit] == id)
            return;
        ActiveTexture(TEXTURE0 + unit);
        glBindTexture(GL_TEXTURE_2D, id);
        ActiveTexture(TEXTURE0);
        unitTexture[unit] = id;
        unitKnown[unit] = true;
    }
    void setModel(const Mat4 &model)
    {
        if (modelKnown && model.m == modelMatrix.m)
            return;
        UniformMatrix4fv(modelLocation, 1, GL_FALSE, model.m.data());
        modelMatrix = model;
        modelKnown = true;
    }
    void apply(GLuint color, GLuint normal, GLuint ormMap, Vec3 tint, float emissive, int surface,
               float alpha, float roughness, float metallic, bool detail)
    {
        bindUnit(0, color ? color : white);
        bindUnit(1, normal ? normal : white);
        if (ormMap)
            bindUnit(3, ormMap);
        setFlag(Constant::hasNormal, normal ? 1 : 0);
        setFlag(Constant::hasOrm, ormMap ? 1 : 0);
        setFlag(Constant::detail, detail ? 1 : 0);
        setFlag(Constant::treeLod, -1);
        setFlag(Constant::instanced, 0);
        setFlag(Constant::surface, surface);
        setColour(Constant::tint, tint);
        setScalar(Constant::emissive, emissive);
        setScalar(Constant::alpha, alpha);
        setScalar(Constant::roughness, roughness);
        setScalar(Constant::metallic, metallic);
    }
    void material(GLuint color, GLuint normal, Vec3 tint, float emissive = 0)
    {
        apply(color, normal, 0, tint, emissive, 0, 1, .7f, 0, false);
    }
    void draw(const Batch &b, const Mat4 &model, bool beacon = false)
    {
        setModel(model);
        apply(b.color, b.normal, b.orm, b.tint, beacon ? 1.f : b.emissive, b.surface, b.alpha, b.roughness,
              b.metallic, b.detail);
        drawGeometry(b);
    }
    std::vector<const Batch *> shadowCutouts;
    void drawStaticShadows(const auto &visible, const Mat4 &matrix, bool distant)
    {
        UseProgram(staticDepth);
        UniformMatrix4fv(staticLightMatrix, 1, GL_FALSE, matrix.m.data());
        shadowCutouts.clear();
        struct Ranges
        {
            std::array<GLsizei, SurfaceCount> counts;
            std::array<GLint, SurfaceCount> bases;
            std::array<const void *, SurfaceCount> offsets;
            GLsizei size{};
        };
        for (const auto &sector : sectors)
        {
            if (!sector.installed)
                continue;
            Ranges ranges[2];
            for (const auto &b : sector.batches)
            {
                if (b.slot == Water || b.slot >= PotholeDecal || !visible(b.centre, b.radius))
                    continue;
                if (distant && (b.slot == Windows || b.slot == Glass || b.slot == Frame || b.slot == Lamps ||
                                (b.slot >= Sign && b.slot < Rust)))
                    continue;
                if (b.color != white && b.slot > Paint && b.slot != Roof && b.slot != Foundation &&
                    b.slot != Metal && b.slot != Sand && b.slot != WorkshopSteel)
                {
                    shadowCutouts.push_back(&b);
                    continue;
                }
                auto &group = ranges[b.mesh.indexType == GL_UNSIGNED_INT];
                const auto i = group.size++;
                group.counts[i] = b.mesh.count;
                group.offsets[i] = reinterpret_cast<const void *>(b.indexOffset);
                group.bases[i] = b.baseVertex;
            }
            if (!ranges[0].size && !ranges[1].size)
                continue;
            BindVertexArray(sector.storage.vao);
            for (int wide = 0; wide < 2; ++wide)
            {
                const auto &group = ranges[wide];
                if (group.size)
                {
                    MultiDrawElementsBaseVertex(GL_TRIANGLES, group.counts.data(),
                                                wide ? GL_UNSIGNED_INT : GL_UNSIGNED_SHORT,
                                                group.offsets.data(), group.size, group.bases.data());
                    ++shadowBatches;
                }
            }
        }
        UseProgram(scene);
        for (const auto *cutout : shadowCutouts)
        {
            draw(*cutout, Mat4::identity());
            ++shadowBatches;
        }
    }

    static void drawGeometry(const Batch &b)
    {
        if (b.sectorVao)
        {
            BindVertexArray(b.sectorVao);
            DrawElementsBaseVertex(GL_TRIANGLES, b.mesh.count, b.mesh.indexType,
                                   reinterpret_cast<const void *>(b.indexOffset), b.baseVertex);
        }
        else
            b.mesh.draw();
    }
    void solid(Vec3 centre, Vec3 size, Vec3 color, float emissive = 0)
    {
        setModel(Mat4::translate(centre) * Mat4::scale(size));
        material(white, 0, color, emissive);
        cube.draw();
    }
    int hintBar(std::vector<ControlHint> hints, int w, int h)
    {
        // Measure and draw the same translated text; text() must not translate it again.
        for (auto &hint : hints)
        {
            hint.key = localize(hint.key, language);
            hint.action = localize(hint.action, language);
        }
        struct Item
        {
            int x, row, cap;
        };
        std::vector<Item> items;
        items.reserve(hints.size());
        const int left = w >= 650 ? 235 : 8;
        const int right = w - 14;
        auto measure = [&](const std::wstring &s) { return font->width(s); };
        int row = 0, cursor = left + 9;
        for (const auto &hint : hints)
        {
            int cap = std::max(26, measure(hint.key) + 12);
            int hintWidth = cap + 6 + measure(hint.action);
            if (cursor > left + 9 && cursor + hintWidth > right - 8)
            {
                ++row;
                cursor = left + 9;
            }
            items.push_back({cursor, row, cap});
            cursor += hintWidth + 17;
        }
        int top = h - 18 - (row + 1) * 33 - 4;
        rect(left, top, right - left, (row + 1) * 33 + 4, rgb(14, 23, 21));
        rect(left, top, right - left, 2, rgb(83, 124, 94));
        for (const auto &[hint, item] : std::views::zip(hints, items))
        {
            auto [x, line, cap] = item;
            int y = top + 5 + line * 33;
            rect(x, y, cap, 27, rgb(122, 153, 128));
            rect(x + 1, y + 1, cap - 2, 25, rgb(35, 49, 42));
            draw(x + 6, y + 1, hint.key, rgb(226, 238, 217), false);
            draw(x + cap + 6, y + 1, hint.action, rgb(205, 214, 203), false);
        }
        return top;
    }
    void canvas(int w, int h)
    {
        if (width == w && height == h)
            return;
        hud.resize(w, h);
        width = w;
        height = h;
        hudDamage.resize(w, h);
        hudTextureSlot = 0;
        for (GLuint texture : hudTextures)
        {
            glBindTexture(GL_TEXTURE_2D, texture);
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_BGRA_EXT, GL_UNSIGNED_BYTE, hud.data());
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        }
    }

    void uploadHud()
    {
        hudRegions.clear();
        hudPixelsUploaded = 0;
        hudDamage.upload_regions(hudTextureSlot, [&](int x, int y, int w, int h) {
            hudRegions.push_back({x, y, w, h, hudPixelsUploaded});
            hudPixelsUploaded += size_t(w) * h;
        });
        glBindTexture(GL_TEXTURE_2D, hudTextures[hudTextureSlot]);
        hudTextureSlot = (hudTextureSlot + 1) % hudTextures.size();
        if (hudRegions.empty())
            return;

        hudStaging.resize(hudPixelsUploaded);
        const auto *source = hud.data();
        for (const auto &area : hudRegions)
        {
            for (int row = 0; row < area.height; ++row)
                std::memcpy(hudStaging.data() + area.offset + size_t(row) * area.width,
                            source + size_t(area.y + row) * width + area.x,
                            size_t(area.width) * sizeof(std::uint32_t));
        }
        // Replace the backing store in one submission; older transfers may still be in flight.
        BindBuffer(PIXEL_UNPACK_BUFFER, hudUploadBuffer);
        BufferData(PIXEL_UNPACK_BUFFER, std::ptrdiff_t(hudPixelsUploaded * sizeof(std::uint32_t)),
                   hudStaging.data(), STREAM_DRAW);
        for (const auto &area : hudRegions)
            glTexSubImage2D(GL_TEXTURE_2D, 0, area.x, area.y, area.width, area.height, GL_BGRA_EXT, GL_UNSIGNED_BYTE,
                            reinterpret_cast<const void *>(area.offset * sizeof(std::uint32_t)));
        BindBuffer(PIXEL_UNPACK_BUFFER, 0);
    }
    // Game text is authored in Russian and translated at the moment it is drawn.
    void text(int x, int y, const std::wstring &s, Color color = rgb(235, 231, 208), bool big = false)
    {
        draw(x, y, localize(s, language), color, big);
    }
    void draw(int x, int y, const std::wstring &s, Color color, bool big)
    {
        const Font &face = big ? *large : *font;
        // Include glyph overhang at either end of the run.
        hudDamage.touch(x - 3, y - 1, face.width(s) + 6, face.height() + 2);
        hud.text(face, x, y, s, color);
    }
    void menuText(int x, int y, int boxWidth, int boxHeight, const std::wstring &source, Color color,
                  bool title = false)
    {
        hud.text_box(title ? *large : *font, x, y, boxWidth, boxHeight, localize(source, language), color, false,
                     true);
        hudDamage.touch(x, y, boxWidth, boxHeight);
    }
    void rect(int x, int y, int w, int h, Color color)
    {
        hudDamage.touch(x, y, w, h);
        hud.fill(x, y, w, h, color);
    }
    // Blends an RGBA picture over what is already painted, averaging the source texels
    // under every HUD pixel. Black is the unpainted canvas, so the result is never black.
    void icon(int x, int y, int w, int h, const Image &im)
    {
        const int sourceWidth = int(im.width), sourceHeight = int(im.height);
        if (im.rgba.size() < size_t(sourceWidth) * sourceHeight * 4 || sourceWidth <= 0 ||
            sourceHeight <= 0 || w <= 0 || h <= 0)
            return;
        hudDamage.touch(x, y, w, h);
        auto surface = hud.data();
        for (int row = 0; row < h; ++row)
        {
            const int line = y + row;
            if (line < 0 || line >= height)
                continue;
            const int top = row * sourceHeight / h, bottom = std::max(top + 1, (row + 1) * sourceHeight / h);
            for (int column = 0; column < w; ++column)
            {
                const int at = x + column;
                if (at < 0 || at >= width)
                    continue;
                const int left = column * sourceWidth / w,
                          right = std::max(left + 1, (column + 1) * sourceWidth / w);
                unsigned red = 0, green = 0, blue = 0, alpha = 0;
                for (int v = top; v < bottom; ++v)
                    for (int u = left; u < right; ++u)
                    {
                        const unsigned char *texel = &im.rgba[(size_t(v) * sourceWidth + u) * 4];
                        red += unsigned(texel[0]) * texel[3];
                        green += unsigned(texel[1]) * texel[3];
                        blue += unsigned(texel[2]) * texel[3];
                        alpha += texel[3];
                    }
                const unsigned samples = unsigned((bottom - top) * (right - left)) * 255u;
                uint32_t &pixel = surface[size_t(line) * width + at];
                const unsigned keep = samples - alpha;
                const unsigned mixedRed = (red + ((pixel >> 16) & 255u) * keep) / samples,
                               mixedGreen = (green + ((pixel >> 8) & 255u) * keep) / samples,
                               mixedBlue = (blue + (pixel & 255u) * keep) / samples;
                pixel =
                    std::max(mixedRed, 1u) << 16 | std::max(mixedGreen, 1u) << 8 | std::max(mixedBlue, 1u);
            }
        }
    }
};
Renderer::Renderer(const std::filesystem::path &content) : impl_(std::make_unique<Impl>(content)) {}
Renderer::~Renderer() = default;
void Renderer::sceneMode(SceneMode mode)
{
    if (mode == SceneMode::original)
        impl_->post.releaseTargets();
    impl_->mode = mode;
}
void Renderer::language(Language language)
{
    impl_->language = language;
    auto &lods = impl_->cars[gasVanModel].lods;
    for (size_t lod = 0; lod < lods.size(); ++lod)
        lods[lod][impl_->serviceLetters[lod]].color = impl_->serviceLettering[std::to_underlying(language)];
}
void Renderer::graphics(const GraphicsPreferences &settings)
{
    impl_->graphics = settings;
}
void Renderer::profileGpu()
{
    impl_->timer.enable();
}
std::string Renderer::gpuReport() const
{
    return impl_->timer.report();
}
std::string Renderer::graphicsInfo() const
{
    return std::string(reinterpret_cast<const char *>(glGetString(GL_RENDERER))) + " / " +
           reinterpret_cast<const char *>(glGetString(GL_VERSION));
}
std::string Renderer::sceneryStats() const
{
    auto &r = *impl_;
    auto stream = streamingStats();
    return "Scenery batches=" + std::to_string(r.submittedBatches) + "/" + std::to_string(r.sectorBatches) +
           " near=" + std::to_string(r.submittedDetails) +
           " submitted_triangles=" + std::to_string(r.submittedTriangles) +
           " trees=" + std::to_string(r.treeCount) + " shadow_batches=" + std::to_string(r.shadowBatches) +
           " resident_mesh_MiB=" +
           std::to_string((r.sceneryBytes + r.sectorBytes + stream.gpu_bytes) / (1024 * 1024)) +
           " base_MiB=" + std::to_string((r.sceneryBytes + r.sectorBytes) / (1024 * 1024)) +
           " detail_MiB=" + std::to_string(stream.gpu_bytes / (1024 * 1024)) +
           " detail_cells=" + std::to_string(stream.resident_cells) + "/" + std::to_string(stream.total_cells) +
           " hud_upload_pixels=" + std::to_string(r.hudPixelsUploaded) + "/" +
           std::to_string(size_t(r.width) * r.height) + " effect_quads=" + std::to_string(r.effects.size()) +
           " effect_draws=" + std::to_string(r.effects.size() != 0) +
           " detail_uploads=" + std::to_string(stream.uploads) +
           " evictions=" + std::to_string(stream.evictions) +
           " peak_detail_MiB=" + std::to_string(stream.peak_gpu_bytes / (1024 * 1024)) +
           " max_detail_upload_ms=" + std::to_string(stream.maxUploadMs) +
           " sectors=" + std::to_string(stream.residentSectors) + "/" + std::to_string(stream.totalSectors) +
           " sector_uploads=" + std::to_string(stream.sectorUploads) +
           " sector_evictions=" + std::to_string(stream.sectorEvictions) +
           " peak_base_MiB=" + std::to_string((r.sceneryBytes + stream.peakSectorBytes) / (1024 * 1024)) +
           " max_sector_upload_ms=" + std::to_string(stream.maxSectorUploadMs) +
           " probe_faces=" + std::to_string(r.probe.captured()) +
           " probe_batches=" + std::to_string(r.probeBatches) +
           " probe_triangles=" + std::to_string(r.probeTriangles);
}
StreamingStats Renderer::streamingStats() const
{
    const auto &r = *impl_;
    if (!r.detailStream)
        return {};
    const auto worker = r.detailStream->stats();
    StreamingStats stats{r.detailMeshes.size(),
                         r.detailStream->cells().size(),
                         r.detailResidency->bytes() + r.pendingBytes,
                         r.peakDetailBytes,
                         r.detailUploads,
                         r.detailEvictions,
                         worker.queued,
                         worker.ready,
                         r.maxDetailUploadMs};
    if (r.sectorStream)
    {
        const auto sectorWorker = r.sectorStream->stats();
        stats.residentSectors = r.residentSectors;
        stats.totalSectors = r.sectors.size();
        stats.sectorBytes = r.sectorBytes + (r.pendingSector ? r.pendingSector->bytes : 0);
        stats.peakSectorBytes = r.peakSectorBytes;
        stats.sectorUploads = r.sectorUploads;
        stats.sectorEvictions = r.sectorEvictions;
        stats.sectorsQueued = sectorWorker.queued;
        stats.sectorsReady = sectorWorker.ready;
        stats.maxSectorUploadMs = r.maxSectorUploadMs;
        const auto all = r.sectorStream->sectors();
        for (size_t id = 0; id < all.size(); ++id)
            if (!r.sectors[id].installed)
                stats.nearestMissingSector =
                    std::min(stats.nearestMissingSector, all[id].distance(r.sectorEye));
    }
    return stats;
}
void Renderer::prepare(const World &world)
{
    impl_->headlights = {};
    impl_->farShadowCache = {};
    auto &r = *impl_;
    r.street.prepare(world);
    r.effects.clearWeatherCache();
    r.grassCell.reset();
    r.grassInstances.clear();
    r.probe.reset();
    r.minimap.rebuild(world);
    r.buildLampMap(world);
    r.buildYardMask(world.layout);
    r.detailStream.reset(); // Join old immutable jobs before starting a new city.
    r.detailAnchor.reset();
    r.detailMeshes.clear();
    r.pendingDetail.reset();
    r.pendingBatches.clear();
    r.pendingBytes = r.detailUploads = r.detailEvictions = r.peakDetailBytes = 0;
    r.maxDetailUploadMs = 0;
    r.sectorStream.reset(); // The worker of the old city stops before its meshes go.
    r.sectors.clear();
    r.openSea.reset();
    r.sectorOrders.clear();
    r.sectorAnchor.reset();
    r.pendingSector.reset();
    r.pendingSectorBytes = r.sectorBytes = r.peakSectorBytes = r.residentSectors = r.sectorUploads =
        r.sectorEvictions = r.sectorBatches = 0;
    r.maxSectorUploadMs = 0;
    r.sceneryBytes = 0;
    r.actorMotion.clear();
    auto &walls = r.walls;
    auto &normals = r.normals;
    auto &orm = r.orm;
    auto &signs = r.signs;
    auto manifest = nlohmann::json::parse(read_text(r.root / "Textures/manifest.json"));
    for (size_t i = 0; i < walls.size(); ++i)
    {
        const auto &m = manifest[10 + i];
        walls[i] = r.texture(r.root / "Textures" / m.at("color").get<std::string>());
        if (m.contains("normal"))
            normals[i] = r.texture(r.root / "Textures" / m.at("normal").get<std::string>());
        if (m.contains("orm"))
            orm[i] = r.texture(r.root / "Textures" / m.at("orm").get<std::string>());
    }
    for (int i = 0; i < 3; ++i)
        signs[i] = r.texture(r.root / "Textures" / manifest[4 + i].at("color").get<std::string>());
    r.kerbConcrete = r.texture(r.root / "Textures/Facades/concrete_wall_006_diff.jpg");
    const auto ground = r.root / "Textures/Ground";
    r.lawnColor = r.texture(ground / "lawn-color.jpg");
    r.lawnNormal = r.texture(ground / "lawn-normal.jpg");
    r.asphaltColor = r.texture(ground / "asphalt-color.jpg");
    r.asphaltNormal = r.texture(ground / "asphalt-normal.jpg");
    if (!r.grassTuft.vao)
        r.grassTuft.uploadStatic(grassTuftMesh());
    r.stationSign = r.texture(r.root / "Textures/Signs/station.png");
    for (int i = 0; i < 3; ++i)
    {
        // Without the photograph a hole is still a darker patch of the road.
        try
        {
            r.potholeArt[i] = r.texture(r.root / "Textures/Road" / std::format("pothole_{}.png", i + 1));
        }
        catch (const std::exception &)
        {
            r.potholeArt[i] = r.white;
        }
    }
    // The standing city within the draw distance of the player is built before the
    // first frame; from then on the worker follows the eye (updateSectors).
    r.sectorStream = std::make_unique<SectorStream>(world);
    r.sectors.resize(r.sectorStream->sectors().size());
    // Names and vertex formats live for the city. Only resident sectors have backing storage.
    for (auto &sector : r.sectors)
    {
        auto &m = sector.storage;
        GenVertexArrays(1, &m.vao);
        GenBuffers(1, &m.vbo);
        GenBuffers(1, &m.ibo);
        BindVertexArray(m.vao);
        BindBuffer(ARRAY_BUFFER, m.vbo);
        BindBuffer(ELEMENT_ARRAY_BUFFER, m.ibo);
        constexpr int sizes[] = {3, 3, 2, 4};
        constexpr std::uintptr_t offsets[] = {0, 12, 24, 32};
        for (GLuint i = 0; i < 4; ++i)
        {
            EnableVertexAttribArray(i);
            VertexAttribPointer(i, sizes[i], GL_FLOAT, GL_FALSE, SectorStream::Packet::stride,
                                reinterpret_cast<const void *>(offsets[i]));
        }
    }
    BindVertexArray(0);
    r.sectorResident.assign(r.sectors.size(), false);
    {
        const auto at = world.position();
        const Vec3 eye{float(at.x), 0, float(at.z)};
        for (const auto &order :
             planSectors(r.sectorStream->sectors(), r.sectorResident, eye, float(r.graphics.distance)).load)
            r.installSector(order.id);
    }
    if (world.layout.revision())
    {
        // The outer sea meets the streamed water at the sector boundary.
        const float edge = float(std::ceil(world.layout.edge() / Sector::size) * Sector::size),
                    out = edge + 1800, y = float(CityLayout::waterLevel);
        MeshData sea;
        quad(sea, {-out, y, out}, {out, y, out}, {out, y, edge}, {-out, y, edge});
        quad(sea, {-out, y, -edge}, {out, y, -edge}, {out, y, -out}, {-out, y, -out});
        quad(sea, {-out, y, edge}, {-edge, y, edge}, {-edge, y, -edge}, {-out, y, -edge});
        quad(sea, {edge, y, edge}, {out, y, edge}, {out, y, -edge}, {edge, y, -edge});
        for (auto &vertex : sea.vertices)
            vertex.uv = {8.f, 1.f};
        r.openSea = r.uploadSurface(sea, Water, false);
    }
    r.detailStream = std::make_unique<DetailStream>(world);
    r.measuredDetailBytes.assign(r.detailStream->cells().size(), 0);
    r.detailResidency = std::make_unique<DetailResidency>(r.detailStream->cells().size());
    r.treeCount = world.trees.size();
    for (int species = 0; species < 3; ++species)
        for (int variant = 0; variant < 3; ++variant)
            for (int lod = 0; lod < 2; ++lod)
            {
                TreeMesh data;
                TreePlant prototype{{0, 0, 0},
                                    8,
                                    8 * (species == 1   ? .28f
                                         : species == 0 ? .24f
                                                        : .22f),
                                    TreeKind(species),
                                    std::uint32_t(719 + variant * 4321)};
                append_tree(data, prototype, lod != 0);
                auto &model = r.treeModels[(species * 3 + variant) * 2 + lod];
                for (int part = 0; part < 2; ++part)
                {
                    model[part].upload(data[part]);
                    r.sceneryBytes +=
                        data[part].vertices.size() * sizeof(Vertex) + data[part].indices.size() * 4;
                }
            }
}
void Renderer::render(const World &world, const View &view, int w, int h)
{
    auto &r = *impl_;
    r.weaponSight = view.weapon_sight;
    const bool post = r.mode != SceneMode::original;
    const int sceneWidth = r.mode == SceneMode::scaled ? std::max(1, (w * 3 + 2) / 4) : w;
    const int sceneHeight = r.mode == SceneMode::scaled ? std::max(1, (h * 3 + 2) / 4) : h;
    if (post)
        r.post.resize(sceneWidth, sceneHeight);
    frame_trace.enter(FrameTrace::sectors);
    r.updateSectors(view.eye);
    frame_trace.enter(FrameTrace::details);
    r.updateDetails(view.eye);
    frame_trace.enter(FrameTrace::prepare);
    r.timer.begin();
    ActiveTexture(TEXTURE0 + 12);
    glBindTexture(GL_TEXTURE_2D, r.cloudDensity);
    ActiveTexture(TEXTURE0);
    float day = float(world.clock.daylight()), rain = float(world.rain);
    // The sky and the haze of the managed game for this hour and weather.
    const auto air = Atmosphere::create(world.rain, world.clock.daylight(), world.clock.sun_height(),
                                        world.wetness, world.clock.hour > 12);
    const Vec3 fog = air.horizon;

    glEnable(GL_DEPTH_TEST);
    glDepthFunc(GL_LEQUAL);
    glDisable(GL_BLEND);
    glDisable(GL_CULL_FACE);
    UseProgram(r.scene);
    const float range = float(r.graphics.distance);
    const float detailScale = .5f + .25f * r.graphics.quality;
    const float nearPlane = std::clamp(view.distance * .01f, .12f, 8.f);
    const float farPlane = std::max(650.f, range + view.distance + 100.f);
    const auto projection = Mat4::perspective(float(pi / 3), float(w) / h, nearPlane, farPlane);
    auto vp = projection * Mat4::look_at(view.eye, view.target);
    Vec3 sun{float(std::cos((world.clock.hour - 6) * pi / 12)), float(world.clock.sun_height()), .3f};
    const Vec3 shadowFocus = view.weapon_sight ? view.eye : view.target;
    auto shadow = sunShadow(shadowFocus, sun);
    const bool farEnabled = view.shadows && r.graphics.quality > 1 && shadow.strength > 0.f;
    const Vec3 farFocus = shadowFocus + (view.target - view.eye).unit() * 54.f;
    const Vec3 lightDirection = sun.unit();
    const auto farUpdate =
        r.farShadowCache.advance(farEnabled, farFocus, lightDirection, r.sectorUploads + r.sectorEvictions);
    using Portion = FarShadowCache::Portion;
    const bool refreshFar = farUpdate.portion != Portion::none;
    r.timer.farRefresh(refreshFar);
    Uniform1i(r.uniform("uFarShadow"), 10);
    Uniform1i(r.uniform("uFarEnabled"), farEnabled);
    UniformMatrix4fv(r.uniform("uFarShadowMatrix"), 1, GL_FALSE, farUpdate.visible.m.data());
    if (!view.shadows || r.graphics.quality == 0)
        shadow.strength = 0;
    UniformMatrix4fv(r.uniform("uShadowMatrix"), 1, GL_FALSE, shadow.matrix.m.data());
    Uniform1f(r.uniform("uShadowStrength"), shadow.strength * (1 - rain * .7f));
    Uniform1i(r.uniform("uShadow"), 5);
    r.submittedBatches = r.submittedTriangles = r.submittedDetails = r.shadowBatches = 0;
    world.fixturesNear({view.eye.x, view.eye.z}, 145, r.visibleFixtures);
    // Four pairs illuminate visible road, followed by up to four emergency beacons.
    std::array<float, 36> lampPosition{}, lampColor{}, lampDirection{};
    {
        const float dark = 1 - day;
        const Vec2 focus{view.target.x, view.target.z};
        auto put = [&](int slot, Vec3 position, Vec3 colour, Vec3 direction)
        {
            const float values[3][3] = {{position.x, position.y, position.z},
                                        {colour.x, colour.y, colour.z},
                                        {direction.x, direction.y, direction.z}};
            for (int k = 0; k < 3; ++k)
            {
                lampPosition[size_t(slot) * 3 + k] = values[0][k];
                lampColor[size_t(slot) * 3 + k] = values[1][k];
                lampDirection[size_t(slot) * 3 + k] = values[2][k];
            }
        };
        r.headlights.update(world, view.eye, Frustum(vp), dark > .02f);
        {
            for (size_t k = 0; k < r.headlights.slots.size(); ++k)
            {
                const auto &slot = r.headlights.slots[k];
                if (slot.car < 0)
                    continue;
                const auto &car = world.cars[slot.car];
                const auto f = forward(car.heading());
                const Vec3 ahead{float(f.x), 0, float(f.z)}, right{float(f.z), 0, float(-f.x)};
                const Vec3 nose =
                    Vec3{float(car.body.p.x), float(car.body.height - .02), float(car.body.p.z)} +
                    ahead * (float(car.body.halfH) + .03f);
                const Vec3 beam = (ahead + Vec3{0, float(std::sin(car.terrainPitch)) - .10f, 0}).unit();
                for (int side = 0; side < 2; ++side)
                    put(int(k) * 2 + side, nose + right * ((side * 2 - 1) * float(car.body.halfW) * .77f),
                        Vec3{5.4f, 5.05f, 4.35f} * (dark * slot.intensity), beam);
            }
        }
        int slot = 8;
        for (int i = 0; i < int(world.cars.size()) && slot < 12; ++i)
        {
            const auto &car = world.cars[i];
            const auto *call = car.kind == 4 ? world.ambulanceCall(i) : nullptr;
            const bool ambulance = call && call->stage != World::RescueStage::standby;
            if (car.destroyed || car.falling || !(car.responding || ambulance) ||
                (car.body.p - focus).length2() > 60 * 60)
                continue;
            const bool blue = std::sin(world.time * 9 + car.id) > 0;
            const Vec3 colour = ambulance ? Vec3{.08f, .45f, 2.5f} * (blue ? 1.f : .12f)
                                : blue    ? Vec3{.08f, .45f, 2.5f}
                                          : Vec3{2.2f, .08f, .025f};
            put(slot++,
                {float(car.body.p.x), float(car.body.height - .8 + car.body.verticalExtent),
                 float(car.body.p.z)},
                colour * (1 - .92f * day), {});
        }
    }
    const bool localLights = day < .999f || std::ranges::any_of(lampColor, [](float c) { return c > 0.f; });
    r.street.update(world, view.eye, r.headlights);
    r.sceneStreet.upload(r.street);
    // Covered tunnels for the shader: centre and half-length of the roofed part, then
    // the linear floor under the roof and the phase of the ceiling lamps along x.
    std::array<float, 24> tunnelVolume{}, tunnelFloor{};
    int tunnels = 0;
    for (const auto &level : world.roadLevels)
        if (level.tunnel && tunnels < 8)
        {
            const double slope = (level.floorSamples[198] - level.floorSamples[90]) / 108;
            const double lamps = level.x - RoadLevel::length + .5;
            tunnelVolume[tunnels * 3] = float(level.x);
            tunnelVolume[tunnels * 3 + 1] = float(level.z + 18);
            tunnelVolume[tunnels * 3 + 2] = float(RoadLevel::covered);
            tunnelFloor[tunnels * 3] = float(level.floorSamples[144]);
            tunnelFloor[tunnels * 3 + 1] = float(slope);
            tunnelFloor[tunnels * 3 + 2] = float(lamps - std::floor(lamps / 9) * 9);
            ++tunnels;
        }
    auto probeUnderRoof = [&](Vec3 centre)
    {
        const Vec2 p{centre.x, centre.z};
        const auto *level = world.levelAt(p);
        return level && level->roof(p.x) &&
               centre.y < level->floor(world.terrain, p) + RoadLevel::clearance;
    };
    // The reflection probe of the managed game follows the player, 1.8 m above the ground
    // under them. The lowest quality does without it.
    int probeFace = -1;
    Vec3 probeEye{};
    if (r.graphics.quality > 0)
    {
        const bool driving = world.controlledCar >= 0;
        const Body &body = driving ? world.cars[world.controlledCar].body : world.player;
        const Vec3 centre{float(body.p.x), float(body.height - (driving ? .8 : .85) + 1.8), float(body.p.z)};
        // The cube is taken again when the city around it has changed: a sector within
        // its reach has arrived or gone.
        int surroundings = 0;
        if (r.sectorStream)
        {
            const auto all = r.sectorStream->sectors();
            for (size_t id = 0; id < all.size(); ++id)
                if (r.sectors[id].installed && all[id].distance(centre) < ReflectionProbe::farPlane)
                    surroundings += int(id) + 1;
        }
        probeFace = r.probe.begin(centre, world.clock.hour, world.rain, world.time, surroundings);
        probeEye = r.probe.pending();
    }
    const Mat4 probeMatrix = probeFace < 0
                                 ? Mat4::identity()
                                 : ReflectionProbe::projection() * ReflectionProbe::view(probeEye, probeFace);
    const Vec3 sunRay{float(std::cos((world.clock.hour - 6) * pi / 12)), float(world.clock.sun_height()), .3f};
    Uniform3f(r.uniform("uFog"), fog.x, fog.y, fog.z);
    Uniform3f(r.uniform("uZenith"), air.zenith.x, air.zenith.y, air.zenith.z);
    Uniform4f(r.uniform("uAir"), air.density, air.heightScale, air.golden, float(world.wetness));
    Uniform3f(r.uniform("uSun"), sunRay.x, sunRay.y, sunRay.z);
    Uniform1f(r.uniform("uDay"), day);
    Uniform1f(r.uniform("uTime"), float(world.time));
    Uniform1f(r.uniform("uHour"), float(world.clock.hour));
    Uniform1f(r.uniform("uRain"), rain);
    Uniform1i(r.uniform("uColor"), 0);
    Uniform1i(r.uniform("uNormal"), 1);
    Uniform1i(r.uniform("uBones"), 2);
    Uniform1i(r.uniform("uOrm"), 3);
    Uniform1i(r.uniform("uRug"), 4);
    Uniform1i(r.uniform("uLocalLights"), localLights);
    Uniform1f(r.uniform("uWaterEdge"), float(world.layout.edge()));
    Uniform1i(r.uniform("uGridNodes"), r.lampNodes);
    Uniform1f(r.uniform("uGridLow"), r.lampLow);
    Uniform1f(r.uniform("uLampSide"), r.lampSide);
    Uniform1i(r.uniform("uLampMap"), 6);
    Uniform3fv(r.uniform("uLampPosition"), 12, lampPosition.data());
    Uniform3fv(r.uniform("uLampColor"), 12, lampColor.data());
    Uniform3fv(r.uniform("uLampDirection"), 12, lampDirection.data());
    Uniform1i(r.uniform("uTunnelCount"), tunnels);
    Uniform3fv(r.uniform("uTunnels"), 8, tunnelVolume.data());
    Uniform3fv(r.uniform("uTunnelFloors"), 8, tunnelFloor.data());
    ActiveTexture(TEXTURE0 + 6);
    glBindTexture(GL_TEXTURE_2D, r.lampMap);
    ActiveTexture(TEXTURE0 + 8);
    glBindTexture(GL_TEXTURE_2D, r.yardMask);
    Uniform1i(r.uniform("uYardMask"), 8);
    ActiveTexture(TEXTURE0);
    Uniform1f(r.uniform("uDetailStart"), building_detail_start * detailScale);
    Uniform1f(r.uniform("uDetailEnd"), building_detail_end * detailScale);
    ActiveTexture(TEXTURE0 + 4);
    glBindTexture(GL_TEXTURE_2D, r.rug);
    ActiveTexture(TEXTURE0);
    const auto shadowReceivers = Frustum(vp).extruded(lightDirection * SunShadow::depth);
    // Far and near sunlight, the pending reflection face, then the camera view.
    for (int pass = 0; pass < 4; ++pass)
    {
        const bool farPass = pass == 0, depthPass = pass <= 1, probePass = pass == 2, viewPass = pass == 3;
        if (farPass && !refreshFar)
            continue;
        const int cascade = farPass ? farUpdate.writeTarget : 0;
        const int shadowSize = farPass ? SunShadow::farResolution : SunShadow::resolution;
        if (probePass)
            r.timer.mark(SceneTimer::reflection);
        if (viewPass)
            r.timer.mark(SceneTimer::opaque);
        if ((depthPass && shadow.strength <= 0) || (probePass && probeFace < 0))
            continue;
        frame_trace.enter(depthPass ? FrameTrace::shadow : probePass ? FrameTrace::probe : FrameTrace::scene);
        const Vec3 eye = probePass ? probeEye : view.eye;
        const Mat4 &passMatrix = depthPass   ? (farPass ? farUpdate.draw : shadow.matrix)
                                 : probePass ? probeMatrix
                                             : vp;
        const float passRange = probePass ? ReflectionProbe::farPlane : range;
        ActiveTexture(TEXTURE0 + 5);
        // Never sample a depth texture while rendering into it.
        glBindTexture(GL_TEXTURE_2D, depthPass ? 0 : r.shadowTexture[0]);
        ActiveTexture(TEXTURE0 + 10);
        glBindTexture(GL_TEXTURE_2D, depthPass ? 0 : r.shadowTexture[farUpdate.readTarget]);
        ActiveTexture(TEXTURE0 + Impl::probeUnit);
        glBindTexture(TEXTURE_CUBE_MAP, r.probeCubes[r.probeFront]);
        ActiveTexture(TEXTURE0);
        BindFramebuffer(FRAMEBUFFER, depthPass   ? r.shadowFbo[cascade]
                                     : probePass ? r.probeFbo
                                                 : (post ? r.post.sceneTarget() : 0));
        if (probePass)
            FramebufferTexture2D(FRAMEBUFFER, COLOR_ATTACHMENT0, TEXTURE_CUBE_MAP_POSITIVE_X + probeFace,
                                 r.probeCubes[1 - r.probeFront], 0);
        const int passWidth = depthPass ? shadowSize : probePass ? ReflectionProbe::size : sceneWidth;
        const int passHeight = depthPass ? shadowSize : probePass ? ReflectionProbe::size : sceneHeight;
        glViewport(0, 0, passWidth, passHeight);
        glColorMask(!depthPass, !depthPass, !depthPass, !depthPass);
        if (depthPass)
        {
            glEnable(GL_POLYGON_OFFSET_FILL);
            glPolygonOffset(1.4f, 2.f);
        }
        else
        {
            glDisable(GL_POLYGON_OFFSET_FILL);
            glClearColor(fog.x, fog.y, fog.z, 1);
        }
        const bool halfMap = farPass && farUpdate.portion != Portion::full;
        Mat4 clipRegion = Mat4::identity();
        if (halfMap)
        {
            const bool right = farUpdate.portion == Portion::right;
            glEnable(GL_SCISSOR_TEST);
            glScissor(right ? shadowSize / 2 : 0, 0, shadowSize / 2, shadowSize);
            clipRegion.m[0] = 2.f;
            clipRegion.m[12] = right ? -1.f : 1.f;
        }
        glClear(GL_DEPTH_BUFFER_BIT | (depthPass ? 0 : GL_COLOR_BUFFER_BIT));
        Uniform1i(r.uniform("uDepthPass"), depthPass ? 1 : 0);
        UniformMatrix4fv(r.uniform("uViewProjection"), 1, GL_FALSE, passMatrix.m.data());
        Uniform3f(r.uniform("uEye"), eye.x, eye.y, eye.z);
        // A face of the probe mirrors only the sky and the analytic city: no recursion.
        Uniform1i(r.uniform("uProbe"), Impl::probeUnit);
        Uniform1i(r.uniform("uProbePass"), probePass ? 1 : 0);
        Uniform1i(r.uniform("uProbeReady"), viewPass && r.probe.ready() && r.graphics.quality > 0 ? 1 : 0);
        const Vec3 probeCentre = r.probe.centre();
        Uniform3f(r.uniform("uProbeCentre"), probeCentre.x, probeCentre.y, probeCentre.z);
        Uniform1i(r.uniform("uProbeRoofed"), probeUnderRoof(probeCentre));
        Uniform1i(r.uniform("uCoat"), 0);
        Uniform1i(r.uniform("uPaintMask"), 0);
        Uniform1i(r.uniform("uSkin"), 0);
        r.forgetConstants();
        r.setScalar(Impl::Constant::alpha, 1);
        if (probePass)
            r.probeBatches = r.probeTriangles = 0;
        Frustum frustum(halfMap ? clipRegion * passMatrix : passMatrix);
        auto inPass = [&](Vec3 p, float radius)
        {
            return frustum.sphere(p, radius) &&
                   (!depthPass || farPass || probeFace >= 0 || shadowReceivers.sphere(p, radius));
        };
        auto visible = [&](Vec3 p, float radius, float objectRange)
        {
            auto delta = p - eye;
            const float limit = std::min(passRange, objectRange) + radius;
            return (depthPass || dot(delta, delta) < limit * limit) && inPass(p, radius);
        };
        r.visibleRails.clear();
        if (depthPass)
            r.drawStaticShadows(inPass, passMatrix, farPass);
        auto drawScenery = [&](const auto &b)
        {
            if (b.slot == ThinRail)
            {
                if (viewPass &&
                    visible(b.centre, b.radius, b.detail ? building_detail_end * detailScale : range))
                    r.visibleRails.push_back(&b);
                return;
            }
            // Facade details are too small for the sun map and for a face of the probe.
            if ((viewPass || (!b.detail && (probePass || b.surface != 4))) &&
                visible(b.centre, b.radius, b.detail ? building_detail_end * detailScale : range))
            {
                if (depthPass)
                    ++r.shadowBatches;
                else if (viewPass)
                {
                    ++r.submittedBatches;
                    r.submittedTriangles += size_t(b.mesh.count) / 3;
                    if (b.detail)
                        ++r.submittedDetails;
                }
                else
                {
                    ++r.probeBatches;
                    r.probeTriangles += size_t(b.mesh.count) / 3;
                }
                r.draw(b, Mat4::identity());
            }
        };
        if (viewPass) frame_trace.enter(FrameTrace::buildings);
        // The standing city is drawn surface by surface, not sector by sector: all of
        // its road, then all of its pavement. The batches of one surface share their
        // textures and constants, so only the first of a run has to set them.
        for (int slot = 0; !depthPass && slot < SurfaceCount; ++slot)
        {
            for (const auto &sector : r.sectors)
                if (const int at = sector.at[size_t(slot)]; at >= 0)
                    drawScenery(sector.batches[size_t(at)]);
            if (slot == Water && r.openSea)
                drawScenery(*r.openSea);
        }
        if (viewPass)
            for (const auto &[id, batches] : r.detailMeshes)
                for (const auto &b : batches)
                    drawScenery(b);
        if (viewPass) frame_trace.enter(FrameTrace::fixtures);
        for (auto &instances : r.fixtureInstances)
            instances.clear();
        for (auto id : r.visibleFixtures)
        {
            if (probePass || farPass)
                break;
            const auto &f = world.fixtures[id];
            const auto &body = world.staticBodies[f.body];
            Vec3 at{float(body.p.x), float(body.height), float(body.p.z)};
            const auto delta = at - view.eye;
            const float limit = std::min(range, f.kind >= World::FixtureKind::bag ? 38.f : 145.f);
            if (dot(delta, delta) > limit * limit || !inPass(at, 1.6f))
                continue;
            float yaw = -float(body.angle), roll = float(f.tilt);
            if (f.kind == World::FixtureKind::drinkMachine)
            {
                const auto &lods = r.foodModels[1].lods;
                size_t lod = dot(delta, delta) > 65 * 65 ? lods.size() - 1 : 0;
                // Imported cabinets stand on a base pivot; roll the wreck around its body centre.
                auto transform = Mat4::translate(at) * Mat4::yaw(yaw) * Mat4::roll(roll) *
                                 Mat4::translate({0, -float(f.standingHeight) * .5f, 0});
                for (const auto &part : lods[lod])
                {
                    r.draw(part, transform);
                    if (depthPass)
                        ++r.shadowBatches;
                    else
                    {
                        ++r.submittedBatches;
                        r.submittedTriangles += size_t(part.mesh.count) / 3;
                    }
                }
                continue;
            }
            r.fixtureInstances[std::to_underlying(f.kind)].push_back(
                {at.x, at.y, at.z, 1, std::sin(yaw), std::cos(yaw), std::sin(roll), std::cos(roll)});
        }
        struct Finish
        {
            Vec3 body, trim;
            float metal;
        };
        static constexpr Finish finishes[] = {
            {{.27f, .39f, .30f}, {.18f, .20f, .18f}, .36f}, {{.84f, .17f, .085f}, {.18f, .20f, .18f}, .36f},
            {{.92f, .89f, .78f}, {.18f, .20f, .18f}, .36f}, {{.48f, .30f, .13f}, {.15f, .17f, .16f}, .03f},
            {{.26f, .44f, .43f}, {.18f, .21f, .20f}, .32f}, {{.62f, .24f, .17f}, {.55f, .61f, .56f}, .24f},
            {{.15f, .29f, .47f}, {.19f, .21f, .23f}, .30f}, {{.76f, .76f, .58f}, {.73f, .74f, .62f}, 0},
            {{.13f, .35f, .19f}, {.47f, .42f, .24f}, .10f}, {{.77f, .73f, .56f}, {.64f, .17f, .11f}, 0},
            {{.47f, .20f, .10f}, {.65f, .68f, .66f}, .62f}, {{.09f, .11f, .10f}, {.16f, .17f, .14f}, .06f}};
        static_assert(std::size(finishes) == Impl::fixtureKinds);
        for (size_t kind = 0; kind < r.fixtureInstances.size(); ++kind)
        {
            const auto &instances = r.fixtureInstances[kind];
            if (instances.empty())
                continue;
            for (int part = 0; part < 2; ++part)
            {
                auto &mesh = r.fixtureModels[kind][part];
                r.setModel(Mat4::identity());
                r.material(!part && kind == size_t(World::FixtureKind::dumpster) ? r.dumpsterTexture
                                                                                 : r.white,
                           0, part ? finishes[kind].trim : finishes[kind].body);
                r.setFlag(Impl::Constant::instanced, 2);
                r.setScalar(Impl::Constant::metallic, finishes[kind].metal);
                mesh.drawInstances(instances);
                if (depthPass)
                    ++r.shadowBatches;
                else
                {
                    ++r.submittedBatches;
                    r.submittedTriangles += size_t(mesh.count) / 3 * instances.size();
                }
            }
        }
        if (viewPass) frame_trace.enter(FrameTrace::trees);
        if (viewPass && r.graphics.quality > 0)
            r.drawGrass(world, eye);
        for (auto &instances : r.treeInstances)
            instances.clear();
        for (const auto &t : world.trees)
        {
            auto centre = t.foot + Vec3{0, t.height * .55f, 0};
            // Conservative sphere covers both LODs, the crown and the wind deformation.
            float radius = t.height * .8f, distance = (centre - eye).length();
            if (distance - radius > std::min(float(tree_draw_distance), passRange) ||
                !inPass(centre, radius))
                continue;
            float angle = float(t.seed % 1000) * .01f;
            std::array<float, 8> instance{
                t.foot.x,        t.foot.y,        t.foot.z, t.height / 8,
                std::sin(angle), std::cos(angle), angle,    float(t.seed % 97) / 97};
            int base = (std::to_underlying(t.kind) * 3 + int(t.seed % 3)) * 2;
            if (!farPass && distance - radius < tree_lod_end)
                r.treeInstances[base].push_back(instance);
            if (farPass || distance + radius > tree_lod_start)
                r.treeInstances[base + 1].push_back(instance);
        }
        auto drawTrees = [&](bool leafPass)
        {
            for (size_t k = 0; k < r.treeModels.size(); ++k)
            {
                auto &instances = r.treeInstances[k];
                if (instances.empty())
                    continue;
                for (int part = 0; part < 2; ++part)
                {
                    if (viewPass && leafPass != (part == 1))
                        continue;
                    r.setModel(Mat4::identity());
                    r.material(part ? r.leafCoverage : r.white, 0, {1, 1, 1});
                    r.setFlag(Impl::Constant::instanced, 1);
                    r.setFlag(Impl::Constant::surface, 8 + part);
                    r.setFlag(Impl::Constant::treeLod, farPass ? -1 : int(k % 2));
                    auto &mesh = r.treeModels[k][part];
                    mesh.drawInstances(instances);
                    if (depthPass)
                        ++r.shadowBatches;
                    else if (viewPass)
                    {
                        ++r.submittedBatches;
                        r.submittedTriangles += size_t(mesh.count) / 3 * instances.size();
                    }
                    else
                    {
                        ++r.probeBatches;
                        r.probeTriangles += size_t(mesh.count) / 3 * instances.size();
                    }
                }
            }
        };
        drawTrees(false);
        if (farPass)
        {
            if (halfMap) glDisable(GL_SCISSOR_TEST);
            continue;
        }
        if (viewPass) frame_trace.enter(FrameTrace::actors);
        if (probePass)
        {
            // The probe holds the city that stands still and the sky, as in the managed
            // game: nothing that moves, and nothing small.
            r.drawSky(air, sunRay, ReflectionProbe::direction(probeFace), ReflectionProbe::up(probeFace), 1,
                      1, float(world.time), true, probeEye);
            if (r.probe.finish(world.time))
            {
                r.probeFront = 1 - r.probeFront;
                ActiveTexture(TEXTURE0 + Impl::probeUnit);
                glBindTexture(TEXTURE_CUBE_MAP, r.probeCubes[r.probeFront]);
                GenerateMipmap(TEXTURE_CUBE_MAP);
                ActiveTexture(TEXTURE0);
            }
            continue;
        }
        for (const auto &site : world.sites)
        {
            if (!site.food())
                continue;
            Vec3 p{float(site.centre.x), float(world.terrain.height(site.centre)), float(site.centre.z)};
            if (!visible(p, 4, 200))
                continue;
            const auto &model = r.foodModels[site.kind == ServiceKind::kiosk ? 0 : 1];
            const auto lod = std::min(model.lods.size() - 1, (p - view.eye).length() > 65 ? 1uz : 0uz);
            const auto transform = Mat4::translate(p) * Mat4::yaw(float(pi));
            for (const auto &batch : model.lods[lod])
                r.draw(batch, transform);
        }
        for (const Vec2 sign : world.busStops)
        {
            // The shelter is modelled across x; it stands along the street with its back to
            // the houses.
            const Vec2 centre = World::shelter(sign);
            Vec3 p{float(centre.x), float(world.terrain.height(centre)), float(centre.z)};
            if (!visible(p, 4, 220))
                continue;
            const float distance = (p - view.eye).length();
            const auto &lods = r.busShelter.lods;
            const auto lod = std::min(lods.size() - 1, distance > 150 ? 2uz : distance > 65 ? 1uz : 0uz);
            const auto transform = Mat4::translate(p) * Mat4::yaw(float(pi * .5));
            for (const auto &batch : lods[lod])
                r.draw(batch, transform);
        }
        if (viewPass)
        {
            frame_trace.enter(FrameTrace::sky);
            // Before anything translucent: glass does not write depth.
            r.drawSky(air, sunRay, view.target - view.eye, {0, 1, 0}, float(std::tan(pi / 6)), float(w) / h,
                      float(world.time), false, view.eye);
            frame_trace.enter(FrameTrace::actors);
        }
        auto vehicleTransform = [](const Car &c)
        {
            Vec3 base{float(c.body.p.x), float(c.body.height - .8), float(c.body.p.z)};
            if (c.roll != 0 || c.pitch != 0)
            {
                // Model origin is at tire level. Keep the lowest rotated hull corner on the road.
                const double cp = std::cos(c.pitch);
                base.y += float(std::abs(cp * std::sin(c.roll)) * c.body.halfW +
                                std::abs(std::sin(c.pitch)) * c.body.halfH +
                                std::max(0., -cp * std::cos(c.roll)) * c.body.verticalExtent);
            }
            return Mat4::translate(base) * Mat4::yaw(float(c.heading())) *
                   Mat4::pitch(float(-c.terrainPitch + c.pitch)) * Mat4::roll(float(-c.terrainRoll + c.roll));
        };
        // The crowd and the traffic past the reach of their full models: one instanced draw per
        // clothing colour and per vehicle model and paint. Only the camera view needs them.
        auto drawDistantLife = [&]
        {
            constexpr float personNear = 130, vehicleNear = 400;
            const float crowdFar = std::min(passRange, 900.f), trafficFar = std::min(passRange, 1600.f);
            for (auto &group : r.crowdInstances)
                group.clear();
            for (const auto &p : world.people)
            {
                if (p.riding >= 0 || p.dead)
                    continue;
                const Vec3 feet{float(p.body.p.x), float(p.body.height - .85), float(p.body.p.z)};
                const float distance = (feet - eye).length();
                if (distance <= personNear || !visible(feet + Vec3{0, .9f, 0}, 1.5f, crowdFar))
                    continue;
                // Grown a little with distance so that a passer-by stays a few pixels tall.
                const float scale = 1.f + std::clamp((distance - 250.f) / 650.f, 0.f, 1.f) * .6f;
                const float angle = float(p.heading);
                const int colour = p.officer() ? 6
                                   : p.uniform == Person::Uniform::medical
                                       ? 7
                                       : int(p.body.id * 2654435761u % 6);
                r.crowdInstances[size_t(colour)].push_back(
                    {feet.x, feet.y, feet.z, scale, std::sin(angle), std::cos(angle), 0, 0});
            }
            static constexpr Vec3 clothes[Impl::crowdColours] = {
                {.18f, .20f, .26f}, {.42f, .17f, .13f}, {.24f, .30f, .20f}, {.55f, .50f, .40f},
                {.12f, .12f, .13f}, {.33f, .36f, .48f}, {.20f, .26f, .38f}, {.86f, .86f, .82f}};
            r.setModel(Mat4::identity());
            for (int colour = 0; colour < Impl::crowdColours; ++colour)
            {
                const auto &group = r.crowdInstances[size_t(colour)];
                if (group.empty())
                    continue;
                // A material resets the instancing flag, so it is set after it.
                r.material(r.white, 0, clothes[colour]);
                r.setFlag(Impl::Constant::instanced, 1);
                r.farPerson.drawInstances(group);
                ++r.submittedBatches;
                r.submittedTriangles += size_t(r.farPerson.count) / 3 * group.size();
            }
            constexpr int paints = 16;
            r.trafficInstances.resize(r.cars.size() * paints);
            for (auto &group : r.trafficInstances)
                group.clear();
            for (const auto &c : world.cars)
            {
                if (c.falling || int(c.id) == world.controlledCar)
                    continue;
                const Vec3 base{float(c.body.p.x), float(c.body.height - .8), float(c.body.p.z)};
                const float distance = (base - eye).length();
                if (distance <= vehicleNear || !visible(base, float(c.body.halfH + 2), trafficFar))
                    continue;
                const float angle = float(c.heading() + (fleet[c.model].reversed ? pi : 0.));
                r.trafficInstances[size_t(c.model) * paints + size_t(std::clamp(c.paint, 0, paints - 1))].push_back(
                    {base.x, base.y, base.z, 1, std::sin(angle), std::cos(angle), 0, 0});
            }
            for (size_t k = 0; k < r.trafficInstances.size(); ++k)
            {
                const auto &group = r.trafficInstances[k];
                if (group.empty())
                    continue;
                auto &model = r.cars[k / paints];
                Uniform1i(r.uniform("uPaintIndex"), int(k % paints));
                Uniform1i(r.uniform("uCoat"), 1);
                for (auto &b : model.lods.back())
                {
                    if (b.alpha < .99f)
                        continue;
                    r.apply(b.color, b.normal, b.orm, b.tint, b.emissive, b.surface, b.alpha, b.roughness,
                            b.metallic, b.detail);
                    Uniform1i(r.uniform("uPaintMask"), b.paintMask);
                    r.setFlag(Impl::Constant::instanced, 1);
                    b.mesh.drawInstances(group);
                    ++r.submittedBatches;
                    r.submittedTriangles += size_t(b.mesh.count) / 3 * group.size();
                }
            }
            Uniform1i(r.uniform("uPaintIndex"), 0);
            Uniform1i(r.uniform("uPaintMask"), 0);
            Uniform1i(r.uniform("uCoat"), 0);
            r.setFlag(Impl::Constant::instanced, 0);
        };
        auto person = [&](const Body &b, const Appearance &look, double heading, double distance, bool dead,
                          bool player, const Car *cabin = nullptr, WeaponId drawn = WeaponId::none,
                          bool ridingScooter = false, bool courier = false)
        {
            Vec3 p{float(b.p.x), float(b.height - .85), float(b.p.z)};
            if (!visible(p, 2, 130))
                return;
            const int k = look.model;
            const bool medic = look.outfit == Appearance::medic, officer = look.officer();
            auto &motion = r.actorMotion[b.id];
            ActorState state;
            state.id = b.id;
            state.heading = heading;
            state.speed = b.v.length();
            state.distance = distance;
            state.dead = dead;
            state.player = player;
            state.scooter = ridingScooter;
            if (ridingScooter)
                p.y += .23f;
            state.aiming = player ? world.aiming : drawn != WeaponId::none;
            auto animation = motion.sample(state, world.time, r.poses[k]);
            if (cabin)
            {
                if (r.poses[k] >= 384)
                    animation = {320, 320, 1};
                const auto seat = rotate(b.p - cabin->body.p, -cabin->body.angle);
                const float floor = float((b.height - .85) - (cabin->body.height - .8));
                // The baked seated pose is 1.47 m tall. Low cars need a more reclined posture.
                const float posture =
                    std::clamp((float(cabin->body.verticalExtent) - floor - .14f) / 1.48f, .65f, 1.f);
                r.setModel(vehicleTransform(*cabin) * Mat4::translate({float(seat.x), floor, float(seat.z)}) *
                           Mat4::yaw(float(heading - cabin->heading())) *
                           Mat4::scale({look.width, posture, look.depth}));
            }
            else
            {
                // Managed build of the body, and the sway of somebody who had a few.
                const float sway = look.outfit == Appearance::drunk && !dead && !ridingScooter
                                       ? float(std::sin(world.time * 1.7 + b.id) * .075)
                                       : 0.f;
                r.setModel(Mat4::translate(p + Vec3{0, dead ? .23f : 0.f, 0}) * Mat4::yaw(motion.heading) *
                           Mat4::pitch(dead ? float(pi * .5) : motion.lean) * Mat4::roll(sway) *
                           Mat4::scale({look.width, look.height, look.depth}));
            }
            r.material(r.peopleTexture[k], 0, {1, 1, 1});
            Uniform3f(r.uniform("uOutfit"), float(look.outfit + 1), float(look.tone), float(look.flags()));
            Uniform3f(r.uniform("uHair"), look.hair.x, look.hair.y, look.hair.z);
            Uniform1i(r.uniform("uSkin"), 1);
            Uniform1f(r.uniform("uPose"), animation.to);
            Uniform1f(r.uniform("uPreviousPose"), animation.from);
            Uniform1f(r.uniform("uPoseBlend"), animation.blend);
            ActiveTexture(TEXTURE0 + 2);
            glBindTexture(GL_TEXTURE_2D, r.bones[k]);
            ActiveTexture(TEXTURE0);
            r.skin[k].draw();
            Uniform3f(r.uniform("uOutfit"), 0, 0, 0);
            if ((officer || medic) && !dead)
            {
                // The cap of the force; a white one for the ambulance crew.
                r.material(r.white, 0, medic ? Vec3{.88f, .90f, .86f} : look.cap());
                r.patrolCap[k].draw();
            }
            Uniform1i(r.uniform("uSkin"), 0);
            // Things carried follow the build of the body.
            const Mat4 build = Mat4::translate(p) * Mat4::yaw(motion.heading) *
                               Mat4::scale({look.width, look.height, look.depth});
            auto carried = [&](Vec3 at, Vec3 size, Vec3 tint)
            {
                r.setModel(build * Mat4::translate(at) * Mat4::scale(size));
                r.material(r.white, 0, tint);
                r.cube.draw();
            };
            if (officer && !dead && !cabin)
                carried({.088f, 1.35f, .115f}, {.034f, .048f, .012f}, {.76f, .65f, .32f});
            if (look.outfit == Appearance::bagElder && !dead && !cabin)
                // Two string bags, swinging a little with every step.
                for (float side : {-1.f, 1.f})
                {
                    const float swing = float(std::sin(distance * 9 + side) * .04);
                    carried({side * .31f, .47f, swing}, {.17f, .27f, .15f},
                            side < 0 ? Vec3{.48f, .43f, .29f} : Vec3{.33f, .38f, .47f});
                    carried({side * .31f, .68f, swing}, {.015f, .17f, .015f}, {.30f, .26f, .16f});
                }
            if (look.outfit == Appearance::homeless && !dead && !cabin)
                carried({0, 1.1f, -.2f}, {.36f, .5f, .2f}, {.20f, .18f, .12f});
            if (medic && !dead && !cabin)
            {
                auto kit = build;
                auto casePart = [&](Vec3 at, Vec3 size, Vec3 tint)
                {
                    r.setModel(kit * Mat4::translate(at) * Mat4::scale(size));
                    r.material(r.white, 0, tint);
                    r.cube.draw();
                };
                casePart({.29f, .67f, -.03f}, {.16f, .25f, .38f}, {.80f, .80f, .74f});
                casePart({.373f, .67f, -.03f}, {.008f, .14f, .04f}, {.58f, .10f, .08f});
                casePart({.373f, .67f, -.03f}, {.008f, .04f, .14f}, {.58f, .10f, .08f});
            }
            if (courier && !cabin)
            {
                auto torso = Mat4::translate(p + Vec3{0, dead ? .23f : 0.f, 0}) * Mat4::yaw(motion.heading) *
                             Mat4::pitch(dead ? float(pi * .5) : motion.lean) *
                             Mat4::scale({look.width, look.height, look.depth});
                auto part = [&](Vec3 at, Vec3 size, Vec3 tint)
                {
                    r.setModel(torso * Mat4::translate(at) * Mat4::scale(size));
                    r.material(r.white, 0, tint);
                    r.cube.draw();
                };
                Vec3 canvas = b.id % 2 ? Vec3{.79f, .43f, .07f} : Vec3{.08f, .46f, .32f};
                part({0, 1.22f, -.28f}, {.45f, .46f, .3f}, canvas);
                part({0, 1.44f, -.28f}, {.46f, .055f, .32f}, {.14f, .16f, .15f});
                part({0, 1.19f, -.435f}, {.38f, .032f, .012f}, {.85f, .82f, .65f});
                for (float x : {-.135f, .135f})
                    part({x, 1.39f, .115f}, {.041f, .33f, .035f}, {.13f, .14f, .13f});
            }
            if (state.aiming && !dead && !cabin &&
                (drawn == WeaponId::pistol || drawn == WeaponId::automatic))
            {
                auto hands = build * Mat4::translate({.14f, 1.32f, .43f});
                auto gunPart = [&](Vec3 offset, Vec3 size, Vec3 colour)
                {
                    r.setModel(hands * Mat4::translate(offset) * Mat4::scale(size));
                    r.material(r.white, 0, colour);
                    r.cube.draw();
                };
                bool rifle = drawn == WeaponId::automatic;
                gunPart({}, {.05f, .07f, rifle ? .36f : .18f}, {.16f, .17f, .17f});
                gunPart({0, -.055f, -.045f}, {.044f, .13f, .05f}, {.12f, .10f, .08f});
                if (rifle)
                {
                    gunPart({0, .01f, .27f}, {.032f, .032f, .23f}, {.12f, .13f, .13f});
                    gunPart({0, -.025f, -.235f}, {.058f, .09f, .18f}, {.29f, .18f, .12f});
                }
            }
        };
        for (auto &p : world.people)
        {
            const int index = int(&p - world.people.data());
            auto look = appearance(p, world.paramedic_call(index) != nullptr);
            // The arresting officer is a militiaman from the moment the patrol stops.
            if (index == world.patrol.officer && !look.officer())
                look = appearance(p.body.id, p.style, Person::Uniform::police);
            person(p.body, look, p.heading, p.distance, p.dead || p.knockedUntil > world.time, false,
                   p.riding >= 0 ? &world.cars[p.riding] : nullptr,
                   p.aimSeconds > 0 ? p.sidearm : WeaponId::none,
                   p.ownedScooter >= 0 && world.scooters[p.ownedScooter].rider >= 0, p.courier);
        }
        if (world.controlledCar < 0)
        {
            auto gun = world.aiming ? world.inventory.active() : nullptr;
            person(world.player, hero_appearance(), -world.player.angle, world.playerDistance,
                   world.playerKnockedUntil > world.time, true, nullptr, gun ? gun->id : WeaponId::none,
                   world.playerScooter >= 0);
        }
        if (world.transitPassenger())
            person(world.player, hero_appearance(), -world.player.angle, 0, false, true,
                   &world.cars[world.controlledCar]);
        // Light road vehicles use the existing seated rig. Only nearby cabins
        // need a driver mesh; full-size opaque and public-service cabins have their own layout.
        if (viewPass) frame_trace.enter(FrameTrace::vehicles);
        for (const auto &car : world.cars)
        {
            bool driving = int(car.id) == world.controlledCar && !world.transitPassenger();
            // Kind 6 is the second militia unit on the same glazed sedan body as kind 3.
            if ((car.kind != 0 && car.kind != 3 && car.kind != 6 && car.kind != 13) || car.destroyed ||
                // The arresting officer rides beside the driver, who stays at the wheel.
                (!driving && (car.driverless || car.personal || car.summoned || car.id == 0)))
                continue;
            Vec3 centre{float(car.body.p.x), float(car.body.height), float(car.body.p.z)};
            if (!visible(centre, 2, 38))
                continue;
            Body seated = driving ? world.player : car.body;
            if (!driving)
                seated.id = 0xa0000000u + car.id;
            seated.p = car.body.p + rotate({-.45 * car.body.halfW, .13 * car.body.halfH}, car.body.angle);
            seated.height = car.body.height + .14;
            // Whoever the crew turns out to be once set down, at the wheel it is in uniform.
            person(seated,
                   driving ? hero_appearance()
                           : appearance(seated.id, car.id % 2,
                                        car.kind == 3 || car.kind == 6 ? Person::Uniform::police
                                        : car.kind == 13 ? Person::Uniform::fsb
                                                         : Person::Uniform::none),
                   car.heading(), 0, false, driving, &car);
        }
        for (auto &c : world.cars)
        {
            Vec3 p{float(c.body.p.x), float(c.body.height - .8), float(c.body.p.z)};
            if (!visible(p, float(c.body.halfH + 2), 400))
                continue;
            const bool reversed = fleet[c.model].reversed;
            auto transform = vehicleTransform(c);
            if (reversed)
                transform = transform * Mat4::yaw(float(pi));
            auto &model = r.cars.at(c.model);
            const auto *crew = c.model == ambulanceModel ? world.ambulanceCall(c.id) : nullptr;
            const bool beacon = crew && crew->stage != World::RescueStage::standby && !c.destroyed;
            float distance = (p - view.eye).length();
            size_t lod = std::min(model.lods.size() - 1, size_t(distance > 150 * detailScale  ? 2
                                                                : distance > 65 * detailScale ? 1
                                                                                              : 0));
            for (int translucent = 0; translucent < (depthPass ? 1 : 2); ++translucent)
            {
                if (translucent)
                {
                    glEnable(GL_BLEND);
                    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                    glDepthMask(GL_FALSE);
                }
                for (auto &b : model.lods[lod])
                {
                    if ((b.alpha < .99f) != bool(translucent))
                        continue;
                    auto partTransform = transform;
                    // Imported manifest joints: 0 body, 1/2 doors, 3+ wheels, 100+ special equipment.
                    if ((b.part == 1 || b.part == 2) && c.doorOpen > 0)
                    {
                        // Turned round, the model's left door is on the vehicle's right.
                        const int side = (b.part == 1) != reversed ? -1 : 1;
                        if (c.doorSide == 0 || c.doorSide == side)
                        {
                            const auto pivot = model.hinges[b.part - 1];
                            partTransform = transform * Mat4::translate(pivot) *
                                            Mat4::yaw(float(-side * 1.05 * c.doorOpen)) *
                                            Mat4::translate(pivot * -1.f);
                        }
                    }
                    if (b.part >= 3 && b.part < 3 + int(model.wheels.size()))
                    {
                        const auto &wheel = model.wheels[b.part - 3];
                        partTransform = transform * Mat4::translate(wheel.pivot) *
                                        Mat4::yaw(wheel.front ? float(c.steering) : 0.f) *
                                        Mat4::pitch(float(c.wheelTravel / std::max(.05f, wheel.radius))) *
                                        Mat4::translate(wheel.pivot * -1.f);
                    }
                    if (c.kind == 11 && b.part == 100)
                    {
                        Vec3 centre{0, 1.8f, .2f};
                        partTransform = transform * Mat4::translate(centre) * Mat4::yaw(float(c.turretYaw)) *
                                        Mat4::translate(centre * -1.f);
                    }
                    if (c.kind == 14 && (b.part == 100 || b.part == 103))
                    {
                        Vec3 pivot{0, 2.2f, .55f};
                        auto pitch = b.part == 103 ? Mat4::pitch(float(-c.turretPitch)) : Mat4::identity();
                        partTransform = transform * Mat4::translate(pivot) * Mat4::yaw(float(c.turretYaw)) *
                                        pitch * Mat4::translate(pivot * -1.f);
                    }
                    if (c.kind == 12 && (b.part == 101 || b.part == 102))
                    {
                        const bool mainRotor = b.part == 101;
                        const Vec3 pivot = mainRotor ? Vec3{0, 3.03f, 0} : Vec3{.25f, 2.5f, -3.9f};
                        auto spin = mainRotor ? Mat4::yaw(float(c.wheelTravel))
                                              : Mat4::pitch(float(-c.wheelTravel * 3));
                        partTransform =
                            transform * Mat4::translate(pivot) * spin * Mat4::translate(pivot * -1.f);
                    }
                    if (!depthPass)
                    {
                        Uniform1i(r.uniform("uPaintIndex"), c.paint);
                        Uniform1i(r.uniform("uPaintMask"), b.paintMask);
                        // The body and the glass mirror the city; a wreck mirrors nothing.
                        Uniform1i(r.uniform("uCoat"), c.destroyed ? -1 : translucent ? 2 : 1);
                    }
                    r.draw(b, partTransform, beacon);
                }
                if (translucent)
                {
                    glDepthMask(GL_TRUE);
                    glDisable(GL_BLEND);
                }
            }
            if (!depthPass)
            {
                Uniform1i(r.uniform("uPaintIndex"), 0);
                Uniform1i(r.uniform("uPaintMask"), 0);
                Uniform1i(r.uniform("uCoat"), 0);
            }
        }
        if (viewPass)
            drawDistantLife();
        if (viewPass) frame_trace.enter(FrameTrace::animals);
        for (const auto &s : world.scooters)
        {
            bool fallen = s.rider == Scooter::free;
            Vec3 at{float(s.body.p.x), float(s.body.height - .15 + (fallen ? .18 : .025)), float(s.body.p.z)};
            if (!visible(at, 1.5f, 110))
                continue;
            r.setModel(Mat4::translate(at) * Mat4::yaw(float(-s.body.angle)) *
                       Mat4::roll(fallen ? 1.35f : 0.f));
            r.material(r.white, 0, {.43f, .47f, .46f});
            r.scooterFrame.draw();
            r.material(r.white, 0, {.065f, .071f, .066f});
            r.scooterRubber.draw();
            r.material(r.white, 0, {.78f, .49f, .12f});
            r.scooterTrim.draw();
        }
        for (size_t i = 0; i < world.animals.size(); ++i)
        {
            const auto &a = world.animals[i];
            // Dead, or stunned by a car: it lies, and in the first second after the blow tumbles.
            const bool stunned = a.knockedUntil > world.time;
            bool dead = a.deadUntil > 0;
            Vec2 pos = a.p + a.velocity * (world.animalClock - .1);
            // A body in the air moves between the steps of the animals' clock as well.
            const double since = world.animalClock - .1;
            const double flight =
                (dead || stunned) && a.rise != 0 ? a.rise * since - .5 * 9.81 * since * since : 0;
            Vec3 at{float(pos.x), float(a.altitude + flight + .065 + (dead ? .09 : 0.)), float(pos.z)};
            if (!visible(at, 1.25f, 105))
                continue;
            const auto &rig = r.animalRigs[a.variant];
            float distance = (at - view.eye).length();
            size_t lod = std::min(distance < 14 && !depthPass ? 0uz
                                  : distance < 40             ? 1uz
                                                              : 2uz,
                                  rig.model.lods.size() - 1);
            int clip = dead || stunned                                ? 0
                       : a.flock >= 0 && world.flocks[a.flock].flying ? 3
                       : a.velocity.length2() > 4                     ? 2
                       : a.velocity.length2() > .01                   ? 1
                                                                      : 0;
            double phase = (clip == 1 || clip == 2 ? a.distance : world.time) / rig.cycle[clip] + i * .137;
            float pose = dead || stunned ? 0.f : float(clip * 64 + std::fmod(phase, 1.) * 64);
            float scale = a.species == Species::bird ? .72f : float(.94 + (i % 5) * .03);
            const float tilt = dead      ? 1.5f
                               : stunned ? float(clamp(a.knockedUntil - world.time, 0., 1.)) * 1.2f
                                         : 0.f;
            Mat4 model = Mat4::translate(at) * Mat4::yaw(float(a.heading)) * Mat4::roll(tilt) *
                         Mat4::scale({scale, scale, scale});
            Uniform1i(r.uniform("uSkin"), 1);
            Uniform1f(r.uniform("uPose"), pose);
            Uniform1f(r.uniform("uPoseBlend"), 1);
            ActiveTexture(TEXTURE0 + 2);
            glBindTexture(GL_TEXTURE_2D, rig.palette);
            ActiveTexture(TEXTURE0);
            for (const auto &b : rig.model.lods[lod])
                r.draw(b, model);
        }
        Uniform1i(r.uniform("uSkin"), 0);
        if (viewPass)
        {
            glEnable(GL_BLEND);
            glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            glDepthMask(GL_FALSE);
            drawTrees(true);
            Uniform2f(r.uniform("uViewport"), float(sceneWidth), float(sceneHeight));
            for (const auto *rail : r.visibleRails)
            {
                r.draw(*rail, Mat4::identity());
                ++r.submittedBatches;
                r.submittedTriangles += size_t(rail->mesh.count) / 3;
            }
            glDepthMask(GL_TRUE);
            glDisable(GL_BLEND);
        }
    } // sun/camera geometry passes
    frame_trace.enter(FrameTrace::effects);
    Frustum effectFrustum(vp);
    auto visible = [&](Vec3 p, float radius, float objectRange)
    {
        const auto delta = p - view.eye;
        const float limit = std::min(range, objectRange) + radius;
        return dot(delta, delta) < limit * limit && effectFrustum.sphere(p, radius);
    };
    for (const auto &site : world.sites)
    {
        if ((site.entrance - Vec2{view.eye.x, view.eye.z}).length2() > 47 * 47)
            continue;
        Vec3 p{float(site.entrance.x), float(world.terrain.height(site.entrance) + .085),
               float(site.entrance.z)};
        if (!visible(p, 2, 45))
            continue;
        Vec3 color = site.kind == ServiceKind::clinic   ? Vec3{.16f, .55f, 1.f}
                     : site.kind == ServiceKind::market ? Vec3{.84f, .42f, .92f}
                     : (world.ownedGarage >= 0 && &site == &world.sites[world.ownedGarage])
                         ? Vec3{.20f, .95f, .85f}
                     : site.kind == ServiceKind::garage ? Vec3{1.f, .65f, .16f}
                                                        : Vec3{.3f, .85f, .48f};
        for (int side : {-1, 1})
        {
            r.solid(p + Vec3{side * .78f, 0, 0}, {.045f, .025f, 1.6f}, color, .45f);
            r.solid(p + Vec3{0, 0, side * .78f}, {1.6f, .025f, .045f}, color, .45f);
        }
    }
    if (world.drone.active)
    {
        const auto &b = world.buildings[world.drone.targets[world.drone.scanned]];
        Vec3 point{float(b.p.x), float(world.terrain.height(b.p) + b.height + 1.25), float(b.p.z)};
        if (visible(point, 2, 210))
        {
            r.material(r.white, 0, {.32f, .92f, .72f}, .65f);
            r.setModel(Mat4::translate(point) * Mat4::yaw(float(world.time)));
            r.marker.draw();
        }
    }
    if (world.job.kind == JobKind::none && world.controlledCar >= 0)
        for (auto hail : world.hailers)
        {
            if (hail.actor < 0 || hail.until <= world.time)
                continue;
            const auto &actor = world.people[hail.actor];
            if (actor.dead || actor.fear > 0 || actor.riding >= 0)
                continue;
            Vec3 at{float(actor.body.p.x), float(actor.body.height + 1.45), float(actor.body.p.z)};
            if (!visible(at, 1, 95))
                continue;
            r.material(r.white, 0, {.36f, .91f, .70f}, .6f);
            r.setModel(Mat4::translate(at) * Mat4::yaw(float(world.time)));
            r.marker.draw();
        }
    if (world.job.kind != JobKind::none && !world.job.escaping())
    {
        auto point = world.jobTarget();
        double height = world.terrain.height(point) + 2.;
        if (world.job.kind == JobKind::elimination || (world.job.passenger() && !world.job.acquired))
            height = world.people[world.job.actor].body.height + 1.3;
        if (world.job.kind == JobKind::vehicle && !world.job.acquired)
            height = world.cars[world.job.actor].body.height + 1.4;
        Vec3 p{float(point.x), float(height + .12 * std::sin(3 * world.time)), float(point.z)};
        if (visible(p, 2, 240))
        {
            r.material(r.white, 0, {1, .61f, .12f}, .65f);
            r.setModel(Mat4::translate(p) * Mat4::yaw(float(std::fmod(world.time, 2 * pi))));
            r.marker.draw();
        }
    }
    for (auto &p : world.pickups)
    {
        Vec3 v{float(p.p.x), float(world.surface(p.p, p.roadLevel) + .7 + .14 * std::sin(world.time * 3)),
               float(p.p.z)};
        if (!visible(v, 1, 60))
            continue;
        if (p.energy)
        {
            r.solid(v, {.14f, .29f, .14f}, {.12f, .68f, .83f}, .45f);
            r.solid(v + Vec3{0, .15f, 0}, {.145f, .025f, .145f}, {.78f, .83f, .85f});
        }
        else if (p.health)
        {
            r.solid(v, {.38f, .25f, .17f}, {.90f, .87f, .77f});
            r.solid(v + Vec3{0, 0, .09f}, {.16f, .05f, .014f}, {.85f, .14f, .12f});
            r.solid(v + Vec3{0, 0, .09f}, {.05f, .16f, .015f}, {.85f, .14f, .12f});
        }
        else if (p.weapon == WeaponId::none)
            r.solid(v, {.29f, .06f, .15f}, {.43f, .75f, .38f}, .4f);
        else
        {
            r.solid(v, {.39f, .085f, .07f}, {.44f, .45f, .44f}, .35f);
            r.solid(v + Vec3{-.12f, -.07f, 0}, {.065f, .15f, .07f}, {.28f, .20f, .13f});
        }
    }
    for (auto &rocket : world.rockets)
    {
        Vec3 p{float(rocket.p.x), float(rocket.height), float(rocket.p.z)};
        if (visible(p, 1, 250))
            r.solid(p, {.12f, .12f, .12f}, {1, .6f, .2f}, 2);
    }
    {
        r.timer.mark(SceneTimer::particles);
        // Puddles mirror the city, and the haze takes distant smoke: the effects share
        // the sky, the haze and the probe of the scene.
        UseProgram(r.effectShader);
        r.effectStreet.upload(r.street);
        const Vec3 probeCentre = r.probe.centre();
        const Frustum camera(vp);
        const bool reflectPuddles =
            post && r.graphics.quality > 1 &&
            std::ranges::any_of(world.puddles,
                                [&](const RoadPuddle &puddle)
                                {
                                    const Vec3 centre = (puddle.corners[0] + puddle.corners[2]) * .5f;
                                    const Vec3 delta = centre - view.eye;
                                    return dot(delta, delta) < 34.f * 34.f && camera.sphere(centre, 2.5f);
                                });
        if (post)
            r.post.beginEffects(9, reflectPuddles);
        Uniform1i(r.reflectPuddlesUniform, reflectPuddles);
        Uniform1i(r.effectUniforms.softDepth, post);
        Uniform2f(r.effectUniforms.clip, nearPlane, farPlane);
        Uniform3fv(r.effectUniforms.lamps, 8, lampPosition.data());
        Uniform3fv(r.effectUniforms.colors, 8, lampColor.data());
        Uniform3fv(r.effectUniforms.axes, 8, lampDirection.data());
        Uniform3f(r.effectUniforms.eye, view.eye.x, view.eye.y, view.eye.z);
        Uniform1f(r.effectUniforms.time, float(world.time));
        Uniform1f(r.effectUniforms.waterEdge, float(world.layout.edge()));
        Uniform3f(r.effectUniforms.sun, sunRay.x, sunRay.y, sunRay.z);
        Uniform3f(r.effectUniforms.fog, fog.x, fog.y, fog.z);
        Uniform3f(r.effectUniforms.zenith, air.zenith.x, air.zenith.y, air.zenith.z);
        Uniform4f(r.effectUniforms.air, air.density, air.heightScale, air.golden, float(world.wetness));
        Uniform1f(r.effectUniforms.day, day);
        Uniform1f(r.effectUniforms.rain, rain);
        Uniform1i(r.effectUniforms.probeReady, r.probe.ready() && r.graphics.quality > 0 ? 1 : 0);
        Uniform3f(r.effectUniforms.probeCentre, probeCentre.x, probeCentre.y, probeCentre.z);
        Uniform1i(r.effectUniforms.probeRoofed, probeUnderRoof(probeCentre));
    }
    r.effects.draw(r.effectShader, world, view, vp, r.graphics.effects);
    r.timer.mark(SceneTimer::post);
    frame_trace.enter(FrameTrace::post);
    r.post.mourning(float(world.screenDrain()), float(world.screenDarkness()));
    if (post)
        // Contact shading follows the quality setting, as in the managed game: none at
        // the lowest, eight samples at the middle one and twelve at the highest.
        r.post.present(w, h, day, rain, float(world.clock.sun_height()), r.graphics.bloom,
                       {projection.m[0], projection.m[5], nearPlane, farPlane,
                        r.graphics.quality > 1   ? 12
                        : r.graphics.quality > 0 ? 8
                                                 : 0});
    r.timer.end();
    UseProgram(0);
}
void Renderer::overlay(const World *world, int w, int h, double fps, bool menu, const GameConsole *console,
                       const std::wstring &message, const AudioLevels &levels, float progress, bool jobsOpen,
                       const FrontEnd *front)
{
    auto &r = *impl_;
    glViewport(0, 0, w, h);
    r.canvas(w, h);
    r.hudDamage.begin();
    auto surface = r.hud.data();
    r.hudDamage.regions(false,
                        [&](int x, int y, int width, int height)
                        {
                            for (int row = y; row < y + height; ++row)
                                std::fill_n(surface + size_t(row) * w + x, width, uint32_t{});
                        });
    if (progress >= 0)
    {
        const int width = std::min(600, w - 32), x = (w - width) / 2;
        r.text(x, h / 2 - 63, L"PROMZONA", rgb(220, 173, 96), true);
        r.rect(x, h / 2, width, 15, rgb(37, 43, 40));
        r.rect(x, h / 2, int(width * progress), 15, rgb(113, 151, 98));
        r.text(x, h / 2 + 26, L"Подготовка города");
    }
    else if (menu && front)
    {
        const auto rows = front->rows(world != nullptr, r.graphics, r.mode, levels);
        MenuLayout layout(w, h, int(rows.size()));
        const int x = layout.x, y = layout.y, width = layout.width, step = layout.step;
        const int bottom = y + int(rows.size()) * step;
        const int headingHeight = std::min(y - 4, 68);
        r.rect(x - 12, y - headingHeight - 3, width + 24, bottom - y + headingHeight + 60, rgb(19, 27, 26));
        const wchar_t *heading = front->page == MenuPage::settings ? L"НАСТРОЙКИ"
                                 : front->page == MenuPage::home   ? L"PROMZONA"
                                                                   : L"ЗАМЕНА СЛОТА";
        r.menuText(x + 10, y - headingHeight, width - 20, headingHeight, heading, rgb(231, 193, 122),
                   width >= 450 && headingHeight >= 60);
        for (int i = 0; i < int(rows.size()); ++i)
        {
            const auto &row = rows[i];
            const int top = y + i * step;
            const bool selected = i == front->focus;
            r.rect(x, top, width, step - 2, selected ? rgb(57, 66, 59) : rgb(27, 36, 34));
            if (selected)
                r.rect(x, top, 3, step - 2, rgb(231, 177, 94));
            const Color ink = row.enabled ? rgb(229, 231, 212) : rgb(140, 146, 137);
            const int column = width * 53 / 100;
            r.menuText(x + 12, top, (row.value.empty() ? width : column) - 20, step - 2, row.label, ink);
            if (!row.value.empty())
                r.menuText(x + column, top, width - column - 10, step - 2, row.value, ink);
        }
        if (world && front->page == MenuPage::home)
            r.text(x, bottom + 2, L"Автосохранение: слот " + std::to_wstring(front->active),
                   rgb(190, 197, 177));
        r.menuText(x + 8, bottom + 22, width - 16, 30, L"↑↓ Выбор     ←→ Изменить     Enter ОК     Esc Назад",
                   rgb(190, 197, 177));
    }
    // While the hero lies dead, and until the clinic has faded in, the HUD stands aside for the
    // caption: nothing to steer and nothing to read over a darkening picture.
    const bool mourning = world && (world->dying() || world->screenDarkness() > .35);
    if (world && !menu && progress < 0 && world->dying())
    {
        const double t = world->deathSince();
        // The caption comes in a moment after the fall and leaves as the picture goes black.
        if (t > .45 && world->screenDarkness() < .85)
        {
            const std::wstring caption = localize(L"УБИТ", r.language);
            const int width = r.huge->width(caption), x = (w - width) / 2, y = h / 2 - r.huge->height() / 2 - h / 10;
            r.hudDamage.touch(x - 6, y - 4, width + 12, r.huge->height() + 10);
            // A dark rim keeps the letters readable over sky and over asphalt alike.
            for (auto [dx, dy] : {std::pair{-3, 0}, {3, 0}, {0, -3}, {0, 3}, {3, 3}})
                r.hud.text(*r.huge, x + dx, y + dy, caption, rgb(26, 4, 3));
            r.hud.text(*r.huge, x, y, caption, rgb(196, 28, 22));
        }
    }
    if (world && !menu && progress < 0 && !mourning)
    {
        // The person and the driven vehicle have separate meters, as in the managed game:
        // pills and food heal the person, a garage repairs the car.
        const Car *driven = world->controlledCar >= 0 && !world->transitPassenger()
                                ? &world->cars[world->controlledCar]
                                : nullptr;
        r.rect(20, 20, 270, driven ? 112 : 70, rgb(12, 19, 18));
        r.text(35, 28, L"$ " + std::to_wstring(world->money), rgb(154, 207, 144));
        r.rect(35, 66, 230, 12, rgb(53, 57, 55));
        r.rect(35, 66, int(230 * clamp(world->health, 0., 1.)), 12,
               world->godMode ? rgb(226, 196, 92) : rgb(117, 182, 103));
        if (driven)
        {
            const double condition = clamp(driven->health, 0., 1.);
            r.text(35, 84,
                   driven->fireTime > 0
                       ? std::wstring(L"МАШИНА ГОРИТ")
                       : L"МАШИНА  " + std::to_wstring(int(std::round(condition * 100))) + L"%",
                   driven->fireTime > 0 ? rgb(255, 112, 58) : rgb(166, 194, 217));
            r.rect(35, 114, 230, 6, rgb(53, 57, 55));
            r.rect(35, 114, int(230 * condition), 6,
                   driven->fireTime > 0 ? rgb(230, 66, 26) : rgb(82, 145, 201));
        }
        // Weapon panel of the managed HUD: badge, name and rounds. A mounted gun never runs dry.
        const auto weapon = world->inventory.active();
        const bool cannon = driven && driven->kind == 11, mountedGun = driven && driven->kind == 14;
        // Beside the status box as in C#; below it when the window is too narrow for both
        // the panel and the frame counter.
        const bool beside = w >= 830;
        const int statusBottom = driven ? 138 : 96;
        const bool armed = world->playerScooter < 0 && !world->drone.active && !world->transitPassenger() &&
                           (cannon || mountedGun || !driven || weapon);
        if (armed)
        {
            constexpr std::array weaponLabels{L"КУЛАКИ", L"ПИСТОЛЕТ", L"АВТОМАТ", L"БАЗУКА", L"ОГНЕМЁТ"};
            const size_t art = cannon ? 5 : mountedGun ? 6 : weapon ? size_t(weapon->id) : 0;
            const int px = beside ? 298 : 20, py = beside ? 20 : statusBottom;
            r.rect(px, py, 270, 63, rgb(12, 19, 18));
            r.rect(px, py, 270, 2, rgb(227, 166, 82));
            r.icon(px + 4, py + 10, 88, 44, r.weaponArt[art]);
            r.text(px + 100, py + 4,
                   cannon       ? L"ТАНКОВАЯ ПУШКА"
                   : mountedGun ? L"БОРТОВОЙ ПУЛЕМЁТ"
                                : weaponLabels[weapon ? art : 0],
                   rgb(245, 227, 189));
            r.draw(px + 100, py + 30,
                   cannon || mountedGun ? L"∞"
                   : weapon             ? std::to_wstring(world->inventory.ammo(weapon->id))
                                        : L"—",
                   rgb(227, 166, 82), false);
        }
        const int energyTop = statusBottom + (armed && !beside ? 69 : 0);
        {
            // Real frame intervals, not simulation time.
            wchar_t rate[48];
            std::swprintf(rate, std::size(rate), L"%d FPS  ·  %.1f ms", int(fps), 1000. / std::max(1., fps));
            r.draw(w - 250, 25, rate, rgb(235, 231, 208), false);
        }
        if (world->wantedLevel() > 0)
        {
            r.text(w - 250, 60, L"В РОЗЫСКЕ", rgb(239, 159, 68));
            for (int i = 0; i < world->wantedLevel(); i++)
                r.rect(w - 250 + i * 30, 90, 24, 10, rgb(228, 121, 44));
            if (world->patrol.hold > 0)
            {
                r.text(w - 250, 108, L"Задержание", rgb(250, 199, 126));
                r.rect(w - 250, 139, 174, 5, rgb(51, 45, 38));
                double limit = world->controlledCar < 0 ? 1.5 : 2.5;
                r.rect(w - 250, 139, int(174 * std::min(1., world->patrol.hold / limit)), 5,
                       rgb(239, 159, 68));
            }
        }
        int mx = 22, my = h - 245;
        constexpr int ms = Minimap::size;
        r.rect(mx - 5, my - 5, ms + 10, ms + 10, rgb(17, 28, 26));
        auto focus = world->drone.active ? world->drone.position : world->position();
        if (world->layout.districts())
        {
            const auto &layout = world->layout;
            int i = int(std::floor((focus.x - layout.coordinate(0)) / layout.spacing));
            int j = int(std::floor((focus.z - layout.coordinate(0)) / layout.spacing));
            const wchar_t *label = L"ПОБЕРЕЖЬЕ";
            if (i >= 0 && j >= 0 && i < layout.nodes() - 1 && j < layout.nodes() - 1)
            {
                constexpr std::array labels{L"МИКРОРАЙОН", L"СТАРЫЕ ДВОРЫ",        L"САДОВЫЙ ПОСЁЛОК",
                                            L"ЦЕНТР",      L"ГАРАЖНЫЙ КООПЕРАТИВ", L"ПРОМЗОНА"};
                label = labels[std::to_underlying(layout.district(i, j))];
            }
            // In a very short window the labels would run into the status box.
            if (my - 64 >= energyTop + 30)
                r.text(mx, my - 64, label, rgb(225, 218, 191));
        }
        // The street has a line of its own: beside the clock it ran under the key hints.
        if (my - 36 >= energyTop + 30)
            r.text(mx, my - 36, street_at(focus, World::nodes, World::spacing), rgb(196, 205, 188));
        r.minimap.copy_view(focus, r.mapPixels);
        r.hud.blit(mx, my, ms, ms, r.mapPixels.data());
        auto map = [&](Vec2 p)
        {
            Vec2 offset = Minimap::offset(p - focus);
            struct Point
            {
                int x, y;
            };
            return Point{mx + ms / 2 + int(std::lround(offset.x)), my + ms / 2 + int(std::lround(offset.z))};
        };
        r.hud.push_clip(mx, my, ms, ms);
        for (auto &c : world->cars)
        {
            auto p = map(c.body.p);
            // A bus, a tram and a trolleybus are the larger marks.
            const int half = c.kind == 5 || c.kind == 9 || c.kind == 10 ? 2 : 1;
            if (p.x > mx && p.x < mx + ms && p.y > my && p.y < my + ms)
                r.rect(p.x - half, p.y - half, 2 * half + 1, 2 * half + 1, rgb(104, 157, 176));
        }
        for (const auto &pickup : world->pickups)
        {
            if (pickup.wait > 0)
                continue;
            auto p = map(pickup.p);
            if (p.x <= mx || p.x >= mx + ms || p.y <= my || p.y >= my + ms)
                continue;
            if (pickup.health)
            {
                r.rect(p.x - 2, p.y, 5, 1, rgb(125, 236, 172));
                r.rect(p.x, p.y - 2, 1, 5, rgb(125, 236, 172));
            }
            else
                r.rect(p.x - 1, p.y - 1, 3, 3, pickup.energy ? rgb(184, 225, 75) : rgb(243, 178, 68));
        }
        if (world->job.kind == JobKind::none && world->controlledCar >= 0)
            for (auto hail : world->hailers)
            {
                if (hail.actor < 0 || hail.until <= world->time)
                    continue;
                const auto &person = world->people[hail.actor];
                if (person.dead || person.fear > 0 || person.riding >= 0)
                    continue;
                auto p = map(person.body.p);
                if (p.x < mx || p.x >= mx + ms || p.y < my || p.y >= my + ms)
                    continue;
                auto ink = rgb(84, 232, 179);
                r.rect(p.x - 1, p.y - 4, 3, 2, ink);
                r.rect(p.x - 3, p.y - 2, 2, 3, ink);
                r.rect(p.x + 2, p.y - 2, 2, 3, ink);
                r.rect(p.x - 1, p.y + 1, 3, 2, ink);
            }
        if (world->ownedGarage >= 0)
        {
            auto door = map(world->sites[world->ownedGarage].entrance);
            auto ink = rgb(87, 237, 215);
            r.rect(door.x - 4, door.y - 3, 9, 2, ink);
            r.rect(door.x - 4, door.y - 1, 2, 5, ink);
            r.rect(door.x + 3, door.y - 1, 2, 5, ink);
        }
        if (world->job.kind != JobKind::none && !world->job.escaping())
        {
            Vec2 d = world->jobTarget() - focus;
            auto p = map(focus + d * (1 / std::max(1., std::max(std::abs(d.x), std::abs(d.z)) / 122.)));
            r.rect(p.x - 4, p.y - 4, 9, 9, rgb(47, 42, 30));
            r.rect(p.x - 2, p.y - 2, 5, 5, rgb(255, 189, 65));
        }
        if (world->drone.active)
        {
            auto roof = map(world->buildings[world->drone.targets[world->drone.scanned]].p);
            auto pilot = map(world->player.p);
            r.rect(roof.x - 3, roof.y - 3, 7, 7, rgb(94, 241, 191));
            r.rect(pilot.x - 2, pilot.y - 2, 5, 5, rgb(255, 255, 255));
        }
        r.hud.pop_clip();
        r.rect(mx + ms / 2 - 3, my + ms / 2 - 3, 7, 7, rgb(255, 189, 76));
        double heading = world->drone.active         ? world->drone.heading
                         : world->controlledCar >= 0 ? world->cars[world->controlledCar].heading()
                                                     : -world->player.angle;
        Vec2 nose = Minimap::offset(forward(heading) * 12.);
        for (int k = 3; k <= 8; ++k)
            r.rect(mx + ms / 2 + int(std::lround(nose.x * k / 8)) - 1,
                   my + ms / 2 + int(std::lround(nose.z * k / 8)) - 1, 3, 3, rgb(255, 200, 90));
        r.rect(mx + 4, my + 4, 18, 23, rgb(20, 28, 25));
        r.text(mx + 6, my + 2, L"N");
        int hour = int(world->clock.hour), minute = int((world->clock.hour - hour) * 60);
        wchar_t time[32];
        std::swprintf(time, std::size(time), L"%02d:%02d", hour, minute);
        r.draw(mx, my + ms + 6, time, rgb(235, 231, 208), false);
        if (world->drone.active)
        {
            const auto &d = world->drone;
            auto delta = world->buildings[d.targets[d.scanned]].p - d.position;
            const auto mint = rgb(119, 237, 189);
            const int left = w / 2 - 155;
            r.rect(left, 22, 310, 81, rgb(17, 28, 26));
            r.text(left + 12, 27, L"РАЗВЕДКА     АКБ " + std::to_wstring(int(std::ceil(d.battery))) + L"%",
                   mint);
            r.text(left + 12, 55,
                   L"Крыша " + std::to_wstring(d.scanned + 1) + L"/3   " +
                       std::to_wstring(int(delta.length())) + L" м",
                   mint);
            r.rect(left + 12, 92, 286, 4, rgb(70, 94, 85));
            r.rect(left + 12, 92, int(286 * clamp(d.scanTime / 2., 0., 1.)), 4, mint);
            r.rect(w / 2 - 7, h / 2, 15, 1, mint);
            r.rect(w / 2, h / 2 - 7, 1, 15, mint);
        }
        if (world->transitPassenger())
        {
            const auto &car = world->cars[world->controlledCar];
            const auto &line = world->lines[car.line];
            const bool stopped = car.dwell > 0;
            const int stop =
                stopped ? (car.next_stop + int(line.stops.size()) - 1) % int(line.stops.size()) : car.next_stop;
            const auto title =
                (car.kind == 9 ? L"Трамвай №" : L"Троллейбус №") + std::to_wstring(car.line + 1);
            const auto label = world->transitExitRequested ? L"Выход на остановке "
                               : stopped                   ? L"Остановка "
                                                           : L"Следующая остановка ";
            const auto status = label + std::to_wstring(stop + 1);
            const int left = w / 2 - 174;
            r.rect(left, 22, 348, 62, rgb(18, 33, 32));
            r.text(left + 12, 26, title, rgb(133, 225, 208));
            r.text(left + 12, 53, status);
        }
        if (r.weaponSight && !console && !jobsOpen && world->openMarket < 0)
        {
            r.rect(w / 2 - 2, h / 2 - 2, 5, 5, rgb(28, 32, 29));
            r.rect(w / 2 - 1, h / 2 - 1, 3, 3, rgb(243, 235, 209));
        }
        const auto hints = jobsOpen
            ? std::vector<ControlHint>{{L"1–8", L"Выбрать заказ"}, {L"J / Esc", L"Закрыть"}}
            : control_hints(*world);
        int hintTop = r.hintBar(hints, w, h);
        if (world->energySeconds > 0)
            r.text(35, energyTop,
                   L"Энергия  " + std::to_wstring(int(std::ceil(world->energySeconds))) + L" с",
                   rgb(93, 218, 226));
        if (world->job.kind != JobKind::none)
        {
            const auto &j = world->job;
            auto title = j.kind == JobKind::courier       ? L"Передать посылку"
                         : j.kind == JobKind::elimination ? L"Устранить цель"
                         : j.passenger() ? (!j.acquired               ? L"Забрать пассажира"
                                            : j.kind == JobKind::taxi ? L"Довезти пассажира"
                                                                      : L"В поликлинику")
                         : j.acquired    ? L"Доставить в гараж"
                                         : L"Забрать автомобиль";
            std::wstring status = title;
            if (j.peace())
                status = std::wstring(j.kind == JobKind::leaflets ? L"Листовки" : L"Громкоговоритель") +
                         L"   " + std::to_wstring(j.route.completed) + L"/" + std::to_wstring(j.stop_count()) +
                         L"   " + std::to_wstring(int(j.stopped)) +
                         (j.kind == JobKind::leaflets ? L"/3 с" : L"/30 с");
            if (j.escaping())
                status = L"Скройтесь от преследования";
            else
                status += L"   " + std::to_wstring(int((world->jobTarget() - focus).length())) + L" м   " +
                          street_at(world->jobTarget(), World::nodes, World::spacing);
            if (j.series)
                status = std::to_wstring(world->seriesStage + 1) + L"/3   " + status;
            std::wstring details = L"$" + std::to_wstring(world->expectedPay());
            int seconds = int(std::ceil(std::max(0., j.deadline - world->time)));
            if (seconds > 0)
                details += L"   " + std::to_wstring(seconds / 60) + L":" + (seconds % 60 < 10 ? L"0" : L"") +
                           std::to_wstring(seconds % 60);
            else
                details += L"   Опоздание";
            if (j.kind == JobKind::vehicle)
                details +=
                    L"   Кузов " + std::to_wstring(int(std::round(world->cars[j.actor].health * 100))) +
                    L"%   нужно " + std::to_wstring(int(std::round(world->deliveryHealth() * 100))) + L"%";
            else if (j.passenger() && j.acquired)
                details += j.ride.scares == 0 ? L"   Пассажир спокоен" : L"   Пассажир испуган";
            r.rect(235, hintTop - 69, 520, 63, rgb(17, 26, 24));
            r.text(247, hintTop - 65, status, rgb(242, 194, 98));
            r.text(247, hintTop - 37, details, rgb(202, 218, 201));
        }
        if (world->time < world->noticeUntil)
            r.text(235, hintTop - (world->job.kind != JobKind::none ? 100 : 35), world->notice,
                   rgb(252, 206, 124));
        if (world->openMarket >= 0)
        {
            int left = std::max(12, w - 430), top = std::max(15, h / 2 - 122);
            r.rect(left, top, 404, 244, rgb(24, 32, 29));
            r.text(left + 20, top + 12, L"РЫНОК", rgb(244, 192, 103));
            std::array<std::wstring, 4> names{
                world->inventory.owns(WeaponId::pistol) ? L"Пистолетные патроны ×30"
                                                        : L"Пистолет и 30 патронов",
                world->inventory.owns(WeaponId::automatic) ? L"Автоматные патроны ×30"
                                                           : L"Автомат и 30 патронов",
                L"Таблетки", L"Энергетик"};
            for (int i = 0; i < 4; ++i)
            {
                int price = world->shop_price(static_cast<ShopItem>(i));
                auto color = price > world->money ? rgb(159, 148, 139) : rgb(232, 229, 211);
                r.text(left + 20, top + 54 + 34 * i, L"[" + std::to_wstring(i + 1) + L"]  " + names[i],
                       color);
                r.text(left + 330, top + 54 + 34 * i, L"$" + std::to_wstring(price), color);
            }
            r.text(left + 20, top + 201, L"[E] Закрыть", rgb(161, 184, 165));
        }
        else if (jobsOpen)
        {
            int left = std::max(12, w / 2 - 254), top = std::max(10, h / 2 - 204);
            r.rect(left, top, 508, 408, rgb(21, 30, 27));
            r.text(left + 20, top + 12, L"ЗАКАЗЫ", rgb(244, 192, 103));
            r.text(left + 20, top + 56, L"[1] Доставить автомобиль");
            r.text(left + 20, top + 90, L"[2] Устранить цель");
            r.text(left + 20, top + 124, L"[3] Отказаться от текущего заказа");
            r.text(left + 20, top + 158, L"[4] Раздать листовки");
            r.text(left + 20, top + 192, L"[5] Рейс с громкоговорителем");
            r.text(left + 20, top + 226, L"[6] Такси");
            r.text(left + 20, top + 260, L"[7] Доставить пациента");
            r.text(left + 20, top + 294,
                   L"[8] Серия контрактов   " + std::to_wstring(world->seriesStage + 1) + L"/3");
            r.text(left + 20, top + 336,
                   L"Выполнено " + std::to_wstring(world->jobsCompleted) + L"   Серий " +
                       std::to_wstring(world->seriesCompleted) + L"   Заработано $" +
                       std::to_wstring(world->jobEarnings),
                   rgb(182, 205, 181));
            r.text(left + 20, top + 371, L"[J] Закрыть", rgb(148, 170, 151));
        }
    }
    if (console)
    {
        int height = std::min(290, std::max(135, h / 2));
        r.rect(0, 0, w, height, rgb(12, 20, 20));
        r.text(16, 8, L"КОНСОЛЬ   ·   help   ·   ↑↓ история   ·   Tab дополнить   ·   PgUp/PgDn журнал",
               rgb(169, 195, 178));
        int y = 44;
        for (const auto &line : console->visible_lines(size_t((height - 89) / 24)))
        {
            r.text(18, y, line, rgb(224, 222, 201));
            y += 24;
        }
        auto prompt = console->input();
        prompt.insert(console->caret(), 1, L'│');
        // The player's own input is shown as typed.
        r.draw(16, height - 36, L"> " + prompt, rgb(245, 196, 104), false);
    }
    else if (!message.empty())
        r.text(25, menu ? h - 30 : 196, message, rgb(224, 172, 91));
    frame_trace.enter(FrameTrace::hudUpload);
    glDisable(GL_DEPTH_TEST);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    UseProgram(r.ui);
    ActiveTexture(TEXTURE0);
    const auto transform = r.uiSample;
    if (menu && progress < 0)
    {
        const float aspect = float(w) / h;
        Uniform3f(transform, std::min(1.f, aspect / r.menuAspect), std::min(1.f, r.menuAspect / aspect), .7f);
        glBindTexture(GL_TEXTURE_2D, r.menuArt);
        r.uiQuad.draw();
    }
    // Without post-processing there is no resolve pass to darken the death and the clinic's
    // fade-in, so the picture is dimmed here before the HUD goes on top of it.
    if (const double dark = world && !menu && r.mode == SceneMode::original ? world->screenDarkness() : 0; dark > 0)
    {
        const float keep = float(1 - dark);
        BlendColor(keep, keep, keep, 1);
        glBlendFunc(GL_ZERO, 0x8001 /* GL_CONSTANT_COLOR */);
        Uniform3f(transform, 1, 1, 1);
        glBindTexture(GL_TEXTURE_2D, r.white);
        r.uiQuad.draw();
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    }
    Uniform3f(transform, 1, 1, 1);
    r.uploadHud();
    r.uiQuad.draw();
    UseProgram(0);
    glDisable(GL_BLEND);
}
void Renderer::screenshot(const std::filesystem::path &file, int w, int h)
{
    std::vector<unsigned char> pixels(size_t(w) * h * 4);
    glReadPixels(0, 0, w, h, 0x80e1 /* GL_BGRA */, GL_UNSIGNED_BYTE, pixels.data());
    // A bottom-up 32-bit BMP, written field by field so no structure packing is involved.
    std::vector<unsigned char> header;
    auto put = [&](std::uint32_t value, int bytes)
    {
        for (int i = 0; i < bytes; ++i)
            header.push_back(static_cast<unsigned char>(value >> (8 * i)));
    };
    constexpr std::uint32_t headers = 14 + 40;
    put(0x4d42, 2);
    put(headers + std::uint32_t(pixels.size()), 4);
    put(0, 4);
    put(headers, 4);
    put(40, 4);
    put(std::uint32_t(w), 4);
    put(std::uint32_t(h), 4);
    put(1, 2);
    put(32, 2);
    put(0, 4); // BI_RGB
    put(std::uint32_t(pixels.size()), 4);
    put(0, 4);
    put(0, 4);
    put(0, 4);
    put(0, 4);
    std::ofstream out(file, std::ios::binary);
    out.write(reinterpret_cast<const char *>(header.data()), std::streamsize(header.size()));
    out.write(reinterpret_cast<const char *>(pixels.data()), std::streamsize(pixels.size()));
    if (!out)
        throw std::runtime_error("Screenshot write failed");
}
} // namespace Promzona
