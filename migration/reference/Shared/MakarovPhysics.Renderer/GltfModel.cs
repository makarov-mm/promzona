using System.Text;
using System.Text.Json;

namespace MakarovPhysics.Renderer;

/// <summary>
/// A minimal binary glTF (.glb) reader: enough to bring a single static, singly
/// textured model into the scene. It reads the JSON chunk, walks the scene graph
/// so node transforms are baked into the vertices, gathers POSITION, NORMAL and
/// TEXCOORD_0 through the accessor indirection, and decodes the embedded PNG.
///
/// Deliberately NOT supported: animation, skinning, sparse accessors, Draco or
/// any other compression, morph targets, cameras, lights, external .gltf + .bin
/// pairs. The loader throws on anything it does not understand rather than
/// silently drawing something wrong.
/// </summary>
public sealed class GltfModel : IDisposable
{
    public TexturedMesh Mesh { get; }

    /// <summary>Base colour map, or null when the material carries none.</summary>
    public Texture? BaseColor { get; }

    /// <summary>Bounds of the baked geometry, so callers can fit it to a size.</summary>
    public Vec3 Min { get; }
    public Vec3 Max { get; }
    public Vec3 Centre => (Min + Max) * 0.5f;

    /// <summary>Largest span across the three axes.</summary>
    public float Extent
    {
        get
        {
            Vec3 d = Max - Min;
            return MathF.Max(d.X, MathF.Max(d.Y, d.Z));
        }
    }

    private GltfModel(TexturedMesh mesh, Texture? baseColor, Vec3 min, Vec3 max)
    {
        Mesh = mesh;
        BaseColor = baseColor;
        Min = min;
        Max = max;
    }

    public static GltfModel Load(string path)
    {
        byte[] blob = File.ReadAllBytes(path);
        (string json, byte[] bin) = SplitGlb(blob);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        List<float> vertices = [with(8192)];
        List<uint> indices = [with(8192)];
        var materialIndices = new HashSet<int>();
        Vec3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vec3 max = new(float.MinValue, float.MinValue, float.MinValue);

        foreach ((int meshIndex, Mat4 world) in EnumerateMeshNodes(root))
        {
            JsonElement mesh = Element(root, "meshes")[meshIndex];

            foreach (JsonElement prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                // 4 is TRIANGLES and is also the default when mode is absent
                if (prim.TryGetProperty("mode", out JsonElement mode) && mode.GetInt32() != 4)
                {
                    throw new NotSupportedException($"glTF primitive mode {mode.GetInt32()} is not supported.");
                }

                if (prim.TryGetProperty("material", out JsonElement mat))
                {
                    materialIndices.Add(mat.GetInt32());
                }

                AppendPrimitive(root, bin, prim, world, vertices, indices, ref min, ref max);
            }
        }

        if (indices.Count == 0) throw new InvalidDataException("The glTF file contains no triangles.");

        // a single-textured loader must actually BE single-material: taking
        // materials[0] while primitives point elsewhere would texture the
        // model with someone else's map
        if (materialIndices.Count > 1)
        {
            throw new NotSupportedException("Multi-material models are not supported.");
        }

        int materialIndex = materialIndices.Count == 1 ? materialIndices.First() : 0;
        Texture? tex = null;
        TexturedMesh? built = null;

        try
        {
            tex = LoadBaseColor(root, bin, materialIndex);
            built = new TexturedMesh([.. vertices], [.. indices]);
        }
        catch
        {
            tex?.Dispose();
            built?.Dispose();
            throw;
        }

        return new GltfModel(built, tex, min, max);
    }

