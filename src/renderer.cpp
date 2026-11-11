#include "promzona/renderer.hpp"
#include "promzona/audio.hpp"
#include "promzona/fleet.hpp"
#include "json.hpp"
#include "effect_batch.hpp"
#include "post_processing.hpp"
#include "promzona/minimap.hpp"
#include "promzona/control_hints.hpp"
#include "promzona/hud_damage.hpp"
#include "promzona/gl.hpp"
#include "promzona/animation.hpp"
#include "promzona/city_mesh.hpp"
#include "promzona/architecture.hpp"
#include "promzona/visibility.hpp"
#include "promzona/sun_shadow.hpp"
#include "promzona/detail_stream.hpp"
#include <chrono>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <map>
#include <sstream>
namespace pz
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
struct Mesh
{
    GLuint vao{}, vbo{}, ibo{}, instanceVbo{};
    GLsizei count{};
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
    void draw() const
    {
        BindVertexArray(vao);
        glDrawElements(GL_TRIANGLES, count, GL_UNSIGNED_INT, nullptr);
    }
    void drawInstances(const std::vector<std::array<float, 8>> &instances)
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
        BufferData(ARRAY_BUFFER, std::ptrdiff_t(instances.size() * sizeof(instances[0])), instances.data(),
                   DYNAMIC_DRAW);
        DrawElementsInstanced(GL_TRIANGLES, count, GL_UNSIGNED_INT, nullptr, GLsizei(instances.size()));
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
void cylinder(MeshData &mesh, Vec3 a, Vec3 b, float radius, int sides)
{
    Vec3 axis = (b-a).unit();
    Vec3 tangent = cross(axis, std::abs(axis.y) > .8f ? Vec3{1,0,0} : Vec3{0,1,0}).unit();
    Vec3 bitangent = cross(axis, tangent);
    auto rim = [&](int i) {
        float angle = float(2*pi*i/sides);
        return (tangent*std::cos(angle) + bitangent*std::sin(angle))*radius;
    };
    for (int i = 0; i < sides; ++i)
    {
        auto r = rim(i), s = rim(i+1);
        quad(mesh, a+r, a+s, b+s, b+r);
        quad(mesh, a+s, a+r, a, a);
        quad(mesh, b+r, b+s, b, b);
    }
}
// Model the cap in metres on the standing pose, then bind it to the same head
// joint as the hair. The two human rigs have different joint-space origins.
MeshData policeCap(const SkinData &skin)
{
    constexpr int head = 7;
    if (skin.joints <= head) throw std::runtime_error("Human rig has no head joint");
    const float *bone = skin.bones.data() + head * 16;
    const Vec3 x{bone[0],bone[1],bone[2]}, y{bone[4],bone[5],bone[6]}, z{bone[8],bone[9],bone[10]};
    const Vec3 origin{bone[12],bone[13],bone[14]};
    MeshData mesh;
    cylinder(mesh,{0,1.68f,.025f},{0,1.747f,.025f},.103f,12);
    box(mesh,{0,1.688f,.121f},{.077f,.009f,.052f});
    for (auto &vertex : mesh.vertices)
    {
        const auto delta = vertex.position - origin;
        vertex.position = {dot(delta,x)/dot(x,x),dot(delta,y)/dot(y,y),dot(delta,z)/dot(z,z)};
        const auto n = vertex.normal;
        vertex.normal = Vec3{dot(n,x),dot(n,y),dot(n,z)}.unit();
        vertex.joints = {float(head),0,0,0};
        vertex.weights = {1,0,0,0};
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
    GLuint scene{}, ui{}, white{}, uiTexture{}, bones[2]{}, rug{}, shadowTexture{}, shadowFbo{};
    Mesh uiQuad, cube, marker, skin[2], patrolCap[2];
    GLuint effectShader{};
    EffectBatch effects;
    SceneMode mode{SceneMode::enhanced};
    GraphicsPreferences graphics;
    GLuint menuArt{};
    float menuAspect{};
    PostProcessing post;
    SceneTimer timer;
    int poses[2]{};
    std::map<std::uint32_t, ActorMotion> actorMotion;
    std::map<std::filesystem::path, GLuint> textures;
    struct Batch
    {
        Mesh mesh;
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
        std::array<Vec3,2> hinges{};
        void readHinges(const nlohmann::json &manifest)
        {
            if (!manifest.contains("hinges")) return;
            for (size_t i = 0; i < hinges.size(); ++i)
            {
                const auto &p = manifest.at("hinges").at(i);
                hinges[i] = {p.at(0), p.at(1), p.at(2)};
            }
        }
    };
    std::vector<Batch> city;
    std::array<GLuint, 10> walls{}, normals{}, orm{};
    std::array<GLuint, 3> signs{};
    GLuint stationSign{};
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
    struct AnimalRig
    {
        Model model;
        GLuint palette{};
        std::array<double,4> cycle{};
    };
    std::array<AnimalRig,5> animalRigs;
    Mesh scooterFrame, scooterRubber, scooterTrim;
    static constexpr size_t fixtureKinds = size_t(World::FixtureKind::count);
    std::array<std::array<Mesh, 2>, fixtureKinds> fixtureModels;
    std::array<std::vector<std::array<float, 8>>, fixtureKinds> fixtureInstances;
    std::vector<size_t> visibleFixtures;
    GLuint dumpsterTexture{};
    GLuint peopleTexture[2]{};
    Minimap minimap;
    HudDamage hudDamage;
    size_t hudPixelsUploaded{};
    std::array<uint32_t, Minimap::size * Minimap::size> mapPixels{};
    int width{}, height{};
    HDC uiDC{};
    HBITMAP bitmap{}, oldBitmap{};
    void *pixels{};
    HFONT font{}, large{};
    std::map<std::string, GLint> locations;
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
        scene = program(readText(root / "Shaders/Native/scene.vert"),
                        readText(root / "Shaders/Native/scene.frag"));
        ui = program(readText(root / "Shaders/Native/ui.vert"), readText(root / "Shaders/Native/ui.frag"));
        effectShader = program(readText(root / "Shaders/Native/effects.vert"), readText(root / "Shaders/Native/effects.frag"));
        effects.initialize(effectShader);
        const auto fullscreen = readText(root / "Shaders/Native/fullscreen.vert");
        post.initialize(program(fullscreen,readText(root / "Shaders/Native/glow.frag")),
                        program(fullscreen,readText(root / "Shaders/Native/resolve.frag")));
        glGenTextures(1, &shadowTexture);
        glBindTexture(GL_TEXTURE_2D, shadowTexture);
        glTexImage2D(GL_TEXTURE_2D, 0, DEPTH_COMPONENT24, SunShadow::resolution, SunShadow::resolution, 0,
                     GL_DEPTH_COMPONENT, GL_UNSIGNED_INT, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
        GenFramebuffers(1, &shadowFbo);
        BindFramebuffer(FRAMEBUFFER, shadowFbo);
        FramebufferTexture2D(FRAMEBUFFER, DEPTH_ATTACHMENT, GL_TEXTURE_2D, shadowTexture, 0);
        glDrawBuffer(GL_NONE);
        glReadBuffer(GL_NONE);
        if (CheckFramebufferStatus(FRAMEBUFFER) != FRAMEBUFFER_COMPLETE)
            throw std::runtime_error("Sun shadow framebuffer incomplete");
        BindFramebuffer(FRAMEBUFFER, 0);
        glGenTextures(1, &white);
        glBindTexture(GL_TEXTURE_2D, white);
        unsigned char rgba[] = {255, 255, 255, 255};
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 1, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, rgba);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        const auto menuPath = root / "Textures/menu-city.png";
        const auto art = loadImage(menuPath);
        menuAspect = float(art.width) / art.height;
        menuArt = uploadTexture(menuPath, art);
        glGenTextures(1, &uiTexture);
        MeshData q;
        quad(q, {-1, -1, 0}, {1, -1, 0}, {1, 1, 0}, {-1, 1, 0});
        uiQuad.upload(q);
        MeshData c;
        box(c, {}, {.5f, .5f, .5f});
        cube.upload(c);
        MeshData arrow;
        const Vec3 rim[] = {{-.43f,.9f,-.43f},{.43f,.9f,-.43f},{.43f,.9f,.43f},{-.43f,.9f,.43f}};
        for (int i = 0; i < 4; ++i)
            quad(arrow, rim[i], rim[(i+1)%4], {}, {});
        quad(arrow, rim[3],rim[2],rim[1],rim[0]);
        marker.upload(arrow);
        uiDC = CreateCompatibleDC(nullptr);
        font = CreateFontW(-21, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
                           CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        large = CreateFontW(-55, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
                            CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        for (int i = 0; i < 2; i++)
        {
            auto name = i ? "woman" : "man";
            auto s = loadSkin(root / (std::string("Models/PeopleSkinned/") + name + ".cityskin"));
            skin[i].upload(s.mesh);
            patrolCap[i].upload(policeCap(s));
            poses[i] = s.poses;
            bones[i] = bonePalette(s);
            peopleTexture[i] = texture(root / (std::string("Textures/People/") + name + "-palette.png"));
        }
        const char *animalNames[] = {"cat-ginger","cat-calico","labrador","puppy","pigeon"};
        for (size_t i=0; i<animalRigs.size(); ++i)
            loadModel(animalRigs[i].model, root / "Models/AnimalsSkinned" / animalNames[i], &animalRigs[i]);
        buildFixtureModels();
        MeshData frame, rubber, trim;
        box(frame, {0,.19f,-.06f},{.13f,.035f,.49f});
        box(rubber,{0,.229f,-.08f},{.115f,.008f,.37f});
        cylinder(frame,{0,.16f,.48f},{0,1.23f,.43f},.027f,8);
        cylinder(frame,{-.25f,1.23f,.43f},{.25f,1.23f,.43f},.024f,8);
        box(trim,{0,.72f,.46f},{.03f,.16f,.027f});
        for (float sign : {-1.f,1.f})
        {
            cylinder(rubber,{-.036f,.13f,sign*.49f},{.036f,.13f,sign*.49f},.12f,16);
            cylinder(frame,{-.042f,.13f,sign*.49f},{.042f,.13f,sign*.49f},.048f,12);
            cylinder(rubber,{sign*.12f,1.23f,.43f},{sign*.255f,1.23f,.43f},.032f,8);
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
    }
    void loadAmbulance(Model &model, const std::filesystem::path &dir)
    {
        auto meta = nlohmann::json::parse(readText(dir / "manifest.json")).at("ambulance");
        model.readHinges(meta);
        for (const auto &w : meta.at("wheels"))
        {
            auto p = w.at("pivot");
            model.wheels.push_back({{p[0],p[1],p[2]}, w.at("radius"), w.at("front")});
        }
        auto &lod = model.lods.emplace_back();
        for (const char *name : {"body","left","right"})
        {
            auto file = dir / (std::string("ambulance-") + name + ".glb");
            auto mesh = loadGlb(file);
            auto image = uploadTexture(file,decodeImage(mesh.embeddedColor));
            // The articulated exporter stores wheel membership in the palette V coordinate.
            std::vector<MeshData> groups(model.wheels.size()+1);
            for (auto index : mesh.indices)
            {
                auto v = mesh.vertices[index];
                int wheel = int(std::floor(v.uv[1])) - 2;
                size_t group = wheel >= 0 && wheel < int(model.wheels.size()) ? size_t(wheel+1) : 0;
                v.uv[1] = .5f;
                auto &g = groups[group];
                g.indices.push_back(std::uint32_t(g.vertices.size()));
                g.vertices.push_back(v);
            }
            for (size_t i=0;i<groups.size();++i)
            {
                if (groups[i].indices.empty()) continue;
                auto &b=lod.emplace_back();
                b.part = i ? int(i+2) : name == std::string_view("left") ? 1 : name == std::string_view("right") ? 2 : 0;
                b.color = image;
                b.surface = 10; // Articulated vehicle palette; alpha holds the material ID.
                b.mesh.upload(groups[i]);
            }
        }
    }
    void loadModel(Model &model, const std::filesystem::path &path, AnimalRig *rig = nullptr)
    {
        auto j = nlohmann::json::parse(readText(path / "manifest.json"));
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
                    auto skinData = loadSkin(file);
                    if (skinData.poses < 256) throw std::runtime_error("Incomplete wildlife animation");
                    if (!rig->palette) rig->palette = bonePalette(skinData);
                    data = std::move(skinData.mesh);
                }
                else data = loadGlb(file);
                b.mesh.upload(data);
                b.part = part.value("part", 0);
                int mat = part.at("material");
                b.color = data.embeddedColor.empty()
                              ? white
                              : uploadTexture(file, decodeImage(data.embeddedColor));
                if (mat >= 0)
                {
                    auto &m = j.at("materials").at(mat);
                    auto name = m.value("name", std::string{});
                    if (name == "paint" || name == "Car_Paint" || name == "CarBody" ||
                        name == "Ukrainer79Custom_bodymat" || name == "olive" || name == "Cabin") b.paintMask = 1;
                    else if (!m.value("glass",false) && b.part < 3 &&
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
        if (model.lods.empty()) throw std::runtime_error("Empty imported model: " + path.string());
    }
    ~Impl()
    {
        if (shadowFbo)
            DeleteFramebuffers(1, &shadowFbo);
        if (shadowTexture)
            glDeleteTextures(1, &shadowTexture);
        city.clear();
        for (auto &v : cars)
            v.lods.clear();
        for (auto &[path, t] : textures)
            glDeleteTextures(1, &t);
        glDeleteTextures(1, &white);
        glDeleteTextures(1, &uiTexture);
        glDeleteTextures(2, bones);
        for (auto &rig : animalRigs) glDeleteTextures(1,&rig.palette);
        if (scene)
            DeleteProgram(scene);
        if (ui)
            DeleteProgram(ui);
        if (effectShader)
            DeleteProgram(effectShader);
        if (bitmap)
        {
            SelectObject(uiDC, oldBitmap);
            DeleteObject(bitmap);
        }
        if (uiDC)
            DeleteDC(uiDC);
        if (font)
            DeleteObject(font);
        if (large)
            DeleteObject(large);
    }
    void buildFixtureModels()
    {
        std::array<MeshData, fixtureKinds> enamel, iron;
        cylinder(enamel[0], {0,-.33f,0}, {0,.32f,0}, .255f, 12);
        cylinder(enamel[0], {0,.30f,0}, {0,.36f,0}, .28f, 12);
        cylinder(iron[0], {0,.36f,0}, {0,.363f,0}, .244f, 12);
        for (float side : {-.28f,.28f})
            box(iron[0], {side,-.10f,0}, {.021f,.325f,.035f});
        cylinder(iron[1], {0,-.41f,0}, {0,-.34f,0}, .22f, 12);
        cylinder(enamel[1], {0,-.35f,0}, {0,.24f,0}, .15f, 12);
        cylinder(enamel[1], {0,.21f,0}, {0,.29f,0}, .18f, 12);
        cylinder(enamel[1], {0,.27f,0}, {0,.36f,0}, .115f, 10);
        cylinder(iron[1], {0,.36f,0}, {0,.41f,0}, .045f, 6);
        cylinder(enamel[1], {-.23f,.025f,0}, {.23f,.025f,0}, .091f, 10);
        for (float side : {-1.f,1.f})
            cylinder(iron[1], {side*.21f,.025f,0}, {side*.25f,.025f,0}, .104f, 8);
        box(enamel[2], {0,.02f,0}, {.72f,.47f,.53f});
        for (float side : {-1.f,1.f})
        {
            box(iron[2], {side*.37f,.53f,0}, {.365f,.045f,.55f});
            box(iron[2], {side*.73f,.28f,0}, {.02f,.033f,.25f});
            for (float z : {-.38f,.38f})
                cylinder(iron[2], {side*.58f,-.48f,z}, {side*.66f,-.48f,z}, .095f, 8);
        }
        using K = World::FixtureKind;
        auto parts = [&](K kind) -> std::pair<MeshData&,MeshData&> {
            return {enamel[size_t(kind)],iron[size_t(kind)]};
        };
        auto [slats, legs] = parts(K::bench);
        for (int n=0; n<4; ++n)
            box(slats,{0,-.02f,-.18f+n*.12f},{1,.035f,.050f});
        for (int n=0; n<3; ++n)
            box(slats,{0,.17f+n*.14f,-.24f},{1,.05f,.03f});
        for (float x : {-.78f,.78f})
        {
            cylinder(legs,{x,-.50f,-.18f},{x,.47f,-.245f},.034f,8);
            cylinder(legs,{x,-.50f,.19f},{x,-.065f,.15f},.034f,8);
            box(legs,{x,-.075f,0},{.036f,.032f,.24f});
        }
        auto [frame, phone] = parts(K::phoneBooth);
        box(frame,{0,1.045f,0},{.65f,.105f,.65f});
        for (float x : {-.56f,.56f})
        {
            for (float z : {-.56f,.56f})
                box(frame,{x,-.10f,z},{.035f,1.05f,.035f});
            box(frame,{x,-.49f,0},{.025f,.025f,.54f});
            box(frame,{x,-.79f,0},{.02f,.27f,.54f});
        }
        box(frame,{0,-.08f,-.56f},{.55f,1.05f,.025f});
        box(phone,{0,.15f,-.43f},{.23f,.32f,.12f});
        box(frame,{.065f,.23f,-.30f},{.075f,.10f,.012f});
        cylinder(frame,{.055f,.14f,-.301f},{.055f,.14f,-.282f},.08f,12);
        for (int hole=0; hole<10; ++hole)
        {
            float a=float(hole*2*pi/10);
            float x=.055f+.055f*std::cos(a), y=.14f+.055f*std::sin(a);
            cylinder(phone,{x,y,-.282f},{x,y,-.278f},.009f,6);
        }
        box(frame,{.055f,.36f,-.300f},{.071f,.014f,.012f});
        cylinder(phone,{-.14f,.10f,-.26f},{-.14f,.42f,-.26f},.042f,8);
        cylinder(phone,{-.14f,.12f,-.26f},{-.12f,-.24f,-.32f},.011f,6);
        for (float y : {.10f,.41f})
            cylinder(phone,{-.14f,y,-.29f},{-.14f,y,-.22f},.062f,8);

        auto [mail,pedestal] = parts(K::mailbox);
        cylinder(pedestal,{0,-.73f,0},{0,.04f,0},.064f,10);
        box(mail,{0,.35f,0},{.32f,.38f,.21f});
        box(pedestal,{0,.54f,.214f},{.23f,.022f,.009f});
        box(mail,{0,.57f,.229f},{.25f,.014f,.025f});
        box(pedestal,{0,.23f,.213f},{.108f,.037f,.009f});

        auto [plastic, handles] = parts(K::bag);
        for (int n=0; n<6; ++n)
        {
            float x=-.155f+n*.052f, next=x+.052f;
            float fold=.03f+.017f*std::sin(n*2.1f), nextFold=.03f+.017f*std::sin((n+1)*2.1f);
            for (float side : {-1.f,1.f})
                quad(plastic,{x*.8f,-.17f,side*.03f},{next*.8f,-.17f,side*.03f},
                     {next,.065f,side*nextFold},{x,.065f,side*fold});
        }
        for (float x : {-.076f,.076f})
            for (int n=0; n<8; ++n)
            {
                auto loop=[&](int i){float a=float(pi)*i/8;
                    return Vec3{x+.045f*std::cos(a),.065f+.105f*std::sin(a),.024f};};
                cylinder(handles,loop(n),loop(n+1),.007f,5);
            }

        auto [glass,cap] = parts(K::bottle);
        cylinder(glass,{-.12f,0,0},{.045f,0,0},.045f,10);
        cylinder(glass,{.044f,0,0},{.065f,0,0},.035f,10);
        cylinder(glass,{.064f,0,0},{.12f,0,0},.019f,8);
        cylinder(cap,{.116f,0,0},{.13f,0,0},.023f,8);
        auto [paper,band] = parts(K::packet);
        box(paper,{0,-.002f,0},{.055f,.035f,.085f});
        box(band,{0,.034f,.04f},{.053f,.003f,.036f});
        auto [label,alu] = parts(K::can);
        cylinder(alu,{-.055f,0,0},{.055f,0,0},.034f,10);
        cylinder(label,{-.041f,0,0},{.041f,0,0},.035f,10);
        auto [sack,knot] = parts(K::garbageBag);
        auto ring=[](int n,float y,float r) {
            float a=float(2*pi)*n/12, pleat=1.f+.065f*std::cos(5*a);
            return Vec3{r*pleat*std::cos(a),y,r*.84f*pleat*std::sin(a)};
        };
        constexpr float ys[]{-.28f,-.21f,-.045f,.11f,.19f,.24f};
        constexpr float rs[]{.15f,.25f,.265f,.19f,.060f,.035f};
        for (int n=0; n<12; ++n)
            for (int j=1; j<6; ++j)
                quad(sack,ring(n,ys[j-1],rs[j-1]),ring(n,ys[j],rs[j]),
                     ring(n+1,ys[j],rs[j]),ring(n+1,ys[j-1],rs[j-1]));
        cylinder(knot,{0,.20f,0},{.053f,.27f,.02f},.026f,7);

        for (size_t k=0; k<fixtureModels.size(); ++k)
        {
            if (k == size_t(K::drinkMachine)) continue; // Uses the textured service cabinet from Content.
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
        return uploadTexture(file, loadImage(file));
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
        textures.emplace(key, t);
        return t;
    }
    Batch uploadSurface(const MeshData &data, int i, bool detail)
    {
        Batch b;
        b.mesh.upload(data);
        b.detail = detail;
        Vec3 low{1e9f, 1e9f, 1e9f}, high{-1e9f, -1e9f, -1e9f};
        for (const auto &vertex : data.vertices)
        {
            const auto &p = vertex.position;
            low = {std::min(low.x, p.x), std::min(low.y, p.y), std::min(low.z, p.z)};
            high = {std::max(high.x, p.x), std::max(high.y, p.y), std::max(high.z, p.z)};
        }
        b.centre = (low + high) * .5f;
        b.radius = (high - low).length() * .5f;
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
            b.tint = {.32f, .40f, .24f};
            b.surface = 1;
        }
        if (i == Road)
        {
            b.tint = {.26f, .28f, .29f};
            b.surface = 2;
            b.roughness = .86f;
        }
        if (i == Pavement)
        {
            b.tint = {.55f, .55f, .49f};
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
            b.tint = {.38f, .39f, .36f};
        if (i == Foundation)
            b.tint = {.30f, .35f, .31f};
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
        if (i == Canvas) b.tint = {.96f,.53f,.20f};
        if (i == SeaCanvas) b.tint = {.12f,.63f,.66f};
        if (i == Ballast) { b.tint = {.40f,.36f,.30f}; b.surface = 2; }
        if (i == StationSign) b.color = stationSign;
        if (i >= Sign && i < Sign+3)
            b.color = signs[i - Sign];
        return b;
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
            detailCandidates = detailOrder(cells, eye, detailPrefetchDistance);
            detailIdle = false;
            evict(0);
        }
        else if (detailIdle)
            return;
        if (pendingDetail && cells[pendingDetail->id].distance(eye) > detailKeepDistance)
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
            if (workingSet > detailGpuBudget)
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
            size_t bytes = meshBytes(mesh);
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
    void setModel(const Mat4 &model) { UniformMatrix4fv(uniform("uModel"), 1, GL_FALSE, model.m.data()); }
    void material(GLuint color, GLuint normal, Vec3 tint, float emissive = 0)
    {
        ActiveTexture(TEXTURE0);
        glBindTexture(GL_TEXTURE_2D, color ? color : white);
        ActiveTexture(TEXTURE0 + 1);
        glBindTexture(GL_TEXTURE_2D, normal ? normal : white);
        ActiveTexture(TEXTURE0);
        Uniform1i(uniform("uHasNormal"), normal ? 1 : 0);
        Uniform1i(uniform("uHasOrm"), 0);
        Uniform1i(uniform("uDetail"), 0);
        Uniform1i(uniform("uTreeLod"), -1);
        Uniform1i(uniform("uInstanced"), 0);
        Uniform1i(uniform("uSurface"), 0);
        Uniform3f(uniform("uTint"), tint.x, tint.y, tint.z);
        Uniform1f(uniform("uEmissive"), emissive);
        Uniform1f(uniform("uAlpha"), 1);
        Uniform1f(uniform("uRoughness"), .7f);
        Uniform1f(uniform("uMetallic"), 0);
    }
    void draw(const Batch &b, const Mat4 &model, bool beacon = false)
    {
        setModel(model);
        material(b.color, b.normal, b.tint, beacon ? 1.f : b.emissive);
        Uniform1i(uniform("uSurface"), b.surface);
        Uniform1f(uniform("uAlpha"), b.alpha);
        Uniform1f(uniform("uRoughness"), b.roughness);
        Uniform1f(uniform("uMetallic"), b.metallic);
        Uniform1i(uniform("uDetail"), b.detail ? 1 : 0);
        Uniform1i(uniform("uHasOrm"), b.orm ? 1 : 0);
        if (b.orm)
        {
            ActiveTexture(TEXTURE0 + 3);
            glBindTexture(GL_TEXTURE_2D, b.orm);
            ActiveTexture(TEXTURE0);
        }
        b.mesh.draw();
    }
    void solid(Vec3 centre, Vec3 size, Vec3 color, float emissive = 0)
    {
        setModel(Mat4::translate(centre) * Mat4::scale(size));
        material(white, 0, color, emissive);
        cube.draw();
    }
    int hintBar(const std::vector<ControlHint> &hints, int w, int h)
    {
        struct Item { int x, row, cap; };
        std::vector<Item> items;
        items.reserve(hints.size());
        const int left = w >= 650 ? 235 : 8;
        const int right = w - 14;
        auto measure = [&](const std::wstring &s) {
            SIZE size{};
            GetTextExtentPoint32W(uiDC, s.c_str(), int(s.size()), &size);
            return int(size.cx);
        };
        auto old = SelectObject(uiDC, font);
        int row=0, cursor=left+9;
        for(const auto &hint:hints)
        {
            int cap=std::max(26,measure(hint.key)+12);
            int hintWidth=cap+6+measure(hint.action);
            if(cursor>left+9 && cursor+hintWidth>right-8) { ++row; cursor=left+9; }
            items.push_back({cursor,row,cap});
            cursor+=hintWidth+17;
        }
        SelectObject(uiDC,old);
        int top=h-18-(row+1)*33-4;
        rect(left,top,right-left,(row+1)*33+4,RGB(14,23,21));
        rect(left,top,right-left,2,RGB(83,124,94));
        for(size_t i=0;i<hints.size();++i)
        {
            auto [x,line,cap]=items[i];
            int y=top+5+line*33;
            rect(x,y,cap,27,RGB(122,153,128));
            rect(x+1,y+1,cap-2,25,RGB(35,49,42));
            text(x+6,y+1,hints[i].key,RGB(226,238,217));
            text(x+cap+6,y+1,hints[i].action,RGB(205,214,203));
        }
        return top;
    }
    void canvas(int w, int h)
    {
        if (width == w && height == h)
            return;
        if (bitmap)
        {
            SelectObject(uiDC, oldBitmap);
            DeleteObject(bitmap);
        }
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = w;
        info.bmiHeader.biHeight = -h;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        bitmap = CreateDIBSection(uiDC, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
        if (!bitmap)
            throw std::runtime_error("HUD allocation failed");
        oldBitmap = static_cast<HBITMAP>(SelectObject(uiDC, bitmap));
        width = w;
        height = h;
        glBindTexture(GL_TEXTURE_2D, uiTexture);
        hudDamage.resize(w,h);
        std::memset(pixels,0,size_t(w)*h*4);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, w, h, 0, 0x80e1, GL_UNSIGNED_BYTE, pixels);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    }
    void text(int x, int y, const std::wstring &s, COLORREF color = RGB(235, 231, 208), bool big = false)
    {
        auto old = SelectObject(uiDC, big ? large : font);
        SetBkMode(uiDC, TRANSPARENT);
        SetTextColor(uiDC, color);
        SIZE extent{};
        GetTextExtentPoint32W(uiDC,s.c_str(),int(s.size()),&extent);
        // Include glyph overhang at either end of the run.
        hudDamage.touch(x-3,y-1,extent.cx+6,extent.cy+2);
        TextOutW(uiDC, x, y, s.c_str(), int(s.size()));
        SelectObject(uiDC, old);
    }
    void menuText(int x, int y, int boxWidth, int boxHeight, const std::wstring &s, COLORREF color, bool title = false)
    {
        RECT bounds{x,y,x+boxWidth,y+boxHeight};
        auto old = SelectObject(uiDC,title ? large : font);
        SetBkMode(uiDC,TRANSPARENT);
        SetTextColor(uiDC,color);
        DrawTextW(uiDC,s.c_str(),int(s.size()),&bounds,DT_SINGLELINE|DT_VCENTER|DT_END_ELLIPSIS|DT_NOPREFIX);
        hudDamage.touch(x,y,boxWidth,boxHeight);
        SelectObject(uiDC,old);
    }
    void rect(int x, int y, int w, int h, COLORREF color)
    {
        RECT r{x, y, x + w, y + h};
        hudDamage.touch(x,y,w,h);
        SetDCBrushColor(uiDC,color);
        FillRect(uiDC,&r,static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
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
void Renderer::graphics(const GraphicsPreferences &settings) { impl_->graphics = settings; }
void Renderer::profileGpu() { impl_->timer.enable(); }
std::string Renderer::gpuReport() const { return impl_->timer.report(); }
std::string Renderer::graphicsInfo() const
{
    return std::string(reinterpret_cast<const char *>(glGetString(GL_RENDERER))) + " / " +
           reinterpret_cast<const char *>(glGetString(GL_VERSION));
}
std::string Renderer::sceneryStats() const
{
    auto &r = *impl_;
    auto stream = streamingStats();
    return "Scenery batches=" + std::to_string(r.submittedBatches) + "/" + std::to_string(r.city.size()) +
           " near=" + std::to_string(r.submittedDetails) +
           " submitted_triangles=" + std::to_string(r.submittedTriangles) +
           " trees=" + std::to_string(r.treeCount) + " shadow_batches=" + std::to_string(r.shadowBatches) +
           " resident_mesh_MiB=" + std::to_string((r.sceneryBytes + stream.gpuBytes) / (1024 * 1024)) +
           " base_MiB=" + std::to_string(r.sceneryBytes / (1024 * 1024)) +
           " detail_MiB=" + std::to_string(stream.gpuBytes / (1024 * 1024)) +
           " detail_cells=" + std::to_string(stream.residentCells) + "/" + std::to_string(stream.totalCells) +
           " hud_upload_pixels=" + std::to_string(r.hudPixelsUploaded) + "/" + std::to_string(size_t(r.width)*r.height) +
           " effect_quads=" + std::to_string(r.effects.size()) +
           " effect_draws=" + std::to_string(r.effects.size() != 0) +
           " detail_uploads=" + std::to_string(stream.uploads) +
           " evictions=" + std::to_string(stream.evictions) +
           " peak_detail_MiB=" + std::to_string(stream.peakGpuBytes / (1024 * 1024)) +
           " max_detail_upload_ms=" + std::to_string(stream.maxUploadMs);
}
StreamingStats Renderer::streamingStats() const
{
    const auto &r = *impl_;
    if (!r.detailStream)
        return {};
    const auto worker = r.detailStream->stats();
    return {r.detailMeshes.size(),
            r.detailStream->cells().size(),
            r.detailResidency->bytes() + r.pendingBytes,
            r.peakDetailBytes,
            r.detailUploads,
            r.detailEvictions,
            worker.queued,
            worker.ready,
            r.maxDetailUploadMs};
}
void Renderer::prepare(const World &world)
{
    auto &r = *impl_;
    r.effects.clearWeatherCache();
    r.minimap.rebuild(world);
    r.detailStream.reset(); // Join old immutable jobs before starting a new city.
    r.detailAnchor.reset();
    r.detailMeshes.clear();
    r.pendingDetail.reset();
    r.pendingBatches.clear();
    r.pendingBytes = r.detailUploads = r.detailEvictions = r.peakDetailBytes = 0;
    r.maxDetailUploadMs = 0;
    r.city.clear();
    r.sceneryBytes = 0;
    r.actorMotion.clear();
    auto &walls = r.walls;
    auto &normals = r.normals;
    auto &orm = r.orm;
    auto &signs = r.signs;
    auto manifest = nlohmann::json::parse(readText(r.root / "Textures/manifest.json"));
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
    r.stationSign = r.texture(r.root / "Textures/Signs/station.png");
    // Chunk ownership covers the whole island and coast, regardless of grid size.
    double low = -std::ceil(world.landEdge / 144) * 144, high = -low;
    for (double z = low; z < high; z += 144)
        for (double x = low; x < high; x += 144)
        {
            auto data = cityChunk(world, x, z);
            for (int i = 0; i < SurfaceCount; ++i)
                if (!data[i].indices.empty())
                {
                    r.sceneryBytes += meshBytes(data[i]);
                    r.city.push_back(r.uploadSurface(data[i], i, false));
                }
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
                appendTree(data, prototype, lod != 0);
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
    const bool post = r.mode != SceneMode::original;
    const int sceneWidth = r.mode == SceneMode::scaled ? std::max(1,(w*3+2)/4) : w;
    const int sceneHeight = r.mode == SceneMode::scaled ? std::max(1,(h*3+2)/4) : h;
    if (post) r.post.resize(sceneWidth,sceneHeight);
    r.updateDetails(view.eye);
    r.timer.begin();
    float day = float(world.clock.daylight()), rain = float(world.rain);
    Vec3 fog = {.48f * day + .055f, .62f * day + .055f, .73f * day + .07f};
    fog = fog * (1 - rain * .35f);

    glEnable(GL_DEPTH_TEST);
    glDepthFunc(GL_LEQUAL);
    glDisable(GL_BLEND);
    glDisable(GL_CULL_FACE);
    UseProgram(r.scene);
    const float range = float(r.graphics.distance);
    const float detailScale = .5f + .25f * r.graphics.quality;
    const float nearPlane = std::clamp(view.distance * .01f, .12f, 8.f);
    const float farPlane = std::max(650.f, range + view.distance + 100.f);
    auto vp =
        Mat4::perspective(float(pi / 3), float(w) / h, nearPlane, farPlane) * Mat4::lookAt(view.eye, view.target);
    Vec3 sun{float(std::cos((world.clock.hour - 6) * pi / 12)), float(world.clock.sunHeight()), .3f};
    auto shadow = sunShadow(view.target, sun);
    if (!view.shadows || r.graphics.quality == 0)
        shadow.strength = 0;
    UniformMatrix4fv(r.uniform("uShadowMatrix"), 1, GL_FALSE, shadow.matrix.m.data());
    Uniform1f(r.uniform("uShadowStrength"), shadow.strength * (1 - rain * .7f));
    Uniform1i(r.uniform("uShadow"), 5);
    r.submittedBatches = r.submittedTriangles = r.submittedDetails = r.shadowBatches = 0;
    world.fixturesNear({view.eye.x,view.eye.z},145,r.visibleFixtures);
    for (int pass = 0; pass < 2; ++pass)
    {
        bool depthPass = pass == 0;
        if (depthPass && shadow.strength <= 0)
            continue;
        ActiveTexture(TEXTURE0 + 5);
        // Never sample a depth texture while rendering into it.
        glBindTexture(GL_TEXTURE_2D, depthPass ? 0 : r.shadowTexture);
        ActiveTexture(TEXTURE0);
        BindFramebuffer(FRAMEBUFFER, depthPass ? r.shadowFbo : (post ? r.post.sceneTarget() : 0));
        glViewport(0, 0, depthPass ? SunShadow::resolution : sceneWidth,
                   depthPass ? SunShadow::resolution : sceneHeight);
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
        glClear(GL_DEPTH_BUFFER_BIT | (depthPass ? 0 : GL_COLOR_BUFFER_BIT));
        Uniform1i(r.uniform("uDepthPass"), depthPass ? 1 : 0);
        UniformMatrix4fv(r.uniform("uViewProjection"), 1, GL_FALSE,
                         (depthPass ? shadow.matrix : vp).m.data());
        Uniform3f(r.uniform("uEye"), view.eye.x, view.eye.y, view.eye.z);
        Uniform3f(r.uniform("uFog"), fog.x, fog.y, fog.z);
        Uniform3f(r.uniform("uSun"), float(std::cos((world.clock.hour - 6) * pi / 12)),
                  float(world.clock.sunHeight()), .3f);
        Uniform1f(r.uniform("uDay"), day);
        Uniform1f(r.uniform("uTime"), float(world.time));
        Uniform1f(r.uniform("uHour"), float(world.clock.hour));
        Uniform1f(r.uniform("uRain"), rain);
        Uniform1i(r.uniform("uColor"), 0);
        Uniform1i(r.uniform("uNormal"), 1);
        Uniform1i(r.uniform("uBones"), 2);
        Uniform1i(r.uniform("uOrm"), 3);
        Uniform1i(r.uniform("uRug"), 4);
        Uniform1f(r.uniform("uDetailStart"), buildingDetailStart * detailScale);
        Uniform1f(r.uniform("uDetailEnd"), buildingDetailEnd * detailScale);
        ActiveTexture(TEXTURE0 + 4);
        glBindTexture(GL_TEXTURE_2D, r.rug);
        ActiveTexture(TEXTURE0);
        Uniform1i(r.uniform("uSkin"), 0);
        Uniform1f(r.uniform("uAlpha"), 1);
        Frustum frustum(depthPass ? shadow.matrix : vp);
        auto visible = [&](Vec3 p, float radius, float objectRange)
        {
            auto delta = p - view.eye;
            const float limit = std::min(range,objectRange) + radius;
            return (depthPass || dot(delta,delta) < limit*limit) && frustum.sphere(p,radius);
        };
        auto drawScenery = [&](const auto &b)
        {
            if ((!depthPass || (!b.detail && b.surface != 4)) &&
                visible(b.centre, b.radius, b.detail ? buildingDetailEnd * detailScale : range))
            {
                if (depthPass)
                    ++r.shadowBatches;
                else
                {
                    ++r.submittedBatches;
                    r.submittedTriangles += size_t(b.mesh.count) / 3;
                    if (b.detail)
                        ++r.submittedDetails;
                }
                r.draw(b, Mat4::identity());
            }
        };
        for (const auto &b : r.city)
            drawScenery(b);
        if (!depthPass)
            for (const auto &[id, batches] : r.detailMeshes)
                for (const auto &b : batches)
                    drawScenery(b);
        for (auto &instances : r.fixtureInstances) instances.clear();
        for (auto id : r.visibleFixtures)
        {
            const auto &f=world.fixtures[id];
            const auto &body = world.staticBodies[f.body];
            Vec3 at{float(body.p.x),float(body.height),float(body.p.z)};
            const auto delta = at - view.eye;
            const float limit = std::min(range, f.kind >= World::FixtureKind::bag ? 38.f : 145.f);
            if (dot(delta,delta)>limit*limit || !frustum.sphere(at,1.6f)) continue;
            float yaw = -float(body.angle), roll = float(f.tilt);
            if (f.kind == World::FixtureKind::drinkMachine)
            {
                const auto &lods = r.foodModels[1].lods;
                size_t lod = dot(delta,delta) > 65*65 ? lods.size()-1 : 0;
                // Imported cabinets stand on a base pivot; roll the wreck around its body centre.
                auto transform = Mat4::translate(at) * Mat4::yaw(yaw) * Mat4::roll(roll) *
                                 Mat4::translate({0, -float(f.standingHeight)*.5f, 0});
                for (const auto &part : lods[lod])
                {
                    r.draw(part,transform);
                    if (depthPass) ++r.shadowBatches;
                    else { ++r.submittedBatches; r.submittedTriangles += size_t(part.mesh.count)/3; }
                }
                continue;
            }
            r.fixtureInstances[size_t(f.kind)].push_back({at.x,at.y,at.z,1,
                std::sin(yaw),std::cos(yaw),std::sin(roll),std::cos(roll)});
        }
        struct Finish { Vec3 body, trim; float metal; };
        static constexpr Finish finishes[] = {
            {{.27f,.39f,.30f},{.18f,.20f,.18f},.36f}, {{.84f,.17f,.085f},{.18f,.20f,.18f},.36f},
            {{.92f,.89f,.78f},{.18f,.20f,.18f},.36f}, {{.48f,.30f,.13f},{.15f,.17f,.16f},.03f},
            {{.26f,.44f,.43f},{.18f,.21f,.20f},.32f}, {{.62f,.24f,.17f},{.55f,.61f,.56f},.24f},
            {{.15f,.29f,.47f},{.19f,.21f,.23f},.30f}, {{.76f,.76f,.58f},{.73f,.74f,.62f},0},
            {{.13f,.35f,.19f},{.47f,.42f,.24f},.10f}, {{.77f,.73f,.56f},{.64f,.17f,.11f},0},
            {{.47f,.20f,.10f},{.65f,.68f,.66f},.62f}, {{.09f,.11f,.10f},{.16f,.17f,.14f},.06f}
        };
        static_assert(std::size(finishes)==Impl::fixtureKinds);
        for (size_t kind=0; kind<r.fixtureInstances.size(); ++kind)
        {
            const auto &instances = r.fixtureInstances[kind];
            if (instances.empty()) continue;
            for (int part=0; part<2; ++part)
            {
                auto &mesh = r.fixtureModels[kind][part];
                r.setModel(Mat4::identity());
                r.material(!part && kind==size_t(World::FixtureKind::dumpster) ? r.dumpsterTexture : r.white,0,
                           part ? finishes[kind].trim : finishes[kind].body);
                Uniform1i(r.uniform("uInstanced"),2);
                Uniform1f(r.uniform("uMetallic"),finishes[kind].metal);
                mesh.drawInstances(instances);
                if (depthPass) ++r.shadowBatches;
                else
                {
                    ++r.submittedBatches;
                    r.submittedTriangles += size_t(mesh.count)/3 * instances.size();
                }
            }
        }
        for (auto &instances : r.treeInstances)
            instances.clear();
        for (const auto &t : world.trees)
        {
            auto centre = t.foot + Vec3{0, t.height * .55f, 0};
            // Conservative sphere covers both LODs, the crown and the wind deformation.
            float radius = t.height * .8f, distance = (centre - view.eye).length();
            if (distance - radius > std::min(float(treeDrawDistance), range) || !frustum.sphere(centre, radius))
                continue;
            float angle = float(t.seed % 1000) * .01f;
            std::array<float, 8> instance{
                t.foot.x,        t.foot.y,        t.foot.z, t.height / 8,
                std::sin(angle), std::cos(angle), angle,    float(t.seed % 97) / 97};
            int base = (int(t.kind) * 3 + int(t.seed % 3)) * 2;
            if (distance - radius < treeLodEnd)
                r.treeInstances[base].push_back(instance);
            if (distance + radius > treeLodStart)
                r.treeInstances[base + 1].push_back(instance);
        }
        for (size_t k = 0; k < r.treeModels.size(); ++k)
        {
            auto &instances = r.treeInstances[k];
            if (instances.empty())
                continue;
            for (int part = 0; part < 2; ++part)
            {
                r.setModel(Mat4::identity());
                r.material(r.white, 0, {1, 1, 1});
                Uniform1i(r.uniform("uInstanced"), 1);
                Uniform1i(r.uniform("uSurface"), 8 + part);
                Uniform1i(r.uniform("uTreeLod"), GLint(k % 2));
                auto &mesh = r.treeModels[k][part];
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
        for (const auto &site : world.sites)
        {
            if (!site.food()) continue;
            Vec3 p{float(site.centre.x),float(world.terrain.height(site.centre)),float(site.centre.z)};
            if (!visible(p, 4, 200)) continue;
            const auto &model = r.foodModels[site.kind == ServiceKind::kiosk ? 0 : 1];
            const auto lod = std::min<size_t>(model.lods.size()-1, (p-view.eye).length() > 65 ? 1 : 0);
            const auto transform = Mat4::translate(p) * Mat4::yaw(float(pi));
            for (const auto &batch : model.lods[lod]) r.draw(batch, transform);
        }
        auto vehicleTransform = [](const Car &c) {
            Vec3 base{float(c.body.p.x), float(c.body.height - .8), float(c.body.p.z)};
            if (c.roll != 0 || c.pitch != 0)
            {
                // Model origin is at tire level. Keep the lowest rotated hull corner on the road.
                const double cp = std::cos(c.pitch);
                base.y += float(std::abs(cp*std::sin(c.roll))*c.body.halfW +
                                std::abs(std::sin(c.pitch))*c.body.halfH +
                                std::max(0.,-cp*std::cos(c.roll))*c.body.verticalExtent);
            }
            return Mat4::translate(base) * Mat4::yaw(float(c.heading())) *
                   Mat4::pitch(float(-c.terrainPitch + c.pitch)) * Mat4::roll(float(-c.terrainRoll + c.roll));
        };
        auto person = [&](const Body &b, int style, double heading, double distance, bool dead, bool player, const Car *cabin=nullptr, WeaponId drawn=WeaponId::none, bool ridingScooter=false, bool courier=false, bool medic=false, bool officer=false)
        {
            Vec3 p{float(b.p.x), float(b.height - .85), float(b.p.z)};
            if (!visible(p, 2, 130))
                return;
            int k = style % 2;
            auto &motion = r.actorMotion[b.id];
            ActorState state;
            state.id = b.id;
            state.heading = heading;
            state.speed = b.v.length();
            state.distance = distance;
            state.dead = dead;
            state.player = player;
            state.scooter = ridingScooter;
            if (ridingScooter) p.y += .23f;
            state.aiming = player ? world.aiming : drawn != WeaponId::none;
            auto animation = motion.sample(state, world.time, r.poses[k]);
            if (cabin)
            {
                if (r.poses[k] >= 384) animation = {320,320,1};
                const auto seat = rotate(b.p - cabin->body.p, -cabin->body.angle);
                const float floor = float((b.height - .85) - (cabin->body.height - .8));
                // The baked seated pose is 1.47 m tall. Low cars need a more reclined posture.
                const float posture = std::clamp((float(cabin->body.verticalExtent) - floor - .14f) / 1.48f, .65f, 1.f);
                r.setModel(vehicleTransform(*cabin) * Mat4::translate({float(seat.x),floor,float(seat.z)}) *
                           Mat4::yaw(float(heading - cabin->heading())) * Mat4::scale({1,posture,1}));
            }
            else
                r.setModel(Mat4::translate(p + Vec3{0, dead ? .23f : 0.f, 0}) * Mat4::yaw(motion.heading) *
                           Mat4::pitch(dead ? float(pi * .5) : motion.lean));
            r.material(r.peopleTexture[k], 0, officer ? Vec3{.58f,.65f,.78f} : Vec3{1,1,1});
            Uniform1i(r.uniform("uSkin"), 1);
            Uniform1f(r.uniform("uPose"), animation.to);
            Uniform1f(r.uniform("uPreviousPose"), animation.from);
            Uniform1f(r.uniform("uPoseBlend"), animation.blend);
            ActiveTexture(TEXTURE0 + 2);
            glBindTexture(GL_TEXTURE_2D, r.bones[k]);
            ActiveTexture(TEXTURE0);
            r.skin[k].draw();
            if (officer && !dead)
            {
                r.material(r.white, 0, {.08f,.115f,.17f});
                r.patrolCap[k].draw();
            }
            Uniform1i(r.uniform("uSkin"), 0);
            if (officer && !dead && !cabin)
            {
                auto uniform = Mat4::translate(p) * Mat4::yaw(motion.heading);
                r.setModel(uniform * Mat4::translate({.088f,1.35f,.115f}) * Mat4::scale({.034f,.048f,.012f}));
                r.material(r.white, 0, {.76f,.65f,.32f});
                r.cube.draw();
            }
            if (medic && !dead && !cabin)
            {
                auto kit = Mat4::translate(p) * Mat4::yaw(motion.heading);
                auto casePart = [&](Vec3 at, Vec3 size, Vec3 tint) {
                    r.setModel(kit * Mat4::translate(at) * Mat4::scale(size));
                    r.material(r.white,0,tint);
                    r.cube.draw();
                };
                casePart({.29f,.67f,-.03f},{.16f,.25f,.38f},{.80f,.80f,.74f});
                casePart({.373f,.67f,-.03f},{.008f,.14f,.04f},{.58f,.10f,.08f});
                casePart({.373f,.67f,-.03f},{.008f,.04f,.14f},{.58f,.10f,.08f});
            }
            if (courier && !cabin)
            {
                auto torso = Mat4::translate(p + Vec3{0, dead ? .23f : 0.f, 0}) *
                             Mat4::yaw(motion.heading) * Mat4::pitch(dead ? float(pi*.5) : motion.lean);
                auto part = [&](Vec3 at, Vec3 size, Vec3 tint) {
                    r.setModel(torso * Mat4::translate(at) * Mat4::scale(size));
                    r.material(r.white,0,tint);
                    r.cube.draw();
                };
                Vec3 canvas = style%2 ? Vec3{.79f,.43f,.07f} : Vec3{.08f,.46f,.32f};
                part({0,1.22f,-.28f},{.45f,.46f,.3f},canvas);
                part({0,1.44f,-.28f},{.46f,.055f,.32f},{.14f,.16f,.15f});
                part({0,1.19f,-.435f},{.38f,.032f,.012f},{.85f,.82f,.65f});
                for (float x : {-.135f,.135f})
                    part({x,1.39f,.115f},{.041f,.33f,.035f},{.13f,.14f,.13f});
            }
            if (state.aiming && !dead && !cabin && (drawn == WeaponId::pistol || drawn == WeaponId::automatic))
            {
                auto hands = Mat4::translate(p) * Mat4::yaw(motion.heading) *
                             Mat4::translate({.14f, 1.32f, .43f});
                auto gunPart = [&](Vec3 offset, Vec3 size, Vec3 colour) {
                    r.setModel(hands * Mat4::translate(offset) * Mat4::scale(size));
                    r.material(r.white, 0, colour);
                    r.cube.draw();
                };
                bool rifle = drawn == WeaponId::automatic;
                gunPart({}, {.05f,.07f,rifle ? .36f : .18f}, {.16f,.17f,.17f});
                gunPart({0,-.055f,-.045f}, {.044f,.13f,.05f}, {.12f,.10f,.08f});
                if (rifle)
                {
                    gunPart({0,.01f,.27f}, {.032f,.032f,.23f}, {.12f,.13f,.13f});
                    gunPart({0,-.025f,-.235f}, {.058f,.09f,.18f}, {.29f,.18f,.12f});
                }
            }
        };
        for (auto &p : world.people)
            person(p.body, int(p.style), p.heading, p.distance, p.dead, false, p.riding >= 0 ? &world.cars[p.riding] : nullptr, p.aimSeconds > 0 ? p.sidearm : WeaponId::none,
                   p.ownedScooter >= 0 && world.scooters[p.ownedScooter].rider >= 0, p.courier, p.uniform == Person::Uniform::medical || world.paramedicCall(int(&p-world.people.data())) != nullptr,
                   p.uniform == Person::Uniform::police || int(&p-world.people.data()) == world.patrol.officer);
        if (world.controlledCar < 0)
        {
            auto gun = world.aiming ? world.inventory.active() : nullptr;
            person(world.player, 0, -world.player.angle, world.playerDistance, false, true,
                   nullptr, gun ? gun->id : WeaponId::none, world.playerScooter >= 0);
        }
        if (world.transitPassenger())
            person(world.player, 0, -world.player.angle, 0, false, true, &world.cars[world.controlledCar]);
        // Light road vehicles use the existing seated rig. Only nearby cabins
        // need a driver mesh; full-size opaque and public-service cabins have their own layout.
        for (const auto &car : world.cars)
        {
            bool driving = int(car.id) == world.controlledCar && !world.transitPassenger();
            if ((car.kind != 0 && car.kind != 3 && car.kind != 13) || car.destroyed ||
                (!driving && (car.driverless || car.personal || car.summoned || car.id == 0 ||
                              (world.patrol.car == int(car.id) && world.patrol.officer >= 0)))) continue;
            Vec3 centre{float(car.body.p.x),float(car.body.height),float(car.body.p.z)};
            if (!visible(centre,2,38)) continue;
            Body seated = driving ? world.player : car.body;
            if (!driving) seated.id = 0xa0000000u + car.id;
            seated.p = car.body.p + rotate({-.45 * car.body.halfW,.13 * car.body.halfH},car.body.angle);
            seated.height = car.body.height + .14;
            person(seated,driving ? 0 : int(car.id % 2),car.heading(),0,false,driving,&car,
                   WeaponId::none,false,false,false,!driving && car.kind != 0);
        }
        for (auto &c : world.cars)
        {
            Vec3 p{float(c.body.p.x), float(c.body.height - .8), float(c.body.p.z)};
            if (!visible(p, float(c.body.halfH + 2), 400))
                continue;
            auto transform = vehicleTransform(c);
            auto &model = r.cars.at(c.model);
            const auto *crew = c.model == ambulanceModel ? world.ambulanceCall(c.id) : nullptr;
            const bool beacon = crew && crew->stage != World::RescueStage::standby && !c.destroyed;
            float distance = (p - view.eye).length();
            size_t lod = std::min(model.lods.size() - 1, size_t(distance > 150 * detailScale ? 2 : distance > 65 * detailScale ? 1 : 0));
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
                        const int side = b.part == 1 ? -1 : 1;
                        if (c.doorSide == 0 || c.doorSide == side)
                        {
                            const auto pivot = model.hinges[b.part - 1];
                            partTransform = transform * Mat4::translate(pivot) * Mat4::yaw(float(-side * 1.05 * c.doorOpen)) *
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
                    if (c.kind==11 && b.part==100)
                    {
                        Vec3 centre{0,1.8f,.2f};
                        partTransform=transform*Mat4::translate(centre)*Mat4::yaw(float(c.turretYaw))*Mat4::translate(centre*-1.f);
                    }
                    if(c.kind==14 && (b.part==100 || b.part==103))
                    {
                        Vec3 pivot{0,2.2f,.55f};
                        auto pitch=b.part==103?Mat4::pitch(float(-c.turretPitch)):Mat4::identity();
                        partTransform=transform*Mat4::translate(pivot)*Mat4::yaw(float(c.turretYaw))*pitch*Mat4::translate(pivot*-1.f);
                    }
                    if (c.kind==12 && (b.part==101 || b.part==102))
                    {
                        const bool mainRotor=b.part==101;
                        const Vec3 pivot=mainRotor?Vec3{0,3.03f,0}:Vec3{.25f,2.5f,-3.9f};
                        auto spin=mainRotor?Mat4::yaw(float(c.wheelTravel)):Mat4::pitch(float(-c.wheelTravel*3));
                        partTransform=transform*Mat4::translate(pivot)*spin*Mat4::translate(pivot*-1.f);
                    }
                    if (c.paint && !depthPass)
                    {
                        Uniform1i(r.uniform("uPaintIndex"), c.paint);
                        Uniform1i(r.uniform("uPaintMask"), b.paintMask);
                    }
                    r.draw(b, partTransform, beacon);
                }
                if (translucent)
                {
                    glDepthMask(GL_TRUE);
                    glDisable(GL_BLEND);
                }
            }
            if (c.paint && !depthPass) Uniform1i(r.uniform("uPaintIndex"), 0);
        }
        for (const auto &s : world.scooters)
        {
            bool fallen = s.rider == Scooter::free;
            Vec3 at{float(s.body.p.x),float(s.body.height - .15 + (fallen ? .18 : .025)),float(s.body.p.z)};
            if (!visible(at,1.5f,110)) continue;
            r.setModel(Mat4::translate(at) * Mat4::yaw(float(-s.body.angle)) * Mat4::roll(fallen ? 1.35f : 0.f));
            r.material(r.white,0,{.43f,.47f,.46f});
            r.scooterFrame.draw();
            r.material(r.white,0,{.065f,.071f,.066f});
            r.scooterRubber.draw();
            r.material(r.white,0,{.78f,.49f,.12f});
            r.scooterTrim.draw();
        }
        for (size_t i=0; i<world.animals.size(); ++i)
        {
            const auto &a = world.animals[i];
            bool dead = a.deadUntil > 0;
            Vec2 pos = a.p + a.velocity * (world.animalClock - .1);
            Vec3 at{float(pos.x),float(a.altitude+.065+(dead?.09:0.)),float(pos.z)};
            if (!visible(at,1.25f,105)) continue;
            const auto &rig = r.animalRigs[a.variant];
            float distance = (at-view.eye).length();
            size_t lod = std::min<size_t>(distance < 14 && !depthPass ? 0 : distance < 40 ? 1 : 2,
                                         rig.model.lods.size()-1);
            int clip = dead ? 0 : a.flock>=0 && world.flocks[a.flock].flying ? 3 :
                       a.velocity.length2() > 4 ? 2 : a.velocity.length2() > .01 ? 1 : 0;
            double phase = (clip==1 || clip==2 ? a.distance : world.time) / rig.cycle[clip] + i*.137;
            float pose = dead ? 0.f : float(clip*64 + std::fmod(phase,1.)*64);
            float scale = a.species==Species::bird ? .72f : float(.94 + (i%5)*.03);
            Mat4 model = Mat4::translate(at) * Mat4::yaw(float(a.heading)) *
                         Mat4::roll(dead ? 1.5f : 0.f) * Mat4::scale({scale,scale,scale});
            Uniform1i(r.uniform("uSkin"),1);
            Uniform1f(r.uniform("uPose"),pose);
            Uniform1f(r.uniform("uPoseBlend"),1);
            ActiveTexture(TEXTURE0+2);
            glBindTexture(GL_TEXTURE_2D,rig.palette);
            ActiveTexture(TEXTURE0);
            for (const auto &b : rig.model.lods[lod]) r.draw(b,model);
        }
        Uniform1i(r.uniform("uSkin"),0);
    } // sun/camera geometry passes
    Frustum effectFrustum(vp);
    auto visible = [&](Vec3 p, float radius, float objectRange)
    {
        const auto delta = p-view.eye;
        const float limit = std::min(range,objectRange)+radius;
        return dot(delta,delta)<limit*limit && effectFrustum.sphere(p,radius);
    };
    for (const auto &site : world.sites)
    {
        if ((site.entrance - Vec2{view.eye.x,view.eye.z}).length2() > 47*47) continue;
        Vec3 p{float(site.entrance.x),float(world.terrain.height(site.entrance)+.085),float(site.entrance.z)};
        if (!visible(p, 2, 45)) continue;
        Vec3 color = site.kind == ServiceKind::clinic ? Vec3{.16f,.55f,1.f} :
                     site.kind == ServiceKind::market ? Vec3{.84f,.42f,.92f} :
                     (world.ownedGarage >= 0 && &site == &world.sites[world.ownedGarage]) ? Vec3{.20f,.95f,.85f} :
                     site.kind == ServiceKind::garage ? Vec3{1.f,.65f,.16f} : Vec3{.3f,.85f,.48f};
        for (int side : {-1,1})
        {
            r.solid(p + Vec3{side*.78f,0,0}, {.045f,.025f,1.6f}, color, .45f);
            r.solid(p + Vec3{0,0,side*.78f}, {1.6f,.025f,.045f}, color, .45f);
        }
    }
    if (world.drone.active)
    {
        const auto &b=world.buildings[world.drone.targets[world.drone.scanned]];
        Vec3 point{float(b.p.x),float(world.terrain.height(b.p)+b.height+1.25),float(b.p.z)};
        if (visible(point,2,210))
        {
            r.material(r.white,0,{.32f,.92f,.72f},.65f);
            r.setModel(Mat4::translate(point)*Mat4::yaw(float(world.time)));
            r.marker.draw();
        }
    }
    if (world.job.kind == JobKind::none && world.controlledCar >= 0)
        for (auto hail : world.hailers)
        {
            if (hail.actor < 0 || hail.until <= world.time) continue;
            const auto &actor = world.people[hail.actor];
            if (actor.dead || actor.fear > 0 || actor.riding >= 0) continue;
            Vec3 at{float(actor.body.p.x), float(actor.body.height + 1.45), float(actor.body.p.z)};
            if (!visible(at, 1, 95)) continue;
            r.material(r.white, 0, {.36f,.91f,.70f}, .6f);
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
        Vec3 p{float(point.x),float(height + .12*std::sin(3*world.time)),float(point.z)};
        if (visible(p, 2, 240))
        {
            r.material(r.white, 0, {1,.61f,.12f}, .65f);
            r.setModel(Mat4::translate(p) * Mat4::yaw(float(std::fmod(world.time,2*pi))));
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
            r.solid(v, {.14f,.29f,.14f}, {.12f,.68f,.83f}, .45f);
            r.solid(v+Vec3{0,.15f,0}, {.145f,.025f,.145f}, {.78f,.83f,.85f});
        }
        else if (p.health)
        {
            r.solid(v, {.38f,.25f,.17f}, {.90f,.87f,.77f});
            r.solid(v+Vec3{0,0,.09f}, {.16f,.05f,.014f}, {.85f,.14f,.12f});
            r.solid(v+Vec3{0,0,.09f}, {.05f,.16f,.015f}, {.85f,.14f,.12f});
        }
        else if (p.weapon == WeaponId::none)
            r.solid(v, {.29f,.06f,.15f}, {.43f,.75f,.38f}, .4f);
        else
        {
            r.solid(v, {.39f,.085f,.07f}, {.44f,.45f,.44f}, .35f);
            r.solid(v+Vec3{-.12f,-.07f,0}, {.065f,.15f,.07f}, {.28f,.20f,.13f});
        }
    }
    for (auto &rocket : world.rockets)
    {
        Vec3 p{float(rocket.p.x), float(rocket.height), float(rocket.p.z)};
        if (visible(p, 1, 250))
            r.solid(p, {.12f, .12f, .12f}, {1, .6f, .2f}, 2);
    }
    r.effects.draw(r.effectShader,world,view,vp,r.graphics.effects);
    if (post) r.post.present(w,h,day,rain,float(world.clock.sunHeight()),r.graphics.bloom);
    r.timer.end();
    UseProgram(0);
}
void Renderer::overlay(const World *world, int w, int h, double fps, bool menu, const GameConsole *console,
                        const std::wstring &message, const AudioLevels &levels, float progress, bool jobsOpen, const FrontEnd *front)
{
    auto &r = *impl_;
    glViewport(0,0,w,h);
    r.canvas(w, h);
    r.hudDamage.begin();
    auto surface = static_cast<uint32_t *>(r.pixels);
    r.hudDamage.regions(false,[&](int x,int y,int width,int height) {
        for (int row=y;row<y+height;++row)
            std::fill_n(surface+size_t(row)*w+x,width,uint32_t{});
    });
    if (progress >= 0)
    {
        const int width = std::min(600,w-32), x=(w-width)/2;
        r.text(x, h/2-63, L"PROMZONA", RGB(220,173,96), true);
        r.rect(x, h/2, width, 15, RGB(37,43,40));
        r.rect(x, h/2, int(width*progress), 15, RGB(113,151,98));
        r.text(x, h/2+26, L"Подготовка города");
    }
    else if (menu && front)
    {
        const auto rows = front->rows(world != nullptr,r.graphics,r.mode,levels);
        MenuLayout layout(w,h,int(rows.size()));
        const int x=layout.x, y=layout.y, width=layout.width, step=layout.step;
        const int bottom=y+int(rows.size())*step;
        const int headingHeight = std::min(y-4,68);
        r.rect(x-12,y-headingHeight-3,width+24,bottom-y+headingHeight+60,RGB(19,27,26));
        const wchar_t *heading = front->page == MenuPage::settings ? L"НАСТРОЙКИ" :
            front->page == MenuPage::home ? L"PROMZONA" : L"ЗАМЕНА СЛОТА";
        r.menuText(x+10,y-headingHeight,width-20,headingHeight,heading,RGB(231,193,122),width>=450 && headingHeight>=60);
        for (int i=0;i<int(rows.size());++i)
        {
            const auto &row=rows[i];
            const int top=y+i*step;
            const bool selected=i==front->focus;
            r.rect(x,top,width,step-2,selected ? RGB(57,66,59) : RGB(27,36,34));
            if (selected) r.rect(x,top,3,step-2,RGB(231,177,94));
            const COLORREF ink=row.enabled ? RGB(229,231,212) : RGB(140,146,137);
            const int column = width*53/100;
            r.menuText(x+12,top,(row.value.empty()?width:column)-20,step-2,row.label,ink);
            if (!row.value.empty()) r.menuText(x+column,top,width-column-10,step-2,row.value,ink);
        }
        if (world && front->page == MenuPage::home)
            r.text(x,bottom+2,L"Автосохранение: слот " + std::to_wstring(front->active),RGB(190,197,177));
        r.menuText(x+8,bottom+22,width-16,30,L"↑↓ Выбор     ←→ Изменить     Enter ОК     Esc Назад",RGB(190,197,177));
    }
    if (world && !menu && progress < 0)
    {
        r.rect(20, 20, 270, 100, RGB(12, 19, 18));
        r.text(35, 28, L"$ " + std::to_wstring(world->money), RGB(154, 207, 144));
        r.rect(35, 66, 230, 12, RGB(53, 57, 55));
        double health = world->controlledCar >= 0 ? world->cars[world->controlledCar].health : world->health;
        r.rect(35, 66, int(230 * clamp(health, 0., 1.)), 12, RGB(117, 182, 103));
        auto weapon = world->inventory.active();
        constexpr std::array weaponLabels{L"КУЛАКИ", L"ПИСТОЛЕТ", L"АВТОМАТ", L"БАЗУКА", L"ОГНЕМЁТ"};
        r.text(35, 86,
               world->controlledCar>=0 && world->cars[world->controlledCar].kind==11 ? L"ТАНКОВАЯ ПУШКА" :
               world->controlledCar>=0 && world->cars[world->controlledCar].kind==14 ? L"БОРТОВОЙ ПУЛЕМЁТ" :
               weapon ? std::wstring(weaponLabels[static_cast<size_t>(weapon->id)]) + L"  " +
                            std::to_wstring(world->inventory.ammo(weapon->id))
                      : L"КУЛАКИ");
        r.text(w - 250, 25, std::to_wstring(int(fps)) + L" FPS  /  C++23");
        if (world->wantedLevel() > 0)
        {
            r.text(w - 250, 60, L"В РОЗЫСКЕ", RGB(239, 159, 68));
            for (int i = 0; i < world->wantedLevel(); i++)
                r.rect(w - 250 + i * 30, 90, 24, 10, RGB(228, 121, 44));
            if (world->patrol.hold > 0)
            {
                r.text(w - 250, 108, L"Задержание", RGB(250,199,126));
                r.rect(w - 250, 139, 174, 5, RGB(51,45,38));
                double limit = world->controlledCar < 0 ? 1.5 : 2.5;
                r.rect(w - 250, 139, int(174 * std::min(1.,world->patrol.hold / limit)), 5, RGB(239,159,68));
            }
        }
        int mx = 22, my = h - 245;
        constexpr int ms = Minimap::size;
        r.rect(mx - 5, my - 5, ms + 10, ms + 10, RGB(17, 28, 26));
        auto focus = world->drone.active ? world->drone.position : world->position();
        if (world->layout.revision() == CityLayout::districtRevision)
        {
            const auto &layout=world->layout;
            int i=int(std::floor((focus.x-layout.coordinate(0))/layout.spacing));
            int j=int(std::floor((focus.z-layout.coordinate(0))/layout.spacing));
            const wchar_t *label=L"ПОБЕРЕЖЬЕ";
            if (i>=0 && j>=0 && i<layout.nodes()-1 && j<layout.nodes()-1)
            {
                constexpr std::array labels{L"МИКРОРАЙОН",L"СТАРЫЕ ДВОРЫ",L"САДОВЫЙ ПОСЁЛОК",
                    L"ЦЕНТР",L"ГАРАЖНЫЙ КООПЕРАТИВ",L"ПРОМЗОНА"};
                label=labels[size_t(layout.district(i,j))];
            }
            r.text(mx,my-36,label,RGB(225,218,191));
        }
        r.minimap.copyView(focus, r.mapPixels);
        BITMAPINFO mapInfo{};
        mapInfo.bmiHeader = {sizeof(BITMAPINFOHEADER), ms, -ms, 1, 32, BI_RGB};
        SetDIBitsToDevice(r.uiDC,mx,my,ms,ms,0,0,0,ms,r.mapPixels.data(),&mapInfo,DIB_RGB_COLORS);
        auto map = [&](Vec2 p)
        {
            Vec2 offset = Minimap::offset(p-focus);
            return POINT{mx + ms/2 + LONG(std::lround(offset.x)), my + ms/2 + LONG(std::lround(offset.z))};
        };
        int saved = SaveDC(r.uiDC);
        IntersectClipRect(r.uiDC, mx, my, mx + ms, my + ms);
        for (auto &c : world->cars)
        {
            auto p = map(c.body.p);
            if (p.x > mx && p.x < mx + ms && p.y > my && p.y < my + ms)
                r.rect(p.x - 1, p.y - 1, 3, 3, RGB(104, 157, 176));
        }
        for (const auto &pickup : world->pickups)
        {
            if (pickup.wait > 0) continue;
            auto p = map(pickup.p);
            if (p.x <= mx || p.x >= mx + ms || p.y <= my || p.y >= my + ms) continue;
            if (pickup.health) {
                r.rect(p.x-2,p.y,5,1,RGB(125,236,172));
                r.rect(p.x,p.y-2,1,5,RGB(125,236,172));
            }
            else r.rect(p.x-1,p.y-1,3,3,pickup.energy ? RGB(184,225,75) : RGB(243,178,68));
        }
        if (world->job.kind == JobKind::none && world->controlledCar >= 0)
            for (auto hail : world->hailers)
            {
                if (hail.actor < 0 || hail.until <= world->time) continue;
                const auto &person = world->people[hail.actor];
                if (person.dead || person.fear > 0 || person.riding >= 0) continue;
                auto p = map(person.body.p);
                if (p.x < mx || p.x >= mx+ms || p.y < my || p.y >= my+ms) continue;
                auto ink = RGB(84,232,179);
                r.rect(p.x-1,p.y-4,3,2,ink);
                r.rect(p.x-3,p.y-2,2,3,ink);
                r.rect(p.x+2,p.y-2,2,3,ink);
                r.rect(p.x-1,p.y+1,3,2,ink);
            }
        if (world->ownedGarage >= 0)
        {
            auto door = map(world->sites[world->ownedGarage].entrance);
            auto ink = RGB(87,237,215);
            r.rect(door.x-4,door.y-3,9,2,ink);
            r.rect(door.x-4,door.y-1,2,5,ink);
            r.rect(door.x+3,door.y-1,2,5,ink);
        }
        if (world->job.kind != JobKind::none && !world->job.escaping())
        {
            Vec2 d = world->jobTarget() - focus;
            auto p = map(focus + d * (1/std::max(1.,std::max(std::abs(d.x),std::abs(d.z))/122.)));
            r.rect(p.x-4,p.y-4,9,9,RGB(47,42,30));
            r.rect(p.x-2,p.y-2,5,5,RGB(255,189,65));
        }
        if (world->drone.active)
        {
            auto roof=map(world->buildings[world->drone.targets[world->drone.scanned]].p);
            auto pilot=map(world->player.p);
            r.rect(roof.x-3,roof.y-3,7,7,RGB(94,241,191));
            r.rect(pilot.x-2,pilot.y-2,5,5,RGB(255,255,255));
        }
        RestoreDC(r.uiDC, saved);
        r.rect(mx + ms / 2 - 3, my + ms / 2 - 3, 7, 7, RGB(255, 189, 76));
        double heading = world->drone.active ? world->drone.heading :
                         world->controlledCar >= 0 ? world->cars[world->controlledCar].heading() : -world->player.angle;
        Vec2 nose = Minimap::offset(forward(heading) * 12.);
        for (int k=3; k<=8; ++k)
            r.rect(mx+ms/2+int(std::lround(nose.x*k/8))-1,my+ms/2+int(std::lround(nose.z*k/8))-1,
                   3,3,RGB(255,200,90));
        r.rect(mx+4, my+4, 18, 23, RGB(20,28,25));
        r.text(mx+6, my+2, L"N");
        int hour = int(world->clock.hour), minute = int((world->clock.hour - hour) * 60);
        wchar_t time[32];
        swprintf_s(time, L"%02d:%02d", hour, minute);
        r.text(mx, my + ms + 6, time);
        if (world->drone.active)
        {
            const auto &d=world->drone;
            auto delta=world->buildings[d.targets[d.scanned]].p-d.position;
            const auto mint=RGB(119,237,189);
            const int left=w/2-155;
            r.rect(left,22,310,81,RGB(17,28,26));
            r.text(left+12,27,L"РАЗВЕДКА     АКБ "+std::to_wstring(int(std::ceil(d.battery)))+L"%",mint);
            r.text(left+12,55,L"Крыша "+std::to_wstring(d.scanned+1)+L"/3   "+std::to_wstring(int(delta.length()))+L" м",mint);
            r.rect(left+12,92,286,4,RGB(70,94,85));
            r.rect(left+12,92,int(286*clamp(d.scanTime/2.,0.,1.)),4,mint);
            r.rect(w/2-7,h/2,15,1,mint);
            r.rect(w/2,h/2-7,1,15,mint);
        }
        if (world->transitPassenger())
        {
            const auto &car = world->cars[world->controlledCar];
            const auto &line = world->lines[car.line];
            const bool stopped = car.dwell > 0;
            const int stop = stopped ? (car.nextStop + int(line.stops.size()) - 1) % int(line.stops.size()) : car.nextStop;
            const auto title = (car.kind == 9 ? L"Трамвай №" : L"Троллейбус №") + std::to_wstring(car.line+1);
            const auto label = world->transitExitRequested ? L"Выход на остановке " :
                               stopped ? L"Остановка " : L"Следующая остановка ";
            const auto status = label + std::to_wstring(stop+1);
            const int left = w/2-174;
            r.rect(left,22,348,62,RGB(18,33,32));
            r.text(left+12,26,title,RGB(133,225,208));
            r.text(left+12,53,status);
        }
        int hintTop=r.hintBar(controlHints(*world),w,h);
        if (world->energySeconds > 0)
            r.text(35,123,L"Энергия  " + std::to_wstring(int(std::ceil(world->energySeconds))) + L" с",RGB(93,218,226));
        if (world->job.kind != JobKind::none)
        {
            const auto &j = world->job;
            auto title = j.kind == JobKind::courier ? L"Передать посылку" :
                         j.kind == JobKind::elimination ? L"Устранить цель" :
                         j.passenger() ? (!j.acquired ? L"Забрать пассажира" : j.kind == JobKind::taxi ? L"Довезти пассажира" : L"В поликлинику") :
                         j.acquired ? L"Доставить в гараж" : L"Забрать автомобиль";
            std::wstring status=title;
            if (j.peace())
                status=std::wstring(j.kind==JobKind::leaflets ? L"Листовки" : L"Громкоговоритель")+
                    L"   "+std::to_wstring(j.route.completed)+L"/"+std::to_wstring(j.stopCount())+L"   "+
                    std::to_wstring(int(j.stopped))+(j.kind==JobKind::leaflets ? L"/3 с" : L"/30 с");
            if (j.escaping()) status=L"Скройтесь от преследования";
            else status+=L"   "+std::to_wstring(int((world->jobTarget()-focus).length()))+L" м";
            if (j.series) status = std::to_wstring(world->seriesStage+1) + L"/3   " + status;
            std::wstring details = L"$" + std::to_wstring(world->expectedPay());
            int seconds = int(std::ceil(std::max(0., j.deadline - world->time)));
            if (seconds > 0) details += L"   " + std::to_wstring(seconds/60) + L":" +
                (seconds%60 < 10 ? L"0" : L"") + std::to_wstring(seconds%60);
            else details += L"   Опоздание";
            if (j.kind == JobKind::vehicle)
                details += L"   Кузов " + std::to_wstring(int(std::round(world->cars[j.actor].health*100))) +
                           L"%   нужно " + std::to_wstring(int(std::round(world->deliveryHealth()*100))) + L"%";
            else if (j.passenger() && j.acquired)
                details += j.ride.scares == 0 ? L"   Пассажир спокоен" : L"   Пассажир испуган";
            r.rect(235,hintTop-69,435,63,RGB(17,26,24));
            r.text(247,hintTop-65,status, RGB(242,194,98));
            r.text(247,hintTop-37,details, RGB(202,218,201));
        }
        if (world->time < world->noticeUntil)
            r.text(235, hintTop-(world->job.kind!=JobKind::none?100:35), world->notice, RGB(252,206,124));
        if (world->openMarket >= 0)
        {
            int left = std::max(12,w-430), top = std::max(15,h/2-122);
            r.rect(left,top,404,244,RGB(24,32,29));
            r.text(left+20,top+12,L"РЫНОК",RGB(244,192,103));
            std::array<std::wstring,4> names{
                world->inventory.owns(WeaponId::pistol) ? L"Пистолетные патроны ×30" : L"Пистолет и 30 патронов",
                world->inventory.owns(WeaponId::automatic) ? L"Автоматные патроны ×30" : L"Автомат и 30 патронов",
                L"Таблетки", L"Энергетик"};
            for (int i=0; i<4; ++i)
            {
                int price = world->shopPrice(static_cast<ShopItem>(i));
                auto color = price > world->money ? RGB(159,148,139) : RGB(232,229,211);
                r.text(left+20,top+54+34*i,L"[" + std::to_wstring(i+1) + L"]  " + names[i],color);
                r.text(left+330,top+54+34*i,L"$"+std::to_wstring(price),color);
            }
            r.text(left+20,top+201,L"[E] Закрыть",RGB(161,184,165));
        }
        else if (jobsOpen)
        {
            int left = std::max(12,w/2-254), top = std::max(10,h/2-204);
            r.rect(left,top,508,408,RGB(21,30,27));
            r.text(left+20,top+12,L"ЗАКАЗЫ",RGB(244,192,103));
            r.text(left+20,top+56,L"[1] Доставить автомобиль");
            r.text(left+20,top+90,L"[2] Устранить цель");
            r.text(left+20,top+124,L"[3] Отказаться от текущего заказа");
            r.text(left+20,top+158,L"[4] Раздать листовки");
            r.text(left+20,top+192,L"[5] Рейс с громкоговорителем");
            r.text(left+20,top+226,L"[6] Такси");
            r.text(left+20,top+260,L"[7] Доставить пациента");
            r.text(left+20,top+294,L"[8] Серия контрактов   " + std::to_wstring(world->seriesStage+1) + L"/3");
            r.text(left+20,top+336,L"Выполнено " + std::to_wstring(world->jobsCompleted) +
                   L"   Серий " + std::to_wstring(world->seriesCompleted) +
                   L"   Заработано $" + std::to_wstring(world->jobEarnings),RGB(182,205,181));
            r.text(left+20,top+371,L"[J] Закрыть",RGB(148,170,151));
        }
    }
    if (console)
    {
        int height = std::min(290, std::max(135, h/2));
        r.rect(0, 0, w, height, RGB(12, 20, 20));
        r.text(16, 8, L"КОНСОЛЬ   ·   help   ·   ↑↓ история   ·   Tab дополнить   ·   PgUp/PgDn журнал", RGB(169,195,178));
        int y = 44;
        for (const auto &line : console->visibleLines(size_t((height-89)/24)))
        {
            r.text(18,y,line,RGB(224,222,201));
            y += 24;
        }
        auto prompt = console->input();
        prompt.insert(console->caret(),1,L'│');
        r.text(16,height-36,L"> " + prompt, RGB(245,196,104));
    }
    else if (!message.empty())
        r.text(25, menu ? h-30 : 140, message, RGB(224, 172, 91));
    GdiFlush(); // Finish drawing into the DIB before OpenGL reads its memory.
    glDisable(GL_DEPTH_TEST);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    UseProgram(r.ui);
    ActiveTexture(TEXTURE0);
    const auto transform = GetUniformLocation(r.ui,"uSample");
    if (menu && progress < 0)
    {
        const float aspect = float(w)/h;
        Uniform3f(transform,std::min(1.f,aspect/r.menuAspect),std::min(1.f,r.menuAspect/aspect),.7f);
        glBindTexture(GL_TEXTURE_2D,r.menuArt);
        r.uiQuad.draw();
    }
    Uniform3f(transform,1,1,1);
    glBindTexture(GL_TEXTURE_2D, r.uiTexture);
    r.hudPixelsUploaded = 0;
    glPixelStorei(GL_UNPACK_ROW_LENGTH,w);
    r.hudDamage.regions(true,[&](int x,int y,int width,int height) {
        glTexSubImage2D(GL_TEXTURE_2D,0,x,y,width,height,0x80e1,GL_UNSIGNED_BYTE,surface+size_t(y)*w+x);
        r.hudPixelsUploaded += size_t(width)*height;
    });
    glPixelStorei(GL_UNPACK_ROW_LENGTH,0);
    Uniform1i(GetUniformLocation(r.ui, "uColor"), 0);
    r.uiQuad.draw();
    UseProgram(0);
    glDisable(GL_BLEND);
}
void Renderer::screenshot(const std::filesystem::path &file, int w, int h)
{
    std::vector<unsigned char> pixels(size_t(w) * h * 4);
    glReadPixels(0, 0, w, h, 0x80e1, GL_UNSIGNED_BYTE, pixels.data());
    BITMAPFILEHEADER fh{};
    BITMAPINFOHEADER ih{};
    fh.bfType = 0x4d42;
    fh.bfOffBits = sizeof fh + sizeof ih;
    fh.bfSize = fh.bfOffBits + DWORD(pixels.size());
    ih.biSize = sizeof ih;
    ih.biWidth = w;
    ih.biHeight = h;
    ih.biPlanes = 1;
    ih.biBitCount = 32;
    ih.biCompression = BI_RGB;
    std::ofstream out(file, std::ios::binary);
    out.write(reinterpret_cast<const char *>(&fh), sizeof fh);
    out.write(reinterpret_cast<const char *>(&ih), sizeof ih);
    out.write(reinterpret_cast<const char *>(pixels.data()), std::streamsize(pixels.size()));
    if (!out)
        throw std::runtime_error("Screenshot write failed");
}
} // namespace pz
