using System.Drawing.Imaging;

namespace MakarovPhysics.Renderer;

/// <summary>World type for the procedural fallback map. A caller can instead
/// provide a credited local albedo asset; the same globe lighting and atmosphere
/// are retained. Astronomy maps and provenance are listed in Textures/Astronomy.</summary>
public enum GlobeKind { Earth, Mars, GasGiant, Moon }

/// <summary>A sphere that blocks the globe's sunlight - a moon in front of it.
/// <paramref name="Atmosphere"/> 0..1 reddens its umbra (unused on a globe:
/// nothing with an atmosphere eclipses the Earth; kept for symmetry with the
/// painter's occluders).</summary>
public readonly record struct SphereOccluder(Vec3 Center, float Radius, float Atmosphere = 0);

public sealed class Globe : IDisposable
{
    private readonly TexturedMesh _mesh;
    private readonly Texture _albedo;

    // the 12.08 pass: every Earth in the suite gets an atmosphere and
    // weather for free - both live HERE so no module rolls its own
    private readonly Texture? _cloudTex;
    private readonly Shader? _cloudShader;
    private readonly Shader? _atmoShader;
    private float _cloudPhase;
    private readonly Shader _shader;
    private readonly ColorLineMesh? _graticule;

    public float Radius { get; }
    /// <summary>Opt-in diffuse educational globe; legacy callers retain their material settings.</summary>
    public bool MatteSurface { get; set; }

    /// <summary>Spheres that shadow the globe (at most eight are used), in the
    /// globe's world coordinates - the Moon of EclipseGeometry. Cleared by the
    /// caller; the list is read at each Draw.</summary>
    public List<SphereOccluder> Occluders { get; } = new();

    /// <summary>Angular radius of the directional sun, radians: the real
    /// 0.004652 by default, so the umbra of an occluder drawn to scale has its
    /// true length (total eclipse where it should be).</summary>
    public float LightAngularRadius { get; set; } = 0.004652f;

    /// <summary>Height at which land begins. Tuned against the rendered map
    /// until the water/land split looked like Earth's rather than like a
    /// continent with puddles.</summary>
    private const float SeaLevel = 0.62f;

    /// <summary>How hard the height field is pushed away from its own mean.
    /// Raw fBm hugs 0.5, so any threshold cuts through a smear and the coast
    /// comes out as a wide gradient instead of a shoreline.</summary>
    private const float Contrast = 2.4f;

    public GlobeKind Kind { get; }

    public Globe(float radius, int rings = 96, int segments = 192, int textureSize = 1024, int seed = 20260808, bool graticule = true, GlobeKind kind = GlobeKind.Earth, string? albedoPath = null)
    {
        Radius = radius;
        Kind = kind;
        _mesh = BuildSphere(radius, rings, segments);

        // Texture(Bitmap) clones the source; the temporary map itself holds
        // GDI handles until a finalizer ran - dispose it here (review 09.08)
        using (var map = albedoPath is not null && File.Exists(albedoPath) ? new Bitmap(albedoPath) : BuildMap(kind, textureSize, textureSize / 2, seed))
        {
            _albedo = new Texture(map);
        }

        _shader = new Shader(DefaultShaders.LitTexturedVertex, GlobeFragment);

        if (kind == GlobeKind.Earth)
        {
            using (Bitmap clouds = BuildCloudMap(textureSize, textureSize / 2, seed + 4242))
            {
                _cloudTex = new Texture(clouds);
            }

            _cloudShader = new Shader(CloudVertex, CloudFragment);
            _atmoShader = new Shader(AtmoVertex, AtmoFragment);
        }

        if (graticule)
        {
            _graticule = BuildGraticule(radius);
        }
    }

