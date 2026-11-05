namespace MakarovPhysics.Renderer;

/// <summary>
/// A star: a body that SHINES, as opposed to one that is lit.
///
/// Nine astronomy modules drew their sun through the general solid path - a
/// sphere with a yellow albedo under the scene's key light. The result is a
/// matte olive ball with a shaded side, which is the one thing a star cannot
/// have; on the 21.09 screenshots it was the same dull object in Kepler,
/// Roemer, Retrograde, Seasons, StellarParallax, LagrangePoints, CometTails,
/// ExoplanetObservatory and StellarEvolution, while Cepheids and SpectralLines,
/// which had each built a glowing star by hand, looked like a different product.
///
/// Two layers, all self-contained, no PostFxStack required:
///  - the PHOTOSPHERE: an unlit sphere whose brightness depends only on the
///    angle to the viewer - linear limb darkening, I(mu) = 1 - u (1 - mu) with
///    u = 0.6, the textbook value for the Sun in visible light - warmer toward
///    the limb, with a slow granulation so the disc is not a flat sticker;
///  - the CORONA: a soft additive camera-facing quad of 2.6 R. A glass
///    shell produced a hard white ring; a point sprite could hit the driver's
///    maximum point size. The quad has neither limitation.
///
/// <see cref="Draw"/> leaves the GL state as the solid pass expects it: depth
/// test and depth write on, blending off, back-face culling on.
/// </summary>
public sealed class StarBody : IDisposable
{
    /// <summary>Linear limb-darkening coefficient; 0.6 is the Sun near 550 nm.</summary>
    public float LimbDarkening = 0.6f;

    /// <summary>Strength of the additive layers; 0 draws the photosphere alone.</summary>
    public float Glow = 1f;

    /// <summary>Sunspot coverage of the photosphere, 0 (none) to about 1.5;
    /// two belts of penumbra and umbra from a slow field, as the Solar surface
    /// of CosmosPainter draws them.</summary>
    public float Spots = 0f;

    /// <summary>Prominences and the chromospheric rim in the corona pass (W7):
    /// 0 turns them off, 1 is the quiet-Sun look.</summary>
    public float Prominences = 1f;

    private const string Vertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vNormal;
out vec3 vWorld;
out vec3 vLocal;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorld = world.xyz;
    vNormal = mat3(uModel) * aNormal;
    vLocal = aPos;
    gl_Position = uProj * uView * world;
}";

    private const string Fragment = @"#version 330 core
in vec3 vNormal;
in vec3 vWorld;
in vec3 vLocal;
uniform vec3 uViewPos;
uniform vec3 uColor;
uniform float uLimb;
uniform float uTime;
uniform float uSpots;
out vec4 FragColor;

float hash(vec3 p)
{
    p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x)
{
    vec3 i = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x),
                   mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x),
                   mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void main()
{
    vec3 n = normalize(vNormal);
    vec3 v = normalize(uViewPos - vWorld);
    float mu = clamp(dot(n, v), 0.0, 1.0);

    // linear limb darkening, and the limb a little redder than the centre:
    // at the edge the line of sight ends higher, in cooler gas
    float limb = 1.0 - uLimb * (1.0 - mu);
    vec3 tint = mix(vec3(1.0, 0.80, 0.62), vec3(1.0), mu);

    // granulation: two octaves drifting slowly, a few per cent of contrast
    vec3 p = normalize(vLocal);
    float g = 0.60 * noise(p * 110.0 + vec3(0.0, uTime * 0.05, 0.0))
            + 0.28 * noise(p * 231.0 - vec3(uTime * 0.08, 0.0, 0.0))
            + 0.12 * noise(p * 477.0);
    float grain = 0.78 + 0.42 * smoothstep(.2,.8,g);

    // sunspots: two belts either side of the equator, penumbra then umbra
    // from one slow field (the same recipe as the Solar surface of the
    // celestial painter), only when a module asks for them
    float spotField = 0.6 * noise(p * 9.0 + vec3(uTime * 0.01, 0.0, 0.0)) + 0.4 * noise(p * 19.0 + 3.0);
    float belt = exp(-pow((abs(p.y) - 0.25) / 0.23, 2.0));
    float penumbra = smoothstep(0.52, 0.58, spotField), umbra = smoothstep(0.58, 0.63, spotField);
    float spots = 1.0 - clamp(uSpots, 0.0, 1.5) * belt * (0.40 * penumbra + 0.38 * umbra);

    FragColor = vec4(uColor * tint * (limb * grain * max(0.10, spots)), 1.0);
}";

    private const string CoronaVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uCenter;
uniform float uRadius;
out vec2 vDisc;
void main()
{
    vec4 eye = uView * vec4(uCenter, 1.0);
    eye.xy += aPos.xy * uRadius * 2.6;
    vDisc = aPos.xy * 2.6;
    gl_Position = uProj * eye;
}";

    private const string CoronaFragment = @"#version 330 core
