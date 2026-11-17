#include "promzona/assets.hpp"
#include "json.hpp"

#include <fstream>
#include <stdexcept>
#include "stb/stb_image.h"
#include <cstring>
#include <format>
#include <memory>

namespace Promzona
{
static std::vector<unsigned char> bytes(const std::filesystem::path &file)
{
    std::ifstream s(file, std::ios::binary | std::ios::ate);
    if (!s)
        throw std::runtime_error("Missing asset: " + file.string());
    auto length = s.tellg();
    if (length < 0 || length > 512 * 1024 * 1024)
        throw std::runtime_error("Asset size out of bounds");
    std::vector<unsigned char> r(static_cast<size_t>(length));
    s.seekg(0);
    if (!s.read(reinterpret_cast<char *>(r.data()), length))
        throw std::runtime_error("Asset read failed");
    return r;
}
template <class T> static T scalar(const std::vector<unsigned char> &data, size_t at)
{
    if (at > data.size() || sizeof(T) > data.size() - at)
        throw std::runtime_error("Truncated binary asset");
    T r;
    std::memcpy(&r, data.data() + at, sizeof r);
    return r;
}
std::string read_text(const std::filesystem::path &file)
{
    auto b = bytes(file);
    return {b.begin(), b.end()};
}
MeshData load_glb(const std::filesystem::path &file)
{
    auto b = bytes(file);
    if (scalar<std::uint32_t>(b, 0) != 0x46546c67 || scalar<std::uint32_t>(b, 4) != 2 ||
        scalar<std::uint32_t>(b, 8) != b.size())
        throw std::runtime_error("Invalid GLB: " + file.string());
    nlohmann::json j;
    std::vector<unsigned char> bin;
    for (size_t at = 12; at < b.size();)
    {
        size_t len = scalar<std::uint32_t>(b, at);
        auto type = scalar<std::uint32_t>(b, at + 4);
        at += 8;
        if (at > b.size() || len > b.size() - at)
            throw std::runtime_error("Invalid GLB chunk");
        if (type == 0x4e4f534a)
            j = nlohmann::json::parse(b.begin() + at, b.begin() + at + len);
        if (type == 0x004e4942)
            bin.assign(b.begin() + at, b.begin() + at + len);
        at += len;
    }
    if (j.is_null() || bin.empty())
        throw std::runtime_error("GLB requires JSON and BIN chunks");
    // Imported PROMZONA assets are already normalized/baked. Reject unsupported transforms.
    for (auto &node : j.value("nodes", nlohmann::json::array()))
        if (node.contains("matrix") || node.contains("translation") || node.contains("rotation") ||
            node.contains("scale"))
            throw std::runtime_error("GLB transforms require baking: " + file.string());
    struct Access
    {
        size_t offset, count, stride, element;
        int type;
    };
    auto access = [&](int id, int components)
    {
        auto &a = j.at("accessors").at(id);
        if (a.contains("sparse"))
            throw std::runtime_error("Sparse GLB accessor unsupported");
        auto &v = j.at("bufferViews").at(a.at("bufferView").get<int>());
        if (v.value("buffer", 0) != 0)
            throw std::runtime_error("External GLB buffer");
        int type = a.at("componentType");
        const auto shape = a.at("type").get<std::string>();
        if (shape != (components == 1 ? "SCALAR" : components == 2 ? "VEC2" : "VEC3"))
            throw std::runtime_error("GLB accessor shape mismatch");
        size_t elem = type == 5126 || type == 5125 ? 4 : type == 5123 ? 2 : type == 5121 ? 1 : 0;
        if (!elem)
            throw std::runtime_error("GLB component type");
        size_t count = a.at("count"), viewOffset = v.value("byteOffset", 0uz),
               viewLength = v.at("byteLength"), offset = a.value("byteOffset", 0uz),
               stride = v.value("byteStride", elem * components);
        if (count > 3000000 || stride < elem * components || stride > 1024 || offset > viewLength ||
            viewOffset > bin.size() || viewLength > bin.size() - viewOffset ||
            (count && ((count - 1) * stride + elem * components > viewLength - offset)))
            throw std::runtime_error("GLB accessor out of bounds");
        return Access{viewOffset + offset, count, stride, elem, type};
    };
    MeshData mesh;
    int material = -2;
    for (auto &m : j.at("meshes"))
        for (auto &p : m.at("primitives"))
        {
            int partMaterial = p.value("material", -1);
            if (material != -2 && material != partMaterial)
                throw std::runtime_error("GLB requires one material per imported part");
            material = partMaterial;
            if (p.value("mode", 4) != 4)
                throw std::runtime_error("GLB requires triangle primitives");
            auto &a = p.at("attributes");
            auto pos = access(a.at("POSITION"), 3);
            if (pos.type != 5126)
                throw std::runtime_error("GLB position must be float");
            auto norm = access(a.at("NORMAL"), 3), uv = access(a.at("TEXCOORD_0"), 2);
            if (norm.type != 5126 || uv.type != 5126 || pos.count != norm.count || pos.count != uv.count)
                throw std::runtime_error("GLB attribute mismatch");
            auto base = static_cast<std::uint32_t>(mesh.vertices.size());
            for (size_t i = 0; i < pos.count; i++)
            {
                Vertex v;
                auto f = [&](Access ac, int k)
                {
                    float x = scalar<float>(bin, ac.offset + i * ac.stride + k * 4);
                    if (!std::isfinite(x))
                        throw std::runtime_error("Non-finite GLB vertex");
                    return x;
                };
                v.position = {f(pos, 0), f(pos, 1), f(pos, 2)};
                v.normal = {f(norm, 0), f(norm, 1), f(norm, 2)};
                v.uv = {f(uv, 0), f(uv, 1)};
                mesh.vertices.push_back(v);
            }
            auto ix = access(p.at("indices"), 1);
            if (ix.type != 5121 && ix.type != 5123 && ix.type != 5125)
                throw std::runtime_error("GLB indices must be unsigned integers");
            if (ix.count % 3)
                throw std::runtime_error("Invalid triangle count");
            for (size_t i = 0; i < ix.count; i++)
            {
                size_t offset = ix.offset + i * ix.stride;
                std::uint32_t index = ix.type == 5125   ? scalar<std::uint32_t>(bin, offset)
                                      : ix.type == 5123 ? scalar<std::uint16_t>(bin, offset)
                                                        : scalar<std::uint8_t>(bin, offset);
                if (index >= pos.count)
                    throw std::runtime_error("GLB index out of bounds");
                mesh.indices.push_back(base + index);
            }
        }
    if (material >= 0)
    {
        auto &mat = j.at("materials").at(material);
        if (mat.contains("pbrMetallicRoughness") &&
            mat.at("pbrMetallicRoughness").contains("baseColorTexture"))
        {
            int texture = mat.at("pbrMetallicRoughness").at("baseColorTexture").at("index");
            int source = j.at("textures").at(texture).at("source");
            auto &image = j.at("images").at(source);
            if (!image.contains("bufferView"))
                throw std::runtime_error("Imported GLB image must be embedded");
            auto &view = j.at("bufferViews").at(image.at("bufferView").get<int>());
            size_t offset = view.value("byteOffset", 0uz), length = view.at("byteLength");
            if (view.value("buffer", 0) != 0 || offset > bin.size() || length > bin.size() - offset)
                throw std::runtime_error("GLB image out of bounds");
            mesh.embedded_color.assign(bin.begin() + offset, bin.begin() + offset + length);
        }
    }
    if (mesh.indices.empty())
        throw std::runtime_error("GLB has no triangles");
    return mesh;
}
SkinData load_skin(const std::filesystem::path &file)
{
    auto b = bytes(file);
    if (scalar<std::uint32_t>(b, 0) != 0x314b5343)
        throw std::runtime_error("Invalid cityskin");
    int vertices = scalar<int>(b, 4), indices = scalar<int>(b, 8), joints = scalar<int>(b, 12),
        poses = scalar<int>(b, 16);
    if (vertices < 1 || vertices > 1000000 || indices < 3 || indices > 3000000 || indices % 3 || joints < 1 ||
        joints > 128 || poses < 1 || poses > 1024)
        throw std::runtime_error("Invalid skin dimensions");
    size_t vb = size_t(vertices) * 64, ib = size_t(indices) * 4, bb = size_t(joints) * poses * 64;
    if (b.size() != 20 + vb + ib + bb)
        throw std::runtime_error("Truncated cityskin");
    SkinData r;
    r.joints = joints;
    r.poses = poses;
    r.mesh.vertices.resize(vertices);
    r.mesh.indices.resize(indices);
    r.bones.resize(bb / 4);
    static_assert(sizeof(Vertex) == 64);
    std::memcpy(r.mesh.vertices.data(), b.data() + 20, vb);
    std::memcpy(r.mesh.indices.data(), b.data() + 20 + vb, ib);
    std::memcpy(r.bones.data(), b.data() + 20 + vb + ib, bb);
    for (auto &v : r.mesh.vertices)
    {
        if (!std::isfinite(v.position.x) || !std::isfinite(v.position.y) || !std::isfinite(v.position.z) ||
            !std::isfinite(v.normal.x) || !std::isfinite(v.normal.y) || !std::isfinite(v.normal.z) ||
            !std::isfinite(v.uv[0]) || !std::isfinite(v.uv[1]))
            throw std::runtime_error("Non-finite skin");
        double sum = 0;
        for (int k = 0; k < 4; k++)
        {
            if (v.joints[k] < 0 || v.joints[k] >= joints || v.joints[k] != std::floor(v.joints[k]) ||
                v.weights[k] < 0 || !std::isfinite(v.weights[k]))
                throw std::runtime_error("Invalid skin influences");
            sum += v.weights[k];
        }
        if (std::abs(sum - 1) > .001)
            throw std::runtime_error("Invalid skin weights");
    }
    for (auto i : r.mesh.indices)
        if (i >= std::uint32_t(vertices))
            throw std::runtime_error("Invalid skin index");
    for (float f : r.bones)
        if (!std::isfinite(f))
            throw std::runtime_error("Invalid bone matrix");
    return r;
}

Image load_image(const std::filesystem::path &file)
{
    try
    {
        return decode_image(bytes(file));
    }
    catch (const std::exception &e)
    {
        throw std::runtime_error(file.string() + ": " + e.what());
    }
}
Image decode_image(std::span<const unsigned char> encoded)
{
    if (encoded.empty() || encoded.size() > 512 * 1024 * 1024)
        throw std::runtime_error("Encoded image size out of bounds");
    int width = 0, height = 0, channels = 0;
    if (!stbi_info_from_memory(encoded.data(), int(encoded.size()), &width, &height, &channels))
        throw std::runtime_error(std::format("Image decoder: {}", stbi_failure_reason()));
    // Checked before decoding, so an oversized picture never allocates its pixels.
    if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
        throw std::runtime_error("Image exceeds texture budget");
    std::unique_ptr<stbi_uc, decltype(&stbi_image_free)> pixels(
        stbi_load_from_memory(encoded.data(), int(encoded.size()), &width, &height, &channels, 4), &stbi_image_free);
    if (!pixels)
        throw std::runtime_error(std::format("Image decoder: {}", stbi_failure_reason()));
    Image im{unsigned(width), unsigned(height), {}};
    im.rgba.assign(pixels.get(), pixels.get() + size_t(width) * size_t(height) * 4);
    return im;
}
} // namespace Promzona