    /// <summary>Draw the planet at the origin (the model matrix is the caller's
    /// business - a rotating frame just spins it).</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, Mat4 model, Vec3 lightDir, Vec3 lightColor, float ambient)
    {
        _shader.Use();
        _shader.SetMatrix4("uModel", model);
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetVec3("uObjectColor", new Vec3(1f, 1f, 1f));
        _shader.SetVec3("uLightDir", lightDir);
        _lightDir = lightDir;
        _shader.SetVec3("uLightColor", lightColor);
        _shader.SetVec3("uViewPos", eye);
        _shader.SetFloat("uAmbient", ambient);
        _shader.SetFloat("uMatteSurface", MatteSurface ? 1f : 0f);
        _shader.SetInt("uTexture", 0);
        _shader.SetFloat("uLightAngular", LightAngularRadius);
        _shader.SetFloat("uPointLight", 0); // the globe's sun is a direction
        int count = Math.Min(8, Occluders.Count);
        _shader.SetInt("uOccluderCount", count);
        for (int k = 0; k < count; k++)
        {
            var o = Occluders[k];
            _shader.SetVec4($"uOccluder[{k}]", o.Center.X, o.Center.Y, o.Center.Z, o.Radius);
            _shader.SetFloat($"uOccluderAtmo[{k}]", o.Atmosphere);
        }

        _albedo.Bind(0);
        _mesh.Draw();
    }

    // DefaultShaders.LitTexturedFragment with eclipse shadows: the direct term
    // passes through the occluders (a copy of MakarovPhysics.Numerics.Shadowing,
    // pinned in ShadowingTests), the fill does not - the 0.50 floor is the
    // bench shader's and goes with wave W3 of ASTRONOMY-WOW-PLAN-2026-10-19.
    private const string GlobeFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vUv;
uniform sampler2D uTexture;
uniform vec3 uObjectColor;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uMatteSurface;
out vec4 FragColor;
" + "\n" + DefaultShaders.EclipseShadow + "\n" + @"
void main()
{
    vec3 albedo = texture(uTexture, vUv).rgb * uObjectColor;
    vec3 n = normalize(vNormal);
    vec3 l = normalize(uLightDir);
    float diff = max(dot(n, l), 0.0);
    vec3 v = normalize(uViewPos - vWorldPos);
    vec3 h = normalize(l + v);
    float spec = pow(max(dot(n, h), 0.0), 48.0) * 0.25;
    vec2 sh = uOccluderCount > 0 ? shadow(vWorldPos, l) : vec2(1.0, 0.0);
    vec3 direct = vec3(sh.x) * mix(vec3(1.0), vec3(1.0, 0.30, 0.10), sh.y * 0.9);
    // The fill is the module's ambient as given (wave W3): the bench shader's
    // 0.50 floor kept every night side grey, and a planet's night is dark.
    float up = n.y * 0.5 + 0.5;
    float fill = uAmbient * mix(0.80, 1.50, up);
    float ndv = max(dot(n, normalize(uViewPos - vWorldPos)), 0.0);
    float rim = pow(1.0 - ndv, 4.0) * 0.12 * smoothstep(-0.25, 0.3, dot(n, l));
    vec3 lit = albedo * (fill + diff * uLightColor * direct) + uLightColor * (spec * sh.x + rim) * (1.0 - 0.94 * uMatteSurface);
    lit = lit / (1.0 + max(max(lit.r, lit.g), lit.b) * 0.18);
    FragColor = vec4(lit, 1.0);
}";

    /// <summary>The meridians and parallels, drawn after the globe. Without
    /// them a textured sphere still hides its own rotation whenever the
    /// continents happen to be on the far side.</summary>
    public void DrawGraticule(Mat4 view, Mat4 proj) => _graticule?.Draw(view, proj);

    /// <summary>Advance the weather. Clouds drift eastward slowly relative
    /// to the ground; call once per visual frame.</summary>
    public void UpdateClouds(float dt) => _cloudPhase += dt * 0.012f;

