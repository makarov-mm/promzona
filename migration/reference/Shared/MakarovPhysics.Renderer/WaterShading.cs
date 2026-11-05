namespace MakarovPhysics.Renderer;

/// <summary>
/// The shared "beautiful water" GLSL layer (the WebGL-Water technique on plain
/// GL 3.3), extracted from WaterSurface so Buoyancy - and later the Sandbox
/// water - render the SAME world: a procedural sky, tiled pool interior with
/// grout, real caustics from a texture, per-pixel Snell refraction of the view
/// ray with an analytic pool-box intersection and path-length absorption, and
/// a Schlick-fresnel sky reflection with a sun sparkle.
///
/// Uniform contract every consumer binds (see the chunk below):
///   uSun, uHalf, uDepth, uCaustics (unit 0), uCausticsGain, uSphere, uSphereR
/// (set uSphereR = 0 when there is no floating sphere - the soft floor shadow
/// then vanishes). All shader constants are plain strings - no GL calls here.
/// </summary>
public static class WaterShading
{
    /// <summary>Insert the shared chunk and choose the water alpha expression
    /// ("1.0" = fully analytic surface; something fresnel-based lets rasterized
    /// underwater geometry, like Buoyancy's floating blocks, show through).</summary>
    public static string Expand(string source, string waterAlpha = "1.0")
        => source.Replace("{SHARED}", SharedGlsl).Replace("{WATER_ALPHA}", waterAlpha);

    // Shared GLSL: sky, pool tiles, caustics lookup
    public const string SharedGlsl = @"
uniform vec3 uSun;              // direction TOWARDS the sun
uniform float uHalf;            // pool half-size
uniform float uDepth;           // floor depth (positive)
uniform sampler2D uCaustics;    // floor brightness map, 1.0 = flat water
uniform float uCausticsGain;
uniform vec3 uSphere;           // floating sphere centre
uniform float uSphereR;

vec3 skyColor(vec3 dir)
{
    float up = clamp(dir.y, -0.05, 1.0);
    vec3 horizon = vec3(0.66, 0.80, 0.92);
    vec3 zenith  = vec3(0.10, 0.32, 0.62);
    vec3 sky = mix(horizon, zenith, pow(max(up, 0.0), 0.55));
    float sunDisc = pow(max(dot(dir, normalize(uSun)), 0.0), 2000.0);
    float sunHaze = pow(max(dot(dir, normalize(uSun)), 0.0), 12.0);
    return sky + vec3(10.0, 8.0, 6.0) * sunDisc
               + vec3(0.9, 0.85, 0.7) * sunHaze * 0.25;
}

// procedural pool tiles: light ceramic squares with darker grout
vec3 tileColor(vec3 p)
{
    vec2 cell = fract(p.xz * 2.0);                    // 0.5 m tiles
    float grout = min(min(cell.x, 1.0 - cell.x), min(cell.y, 1.0 - cell.y));
    float line = smoothstep(0.0, 0.045, grout);
    float checker = mod(floor(p.x * 2.0) + floor(p.z * 2.0), 2.0);
    vec3 a = vec3(0.75, 0.80, 0.84);
    vec3 b = vec3(0.66, 0.73, 0.79);
    return mix(vec3(0.35, 0.40, 0.45), mix(a, b, checker), line);
}

// floor shading at a world point: tiles × caustics × the sphere's soft shadow
vec3 floorColor(vec3 p)
{
    vec3 c = tileColor(p);
    vec2 uv = (p.xz + vec2(uHalf)) / (2.0 * uHalf);
    float caustic = texture(uCaustics, clamp(uv, 0.0, 1.0)).r * 4.0;
    c *= 0.42 + 0.58 * caustic * uCausticsGain;
    float shadow = 1.0;
    if (uSphereR > 0.0)
    {
        float d = length(p.xz - uSphere.xz);
        shadow = smoothstep(uSphereR * 0.55, uSphereR * 1.5, d);
    }
    return c * (0.45 + 0.55 * shadow);
}

// water absorbs red first: the classic blue-green with distance
vec3 absorbWater(vec3 color, float pathLen)
{
    return color * exp(-pathLen * vec3(0.18, 0.06, 0.03));
}

// the reference abovewaterColor: everything seen THROUGH the surface gets
// this turquoise multiplier - the signature pool-water look
const vec3 waterTint = vec3(0.25, 1.0, 1.25);";

    // Sky backdrop (fullscreen, ray per pixel)
    public const string SkyVertexShader = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;   // unused, shared vertex format
uniform vec3 uCamF; uniform vec3 uCamR; uniform vec3 uCamU;
uniform float uTanHalf; uniform float uAspect;
out vec3 vDir;
void main()
{
    // aPos.xy is a fullscreen NDC triangle; build the per-pixel view ray
    vDir = uCamF + uCamR * (aPos.x * uTanHalf * uAspect) + uCamU * (aPos.y * uTanHalf);
    gl_Position = vec4(aPos.xy, 0.9999, 1.0);
}";

