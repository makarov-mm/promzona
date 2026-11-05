namespace MakarovPhysics.Renderer;

/// <summary>
/// Procedural day sky for the sandbox (19.07 visual upgrade): a big cube
/// centered on the camera whose fragment shader computes the color from the
/// A real sky, not a blue quad: a fullscreen pass that shades the per-pixel
/// view direction - one sun disc with a warm glow, a smooth
/// horizon-to-zenith gradient and slowly drifting fbm clouds. Drawn first
/// with depth writes off, so the scene renders over it. Construction is
/// Transactional.
/// </summary>
public sealed class SkyDome : IDisposable
{
    private readonly Mesh _cube;
    private readonly Shader _shader;
    public Vec3 SunDir = new(0.35f, 0.55f, 0.30f);
    public Vec3 HorizonColor = new(0.74f, 0.85f, 0.96f);
    public Vec3 ZenithColor = new(0.18f, 0.42f, 0.82f);

    /// <summary>How much of the cloud layer to mix in, 0..1. The sandbox and
    /// the electromagnet want the drifting fbm deck they were built with, so
    /// the default is the old hardcoded strength; a bench that wants a CLEAR
    /// sky (Pulleys, 22.08) sets this to 0 and gets gradient, sun and haze
    /// with nothing overhead.</summary>
    public float CloudAmount = 1f;
    public bool DetailedWeather;
    public float Exposure = 1f;
    public float Daylight = 1f;