    /// <summary>
    /// The cloud deck: a second sphere a shade above the skin, carrying an
    /// alpha map of weather systems and rotating independently of the model
    /// matrix, so the drift is visible against the coastlines. Every number
    /// in the map was chosen by MEASURING the field in Python first:
    /// coverage 51 % (the real planet runs 55-67, but a readable globe wants
    /// its surface), soft-edge fraction 18 %, longitudinal stretch 0.55 for
    /// the banding real weather has. Draw AFTER the globe, BEFORE the
    /// atmosphere.
    /// </summary>
    public void DrawClouds(Mat4 view, Mat4 proj, Vec3 eye, Mat4 model, Vec3 lightDir, float opacity = 0.85f)
    {
        if (_cloudShader is null || _cloudTex is null) return;

        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.DepthMask(false);

        _cloudShader.Use();
        _cloudShader.SetMatrix4("uView", view);
        _cloudShader.SetMatrix4("uProj", proj);
        _cloudShader.SetMatrix4("uModel", model * Mat4.RotateY(_cloudPhase) * Mat4.Scale(1.012f));
        _cloudShader.SetVec3("uLightDir", lightDir);
        _cloudShader.SetFloat("uOpacity", opacity);
        _cloudTex.Bind(0);
        _cloudShader.SetInt("uTexture", 0);
        _mesh.Draw();

        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    /// <summary>
    /// The atmosphere: the same sphere scaled up, FRONT faces culled so only
    /// the far shell survives, additive rim glow strongest at the limb -
    /// grazing normals light up, the disc centre stays clear. Draw LAST.
    /// </summary>
    private Vec3 _lightDir = new Vec3(-.4f, .7f, .6f).Normalized();

    /// <summary>Outer radius of the scattering shell over the globe's radius
    /// (six scale heights of the exaggerated atmosphere).</summary>
    public const float AtmosphereShell = 1.09f;

    /// <summary>The atmosphere as single scattering marched through a thin
    /// shell (wave W3, 20.10): blue limb, a twilight band at the terminator,
    /// nothing on the night side. Lit from the direction of the last Draw.
    /// The shell's NEAR faces are drawn, depth-tested against the globe, so the
    /// haze lies over the disc as well as around it.</summary>
    public void DrawAtmosphere(Mat4 view, Mat4 proj, Vec3 eye, Mat4 model, float intensity = 1.0f)
    {
        if (_atmoShader is null) return;

        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        GL.DepthMask(false);
        GL.CullFace(GL.BACK);

        _atmoShader.Use();
        _atmoShader.SetMatrix4("uView", view);
        _atmoShader.SetMatrix4("uProj", proj);
        _atmoShader.SetMatrix4("uModel", model * Mat4.Scale(AtmosphereShell));
        _atmoShader.SetVec3("uEye", eye);
        _atmoShader.SetVec3("uCenter", model.TransformPoint(Vec3.Zero));
        _atmoShader.SetFloat("uRadius", Radius * model.TransformDirection(new Vec3(1, 0, 0)).Length());
        _atmoShader.SetVec3("uLightDir", _lightDir);
        _atmoShader.SetFloat("uIntensity", intensity);
        _mesh.Draw();

        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    private static TexturedMesh BuildSphere(float radius, int rings, int segments)
    {
        var vertices = new List<float>((rings + 1) * (segments + 1) * 8);
        var indices = new List<uint>(rings * segments * 6);

        for (int r = 0; r <= rings; r++)
        {
            float v = (float)r / rings;
            float phi = v * MathF.PI; // 0 at the north pole
            float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);

            for (int s = 0; s <= segments; s++)
            {
                float u = (float)s / segments;
                float theta = u * 2f * MathF.PI;
                float nx = sinPhi * MathF.Cos(theta);
                float ny = cosPhi;
                float nz = sinPhi * MathF.Sin(theta);

                vertices.Add(nx * radius); vertices.Add(ny * radius); vertices.Add(nz * radius);
                vertices.Add(nx); vertices.Add(ny); vertices.Add(nz);
                vertices.Add(u); vertices.Add(1f - v);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                uint a = (uint)(r * (segments + 1) + s);
                uint b = (uint)(a + segments + 1);

                // counter-clockwise seen from OUTSIDE. Checked rather than
                // guessed: the first ordering I wrote wound the other way, and
                // with back-face culling on that is an invisible planet -
                // exactly the kind of bug that costs a screenshot round trip.
                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(b);
                indices.Add(a + 1);
                indices.Add(b + 1);
                indices.Add(b);
            }
        }

        return new TexturedMesh([.. vertices], [.. indices]);
    }

    private static ColorLineMesh BuildGraticule(float radius)
    {
        List<TrailPoint> pts = [with(4096)];
        float r = radius * 1.002f; // a hair above the skin
        Vec3 faint = new(0.42f, 0.55f, 0.68f);
        Vec3 equator = new(0.70f, 0.80f, 0.92f);

        // parallels every 20 degrees
        for (int latDeg = -80; latDeg <= 80; latDeg += 20)
        {
            float lat = latDeg * MathF.PI / 180f;
            float y = r * MathF.Sin(lat), rr = r * MathF.Cos(lat);
            Vec3 colour = latDeg == 0 ? equator : faint;
            AddCircle(pts, colour, t => new Vec3(rr * MathF.Cos(t), y, rr * MathF.Sin(t)));
        }

        // meridians every 30 degrees
        for (int lonDeg = 0; lonDeg < 360; lonDeg += 30)
        {
            float lon = lonDeg * MathF.PI / 180f;
            float cl = MathF.Cos(lon), sl = MathF.Sin(lon);
            AddCircle(pts, faint, t => new Vec3(r * MathF.Cos(t) * cl, r * MathF.Sin(t), r * MathF.Cos(t) * sl));
        }

        var mesh = new ColorLineMesh(pts.Count)
        {
            Mode = GL.LINES
        };

        mesh.Update(pts);
        return mesh;
    }

    /// <summary>A closed circle as LINE pairs, so one mesh can hold them all
    /// (a strip would join the end of each circle to the start of the next).</summary>
    private static void AddCircle(List<TrailPoint> pts, Vec3 colour, Func<float, Vec3> at)
    {
        const int steps = 96;

        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps * 2f * MathF.PI;
            float t1 = (i + 1) / (float)steps * 2f * MathF.PI;
            pts.Add(new TrailPoint(at(t0), colour));
            pts.Add(new TrailPoint(at(t1), colour));
        }
    }