    public const string SkyFragmentShader = @"#version 330 core
in vec3 vDir;
out vec4 FragColor;
{SHARED}
void main() { FragColor = vec4(skyColor(normalize(vDir)), 1.0); }";

    // Pool interior (floor + walls)
    public const string PoolVertexShader = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uModel; uniform mat4 uView; uniform mat4 uProj;
out vec3 vWorldPos; out vec3 vNormal;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    vNormal = mat3(uModel) * aNormal;
    gl_Position = uProj * uView * world;
}";

    public const string PoolFragmentShader = @"#version 330 core
in vec3 vWorldPos; in vec3 vNormal;
out vec4 FragColor;
{SHARED}
void main()
{
    vec3 c;
    if (vNormal.y > 0.5)
        c = floorColor(vWorldPos);                       // the floor gets caustics
    else
        c = tileColor(vWorldPos) * (0.80 + 0.20 * max(vNormal.y, 0.0));
    // simple sun lighting so the walls read as geometry
    float diff = max(dot(normalize(vNormal), normalize(uSun)), 0.0);
    c *= 0.62 + 0.38 * diff;
    // underwater parts pick up the blue absorption with depth
    if (vWorldPos.y < 0.0)
        c = absorbWater(c, -vWorldPos.y * 1.6);
    FragColor = vec4(c, 1.0);
}";

    // The water surface
    public const string WaterVertexShader = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uModel; uniform mat4 uView; uniform mat4 uProj;
out vec3 vWorldPos; out vec3 vNormal;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    vNormal = mat3(uModel) * aNormal;
    gl_Position = uProj * uView * world;
}";

    public const string WaterFragmentShader = @"#version 330 core
in vec3 vWorldPos; in vec3 vNormal;
uniform vec3 uViewPos;
out vec4 FragColor;
{SHARED}

// where does a ray from inside the pool hit the interior box?
vec3 poolHit(vec3 origin, vec3 dir, out float dist, out bool onFloor)
{
    float big = 1e9;
    float tFloor = dir.y < -1e-6 ? (origin.y + uDepth) / -dir.y : big;
    float tX = dir.x > 1e-6 ? (uHalf - origin.x) / dir.x
             : dir.x < -1e-6 ? (-uHalf - origin.x) / dir.x : big;
    float tZ = dir.z > 1e-6 ? (uHalf - origin.z) / dir.z
             : dir.z < -1e-6 ? (-uHalf - origin.z) / dir.z : big;
    dist = min(tFloor, min(tX, tZ));
    onFloor = dist == tFloor;
    return origin + dir * dist;
}

void main()
{
    vec3 N = normalize(vNormal);
    vec3 V = normalize(uViewPos - vWorldPos);
    if (dot(N, V) < 0.0) N = -N;                 // looking at the underside

    // --- reflection: the sky (and the sun streak) ---
    vec3 R = reflect(-V, N);
    R.y = max(R.y, 0.02);                        // the pool rim hides downward rays
    vec3 reflected = skyColor(normalize(R));

    // --- refraction: bend into the water, hit the tiles ---
    vec3 T = refract(-V, N, 0.7501);             // 1 / 1.333
    vec3 refracted;
    if (dot(T, T) < 1e-6)
    {
        refracted = reflected;                   // total internal reflection
    }
    else
    {
        float dist; bool onFloor;
        vec3 hit = poolHit(vWorldPos, normalize(T), dist, onFloor);
        vec3 c = onFloor ? floorColor(hit)
                         : tileColor(hit) * (0.62 + 0.38 * max(-hit.y / uDepth, 0.0));
        // the reference recipe: rays that go below the surface pick up the
        // turquoise waterTint, plus a mild path absorption for depth
        refracted = absorbWater(c, dist) * waterTint;
    }

    // --- reference fresnel: a 0.25 floor with a cubic falloff - the water
    // always carries a healthy share of sky, which is what makes it read as
    // water instead of glass ---
    float fresnel = mix(0.25, 1.0, pow(1.0 - max(dot(N, V), 0.0), 3.0));
    vec3 color = mix(refracted, reflected, fresnel);

    // ripple sparkle: where a facet's half-vector aligns with the sun the
    // highlight saturates to white (the HDR sun in skyColor does the same
    // for the mirrored disc)
    vec3 H = normalize(normalize(uSun) + V);
    color += vec3(3.4, 3.2, 2.8) * pow(max(dot(N, H), 0.0), 900.0);

    FragColor = vec4(color, {WATER_ALPHA});
}";
}
