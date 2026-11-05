namespace MakarovPhysics.Renderer;

/// <summary>
/// The ground under a day sky: a heightfield meadow instead of a green
/// slab (26.08 - "flat green to the sky line reads as a billiard table",
/// and the ellipsoid hills that were tried read as green pancakes).
/// Terrain: flat where the experiment stands, rolling fbm hills from
/// InnerRadius outward, a ridge line at the far edge. Shading, in its own
/// shader: grass that varies with a second fbm (fresh green, straw, a
/// darker damp tone), slope darkening, lambert sun with a sky-tinted
/// ambient, drifting cloud shadows from the same drift the SkyDome uses,
/// and distance fog into the sky's horizon colour so the meadow dissolves
/// into the dome rather than ending. Vertex height and normal are baked
/// on the CPU once; the mesh is 2 * Cells^2 triangles. Construction is
/// transactional.
/// </summary>
public sealed class Meadow : IDisposable
{
    private readonly Mesh _mesh;
    private readonly Shader _shader;

    public Vec3 SunDir = new(0.35f, 0.55f, 0.30f);
    public Vec3 HorizonColor = new(0.74f, 0.85f, 0.96f);
    public Vec3 GrassColor = new(0.30f, 0.44f, 0.22f);
    public float CloudAmount = 1f;
    public bool DetailedWeather;
    public uint ShadowTexture;
    public Mat4 ShadowMatrix = Mat4.Identity;

    /// <summary>Fog reaches 63 % at this distance from the camera.</summary>
    public float FogDistance = 110f;

    public const int Cells = 160;
    public const float HalfExtent = 150f;
    public const float InnerRadius = 26f;
    public const float HillHeight = 6.5f;

    public Meadow(int seed = 7)
    {
        Mesh? mesh = null;
        Shader? shader = null;

        try
        {
            mesh = Build(seed);
            shader = new Shader(VertexShader, FragmentShader);
        }
        catch
        {
            mesh?.Dispose();
            shader?.Dispose();
            throw;
        }

        _mesh = mesh;
        _shader = shader;
    }

    /// <summary>Terrain height in world metres at (x, z): zero inside
    /// InnerRadius, fbm hills growing outward, so a bench or a tower keeps
    /// its flat yard and the horizon gets a skyline.</summary>
    public static float Height(float x, float z, int seed = 7)
    {
        float r = MathF.Sqrt(x * x + z * z);
        float rise = SmoothStep(InnerRadius, InnerRadius + 45f, r);
        if (rise <= 0f) return 0f;
        float n = Fbm(x * 0.012f + seed * 3.1f, z * 0.012f - seed * 1.7f, 5);
        float far = SmoothStep(80f, 140f, r);
        return rise * (n * HillHeight + far * 4.5f * Fbm(x * 0.03f, z * 0.03f, 3));
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Hash(float x, float y)
    {
        double v = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453;
        return (float)(v - Math.Floor(v));
    }

    private static float Noise(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y), fx = x - ix, fy = y - iy;
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
        return (a + (b - a) * ux) * (1 - uy) + (c + (d - c) * ux) * uy;
    }

    private static float Fbm(float x, float y, int octaves)
    {
        float v = 0, amp = 0.5f;

        for (int i = 0; i < octaves; i++)
        {
            v += amp * Noise(x, y);
            x *= 2.03f; 
            y *= 1.97f;
            amp *= 0.5f;
        }

        return v;
    }

    private static Mesh Build(int seed)
    {
        int n = Cells + 1;
        var v = new float[n * n * 6];
        var h = new float[n, n];

        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = (i / (float)Cells - 0.5f) * 2 * HalfExtent;
                float z = (j / (float)Cells - 0.5f) * 2 * HalfExtent;
                h[j, i] = Height(x, z, seed);
            }

        float cell = 2 * HalfExtent / Cells;

        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = (i / (float)Cells - 0.5f) * 2 * HalfExtent;
                float z = (j / (float)Cells - 0.5f) * 2 * HalfExtent;
                float hl = h[j, Math.Max(i - 1, 0)], hr = h[j, Math.Min(i + 1, n - 1)];
                float hd = h[Math.Max(j - 1, 0), i], hu = h[Math.Min(j + 1, n - 1), i];
                Vec3 nrm = new Vec3(hl - hr, 2 * cell, hd - hu).Normalized();
                int k = (j * n + i) * 6;
                v[k] = x;
                v[k + 1] = h[j, i];
                v[k + 2] = z;
                v[k + 3] = nrm.X;
                v[k + 4] = nrm.Y;
                v[k + 5] = nrm.Z;
            }

        var idx = new uint[Cells * Cells * 6];
        int w = 0;

        for (int j = 0; j < Cells; j++)
            for (int i = 0; i < Cells; i++)
            {
                uint i0 = (uint)(j * n + i), i1 = i0 + 1, i2 = (uint)((j + 1) * n + i), i3 = i2 + 1;
                idx[w++] = i0; 
                idx[w++] = i2; 
                idx[w++] = i1;
                idx[w++] = i1; 
                idx[w++] = i2; 
                idx[w++] = i3;
            }

        return new Mesh(v, idx);
    }

    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float time)
    {
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetMatrix4("uModel", Mat4.Identity);
        _shader.SetVec3("uCamPos", camPos);
        _shader.SetVec3("uSunDir", SunDir);
        _shader.SetVec3("uHorizonColor", HorizonColor);
        _shader.SetVec3("uGrass", GrassColor);
        _shader.SetFloat("uTime", time);
        _shader.SetInt("uDetailedWeather", DetailedWeather ? 1 : 0);
        _shader.SetFloat("uCloudAmount", Math.Clamp(CloudAmount, 0f, 1f));
        _shader.SetFloat("uFogDistance", FogDistance);
        _shader.SetMatrix4("uLightSpace",ShadowMatrix);
        _shader.SetInt("uHasShadow",DetailedWeather && ShadowTexture!=0 ? 1 : 0);
        _shader.SetInt("uShadowMap",6);
        GL.ActiveTexture(GL.TEXTURE0+6);
        GL.BindTexture(GL.TEXTURE_2D,ShadowTexture);
        _mesh.Draw();
        GL.ActiveTexture(GL.TEXTURE0);
    }

    public void Dispose()
    {
        _mesh.Dispose();
        _shader.Dispose();
    }

    private const string VertexShader = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vWorldPos;