    private static Bitmap BuildMap(GlobeKind kind, int width, int height, int seed)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        using var bitsLocker = new BitsLocker(bmp, rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        unsafe
        {
            byte* row = (byte*)bitsLocker.Data.Scan0;

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float phi = v * MathF.PI;
                float lat = MathF.PI * 0.5f - phi; // +pi/2 at the north pole
                float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);
                byte* px = row + y * bitsLocker.Data.Stride;

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float theta = u * 2f * MathF.PI;
                    Vec3 dir = new(sinPhi * MathF.Cos(theta), cosPhi, sinPhi * MathF.Sin(theta));

                    (byte r, byte g, byte b) = kind switch
                    {
                        GlobeKind.Mars => SurfaceMars(dir, lat, seed),
                        GlobeKind.GasGiant => SurfaceGiant(dir, lat, seed),
                        GlobeKind.Moon => SurfaceMoon(dir, seed),
                        _ => Surface(dir, lat, seed),
                    };

                    px[x * 4 + 0] = b; // BGRA, as the uploader expects
                    px[x * 4 + 1] = g;
                    px[x * 4 + 2] = r;
                    px[x * 4 + 3] = 255;
                }
            }
        }

        return bmp;
    }

    /// <summary>Mars: rust plains from slow fBm, dark basalt maria under a
    /// 0.42 threshold (21% coverage, sampled numerically 12.08 before this
    /// was written), dust-storm brightening from fast detail, small polar
    /// caps whose edge wanders with the noise.</summary>
    private static (byte r, byte g, byte b) SurfaceMars(Vec3 dir, float lat, int seed)
    {
        float h = Fbm(dir * 1.9f, seed, 5);
        float m = Fbm(dir * 2.6f, seed + 55, 5);
        float detail = Fbm(dir * 13f, seed + 313, 3);
        float absLat = MathF.Abs(lat);

        var bright = ((byte)188, (byte)110, (byte)62);
        var deep = ((byte)124, (byte)58, (byte)34);
        (byte r, byte g, byte b) ground = Mix(deep, bright, h);

        if (m < 0.42f)
        {
            ground = Mix(ground, (92, 52, 40), MathF.Min(1f, (0.42f - m) / 0.10f) * 0.75f);
        }

        ground = Mix(ground, (214, 154, 96), MathF.Max(0f, detail - 0.62f) * 1.4f);
        float cap = 1.24f + 0.10f * (Fbm(dir * 6.5f, seed + 77, 4) - 0.5f);

        return absLat > cap
            ? Mix(ground, (238, 232, 226), MathF.Min(1f, (absLat - cap) / 0.10f))
            : ground;
    }

    /// <summary>Gas giant: latitude bands (nine zero-crossings pole to pole
    /// at this frequency, counted numerically 12.08) whose edges are bent by
    /// slow fBm so they read as flow, not stripes; fast turbulence stirs the
    /// belt interiors; one anticyclonic oval sits at a fixed spot; the poles
    /// darken.</summary>
    private static (byte r, byte g, byte b) SurfaceGiant(Vec3 dir, float lat, int seed)
    {
        float wobble = (Fbm(dir * 3f, seed, 4) - 0.5f) * 2f;
        float band = 0.5f + 0.5f * MathF.Sin(lat * 9f + 1.6f * wobble);
        float turb = Fbm(new Vec3(dir.X * 6f, dir.Y * 2.2f, dir.Z * 6f), seed + 41, 4);

        var zone = ((byte)216, (byte)196, (byte)160); // bright zones
        var belt = ((byte)162, (byte)118, (byte)80);  // darker belts
        (byte r, byte g, byte b) col = Mix(belt, zone, band);
        col = Mix(col, (150, 96, 66), MathF.Max(0f, 0.5f - band) * (0.5f + 0.8f * turb));
        col = Mix(col, (232, 214, 184), MathF.Max(0f, turb - 0.60f) * 0.9f);

        // the oval: an ellipse in (lat, lon) around a fixed anchor
        float lon = MathF.Atan2(dir.Z, dir.X);
        float dLat = (lat + 0.34f) / 0.10f;
        float dLon = WrapPi(lon - 1.15f) / 0.24f;
        float oval = dLat * dLat + dLon * dLon;

        if (oval < 1.6f)
        {
            col = Mix(col, (176, 88, 56), MathF.Min(1f, (1.6f - oval) / 0.9f));
        }

        float absLat = MathF.Abs(lat);
        return Mix(col, (120, 108, 96), MathF.Max(0f, absLat - 1.05f) * 1.1f);
    }

    private static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= 2f * MathF.PI;
        while (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }

    /// <summary>The moon: gray highlands, darker maria under a 0.40
    /// threshold (30% coverage, sampled numerically 12.08), and a fast
    /// speckle that pits the surface like craters do at map scale.</summary>
    private static (byte r, byte g, byte b) SurfaceMoon(Vec3 dir, int seed)
    {
        float h = Fbm(dir * 2.2f, seed, 5);
        float m = Fbm(dir * 1.6f, seed + 99, 4);
        float speck = Fbm(dir * 18f, seed + 7, 3);

        (byte r, byte g, byte b) high = Mix((150, 150, 154), (196, 196, 200), h);
        (byte r, byte g, byte b) col = m < 0.40f
            ? Mix(high, (96, 98, 104), MathF.Min(1f, (0.40f - m) / 0.08f) * 0.85f)
            : high;

        col = Mix(col, (72, 72, 78), MathF.Max(0f, 0.38f - speck) * 1.6f);
        col = Mix(col, (226, 226, 230), MathF.Max(0f, speck - 0.66f) * 1.2f);

        return col;
    }

    /// <summary>
    /// One texel. Every constant here was settled by rendering the whole map
    /// in Python first and looking at it: the first attempt put land over two
    /// thirds of the globe and painted the deserts as three hard horizontal
    /// belts, which read as a beach towel rather than a planet.
    /// </summary>
    private static (byte r, byte g, byte b) Surface(Vec3 dir, float lat, int seed)
    {
        // continents from a slow fBm, coast roughness from a faster one; the
        // contrast term pushes the distribution away from its mean so the
        // shoreline is a decision rather than a smear
        float land = Fbm(dir * 1.7f, seed, 6);
        float coast = Fbm(dir * 6.5f, seed + 77, 4);
        float h = 0.5f + (land - 0.5f) * Contrast + 0.10f * (coast - 0.5f);

        float absLat = MathF.Abs(lat);
        // the cap edge wanders with the same noise that shapes the coast - a
        // clean threshold looks like a circle drawn on the globe
        float ice = 1.30f + 0.16f * (coast - 0.5f);

        if (h < SeaLevel)
        {
            float depth = (SeaLevel - h) / 0.18f;
            var sea = Mix((46, 112, 156), (8, 24, 62), depth);
            return absLat > ice ? Mix(sea, (232, 240, 248), (absLat - ice) / 0.18f) : sea;
        }

        float above = (h - SeaLevel) / 0.24f;
        float detail = Fbm(dir * 13f, seed + 313, 3);

        // biome by latitude, with the boundary jittered by the detail noise so
        // the zones interlock instead of stacking as painted stripes
        float b = absLat + 0.16f * (detail - 0.5f);
        (byte, byte, byte) tropical = (38, 96, 44), desert = (158, 138, 88);
        (byte, byte, byte) temperate = (74, 104, 60), boreal = (62, 86, 64);

        (byte, byte, byte) basecol = b < 0.30f ? tropical
            : b < 0.52f ? Mix(tropical, desert, (b - 0.30f) / 0.22f)
            : b < 0.80f ? Mix(desert, temperate, (b - 0.52f) / 0.28f)
            : Mix(temperate, boreal, (b - 0.80f) / 0.35f);

        (byte r, byte g, byte b) ground = Mix(basecol, (96, 96, 80), detail * 0.45f);
        (byte r, byte g, byte b) withPeaks = Mix(ground, (168, 168, 164), (above - 0.55f) / 0.45f);

        return Mix(withPeaks, (236, 242, 248), (absLat - ice + 0.10f) / 0.20f);
    }

    private static (byte r, byte g, byte b) Mix((byte r, byte g, byte b) a, (byte r, byte g, byte b) b2, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return ((byte)(a.r + (b2.r - a.r) * t),
                (byte)(a.g + (b2.g - a.g) * t),
                (byte)(a.b + (b2.b - a.b) * t));
    }

    /// <summary>
    /// Weather as an alpha map. Same fBm vocabulary as the ground; the
    /// parameters were swept numerically (12.08): scale 2.2 with the theta
    /// axes compressed 0.55x (bands along latitude circles, like real
    /// systems), the map's own Contrast trick at 3.2 to make cloud/no-cloud
    /// a decision, threshold 0.40 and softness 0.24 - measured coverage
    /// 51 %, soft-edge fraction 18 %.
    /// </summary>
    private static Bitmap BuildCloudMap(int width, int height, int seed)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        using var bitsLocker = new BitsLocker(bmp, rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        unsafe
        {
            byte* row = (byte*)bitsLocker.Data.Scan0;

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float phi = v * MathF.PI;
                float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);
                byte* px = row + y * bitsLocker.Data.Stride;

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float theta = u * 2f * MathF.PI;
                    Vec3 dir = new(sinPhi * MathF.Cos(theta), cosPhi, sinPhi * MathF.Sin(theta));

                    float f = Fbm(new Vec3(dir.X * 0.55f, dir.Y, dir.Z * 0.55f) * 2.2f, seed, 5);
                    f = 0.5f + (f - 0.5f) * 3.2f;
                    float a = Math.Clamp((f - 0.40f) / 0.24f, 0f, 1f);

                    px[x * 4 + 0] = 255;
                    px[x * 4 + 1] = 255;
                    px[x * 4 + 2] = 255;
                    px[x * 4 + 3] = (byte)(a * 255f);
                }
            }
        }

        return bmp;
    }

    private const string CloudVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vNormal;