    /// <summary>Container: a 12 byte header, then length-prefixed JSON and BIN chunks.</summary>
    private static (string json, byte[] bin) SplitGlb(byte[] blob)
    {
        if (blob.Length < 20) 
            throw new InvalidDataException("File is too short to be a .glb.");

        uint magic = BitConverter.ToUInt32(blob, 0);
        if (magic != 0x46546C67) 
            throw new InvalidDataException("Not a binary glTF (bad magic).");

        uint version = BitConverter.ToUInt32(blob, 4);
        if (version != 2) 
            throw new NotSupportedException($"glTF container version {version} is not supported.");

        uint declared = BitConverter.ToUInt32(blob, 8);
        if (declared != blob.Length)
            throw new InvalidDataException($"The .glb declares {declared} bytes but the file has {blob.Length}.");

        string? json = null;
        byte[] bin = [];
        int offset = 12;

        while (offset + 8 <= blob.Length)
        {
            int length = (int)BitConverter.ToUInt32(blob, offset);
            uint type = BitConverter.ToUInt32(blob, offset + 4);
            int start = offset + 8;

            if (length < 0 || start + length > blob.Length)
                throw new InvalidDataException("Chunk runs past the end of the file.");

            if (type == 0x4E4F534A) 
                json = Encoding.UTF8.GetString(blob, start, length); // JSON
            else if (type == 0x004E4942) 
                bin = blob[start..(start + length)]; // BIN

            offset = start + length;
            if ((offset & 3) != 0) offset += 4 - (offset & 3); // 4 byte padding
        }

        if (json is null)
        {
            throw new InvalidDataException("The .glb has no JSON chunk.");
        }

        return (json, bin);
    }

    /// <summary>Every mesh reachable from the default scene, with its world transform.</summary>
    private static IEnumerable<(int mesh, Mat4 world)> EnumerateMeshNodes(JsonElement root)
    {
        JsonElement nodes = Element(root, "nodes");
        var results = new List<(int, Mat4)>();

        int sceneIndex = root.TryGetProperty("scene", out JsonElement s) ? s.GetInt32() : 0;
        JsonElement scenes = Element(root, "scenes");
        JsonElement roots = scenes[sceneIndex].GetProperty("nodes");

        var visiting = new HashSet<int>();

        foreach (JsonElement r in roots.EnumerateArray())
        {
            Walk(r.GetInt32(), Mat4.Identity);
        }

        return results;

        void Walk(int index, Mat4 parent)
        {
            // a malformed cyclic graph would recurse forever; the loader
            // promises to reject what it does not understand, and a cycle
            // is squarely in that category
            if (!visiting.Add(index))
            {
                throw new InvalidDataException($"The scene graph contains a cycle through node {index}.");
            }

            JsonElement node = nodes[index];
            Mat4 world = parent * LocalTransform(node);

            if (node.TryGetProperty("mesh", out JsonElement m))
                results.Add((m.GetInt32(), world));

            if (node.TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray())
                    Walk(k.GetInt32(), world);

            visiting.Remove(index);
        }
    }