out vec3 vNormal;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    vNormal = mat3(uModel) * aNormal;
    gl_Position = uProj * uView * world;
}";

    private const string FragmentShader = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
out vec4 FragColor;
uniform vec3 uCamPos;
uniform vec3 uSunDir;
uniform vec3 uHorizonColor;
uniform vec3 uGrass;
uniform float uTime;
uniform float uCloudAmount;
uniform int uDetailedWeather,uHasShadow;
uniform mat4 uLightSpace;
uniform sampler2D uShadowMap;
float groundShadow(vec3 world) {
    if(uHasShadow==0)return 1.;
    vec4 clip=uLightSpace*vec4(world,1);
    vec3 p=clip.xyz/clip.w*.5+.5;
    if(p.z>=1.||p.z<=0.||p.x<0.||p.x>1.||p.y<0.||p.y>1.)return 1.;
    vec2 texel=1./vec2(textureSize(uShadowMap,0));float shade=0.;
    for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)
        shade+=p.z-.0018>texture(uShadowMap,p.xy+vec2(x,y)*texel).r?1.:0.;
    return 1.-shade/9.*.55;
}
" + OutdoorAtmosphere.Glsl + @"
uniform float uFogDistance;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    float a = hash(i), b = hash(i + vec2(1, 0)), c = hash(i + vec2(0, 1)), d = hash(i + vec2(1, 1));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}
float fbm(vec2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * noise(p); p *= 2.0; a *= 0.5; }
    return v;
}

void main()
{
    vec3 n = normalize(vNormal);
    vec3 sun = normalize(uSunDir);
    if(uDetailedWeather==1) {
        FragColor=vec4(outdoorGround(vWorldPos,n,uGrass,sun,uCamPos,uTime,groundShadow(vWorldPos)),1);return;
    }

    // grass: three tones blended by two fbm scales - a fresh green, a
    // straw-yellow patchiness, and a darker damp tone in the hollows
    float patchy = fbm(vWorldPos.xz * 0.045);
    float fine = fbm(vWorldPos.xz * 0.9 + 3.7);
    vec3 fresh = uGrass;
    vec3 straw = uGrass * vec3(1.25, 1.12, 0.62) + vec3(0.08, 0.06, 0.0);
    vec3 damp = uGrass * vec3(0.72, 0.80, 0.78);
    vec3 col = mix(fresh, straw, smoothstep(0.45, 0.75, patchy));
    col = mix(col, damp, smoothstep(0.6, 0.85, fine) * 0.35);
    // hillsides pale, ridges wear a dusty edge
    float slope = 1.0 - n.y;
    col = mix(col, vec3(0.46, 0.42, 0.30), smoothstep(0.08, 0.30, slope));
    // fine blade texture so the near ground is not a smooth paint
    col *= 0.92 + 0.16 * noise(vWorldPos.xz * 6.0);

    if(uDetailedWeather==1)col=fieldAlbedo(vWorldPos.xz,uGrass);

    // light: sun lambert + sky-tinted ambient (blue from above, warm bounce below)
    float lambert = max(dot(n, sun), 0.0);
    vec3 ambient = mix(vec3(0.28, 0.30, 0.26), vec3(0.42, 0.50, 0.62), 0.5 + 0.5 * n.y);
    vec3 lit = col * (ambient + vec3(1.0, 0.96, 0.88) * lambert * 0.95 * groundShadow(vWorldPos));

    // cloud shadows: the sky's low deck, projected down and drifting with it
    if (uDetailedWeather==1)lit*=cloudShade(vWorldPos,sun,uTime);
    else if (uCloudAmount > 0.0)
    {
        vec2 uvLo = vWorldPos.xz * 0.010 + vec2(uTime * 0.006, uTime * 0.0021);
        float c = fbm(uvLo);
        float shadow = smoothstep(0.52, 0.95, c) * 0.45 * uCloudAmount;
        lit *= 1.0 - shadow;
    }

    // distance fog into the sky's horizon: the meadow dissolves into the dome
    float dist = length(vWorldPos - uCamPos);
    float fog = 1.0 - exp(-dist / uFogDistance);
    fog = clamp(fog * fog * 1.1, 0.0, 0.94);
    vec3 fogCol = uHorizonColor * 0.95;
    FragColor = vec4(mix(lit, fogCol, fog), 1.0);
}";
}