out vec2 vUv;
void main()
{
    vNormal = mat3(uModel) * aNormal;
    vUv = aUv;
    gl_Position = uProj * uView * uModel * vec4(aPos, 1.0);
}";

    private const string CloudFragment = @"#version 330 core
in vec3 vNormal;
in vec2 vUv;
uniform sampler2D uTexture;
uniform vec3 uLightDir;
uniform float uOpacity;
out vec4 FragColor;
void main()
{
    float a = texture(uTexture, vUv).a;
    if (a < 0.01) discard;
    float diff = max(dot(normalize(vNormal), normalize(uLightDir)), 0.0);
    // clouds are bright in daylight and all but vanish on the night side
    // (wave W3: the globe's night is dark now, a grey cloud deck would float)
    float lum = 0.05 + 0.95 * diff;
    FragColor = vec4(vec3(lum), a * uOpacity);
}";

    private const string AtmoVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vNormal;
out vec3 vWorld;
void main()
{
    vNormal = mat3(uModel) * aNormal;
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorld = world.xyz;
    gl_Position = uProj * uView * world;
}";

    // The same single-scattering march as CosmosPainter.AtmosphereFragment,
    // in the globe's normalised space (unit sphere); see the notes there.
    private const string AtmoFragment = @"#version 330 core