    public SkyDome()
    {
        Mesh? cube = null;
        Shader? shader = null;

        try
        {
            cube = Primitives.Cube();
            shader = new Shader(SkyVertexShader, SkyFragmentShader);
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

    /// <summary>Draw the dome around the camera. Depth writes are disabled for
    /// the draw and restored afterward; the caller keeps depth test enabled
    /// so its own state stays untouched.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float time, float farPlane)
    {
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Disable(GL.CULL_FACE); // the camera is inside the cube

        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        // the cube must stay inside the far plane in every direction:
        // half-diagonal = scale/2 * sqrt(3), so scale < far * 1.15 is safe
        float scale = farPlane * 1.1f;
        _shader.SetMatrix4("uModel", Mat4.Translate(camPos) * Mat4.Scale(scale, scale, scale));
        _shader.SetVec3("uCamPos", camPos);
        _shader.SetVec3("uSunDir", SunDir);
        _shader.SetVec3("uHorizonColor", HorizonColor);
        _shader.SetVec3("uZenithColor", ZenithColor);
        _shader.SetFloat("uTime", time);
        _shader.SetFloat("uExposure",Math.Clamp(Exposure,0,1));
        _shader.SetFloat("uDaylight",Math.Clamp(Daylight,0,1));
        _shader.SetInt("uDetailedWeather", DetailedWeather ? 1 : 0);
        _shader.SetFloat("uCloudAmount", Math.Clamp(CloudAmount, 0f, 1f));
        _cube.Draw();

        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    public void Dispose()
    {
        _cube.Dispose();
        _shader.Dispose();
    }

    // The sky shaders live HERE, next to the only class that uses them. They
    // used to sit in SandboxShaders, and moving the dome into Shared without
    // them left the renderer referencing a module it cannot see - 223 errors
    // from one dangling name. A shared resource travels with its assets.
    private const string SkyVertexShader = @"#version 330 core
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

    private const string SkyFragmentShader = @"#version 330 core
in vec3 vWorldPos;
out vec4 FragColor;
uniform vec3 uCamPos;
uniform vec3 uSunDir;
uniform vec3 uHorizonColor;
uniform vec3 uZenithColor;
uniform float uTime;
uniform float uCloudAmount;
uniform int uDetailedWeather;
uniform float uExposure,uDaylight;
" + OutdoorAtmosphere.Glsl + @"

float hash(vec2 p)
{ 
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

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
    vec3 d = normalize(vWorldPos - uCamPos);
    if(uDetailedWeather==1) {
        vec3 day=outdoorSky(d,uCamPos,normalize(uSunDir),uTime);
        vec3 night=mix(vec3(.035,.045,.075),vec3(.007,.014,.037),sqrt(max(0.,d.y)));
        float twilight=4.*uDaylight*(1.-uDaylight);
        day=mix(day,day*vec3(1.45,.70,.38),twilight*.65);
        vec3 sky=mix(night,day,uDaylight);
        sky+=vec3(.20,.08,.025)*twilight*pow(1.-abs(d.y),5.);
        vec2 stars=d.xz/max(.15,d.y)*120.;
        vec2 cell=floor(stars),f=fract(stars)-.5;
        float dotStar=exp(-dot(f,f)*350.)*step(.998,hash(cell));
        sky+=dotStar*(1.-uDaylight)*smoothstep(.1,.5,d.y)*vec3(.35,.43,.55);
        vec3 moon=normalize(vec3(-uSunDir.x,.6,-uSunDir.z));
        sky+=(1.-uDaylight)*vec3(.52,.58,.67)*smoothstep(.99985,.99995,dot(d,moon));
        FragColor=vec4(sky*uExposure,1);return;
    }
    float alt = clamp(d.y, 0.0, 1.0);
    vec3 sun = normalize(uSunDir);

    vec3 sky = mix(uHorizonColor, uZenithColor, pow(alt, 0.55));

    // --- the air itself: a milky haze band that thickens toward the
    // horizon and breathes a little with fbm - distant humid air, so the
    // gradient stops reading as a flat wallpaper (24.08) ---
    float hazeBase = pow(1.0 - alt, 6.0);
    float hazeVar = 0.8 + 0.4 * fbm(vec2(atan(d.z, d.x) * 2.5, uTime * 0.004));
    sky = mix(sky, vec3(0.86, 0.90, 0.96), clamp(hazeBase * hazeVar, 0.0, 1.0) * 0.55);

    // --- the sun: disc, warm glow, and a faint wide halo ring ---
    float s = max(dot(d, sun), 0.0);
    sky += vec3(1.0, 0.96, 0.82) * pow(s, 1500.0) * 1.6;
    sky += vec3(1.0, 0.90, 0.72) * pow(s, 8.0) * 0.20;
    sky += vec3(0.95, 0.93, 0.88) * pow(s, 60.0) * 0.10;

    if (d.y > 0.03 && uCloudAmount > 0.0)
    {
        // aerial perspective: clouds near the horizon lose contrast into
        // the haze instead of tiling to a hard edge
        float aerial = clamp((d.y - 0.03) * 4.0, 0.0, 1.0);

        // --- high cirrus: thin, fast, wind-stretched streaks ---
        vec2 uvHi = d.xz / max(d.y, 0.12);
        uvHi = vec2(uvHi.x * 0.22 + uTime * 0.013, uvHi.y * 0.9);
        float ci = fbm(uvHi * 1.6);
        ci = smoothstep(0.58, 0.85, ci) * aerial;
        sky = mix(sky, vec3(0.97, 0.98, 1.0), ci * 0.30 * uCloudAmount);

        // --- low cumulus: slower, denser, and SHADED - a lit crown
        // toward the sun, a grey-blue base underneath, and a silver
        // lining when the cloud stands near the sun's direction ---
        vec2 uvLo = d.xz / d.y * 0.6 + vec2(uTime * 0.006, uTime * 0.0021);
        float c = fbm(uvLo);
        float dens = smoothstep(0.52, 0.95, c) * aerial;
        if (dens > 0.001)
        {
            // thickness from how far past the threshold the fbm stands
            float thick = clamp((c - 0.52) / 0.43, 0.0, 1.0);
            vec3 lit = vec3(1.02, 1.01, 0.99);
            vec3 shade = vec3(0.72, 0.76, 0.84);
            // thin edges stay lit, thick cores carry the grey base
            vec3 cloudCol = mix(lit, shade, thick * 0.75);
            // silver lining: clouds near the sun catch light on the rim
            float rim = pow(s, 5.0) * (1.0 - thick);
            cloudCol += vec3(0.35, 0.30, 0.22) * rim;
            sky = mix(sky, cloudCol, dens * 0.9 * uCloudAmount);
        }
    }

    // below the horizon the dome fades into the fog color: the floor's
    // distance fog dissolves into exactly this, so the seam disappears
    float below = clamp(-d.y * 6.0, 0.0, 1.0);
    sky = mix(sky, uHorizonColor * 0.9, below);

    FragColor = vec4(sky*uExposure, 1.0);
}";
}
