namespace MakarovPhysics.Renderer;

/// <summary>
/// The SPACE environment (24.08) - the third named backdrop, next to
/// Laboratory (<see cref="LabRoom"/>) and DaySky (<see cref="SkyDome"/>).
///
/// The starfield used to be paint: <see cref="Background"/> style 0, a
/// fullscreen triangle in SCREEN space. Turn the camera and the stars stay
/// glued to the glass - the eye reads wallpaper, exactly the failure the
/// laboratory gradient had before LabRoom. Stars sit at infinity, and
/// infinity has a simple, honest transform: rotation pans it, translation
/// does not move it at all. So this dome shades the DIRECTION of the view
/// ray, like <see cref="SkyDome"/> - a cube around the camera, depth writes
/// off, everything drawn over it.
///
/// The stars come from a triplanar cell hash - three axis-aligned star
/// sheets blended by the direction's components - because a single
/// spherical parametrization pinches at the poles and the pinch shows the
/// moment a module tilts the camera. Per cell the hash decides position,
/// size, brightness and warmth; a slow per-star twinkle keeps the field
/// alive without calling attention to itself. Behind the stars: a faint
/// galactic band and two nebula tints from the same fbm the day sky's
/// clouds use, dialable to zero.
///
/// Adopted through <see cref="EnvironmentStage"/> with
/// <c>EnvironmentKind.Space</c>; the stage forwards <c>StarDensity</c> and
/// <c>NebulaAmount</c>. The carousel may own one directly - it is the shell,
/// not a module, and has its own camera.
/// </summary>
public sealed class SpaceDome : IDisposable
{
    private readonly Mesh _cube;
    private readonly Shader _shader;

    /// <summary>Multiplier on how many cells produce a visible star; 1 is
    /// tuned to read like the old paint, only anchored.</summary>
    public float StarDensity = 1f;

    /// <summary>The galactic band and colour patches, 0..1; zero leaves the
    /// pure starfield for modules that want darkness behind a diagram.</summary>
    public float NebulaAmount = 1f;

    /// <summary>Where Textures/ lives; set by the stage so the dome can read the
    /// Milky Way map (Textures/Astronomy/milkyway-outline.png). Null keeps the
    /// procedural sky of 24.08.</summary>
    public string? TexturesDirectory;

    /// <summary>The Hipparcos stars to V = 6.5 (wave W6) over the hashed dust;
    /// the hashed bright stars step back when the real ones are drawn.</summary>
    public bool CatalogStars = true;

    /// <summary>The IAU constellation figures as faint lines.</summary>
    public bool Constellations;

    /// <summary>Rotation of the catalogue frame (identity: celestial pole up).</summary>
    public Mat4 SkyRotation = Mat4.Identity;

    private CatalogSky? _catalog;
    private Texture? _milkyWay;
    private bool _milkyWayTried;

    public SpaceDome()
    {
        Mesh? cube = null;
        Shader? shader = null;

        try
        {
            cube = Primitives.Cube();
            shader = new Shader(SpaceVertexShader, SpaceFragmentShader);
        }
        catch
        {
            cube?.Dispose();
            shader?.Dispose();
            throw;
        }

        _cube = cube;
        _shader = shader;
    }

    /// <summary>Draw the dome around the camera; state contract identical to
    /// the day sky's.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float time, float farPlane, int viewportHeight = 1080)
    {
        if (!_milkyWayTried && TexturesDirectory != null)
        {
            _milkyWayTried = true;
            string path = Path.Combine(TexturesDirectory, "Astronomy", "milkyway-outline.png");
            if (File.Exists(path))
            {
                using var bitmap = new System.Drawing.Bitmap(path);
                _milkyWay = new Texture(bitmap);
                _milkyWay.SetWrapRepeat();
            }
        }
        bool catalog = CatalogStars && TexturesDirectory != null;

        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Disable(GL.CULL_FACE); // the camera is inside the cube

        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        float scale = farPlane * 1.1f;
        _shader.SetMatrix4("uModel", Mat4.Translate(camPos) * Mat4.Scale(scale, scale, scale));
        _shader.SetMatrix4("uSky", SkyRotation);
        _shader.SetVec3("uCamPos", camPos);
        _shader.SetFloat("uTime", time);
        _shader.SetFloat("uDensity", Math.Clamp(StarDensity, 0f, 3f));
        _shader.SetFloat("uNebula", Math.Clamp(NebulaAmount, 0f, 1f));
        _shader.SetFloat("uCatalog", catalog ? 1 : 0);
        _shader.SetFloat("uHasMilkyWay", _milkyWay != null ? 1 : 0);
        _shader.SetInt("uMilkyWay", 0);
        _milkyWay?.Bind(0);
        _cube.Draw();

        if (catalog)
        {
            _catalog ??= new CatalogSky();
            _catalog.SkyRotation = SkyRotation;
            _catalog.Draw(view, proj, camPos, time, farPlane, viewportHeight, .35f + .65f * Math.Clamp(StarDensity, 0f, 1f));
            if (Constellations)
                _catalog.DrawConstellations(view, proj, camPos, farPlane);
        }

        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    public void Dispose()
    {
        _cube.Dispose();
        _shader.Dispose();
        _catalog?.Dispose();
        _milkyWay?.Dispose();
    }

    private const string SpaceVertexShader = @"#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vWorldPos;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    gl_Position = uProj * uView * world;
}";