in vec3 vNormal;
in vec3 vWorld;
uniform vec3 uEye, uCenter, uLightDir;
uniform float uIntensity, uRadius;
out vec4 FragColor;
vec2 hit(vec3 o,vec3 d,float r){float b=dot(o,d),c=dot(o,o)-r*r,h=b*b-c;if(h<0.)return vec2(-1.);h=sqrt(h);return vec2(-b-h,-b+h);}
void main()
{
    const float H=0.015, TOP=1.+6.*H; const int N=10, M=4;
    vec3 betaR=vec3(.046,.108,.265)*.4/H;
    vec3 betaM=vec3(.012)/H;
    vec3 o=(uEye-uCenter)/uRadius, d=normalize(vWorld-uEye), s=normalize(uLightDir);
    vec2 a=hit(o,d,TOP); if(a.y<=0.) discard;
    float t0=max(a.x,0.), t1=a.y;
    vec2 g=hit(o,d,1.); if(g.x>0.) t1=min(t1,g.x);
    if(t1<=t0) discard;
    float ds=(t1-t0)/float(N), odR=0., odM=0.;
    float mu=dot(d,s);
    float phR=3./(16.*3.14159265)*(1.+mu*mu);
    const float gm=.76; float phM=(1.-gm*gm)/(4.*3.14159265*pow(1.+gm*gm-2.*gm*mu,1.5));
    vec3 sum=vec3(0.);
    for(int i=0;i<N;i++){
        vec3 p=o+d*(t0+(float(i)+.5)*ds);
        float h=length(p)-1., dR=exp(-h/H), dM=exp(-h/(H*.5));
        odR+=dR*ds; odM+=dM*ds;
        float dl=hit(p,s,TOP).y/float(M), sR=0., sM=0.; bool night=false;
        for(int j=0;j<M;j++){ vec3 q=p+s*((float(j)+.5)*dl); float hq=length(q)-1.; if(hq<0.){night=true;break;} sR+=exp(-hq/H)*dl; sM+=exp(-hq/(H*.5))*dl; }
        if(night) continue;
        vec3 tr=exp(-(betaR*(odR+sR)+betaM*(odM+sM)));
        sum+=tr*(betaR*dR*phR+betaM*dM*phM)*ds;
    }
    FragColor = vec4(sum*5.*uIntensity, 1.0);
}";

    /// <summary>Fractal Brownian motion over 3-D value noise, normalised to 0..1.</summary>
    private static float Fbm(Vec3 p, int seed, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(p, seed + i * 1013);
            norm += amp;
            p *= 2.03f;  // not exactly 2: an integer lacunarity lines
            amp *= 0.5f; // the octaves up and prints a visible grid
        }

        return sum / norm;
    }

    private static float ValueNoise(Vec3 p, int seed)
    {
        int xi = (int)MathF.Floor(p.X), yi = (int)MathF.Floor(p.Y), zi = (int)MathF.Floor(p.Z);
        float xf = p.X - xi, yf = p.Y - yi, zf = p.Z - zi;

        // smoothstep in each axis: linear interpolation of a value lattice
        // shows its own grid, and on a planet that reads as tiling
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float w = zf * zf * (3f - 2f * zf);

        float c000 = Hash(xi, yi, zi, seed), c100 = Hash(xi + 1, yi, zi, seed);
        float c010 = Hash(xi, yi + 1, zi, seed), c110 = Hash(xi + 1, yi + 1, zi, seed);
        float c001 = Hash(xi, yi, zi + 1, seed), c101 = Hash(xi + 1, yi, zi + 1, seed);
        float c011 = Hash(xi, yi + 1, zi + 1, seed), c111 = Hash(xi + 1, yi + 1, zi + 1, seed);

        float x00 = c000 + (c100 - c000) * u, x10 = c010 + (c110 - c010) * u;
        float x01 = c001 + (c101 - c001) * u, x11 = c011 + (c111 - c011) * u;
        float y0 = x00 + (x10 - x00) * v, y1 = x01 + (x11 - x01) * v;
        return y0 + (y1 - y0) * w;
    }

    private static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    public void Dispose()
    {
        _mesh.Dispose();
        _albedo.Dispose();
        _shader.Dispose();
        _graticule?.Dispose();
        _cloudTex?.Dispose();
        _cloudShader?.Dispose();
        _atmoShader?.Dispose();
    }
}