in vec2 vDisc;
uniform vec3 uColor;
uniform float uTime;
uniform float uProminences;
out vec4 FragColor;
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise1(float x) { float i = floor(x), f = fract(x); f = f * f * (3.0 - 2.0 * f); return mix(hash21(vec2(i, 1.7)), hash21(vec2(i + 1.0, 1.7)), f); }
void main()
{
    float r = length(vDisc);
    if (r >= 2.6) discard;
    float d = max(0.0, r - 1.0);
    float angle = atan(vDisc.y, vDisc.x);
    // the corona: the old two-exponential halo, now with faint radial
    // streamers that drift slowly, so the halo is not a flat disc (W7)
    float streamers = 0.80 + 0.20 * (noise1(angle * 5.0 + uTime * 0.05) * 0.6 + noise1(angle * 13.0 - uTime * 0.03) * 0.4);
    float glow = (0.15 * exp(-7.0 * d) + 0.08 * exp(-1.8 * d)) * streamers
               * (1.0 - smoothstep(2.1, 2.6, r));
    vec3 c = uColor * glow;
    // the chromosphere and prominences: a thin pink-red rim just outside the
    // limb, with arches where a slow angular field peaks; they rise and fall
    float rim = exp(-pow((r - 1.015) / 0.012, 2.0));
    float arch = pow(noise1(angle * 9.0 + 11.0 + uTime * 0.02), 6.0) * (0.5 + 0.5 * sin(uTime * 0.3 + angle * 4.0));
    float reach = 1.0 - smoothstep(1.0, 1.0 + 0.10 * (0.3 + arch), r);
    float prom = (rim * 0.35 + arch * reach * 1.3) * step(1.0, r) * uProminences;
    c += vec3(1.0, 0.34, 0.20) * prom * length(uColor) * 0.6;
    FragColor = vec4(c, 1.0);
}";

    private const string GlareVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
uniform vec3 uSunNdc;
out vec2 vNdc;
void main()
{
    vNdc = aPos.xy;
    // a full-screen quad at the star's own depth, so a planet in front hides
    // the glare where it stands
    gl_Position = vec4(aPos.xy, uSunNdc.z, 1.0);
}";

    private const string GlareFragment = @"#version 330 core