    /// <summary>Either an explicit matrix or the translation, rotation, scale triple.</summary>
    private static Mat4 LocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement m))
        {
            // glTF stores matrices column major, which is also how Mat4 indexes
            // its backing store, so column c row r lands at 4c + r
            float[] v = ReadFloats(m, 16);
            Mat4 result = Mat4.Identity;

            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    result[r, c] = v[4 * c + r];

            return result;
        }

        Mat4 t = node.TryGetProperty("translation", out JsonElement tr)
            ? Mat4.Translate(Vector(ReadFloats(tr, 3)))
            : Mat4.Identity;

        Mat4 r2 = node.TryGetProperty("rotation", out JsonElement rot)
            ? FromQuaternion(ReadFloats(rot, 4))
            : Mat4.Identity;

        Mat4 s = Mat4.Identity;
        if (node.TryGetProperty("scale", out JsonElement sc))
        {
            float[] k = ReadFloats(sc, 3);

            // the loader transforms normals with the model matrix itself; a
            // non-uniform scale needs the inverse transpose, and drawing with
            // silently skewed lighting contradicts the throw-not-guess policy
            if (MathF.Abs(k[0] - k[1]) > 1e-5f * MathF.Abs(k[0]) ||
                MathF.Abs(k[0] - k[2]) > 1e-5f * MathF.Abs(k[0]))
            {
                throw new NotSupportedException("Non-uniform node scale is not supported (normals would be wrong).");
            }

            s = Mat4.Scale(Vector(k));
        }

        return t * r2 * s;
    }

    /// <summary>glTF quaternions are stored x, y, z, w.</summary>
    private static Mat4 FromQuaternion(float[] q)
    {
        float x = q[0], y = q[1], z = q[2], w = q[3];
        Mat4 m = Mat4.Identity;
        m[0, 0] = 1 - 2 * (y * y + z * z);
        m[0, 1] = 2 * (x * y - w * z);
        m[0, 2] = 2 * (x * z + w * y);
        m[1, 0] = 2 * (x * y + w * z);
        m[1, 1] = 1 - 2 * (x * x + z * z);
        m[1, 2] = 2 * (y * z - w * x);
        m[2, 0] = 2 * (x * z - w * y);
        m[2, 1] = 2 * (y * z + w * x);
        m[2, 2] = 1 - 2 * (x * x + y * y);
        return m;
    }

    private static void AppendPrimitive(JsonElement root, byte[] bin, JsonElement prim, Mat4 world, List<float> vertices, List<uint> indices, ref Vec3 min, ref Vec3 max)
    {
        JsonElement attrs = prim.GetProperty("attributes");

        if (!attrs.TryGetProperty("POSITION", out JsonElement posAcc))
            throw new InvalidDataException("A primitive has no POSITION attribute.");

        float[] pos = ReadAccessor(root, bin, posAcc.GetInt32(), 3);
        int count = pos.Length / 3;

        float[]? nrm = attrs.TryGetProperty("NORMAL", out JsonElement na)
            ? ReadAccessor(root, bin, na.GetInt32(), 3) : null;
        float[]? uv = attrs.TryGetProperty("TEXCOORD_0", out JsonElement ta)
            ? ReadAccessor(root, bin, ta.GetInt32(), 2) : null;

        if (nrm is not null && nrm.Length != count * 3)
            throw new InvalidDataException("NORMAL accessor count does not match POSITION.");

        if (uv is not null && uv.Length != count * 2)
            throw new InvalidDataException("TEXCOORD_0 accessor count does not match POSITION.");

        // normals need the inverse transpose, but the models this loader targets
        // carry rigid transforms with uniform scale, where the rotation part is
        // already orthogonal; a non-uniform scale would need the full inverse
        uint baseVertex = (uint)(vertices.Count / 8);

        for (int i = 0; i < count; i++)
        {
            Vec3 p = TransformPoint(world, new Vec3(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]));
            Vec3 n = nrm is null
                ? Vec3.UnitY
                : TransformDirection(world, new Vec3(nrm[3 * i], nrm[3 * i + 1], nrm[3 * i + 2])).Normalized();

            min = new Vec3(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
            max = new Vec3(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));

            vertices.Add(p.X);
            vertices.Add(p.Y);
            vertices.Add(p.Z);

            vertices.Add(n.X);
            vertices.Add(n.Y);
            vertices.Add(n.Z);

            // glTF puts the texture origin at the TOP-left (v = 0 is the top
            // row), while Texture uploads bitmaps flipped so v = 0 is the
            // bottom row, GL style. Convert at load time. Models that rely on
            // wrapping outside [0, 1] would need the flip in the sampler
            // instead, but this loader targets simple single-textured props.
            vertices.Add(uv is null ? 0f : uv[2 * i]);
            vertices.Add(uv is null ? 0f : 1f - uv[2 * i + 1]);
        }

        if (prim.TryGetProperty("indices", out JsonElement ia))
        {
            foreach (uint v in ReadIndices(root, bin, ia.GetInt32()))
            {
                if (v >= (uint)count)
                {
                    throw new InvalidDataException($"Index {v} is outside the primitive's {count} vertices.");
                }

                indices.Add(baseVertex + v);
            }
        }
        else
        {
            for (uint i = 0; i < count; i++)
            {
                indices.Add(baseVertex + i);
            }
        }
    }

    private static Vec3 TransformPoint(Mat4 m, Vec3 v) => new(
        m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z + m[0, 3],
        m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z + m[1, 3],
        m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z + m[2, 3]);

    private static Vec3 TransformDirection(Mat4 m, Vec3 v) => new(
        m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z,
        m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z,
        m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z);

    private static float[] ReadAccessor(JsonElement root, byte[] bin, int index, int components)
    {
        JsonElement acc = Element(root, "accessors")[index];
        if (acc.TryGetProperty("sparse", out _))
            throw new NotSupportedException("Sparse accessors are not supported.");

        int count = acc.GetProperty("count").GetInt32();
        int componentType = acc.GetProperty("componentType").GetInt32();
        string type = acc.GetProperty("type").GetString() ?? "";
        int declared = ComponentCount(type);

        if (declared != components)
            throw new InvalidDataException($"Accessor {index} is {type}, expected {components} components.");

        if (componentType != 5126)
            throw new NotSupportedException($"Vertex data must be float; accessor {index} is {componentType}.");

        var span = ViewSpan(root, bin, acc, out int stride);
        int tight = components * sizeof(float);
        if (stride == 0) stride = tight;

        var result = new float[count * components];

        for (int i = 0; i < count; i++)
            for (int c = 0; c < components; c++)
                result[i * components + c] = BitConverter.ToSingle(span.Slice(i * stride + c * sizeof(float), 4));

        return result;
    }

    private static uint[] ReadIndices(JsonElement root, byte[] bin, int index)
    {
        JsonElement acc = Element(root, "accessors")[index];
        int count = acc.GetProperty("count").GetInt32();
        int componentType = acc.GetProperty("componentType").GetInt32();
        ReadOnlySpan<byte> span = ViewSpan(root, bin, acc, out int stride);

        int size = componentType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            _ => throw new NotSupportedException($"Index component type {componentType} is not supported."),
        };

        if (stride == 0) stride = size;
        var result = new uint[count];

        for (int i = 0; i < count; i++)
        {
            int at = i * stride;

            result[i] = size switch
            {
                1 => span[at],
                2 => BitConverter.ToUInt16(span.Slice(at, 2)),
                _ => BitConverter.ToUInt32(span.Slice(at, 4)),
            };
        }

        return result;
    }

    /// <summary>The bytes an accessor addresses, with the view's stride if it declares one.</summary>
    private static ReadOnlySpan<byte> ViewSpan(JsonElement root, byte[] bin, JsonElement acc, out int stride)
    {
        stride = 0;
        if (!acc.TryGetProperty("bufferView", out JsonElement bv))
            throw new NotSupportedException("Accessors without a bufferView are not supported.");

        JsonElement view = Element(root, "bufferViews")[bv.GetInt32()];
        int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
        int viewLength = view.GetProperty("byteLength").GetInt32();
        if (view.TryGetProperty("byteStride", out JsonElement bs)) stride = bs.GetInt32();

        int accOffset = acc.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
        int start = viewOffset + accOffset;
        int length = viewLength - accOffset;

        if (start < 0 || length < 0 || start + length > bin.Length)
            throw new InvalidDataException("A bufferView points outside the BIN chunk.");

        return new ReadOnlySpan<byte>(bin, start, length);
    }

    private static Texture? LoadBaseColor(JsonElement root, byte[] bin, int materialIndex)
    {
        if (!root.TryGetProperty("materials", out JsonElement materials) || materials.GetArrayLength() == 0)
            return null;

        if (materialIndex < 0 || materialIndex >= materials.GetArrayLength())
            throw new InvalidDataException($"Primitive references material {materialIndex} of {materials.GetArrayLength()}.");

        JsonElement material = materials[materialIndex];

        if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr)
            ||
            !pbr.TryGetProperty("baseColorTexture", out JsonElement bct))
        {
            return null;
        }

        JsonElement texture = Element(root, "textures")[bct.GetProperty("index").GetInt32()];
        if (!texture.TryGetProperty("source", out JsonElement src)) return null;

        JsonElement image = Element(root, "images")[src.GetInt32()];
        if (!image.TryGetProperty("bufferView", out JsonElement bv))
            throw new NotSupportedException("Only images embedded in the BIN chunk are supported.");

        JsonElement view = Element(root, "bufferViews")[bv.GetInt32()];
        int offset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
        int length = view.GetProperty("byteLength").GetInt32();

        if (offset < 0 || length <= 0 || offset + (long)length > bin.Length)
            throw new InvalidDataException("The embedded image bufferView points outside the BIN chunk.");

        // System.Drawing decodes the PNG: this is a Windows Forms application,
        // so there is no reason to hand-roll an inflate here.
        using var stream = new MemoryStream(bin, offset, length, writable: false);
        using var bitmap = new Bitmap(stream);
        return new Texture(bitmap);
    }

    private static JsonElement Element(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement e)
        ? e
        : throw new InvalidDataException($"The glTF file has no \"{name}\" array.");

    private static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => throw new NotSupportedException($"Accessor type {type} is not supported."),
    };

    private static float[] ReadFloats(JsonElement array, int expected)
    {
        if (array.GetArrayLength() != expected)
        {
            throw new InvalidDataException($"Expected {expected} numbers, found {array.GetArrayLength()}.");
        }

        var result = new float[expected];
        int i = 0;

        foreach (JsonElement v in array.EnumerateArray())
        {
            result[i++] = (float)v.GetDouble();
        }

        return result;
    }

    private static Vec3 Vector(float[] v) => new(v[0], v[1], v[2]);

    public void Dispose()
    {
        Mesh.Dispose();
        BaseColor?.Dispose();
    }
}
