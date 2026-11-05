#include "promzona/renderer.hpp"
#include "json.hpp"
#include "promzona/gl.hpp"
#include "promzona/animation.hpp"
#include "promzona/city_mesh.hpp"
#include "promzona/architecture.hpp"
#include "promzona/visibility.hpp"
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
    GLuint vao{}, vbo{}, ibo{};
    GLsizei count{};
    Mesh() = default;
    Mesh(const Mesh &) = delete;
    Mesh &operator=(const Mesh &) = delete;
    Mesh(Mesh &&o) noexcept
    {
        std::swap(vao, o.vao);
        std::swap(vbo, o.vbo);
        std::swap(ibo, o.ibo);
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
            std::swap(count, o.count);
        }
        return *this;
    }
    ~Mesh() { release(); }
    void release()
    {
        if (ibo)
            DeleteBuffers(1, &ibo);
        if (vbo)
            DeleteBuffers(1, &vbo);
        if (vao)
            DeleteVertexArrays(1, &vao);
        vao = vbo = ibo = 0;
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
} // namespace
struct Renderer::Impl
{
    std::filesystem::path root;
    GLuint scene{}, ui{}, white{}, uiTexture{}, bones[2]{}, rug{};
    Mesh uiQuad, cube, skin[2];
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
    };
    std::vector<Batch> city;
    size_t sceneryBytes{}, submittedBatches{}, submittedTriangles{}, submittedDetails{};
    std::array<Model, 4> cars;
    GLuint peopleTexture[2]{};
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
        glGenTextures(1, &white);
        glBindTexture(GL_TEXTURE_2D, white);
        unsigned char rgba[] = {255, 255, 255, 255};
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 1, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, rgba);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glGenTextures(1, &uiTexture);
        MeshData q;
        quad(q, {-1, -1, 0}, {1, -1, 0}, {1, 1, 0}, {-1, 1, 0});
        uiQuad.upload(q);
        MeshData c;
        box(c, {}, {.5f, .5f, .5f});
        cube.upload(c);
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
            poses[i] = s.poses;
            glGenTextures(1, &bones[i]);
            glBindTexture(GL_TEXTURE_2D, bones[i]);
            glTexImage2D(GL_TEXTURE_2D, 0, RGBA32F, s.joints * 4, s.poses, 0, GL_RGBA, GL_FLOAT,
                         s.bones.data());
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            peopleTexture[i] = texture(root / (std::string("Textures/People/") + name + "-palette.png"));
        }
        const char *models[] = {"old_car_-_vaz_2107", "luaz", "azlh-2140_sl_moskvich", "militia_sedan"};
        rug = texture(root / "Textures/Interiors/rug_red.png");
        for (int k = 0; k < 4; k++)
        {
            auto path = root / "Models/Imported" / models[k];
            if (!std::filesystem::exists(path / "manifest.json"))
            {
                if (k == 3)
                    path = root / "Models/Imported/1970_soviet_car_vaz_2101";
                else
                    throw std::runtime_error("Missing vehicle manifest");
            }
            auto j = nlohmann::json::parse(readText(path / "manifest.json"));
            for (auto &wheel : j.value("wheels", nlohmann::json::array()))
            {
                auto p = wheel.at("pivot");
                cars[k].wheels.push_back({{p[0], p[1], p[2]}, wheel.at("radius"), wheel.at("front")});
            }
            for (auto &lod : j.at("lods"))
            {
                std::vector<Batch> batches;
                for (auto &part : lod)
                {
                    Batch b;
                    auto file = path / part.at("file").get<std::string>();
                    auto data = loadGlb(file);
                    b.mesh.upload(data);
                    b.part = part.value("part", 0);
                    int mat = part.at("material");
                    b.color = data.embeddedColor.empty()
                                  ? white
                                  : uploadTexture(file, decodeImage(data.embeddedColor));
                    if (mat >= 0)
                    {
                        auto &m = j.at("materials").at(mat);
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
                cars[k].lods.push_back(std::move(batches));
            }
        }
    }
    ~Impl()
    {
        city.clear();
        for (auto &v : cars)
            v.lods.clear();
        for (auto &[path, t] : textures)
            glDeleteTextures(1, &t);
        glDeleteTextures(1, &white);
        glDeleteTextures(1, &uiTexture);
        glDeleteTextures(2, bones);
        if (scene)
            DeleteProgram(scene);
        if (ui)
            DeleteProgram(ui);
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
        Uniform1i(uniform("uSurface"), 0);
        Uniform3f(uniform("uTint"), tint.x, tint.y, tint.z);
        Uniform1f(uniform("uEmissive"), emissive);
        Uniform1f(uniform("uAlpha"), 1);
        Uniform1f(uniform("uRoughness"), .7f);
        Uniform1f(uniform("uMetallic"), 0);
    }
    void draw(const Batch &b, const Mat4 &model)
    {
        setModel(model);
        material(b.color, b.normal, b.tint, b.emissive);
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
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, w, h, 0, 0x80e1, GL_UNSIGNED_BYTE, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    }
    void text(int x, int y, const std::wstring &s, COLORREF color = RGB(235, 231, 208), bool big = false)
    {
        auto old = SelectObject(uiDC, big ? large : font);
        SetBkMode(uiDC, TRANSPARENT);
        SetTextColor(uiDC, color);
        TextOutW(uiDC, x, y, s.c_str(), int(s.size()));
        SelectObject(uiDC, old);
    }
    void rect(int x, int y, int w, int h, COLORREF color)
    {
        RECT r{x, y, x + w, y + h};
        auto brush = CreateSolidBrush(color);
        FillRect(uiDC, &r, brush);
        DeleteObject(brush);
    }
};
Renderer::Renderer(const std::filesystem::path &content) : impl_(std::make_unique<Impl>(content)) {}
Renderer::~Renderer() = default;
std::string Renderer::graphicsInfo() const
{
    return std::string(reinterpret_cast<const char *>(glGetString(GL_RENDERER))) + " / " +
           reinterpret_cast<const char *>(glGetString(GL_VERSION));
}
std::string Renderer::sceneryStats() const
{
    auto &r = *impl_;
    return "Scenery batches=" + std::to_string(r.submittedBatches) + "/" + std::to_string(r.city.size()) +
           " near=" + std::to_string(r.submittedDetails) +
           " submitted_triangles=" + std::to_string(r.submittedTriangles) +
           " resident_mesh_MiB=" + std::to_string(r.sceneryBytes / (1024 * 1024));
}
void Renderer::prepare(const World &world)
{
    auto &r = *impl_;
    r.city.clear();
    r.sceneryBytes = 0;
    r.actorMotion.clear();
    std::array<GLuint, 8> walls{};
    std::array<GLuint, 8> normals{};
    std::array<GLuint, 8> orm{};
    std::array<GLuint, 3> signs{};
    auto manifest = nlohmann::json::parse(readText(r.root / "Textures/manifest.json"));
    for (int i = 0; i < 8; i++)
    {
        auto m = manifest[10 + i];
        walls[i] = r.texture(r.root / "Textures" / m.at("color").get<std::string>());
        if (m.contains("normal"))
            normals[i] = r.texture(r.root / "Textures" / m.at("normal").get<std::string>());
        if (m.contains("orm"))
            orm[i] = r.texture(r.root / "Textures" / m.at("orm").get<std::string>());
    }
    for (int i = 0; i < 3; ++i)
        signs[i] = r.texture(r.root / "Textures" / manifest[4 + i].at("color").get<std::string>());
    auto uploadChunk = [&](const CityChunk &data, bool detail)
    {
        for (int i = 0; i < SurfaceCount; ++i)
            if (!data[i].indices.empty())
            {
                Impl::Batch b;
                b.mesh.upload(data[i]);
                r.sceneryBytes +=
                    data[i].vertices.size() * sizeof(Vertex) + data[i].indices.size() * sizeof(std::uint32_t);
                b.detail = detail;
                Vec3 low{1e9f, 1e9f, 1e9f}, high{-1e9f, -1e9f, -1e9f};
                for (const auto &vertex : data[i].vertices)
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
                    b.color = r.white;
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
                if (i >= Sign)
                    b.color = signs[i - Sign];
                r.city.push_back(std::move(b));
            }
    };
    // Chunk ownership covers the whole island and coast, regardless of grid size.
    double low = -std::ceil(world.landEdge / 144) * 144, high = -low;
    for (double z = low; z < high; z += 144)
        for (double x = low; x < high; x += 144)
        {
            auto data = cityChunk(world, x, z);
            uploadChunk(data, false);
        }
    // Near architecture is baked separately in 72m cells. No per-window CPU work per frame.
    std::map<std::pair<int, int>, std::vector<const Building *>> details;
    for (const auto &b : world.buildings)
    {
        auto key = std::pair{int(std::floor(b.p.x / 72)), int(std::floor(b.p.z / 72))};
        details[key].push_back(&b);
    }
    for (const auto &[key, buildings] : details)
    {
        CityChunk data;
        for (const auto *b : buildings)
            buildingDetails(data, planBuilding(*b, world.terrain.height(b->p)));
        uploadChunk(data, true);
    }
}
void Renderer::render(const World &world, const View &view, int w, int h)
{
    auto &r = *impl_;
    float day = float(world.clock.daylight()), rain = float(world.rain);
    Vec3 fog = {.48f * day + .055f, .62f * day + .055f, .73f * day + .07f};
    fog = fog * (1 - rain * .35f);
    glViewport(0, 0, w, h);
    glClearColor(fog.x, fog.y, fog.z, 1);
    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
    glEnable(GL_DEPTH_TEST);
    glDepthFunc(GL_LEQUAL);
    glDisable(GL_BLEND);
    glDisable(GL_CULL_FACE);
    UseProgram(r.scene);
    auto vp =
        Mat4::perspective(float(pi / 3), float(w) / h, .25f, 1800) * Mat4::lookAt(view.eye, view.target);
    UniformMatrix4fv(r.uniform("uViewProjection"), 1, GL_FALSE, vp.m.data());
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
    Uniform1f(r.uniform("uDetailStart"), buildingDetailStart);
    Uniform1f(r.uniform("uDetailEnd"), buildingDetailEnd);
    ActiveTexture(TEXTURE0 + 4);
    glBindTexture(GL_TEXTURE_2D, r.rug);
    ActiveTexture(TEXTURE0);
    Uniform1i(r.uniform("uSkin"), 0);
    Uniform1f(r.uniform("uAlpha"), 1);
    Frustum frustum(vp);
    auto visible = [&](Vec3 p, float radius, float range)
    {
        auto delta = p - view.eye;
        float length = delta.length();
        return length < range + radius && frustum.sphere(p, radius);
    };
    r.submittedBatches = r.submittedTriangles = r.submittedDetails = 0;
    for (auto &b : r.city)
        if (visible(b.centre, b.radius, b.detail ? buildingDetailEnd : 1000))
        {
            ++r.submittedBatches;
            r.submittedTriangles += size_t(b.mesh.count) / 3;
            if (b.detail)
                ++r.submittedDetails;
            r.draw(b, Mat4::identity());
        }
    for (auto &c : world.cars)
    {
        Vec3 p{float(c.body.p.x), float(c.body.height - .8), float(c.body.p.z)};
        if (!visible(p, 5, 400))
            continue;
        auto transform = Mat4::translate(p) * Mat4::yaw(float(c.heading())) *
                         Mat4::pitch(float(-c.terrainPitch + c.pitch)) *
                         Mat4::roll(float(-c.terrainRoll + c.roll));
        auto &model = r.cars[c.model % 4];
        float distance = (p - view.eye).length();
        size_t lod = std::min(model.lods.size() - 1, size_t(distance > 150 ? 2 : distance > 65 ? 1 : 0));
        for (int translucent = 0; translucent < 2; ++translucent)
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
                if (b.part >= 3 && b.part < 3 + int(model.wheels.size()))
                {
                    const auto &wheel = model.wheels[b.part - 3];
                    partTransform = transform * Mat4::translate(wheel.pivot) *
                                    Mat4::yaw(wheel.front ? float(c.steering) : 0.f) *
                                    Mat4::pitch(float(c.wheelTravel / std::max(.05f, wheel.radius))) *
                                    Mat4::translate(wheel.pivot * -1.f);
                }
                r.draw(b, partTransform);
            }
            if (translucent)
            {
                glDepthMask(GL_TRUE);
                glDisable(GL_BLEND);
            }
        }
    }
    auto person = [&](const Body &b, int style, double heading, double distance, bool dead, bool player)
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
        state.aiming = player && world.aiming;
        auto animation = motion.sample(state, world.time, r.poses[k]);
        r.setModel(Mat4::translate(p + Vec3{0, dead ? .23f : 0.f, 0}) * Mat4::yaw(motion.heading) *
                   Mat4::pitch(dead ? float(pi * .5) : motion.lean));
        r.material(r.peopleTexture[k], 0, {1, 1, 1});
        Uniform1i(r.uniform("uSkin"), 1);
        Uniform1f(r.uniform("uPose"), animation.to);
        Uniform1f(r.uniform("uPreviousPose"), animation.from);
        Uniform1f(r.uniform("uPoseBlend"), animation.blend);
        ActiveTexture(TEXTURE0 + 2);
        glBindTexture(GL_TEXTURE_2D, r.bones[k]);
        ActiveTexture(TEXTURE0);
        r.skin[k].draw();
        Uniform1i(r.uniform("uSkin"), 0);
    };
    for (auto &p : world.people)
        person(p.body, int(p.style), p.heading, p.distance, p.dead, false);
    if (world.controlledCar < 0)
        person(world.player, 0, -world.player.angle, world.playerDistance, false, true);
    for (auto &p : world.pickups)
    {
        Vec3 v{float(p.p.x), float(world.terrain.height(p.p) + .7 + .14 * std::sin(world.time * 3)),
               float(p.p.z)};
        if (!visible(v, 1, 60))
            continue;
        r.solid(v, {.28f, .28f, .28f}, p.health ? Vec3{.3f, .9f, .4f} : Vec3{.9f, .65f, .2f}, .8f);
    }
    for (auto &rocket : world.rockets)
    {
        Vec3 p{float(rocket.p.x), float(rocket.height), float(rocket.p.z)};
        if (visible(p, 1, 250))
            r.solid(p, {.12f, .12f, .12f}, {1, .6f, .2f}, 2);
    }
    for (auto &p : world.particles)
        if (visible(p.p, 1, 150))
            r.solid(p.p, {p.size, p.size, p.size}, p.color, 2);
    if (rain > .05f)
    {
        Random rainRandom(4567);
        auto pos = world.position();
        for (int i = 0; i < int(400 * rain); i++)
        {
            float x = float(pos.x + (rainRandom.next() - .5) * 45),
                  z = float(pos.z + (rainRandom.next() - .5) * 45);
            float y = float(world.ground() + std::fmod(rainRandom.next() * 25 + 1000 - world.time * 14, 25));
            r.solid({x, y, z}, {.016f, .65f, .016f}, {.55f, .66f, .72f}, .2f);
        }
    }
    UseProgram(0);
}
void Renderer::overlay(const World *world, int w, int h, double fps, bool menu, bool console,
                       const std::wstring &command, const std::wstring &message, float progress)
{
    auto &r = *impl_;
    r.canvas(w, h);
    std::memset(r.pixels, 0, size_t(w) * h * 4);
    if (menu || progress >= 0)
    {
        r.rect(0, 0, w, h, RGB(9, 12, 13));
        r.text(w / 2 - 180, h / 3 - 65, L"PROMZONA", RGB(220, 173, 96), true);
        r.text(w / 2 - 210, h / 3 + 5, L"Нативная версия C++23 — перенос в работе");
        if (progress >= 0)
        {
            r.rect(w / 2 - 300, h / 2, 600, 22, RGB(37, 43, 40));
            r.rect(w / 2 - 300, h / 2, int(600 * progress), 22, RGB(113, 151, 98));
            r.text(w / 2 - 160, h / 2 + 38, L"Подготовка города и ресурсов...");
        }
        else
        {
            r.text(w / 2 - 150, h / 2, L"Enter — Новая игра");
            r.text(w / 2 - 150, h / 2 + 40, L"L — Загрузить сохранение");
            r.text(w / 2 - 150, h / 2 + 80, L"Esc — Выход");
        }
    }
    if (world && !menu && progress < 0)
    {
        r.rect(20, 20, 270, 100, RGB(12, 19, 18));
        r.text(35, 28, L"$ " + std::to_wstring(world->money), RGB(154, 207, 144));
        r.rect(35, 66, 230, 12, RGB(53, 57, 55));
        double health = world->controlledCar >= 0 ? world->cars[world->controlledCar].health : world->health;
        r.rect(35, 66, int(230 * clamp(health, 0., 1.)), 12, RGB(117, 182, 103));
        auto weapon = world->inventory.active();
        r.text(35, 86,
               weapon ? std::wstring(weapon->name.begin(), weapon->name.end()) + L"  " +
                            std::to_wstring(world->inventory.ammo(weapon->id))
                      : L"КУЛАКИ");
        r.text(w - 250, 25, std::to_wstring(int(fps)) + L" FPS  /  C++23");
        if (world->wantedLevel() > 0)
        {
            r.text(w - 250, 60, L"В РОЗЫСКЕ", RGB(239, 159, 68));
            for (int i = 0; i < world->wantedLevel(); i++)
                r.rect(w - 250 + i * 30, 90, 24, 10, RGB(228, 121, 44));
        }
        int mx = 22, my = h - 245, ms = 190;
        r.rect(mx - 5, my - 5, ms + 10, ms + 10, RGB(17, 28, 26));
        auto focus = world->position();
        double scale = ms / 260.;
        auto map = [&](Vec2 p)
        {
            return POINT{LONG(mx + ms / 2 + (p.x - focus.x) * scale),
                         LONG(my + ms / 2 + (p.z - focus.z) * scale)};
        };
        int saved = SaveDC(r.uiDC);
        IntersectClipRect(r.uiDC, mx, my, mx + ms, my + ms);
        auto mappedRect = [&](Vec2 lo, Vec2 hi, COLORREF color)
        {
            auto a = map(lo), b = map(hi);
            r.rect(a.x, a.y, std::max(1L, b.x - a.x), std::max(1L, b.y - a.y), color);
        };
        const auto &layout = world->layout;
        for (int c : {World::nodes / 3, 2 * World::nodes / 3})
        {
            double x = layout.coordinate(c) + 18;
            mappedRect({x - layout.bankHalf(), focus.z - 130}, {x + layout.bankHalf(), focus.z + 130},
                       RGB(43, 85, 101));
        }
        for (int j = 0; j < World::nodes; ++j)
            for (int i = 0; i < World::nodes; ++i)
            {
                auto p = layout.point(i + j * World::nodes);
                if ((p - focus).length2() > 210 * 210)
                    continue;
                double halfWidth = layout.roadHalf();
                if (layout.road(i, j, 0))
                    mappedRect(p - Vec2{halfWidth, halfWidth}, p + Vec2{halfWidth, 36 + halfWidth},
                               RGB(92, 94, 88));
                if (layout.road(i, j, 1))
                    mappedRect(p - Vec2{halfWidth, halfWidth}, p + Vec2{36 + halfWidth, halfWidth},
                               RGB(92, 94, 88));
            }
        for (auto &b : world->buildings)
        {
            auto p = map(b.p);
            int sx = int(b.half.x * scale), sz = int(b.half.z * scale);
            if (p.x - sx > mx && p.x + sx < mx + ms && p.y - sz > my && p.y + sz < my + ms)
                r.rect(p.x - sx, p.y - sz, sx * 2, sz * 2, RGB(161, 160, 135));
        }
        for (const auto &level : world->roadLevels)
            for (int k = 0; k < 288; ++k)
            {
                double x = level.x - RoadLevel::length + k;
                auto p = map({x, level.centreZ(x)});
                r.rect(p.x - 1, p.y - 3, 3, 6, level.tunnel ? RGB(119, 135, 146) : RGB(177, 168, 135));
            }
        for (auto &c : world->cars)
        {
            auto p = map(c.body.p);
            if (p.x > mx && p.x < mx + ms && p.y > my && p.y < my + ms)
                r.rect(p.x - 1, p.y - 1, 3, 3, RGB(104, 157, 176));
        }
        RestoreDC(r.uiDC, saved);
        r.rect(mx + ms / 2 - 3, my + ms / 2 - 3, 7, 7, RGB(255, 189, 76));
        r.text(mx, my - 30, L"N");
        int hour = int(world->clock.hour), minute = int((world->clock.hour - hour) * 60);
        wchar_t time[32];
        swprintf_s(time, L"%02d:%02d", hour, minute);
        r.text(mx, my + ms + 6, time);
        r.rect(235, h - 57, w - 255, 39, RGB(12, 18, 17));
        r.text(245, h - 50,
               world->controlledCar >= 0
                   ? L"[W/S] Газ/тормоз  [A/D] Руль  [Space] Ручник  [F] Выйти  [R] Вернуть  [F1] Консоль"
                   : L"[WASD] Идти  [Shift] Бежать  [F] Сесть  [ЛКМ] Огонь  [Колесо] Оружие  [F1] Консоль");
    }
    if (console)
    {
        r.rect(0, 0, w, 230, RGB(12, 17, 20));
        r.text(20, 20, L"PROMZONA / native console");
        r.text(20, 60, L"weapons · health · money N · hour N · rain 0/1 · wanted 0..6 · recover");
        r.text(20, 105, L"> " + command + L"_");
        r.text(20, 155, message, RGB(224, 172, 91));
    }
    else if (!message.empty())
        r.text(25, 140, message, RGB(224, 172, 91));
    auto pixels = static_cast<unsigned char *>(r.pixels);
    for (size_t i = 0; i < size_t(w) * h; i++)
    {
        auto p = pixels + i * 4;
        auto brightness = std::max({p[0], p[1], p[2]});
        p[3] = brightness ? 255 : 0;
    }
    glDisable(GL_DEPTH_TEST);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    UseProgram(r.ui);
    ActiveTexture(TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, r.uiTexture);
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, w, h, 0x80e1, GL_UNSIGNED_BYTE, r.pixels);
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
