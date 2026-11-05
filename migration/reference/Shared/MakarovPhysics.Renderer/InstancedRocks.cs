namespace MakarovPhysics.Renderer;

/// <summary>
/// Thousands of small irregular rocks in ONE draw call (wave W8, 20.10): a
/// low-polygon sphere whose vertices the vertex shader pushes in and out by a
/// per-instance seed, so every rock has its own lumps, lit by a sun direction
/// with a stony, slightly varied albedo. Instanced attributes: centre, radius,
/// seed, tint. Opaque, writes depth, for the solid pass - the asteroids of
/// a belt, debris, ejecta; a body that fills the screen stays a CosmicBody.
/// </summary>
public sealed class InstancedRocks : IDisposable
{
    public const int FloatsPerInstance = 8; // x y z radius seed r g b
    private readonly Shader _shader = new(Vertex, Fragment);
    private uint _vao, _vbo, _ebo, _instances;
    private readonly int _indexCount;
    private float[] _data = new float[FloatsPerInstance * 256];
    private int _count;

    public InstancedRocks()
    {
        var (vertices, indices) = Primitives.SphereData(20, 12);
        _indexCount = indices.Length;
        try
        {
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, vertices, GL.STATIC_DRAW);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 6 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, 6 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            _ebo = GL.GenBuffer();
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, indices, GL.STATIC_DRAW);
            _instances = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _instances);
            GL.BufferData(GL.ARRAY_BUFFER, _data, GL.DYNAMIC_DRAW);
            int stride = FloatsPerInstance * sizeof(float);
            GL.VertexAttribPointer(2, 4, GL.FLOAT, false, stride, 0);              // centre, radius
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);
            GL.VertexAttribPointer(3, 4, GL.FLOAT, false, stride, 4 * sizeof(float)); // seed, tint
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Upload the instances: eight floats each (centre, radius, seed, tint).</summary>
    public void Update(IReadOnlyList<RockInstance> rocks)
    {
        _count = rocks.Count;
        if (_count * FloatsPerInstance > _data.Length)
            _data = new float[Math.Max(_count * FloatsPerInstance, _data.Length * 2)];
        for (int i = 0; i < _count; i++)
        {
            var r = rocks[i];
            int k = i * FloatsPerInstance;
            _data[k] = r.Center.X; _data[k + 1] = r.Center.Y; _data[k + 2] = r.Center.Z; _data[k + 3] = r.Radius;
            _data[k + 4] = r.Seed; _data[k + 5] = r.Tint.X; _data[k + 6] = r.Tint.Y; _data[k + 7] = r.Tint.Z;
        }
        GL.BindBuffer(GL.ARRAY_BUFFER, _instances);
        GL.BufferData(GL.ARRAY_BUFFER, _data, GL.DYNAMIC_DRAW);
    }

    /// <summary>Draw the uploaded rocks, opaque and depth-tested, lit from <paramref name="sunDir"/>.</summary>
    /// <param name="lightPos">A point light (the scene's star); null lights every rock from <paramref name="sunDir"/>.</param>
    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, Vec3 sunDir, Vec3? lightPos = null, float ambient = .06f)
    {
        if (_count == 0) return;
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.CULL_FACE);
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetVec3("uEye", eye);
        _shader.SetVec3("uSun", sunDir.Normalized());
        _shader.SetVec3("uLightPos", lightPos ?? Vec3.Zero);
        _shader.SetFloat("uPointLight", lightPos.HasValue ? 1 : 0);
        _shader.SetFloat("uAmbient", ambient);
        GL.BindVertexArray(_vao);
        GL.DrawElementsInstanced(GL.TRIANGLES, _indexCount, GL.UNSIGNED_INT, 0, _count);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _shader.Dispose();
        if (_instances != 0) { GL.DeleteBuffer(_instances); _instances = 0; }
        if (_ebo != 0) { GL.DeleteBuffer(_ebo); _ebo = 0; }
        if (_vbo != 0) { GL.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
    }

    private const string Vertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aInstance;  // centre, radius
layout(location = 3) in vec4 aLook;      // seed, tint
uniform mat4 uView, uProj;
out vec3 vWorld, vNormal, vTint;
out float vSeed;
float hash(vec3 p) { p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3)); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(vec3 x) {
    vec3 i = floor(x), f = fract(x); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
// radial displacement of a lumpy rock, and its gradient for the normal
float lump(vec3 p, float seed) { return 0.28 * (noise(p * 2.3 + seed * 7.1) - 0.5) + 0.12 * (noise(p * 5.1 + seed * 3.3) - 0.5); }
void main()
{
    float seed = aLook.x;
    vec3 p = normalize(aPos);
    // a per-instance squash so not every rock is a potato of the same shape
    vec3 squash = vec3(0.75 + 0.5 * hash(vec3(seed, 1.0, 2.0)), 0.75 + 0.5 * hash(vec3(seed, 3.0, 4.0)), 1.0);
    float r = 1.0 + lump(p, seed);
    vec3 local = p * r * squash;
    const float e = 0.05;
    float gx = lump(normalize(p + vec3(e, 0, 0)), seed) - lump(normalize(p - vec3(e, 0, 0)), seed);
    float gy = lump(normalize(p + vec3(0, e, 0)), seed) - lump(normalize(p - vec3(0, e, 0)), seed);
    float gz = lump(normalize(p + vec3(0, 0, e)), seed) - lump(normalize(p - vec3(0, 0, e)), seed);
    vec3 n = normalize(p * r - vec3(gx, gy, gz) * 0.5 / e * 0.3);
    vec3 world = aInstance.xyz + local * aInstance.w;
    vWorld = world;
    vNormal = normalize(n / squash);
    vTint = aLook.yzw;
    vSeed = seed;
    gl_Position = uProj * uView * vec4(world, 1.0);
}";

    private const string Fragment = @"#version 330 core
in vec3 vWorld, vNormal, vTint;
in float vSeed;
uniform vec3 uEye, uSun, uLightPos;
uniform float uAmbient, uPointLight;
out vec4 FragColor;
float hash(vec3 p) { p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3)); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
void main()
{
    vec3 n = normalize(vNormal);
    vec3 sun = uPointLight > 0.5 ? normalize(uLightPos - vWorld) : uSun;
    float diff = max(dot(n, sun), 0.0);
    // a stony albedo: the tint, a little darker in the hollows, grainy
    float grain = 0.85 + 0.3 * hash(floor(vWorld * 900.0 + vSeed));
    vec3 albedo = vTint * grain;
    vec3 v = normalize(uEye - vWorld);
    float rim = pow(1.0 - max(dot(n, v), 0.0), 3.0) * 0.08;
    vec3 lit = albedo * (uAmbient + 1.15 * diff) + rim * diff;
    FragColor = vec4(lit, 1.0);
}";
}

/// <summary>One rock of <see cref="InstancedRocks"/>: centre and radius in
/// world units, a seed that picks its shape, its colour.</summary>
public readonly record struct RockInstance(Vec3 Center, float Radius, float Seed, Vec3 Tint);