    private const string SpaceFragmentShader = @"#version 330 core
in vec3 vWorldPos;
out vec4 FragColor;
uniform vec3 uCamPos;
uniform float uTime;
uniform float uDensity;
uniform float uNebula;
uniform float uCatalog;      // 1: the Hipparcos stars are drawn over this, so the hashed bright ones step back
uniform float uHasMilkyWay;  // 1: the Milky Way map is bound
uniform sampler2D uMilkyWay; // equirectangular RA/Dec brightness of the band (Vieira's outline, softened)
uniform mat4 uSky;

/* The style-0 star paint of Background.cs, transcribed onto a DIRECTION
   (24.08, the gyroscope-sky request): same base gradient, same quiet
   nebula, same two starField layers with the same thresholds, sizes and
   twinkle - only the coordinates changed. The paint fed screen uv into
   these functions; the dome feeds a stable 2D chart of the view direction,
   so the identical picture now pans with the camera like a sky instead of
   sticking to the glass. Cell hashes hate seams, so the chart is
   octahedral - one continuous 2D map of the sphere with no pole pinch. */

float hash21(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    float a = hash21(i), b = hash21(i + vec2(1, 0)), c = hash21(i + vec2(0, 1)), d = hash21(i + vec2(1, 1));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
    return v;
}

float starField(vec2 sp, float scale, float thr, float radius)
{
    vec2 g = sp * scale;
    float px = max(fwidth(g.x), fwidth(g.y));
    vec2 id = floor(g);
    vec2 f = fract(g) - 0.5;
    float h = hash21(id);
    if (h < thr) return 0.0;
    vec2 off = (vec2(hash21(id + 1.7), hash21(id + 9.1)) - 0.5) * 0.7;
    // A star is round on SCREEN, not in the chart: the octahedral chart is
    // anisotropic away from its centre, and a disc in chart space came out
    // as a dash or an oval wherever the chart was stretched (external
    // screenshot audit 14.09: white dashes in ten modules). The offset is
    // measured through the chart's Jacobian, in pixels, and the radius is
    // converted with the geometric mean of the stretch.
    mat2 J = mat2(dFdx(g), dFdy(g));
    float det = abs(J[0][0] * J[1][1] - J[0][1] * J[1][0]);
    vec2 dv = f - off;
    float d, r;
    if (det > 1e-12)
    {
        mat2 Jinv = mat2(J[1][1], -J[0][1], -J[1][0], J[0][0]) / (J[0][0] * J[1][1] - J[0][1] * J[1][0]);
        d = length(Jinv * dv);                       // pixels
        r = radius * (0.5 + 0.8 * hash21(id + 4.3)) / sqrt(det);   // chart radius -> pixels
        r = min(r, 3.0);                             // a star is never a blob
    }
    else
    {
        d = length(dv);
        r = radius * (0.5 + 0.8 * hash21(id + 4.3));
    }
    float aa = det > 1e-12 ? 1.0 : max(px, 1e-4);
    float core = 1.0 - smoothstep(r - aa, r + aa, d);
    float tw = 0.55 + 0.45 * sin(uTime * 1.4 + h * 60.0);
    return core * tw;
}

/* octahedral chart: sphere direction -> continuous 2D, no poles */
vec2 chart(vec3 d)
{
    d /= (abs(d.x) + abs(d.y) + abs(d.z));
    vec2 p = d.xz;
    if (d.y < 0.0)
        p = (1.0 - abs(p.yx)) * vec2(p.x >= 0.0 ? 1.0 : -1.0, p.y >= 0.0 ? 1.0 : -1.0);
    return p;
}

void main()
{
    vec3 dir = normalize(vWorldPos - uCamPos);
    vec2 sp = chart(dir) * 1.6 + 1.6; // the paint's coordinate scale-ish
    vec2 c = chart(dir) * 1.1; // centred coords for nebula/vignette
    float up = dir.y * 0.5 + 0.5;

    // 0: deep-space gradient (verbatim palette, height from the direction)
    vec3 col = mix(vec3(0.02, 0.025, 0.04), vec3(0.045, 0.06, 0.10), up);

    // nebula - soft and quiet (verbatim)
    vec2 q = c * 1.5;
    float n = fbm(q * 1.3 + vec2(uTime * 0.015, uTime * 0.010));
    float neb = smoothstep(0.45, 1.0, n);
    vec3 nebCol = mix(vec3(0.08, 0.05, 0.16), vec3(0.04, 0.12, 0.16), fbm(q * 2.0 + 3.1));
    col += nebCol * neb * 0.13 * uNebula;

    // the Milky Way: the band's brightness read by the direction's RA/Dec in
    // the catalogue frame, a warm grey with a faint dust-lane tint (W6)
    if (uHasMilkyWay > 0.5)
    {
        vec3 s = transpose(mat3(uSky)) * dir;
        float ra = atan(-s.z, s.x), dec = asin(clamp(s.y, -1.0, 1.0));
        float band = texture(uMilkyWay, vec2(ra / 6.2831853 + 0.5, dec / 3.14159265 + 0.5)).r;
        col += mix(vec3(0.55, 0.52, 0.50), vec3(0.70, 0.66, 0.60), band) * band * 0.34 * (0.4 + 0.6 * uNebula);
    }

    // stars: large sparse ones plus fine dust (verbatim thresholds/sizes;
    // the drift stays as the slow twinkle - a direction field must not slide)
    col += vec3(0.90, 0.94, 1.00) * starField(sp, 24.0, 1.0 - 0.12 * uDensity, 0.05)  * (1.0 - 0.85 * uCatalog);
    col += vec3(0.70, 0.80, 1.00) * starField(sp + 31.7, 50.0, 1.0 - 0.07 * uDensity, 0.035) * 0.55;

    FragColor = vec4(col, 1.0);
}";
}