in vec2 vNdc;
uniform vec3 uSunNdc;
uniform vec3 uColor;
uniform float uAspect, uStrength;
out vec4 FragColor;
float disc(vec2 p, vec2 c, float r) { float d = length((p - c) * vec2(uAspect, 1.0)) / r; return exp(-d * d * 2.5) * (1.0 - smoothstep(0.7, 1.0, d)); }
float ring(vec2 p, vec2 c, float r) { float d = length((p - c) * vec2(uAspect, 1.0)); return exp(-pow((d - r) / (0.12 * r), 2.0)); }
void main()
{
    vec2 sun = uSunNdc.xy;
    float off = length(sun * vec2(uAspect, 1.0));
    // the flare lives while the star is on or near the screen
    float on = 1.0 - smoothstep(1.2, 1.6, off);
    vec2 toCentre = -sun;
    vec3 c = vec3(0.0);
    // ghosts along the line through the screen centre, tinted by their order
    c += vec3(0.9, 0.5, 0.3) * disc(vNdc, sun + toCentre * 0.35, 0.05) * 0.45;
    c += vec3(0.3, 0.6, 0.9) * disc(vNdc, sun + toCentre * 0.62, 0.09) * 0.30;
    c += vec3(0.5, 0.9, 0.5) * disc(vNdc, sun + toCentre * 1.05, 0.04) * 0.35;
    c += vec3(0.9, 0.7, 0.4) * disc(vNdc, sun + toCentre * 1.45, 0.13) * 0.22;
    c += vec3(0.6, 0.4, 0.8) * ring(vNdc, sun + toCentre * 1.8, 0.22) * 0.10;
    // an anamorphic streak across the star, bluish as a coated lens gives it
    vec2 d = (vNdc - sun) * vec2(uAspect, 1.0);
    float streak = exp(-pow(d.y / 0.012, 2.0)) * exp(-abs(d.x) * 3.2);
    c += vec3(0.45, 0.65, 1.0) * streak * 0.55;
    // a soft veil around the star itself, beyond the corona
    c += uColor * exp(-length(d) * 4.5) * 0.10;
    FragColor = vec4(c * uColor * uStrength * on, 1.0);
}";

    private readonly Shader _surface = new(Vertex, Fragment);
    private readonly Shader _corona = new(CoronaVertex, CoronaFragment);
    private readonly Shader _glare = new(GlareVertex, GlareFragment);
    private readonly Mesh _sphere = Primitives.UvSphere(32, 64);
    private readonly Mesh _quad = new(
        [-1, -1, 0, 0, 0, 1, 1, -1, 0, 0, 0, 1,
          1,  1, 0, 0, 0, 1, -1, 1, 0, 0, 0, 1], [0, 1, 2, 0, 2, 3]);

    /// <summary>
    /// The star at <paramref name="center"/>. <paramref name="color"/> is the
    /// colour of the disc's CENTRE; values above 1 are fine and are how a
    /// module says "brighter". The corona is sized in world units;
    /// viewportHeight remains in the signature for existing callers.
    /// </summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, Vec3 center, float radius, Vec3 color,
                     float time, int viewportHeight)
    {
        if (!(radius > 0f)) return;

        // 1. the photosphere - opaque, writes depth, hides what is behind it
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.CULL_FACE);
        _surface.Use();
        _surface.SetMatrix4("uView", view);
        _surface.SetMatrix4("uProj", proj);
        _surface.SetMatrix4("uModel", Mat4.Translate(center) * Mat4.Scale(radius));
        _surface.SetVec3("uViewPos", eye);
        _surface.SetVec3("uColor", color);
        _surface.SetFloat("uLimb", LimbDarkening);
        _surface.SetFloat("uTime", time);
        _surface.SetFloat("uSpots", Spots);
        _sphere.Draw();

        DrawCorona(view, proj, center, radius, color, time);
    }

    /// <summary>Draw after every opaque body so foreground objects hide the halo.
    /// <paramref name="time"/> drives the streamers and prominences.</summary>
    public void DrawCorona(Mat4 view, Mat4 proj, Vec3 center, float radius, Vec3 color, float time = 0)
    {
        if (!(Glow > 0f) || !(radius > 0f)) return;
        GL.Enable(GL.DEPTH_TEST);
        // The corona lies in the centre plane, behind the opaque photosphere.
        // Depth testing hides it behind the disc and any foreground planet.
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        GL.DepthMask(false);

        _corona.Use();
        _corona.SetMatrix4("uView", view);
        _corona.SetMatrix4("uProj", proj);
        _corona.SetVec3("uCenter", center);
        _corona.SetFloat("uRadius", radius);
        _corona.SetVec3("uColor", color * Glow);
        _corona.SetFloat("uTime", time);
        _corona.SetFloat("uProminences", Prominences);
        _quad.Draw();

        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    /// <summary>
    /// The lens flare of a bright star (W7): ghosts along the line through the
    /// screen centre, an anamorphic streak and a soft veil, drawn additively on
    /// a full-screen quad placed at the star's own depth - so a body in front
    /// of the star hides the glare where it stands, and nothing is drawn when
    /// the star is behind the camera or far off screen. Call after the corona.
    /// <paramref name="strength"/> 0.3 is a touch, 1 is a camera pointed at the Sun.
    /// </summary>
    public void DrawGlare(Mat4 view, Mat4 proj, Vec3 center, Vec3 color, float aspect, float strength = .3f)
    {
        if (!(strength > 0f)) return;
        var pv = proj * view;
        var m = pv.M;
        float w = m[3] * center.X + m[7] * center.Y + m[11] * center.Z + m[15];
        if (w <= 1e-5f) return;
        var p = pv.TransformPoint(center);
        float ndcX = p.X / w, ndcY = p.Y / w, ndcZ = p.Z / w;
        if (MathF.Abs(ndcX) > 1.8f || MathF.Abs(ndcY) > 1.8f) return;

        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(false);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        GL.Disable(GL.CULL_FACE);
        _glare.Use();
        _glare.SetVec3("uSunNdc", ndcX, ndcY, ndcZ);
        _glare.SetVec3("uColor", color);
        _glare.SetFloat("uAspect", aspect);
        _glare.SetFloat("uStrength", strength);
        _quad.Draw();
        GL.Enable(GL.CULL_FACE);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    /// <summary>
    /// Colour of a black body of the given temperature, for the disc's centre:
    /// red dwarfs orange, the Sun near white with a warm cast, hot stars blue
    /// white. A fit for display, normalised so that the largest channel is 1.
    /// </summary>
    public static Vec3 ColorOfTemperature(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000.0, 40000.0) / 100.0;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661
                           : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        r = Math.Clamp(r, 0, 255); g = Math.Clamp(g, 0, 255); b = Math.Clamp(b, 0, 255);
        double top = Math.Max(r, Math.Max(g, b));
        return new Vec3((float)(r / top), (float)(g / top), (float)(b / top));
    }

    public void Dispose()
    {
        _surface.Dispose();
        _sphere.Dispose();
        _corona.Dispose();
        _glare.Dispose();
        _quad.Dispose();
    }
}
