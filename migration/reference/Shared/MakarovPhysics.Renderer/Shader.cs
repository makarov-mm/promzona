namespace MakarovPhysics.Renderer;

public sealed class Shader : IDisposable
{
    public uint Program { get; private set; }
    private readonly Dictionary<string, int> _locations = new();

    public Shader(string vertexSrc, string fragmentSrc)
    {
        uint vs = Compile(GL.VERTEX_SHADER, vertexSrc, "vertex");
        uint fs;

        try
        {
            fs = Compile(GL.FRAGMENT_SHADER, fragmentSrc, "fragment");
        }
        catch
        {
            GL.DeleteShader(vs);
            throw;
        }

        Program = GL.CreateProgram();
        GL.AttachShader(Program, vs);
        GL.AttachShader(Program, fs);
        GL.LinkProgram(Program);

        // the shaders are no longer needed regardless of the link outcome
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);

        if (GL.GetProgramInt(Program, GL.LINK_STATUS) == 0)
        {
            string log = GL.GetProgramInfoLog(Program);
            GL.DeleteProgram(Program);
            Program = 0;
            throw new InvalidOperationException("Shader program linking error:\n" + log);
        }
    }

    private static uint Compile(uint type, string src, string label)
    {
        uint id = GL.CreateShader(type);
        GL.ShaderSource(id, src);
        GL.CompileShader(id);

        if (GL.GetShaderInt(id, GL.COMPILE_STATUS) == 0)
        {
            string log = GL.GetShaderInfoLog(id);
            GL.DeleteShader(id);
            throw new InvalidOperationException($"Shader ({label}) compilation error:\n{log}");
        }

        return id;
    }

    /// <summary>The program bound by the last Use(). Tracked so that the
    /// defensive Use() the drawing helpers issue before every call costs
    /// nothing when the program is already current.</summary>
    private static uint _current;

    public void Use()
    {
        if (_current == Program) return;
        GL.UseProgram(Program);
        _current = Program;
    }

    /// <summary>Forget the cached binding: anything that calls glUseProgram
    /// outside this class (a raw pass, a lost context) must invalidate it.</summary>
    public static void InvalidateBinding() => _current = 0;

    private int Loc(string name)
    {
        if (!_locations.TryGetValue(name, out int loc))
        {
            loc = GL.GetUniformLocation(Program, name);
            _locations[name] = loc;
        }
        return loc;
    }

    public void SetInt(string name, int v) => GL.Uniform1i(Loc(name), v);
    public void SetFloat(string name, float v) => GL.Uniform1f(Loc(name), v);
    public void SetVec3(string name, Vec3 v) => GL.Uniform3f(Loc(name), v.X, v.Y, v.Z);
    public void SetVec3(string name, float x, float y, float z) => GL.Uniform3f(Loc(name), x, y, z);
    public void SetVec2(string name, float x, float y) => GL.Uniform2f(Loc(name), x, y);
    public void SetVec4(string name, float x, float y, float z, float w) => GL.Uniform4f(Loc(name), x, y, z, w);
    public void SetMatrix4(string name, Mat4 m) => GL.UniformMatrix4(Loc(name), m);

    public void Dispose()
    {
        if (Program != 0)
        {
            // the driver may hand this numeric name to the NEXT program; a
            // stale cache would then skip glUseProgram for it and uniforms
            // would land in whatever is actually bound (audit 31.07, F-03)
            if (_current == Program)
            {
                GL.UseProgram(0);
                _current = 0;
            }

            GL.DeleteProgram(Program);
            Program = 0;
        }
    }
}

public static class DefaultShaders
{
    // Lit: position + normal. One directional light + ambient (Blinn-Phong).
    // The normal is transformed by the inverse transpose of the model matrix,
    // Which stays correct under NON-uniform scaling (plain
    // mat3(uModel) skews normals on scaled cylinders, cones and boxes,
    // bending the diffuse/specular response).
    public const string LitVertex = @"#version 330 core
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
    mat3 nmat = transpose(inverse(mat3(uModel)));
    vNormal = nmat * aNormal;
    gl_Position = uProj * uView * world;
}";


    /// <summary>Lit vertex plus the OBJECT-space position varying: procedural
    /// materials texture in object space, so the pattern sticks to the part
    /// however the part is placed.</summary>
    public const string SurfaceVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;

out vec3 vWorldPos;
out vec3 vNormal;
out vec3 vObjPos;

void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    mat3 nmat = transpose(inverse(mat3(uModel)));
    vNormal = nmat * aNormal;
    vObjPos = aPos;
    gl_Position = uProj * uView * world;
}";

    // Value noise, three octaves - the whole texture budget of the material
    // library. No image assets: the wood and the metal are functions, which
    // means infinite resolution, zero loading, and nothing for a delta
    // archive to clobber.
    private const string NoiseFunctionsCore = @"
float hash3(vec3 p)
{
    p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = hash3(i);
    float n100 = hash3(i + vec3(1, 0, 0));
    float n010 = hash3(i + vec3(0, 1, 0));
    float n110 = hash3(i + vec3(1, 1, 0));
    float n001 = hash3(i + vec3(0, 0, 1));
    float n101 = hash3(i + vec3(1, 0, 1));
    float n011 = hash3(i + vec3(0, 1, 1));
    float n111 = hash3(i + vec3(1, 1, 1));
    return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y), mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

float fbm(vec3 p)
{
    float a = 0.5;
    float sum = 0.0;
    for (int i = 0; i < 3; i++)
    {
        sum += a * vnoise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return sum;
}

";

    private const string BumpFunction = @"
// bump from a scalar height field: nudge the shading normal against the
// tangential part of the height gradient (finite differences). Assumes the
// part is placed without wild rotations - true for benches and magnets, and
// for detail-scale texture the approximation stays invisible anyway.
vec3 bumpNormal(vec3 n, vec3 p, float h0, float eps, float strength, int kind)
{
    float hx = kind == 0 ? woodHeight(p + vec3(eps, 0, 0)) : metalHeight(p + vec3(eps, 0, 0));
    float hy = kind == 0 ? woodHeight(p + vec3(0, eps, 0)) : metalHeight(p + vec3(0, eps, 0));
    float hz = kind == 0 ? woodHeight(p + vec3(0, 0, eps)) : metalHeight(p + vec3(0, 0, eps));
    vec3 grad = vec3(hx - h0, hy - h0, hz - h0) / eps;
    vec3 tangential = grad - n * dot(grad, n);
    return normalize(n - strength * tangential);
}
";


    private const string NoiseWoodBlock = NoiseFunctionsCore + @"
// Planked bench wood, v2 (03.08 evening). The first pass put the ring
// pattern into the HEIGHT as well as the colour, on the unit-cube object
// space of a strongly non-uniform part - giant smooth normal waves under a
// 0.44 roughness, and the specular swept across them as the camera moved:
// Michael's exact report, the bench shimmered like caramel. Wood does not
// shimmer. The rules now: pattern lives in WORLD space (benches do not fly,
// and every module's stand gets the same believable scale for free), the
// broad figure drives ALBEDO ONLY, the height carries nothing but fine
// grain and the grooves between planks, and the surface stays matte.

const float PLANK = 0.55; // plank width, world units

float plankNoise(vec3 p, float id)
{
    // grain stretched hard along X (the plank length); each plank gets its
    // own phase so the figure never continues across a seam
    return fbm(vec3(p.x * 0.9, p.z * 6.0 + id * 7.31, p.y * 2.2));
}

// broad cathedral figure - colour only
float woodBand(vec3 p)
{
    float id = floor(p.z / PLANK);
    float f = sin((p.x * 1.15 + plankNoise(p, id) * 2.4) * 3.0 + id * 11.0);
    return pow(0.5 + 0.5 * f, 1.4);
}

// seam factor: 1 inside a plank, dips to 0 in the groove
float plankSeam(vec3 p)
{
    float t = fract(p.z / PLANK);
    return smoothstep(0.0, 0.035, t) * smoothstep(1.0, 0.965, t);
}

// height = fine grain + the seam groove; the broad figure stays OUT so the
// normal only carries detail too small to catch a moving highlight
float woodHeight(vec3 p)
{
    float id = floor(p.z / PLANK);
    float grain = fbm(vec3(p.x * 2.2, p.z * 14.0 + id * 7.31, p.y * 3.0));
    return grain * 0.7 - (1.0 - plankSeam(p)) * 0.8;
}

float metalHeight(vec3 p) { return 0.0; } // unused in this shader
" + BumpFunction;

    private const string NoiseMetalBlock = NoiseFunctionsCore + @"
float woodBand(vec3 p) { return 0.0; } // unused in this shader
float woodHeight(vec3 p) { return 0.0; }

// grooves stretched hard along local Y - one pass of a wire brush
float metalHeight(vec3 p)
{
    float streak = fbm(vec3(p.x, p.y * 0.05, p.z));
    float fine = vnoise(vec3(p.x * 3.0, p.y * 0.15, p.z * 3.0));
    return streak * 0.7 + fine * 0.3;
}
" + BumpFunction;


    /// <summary>The bench material: procedural oak with ring bands, along-grain
    /// streaks and bump. Knobs (all optional, 0 = default): uWoodScale,
    /// uBumpStrength. Reuse anywhere a rig needs a stand - see Materials.Wood().</summary>
    public const string WoodFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec3 vObjPos;

uniform float uWoodScale; // rings per object unit; 0 = 3.0
uniform float uBumpStrength; // 0 = 0.55
" + PbrLightUniforms + NoiseWoodBlock + PbrFunctions + @"
void main()
{
    // WORLD space on purpose: see the note above the wood functions
    float scale = uWoodScale > 0.0 ? uWoodScale : 1.0;
    vec3 p = vWorldPos * scale;

    float id = floor(p.z / PLANK);
    float h = woodHeight(p);
    float band = woodBand(p);
    float grain = fbm(vec3(p.x * 2.2, p.z * 14.0 + id * 7.31, p.y * 3.0));
    float seam = plankSeam(p);

    vec3 light = vec3(0.63, 0.47, 0.315);
    vec3 dark = vec3(0.37, 0.25, 0.155);
    // per-plank tone shift, so the bench reads as boards, not one slab
    float tone = 0.92 + 0.16 * hash3(vec3(id, 3.7, 9.1));
    vec3 albedo = mix(dark, light, clamp(band * 0.8 + grain * 0.25, 0.0, 1.0))
                * tone * (0.55 + 0.45 * seam);

    float strength = uBumpStrength > 0.0 ? uBumpStrength : 0.30;
    vec3 n = bumpNormal(normalize(vNormal), p, h, 0.06, strength, 0);

    // matte: wood scatters; only the band breathes the roughness a little
    float rough = clamp(0.80 - 0.10 * band, 0.62, 0.9);
    FragColor = shadePbr(vWorldPos, n, albedo, rough, 0.0, 0.10);
}";

    /// <summary>
    /// Wood from a PHOTOGRAPH rather than from a function. The procedural
    /// board is a fine surface at a metre and falls apart at ten centimetres
    /// and at three metres alike: the plank seams turn into blotches and the
    /// grain into stripes of paint (screenshots, 14.08 - "менее вырвиглазное").
    /// A scanned albedo with its bump map does not have that failure mode,
    /// because it does not have to invent anything.
    ///
    /// Triplanar, like the sandbox: the shared primitives carry position and
    /// normal only, so the map is projected from three directions and blended
    /// by the squared normal - no unwrapping, no seams to author, and the same
    /// code works on a box, a cylinder and a sphere. The projection runs in
    /// WORLD space so a bench and the rail on it show the same grain at the
    /// same size, and uTexScale is repeats per metre, which is a number a
    /// module can reason about: 4 means a board every 25 cm.
    ///
    /// The bump map is SAMPLED, not decorative: three taps give the surface
    /// gradient, and the normal is bent by it before shading. Flat photo wood
    /// under a moving light looks like printed paper; this catches the light
    /// along the grain the way a plank does.
    /// </summary>
    public const string TexturedWoodFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec3 vObjPos;

uniform sampler2D uAlbedoMap;   // unit 0
uniform sampler2D uBumpMap;     // unit 1
uniform float uTexScale;        // repeats per metre; 0 = 3.0
uniform float uBumpStrength;    // 0 = 0.6
uniform vec3 uTint;             // zero = the photograph as it is
uniform vec3 uTexOrigin;        // world anchor of the pattern; 0 = the world itself
" + PbrLightUniforms + PbrFunctions + @"
vec3 triWeights(vec3 n)
{
    vec3 w = abs(n);
    w *= w;
    return w / max(w.x + w.y + w.z, 1e-4);
}

vec3 triAlbedo(vec3 q, vec3 w)
{
    return texture(uAlbedoMap, q.zy).rgb * w.x
         + texture(uAlbedoMap, q.xz).rgb * w.y
         + texture(uAlbedoMap, q.xy).rgb * w.z;
}

float triHeight(vec3 q, vec3 w)
{
    return texture(uBumpMap, q.zy).r * w.x
         + texture(uBumpMap, q.xz).r * w.y
         + texture(uBumpMap, q.xy).r * w.z;
}

void main()
{
    float scale = uTexScale > 0.0 ? uTexScale : 3.0;
    vec3 n0 = normalize(vNormal);
    vec3 w = triWeights(n0);
    // The projection is triplanar in WORLD space, which is right for a bench
    // or a floor: the grain belongs to the room and every board shares it.
    // A body that MOVES needs the pattern anchored to itself, or the stone
    // swims across its faces as it rises. uTexOrigin is that anchor - pass
    // the body's centre and the mapping travels with it, still at true size
    // (an object-space mapping would keep the grain still but stretch it
    // whenever the body is scaled). Zero, the default, is the old behaviour.
    vec3 p = vWorldPos - uTexOrigin;
    vec3 q = p * scale;

    vec3 albedo = triAlbedo(q, w);
    if (dot(uTint, uTint) > 1e-6) albedo *= uTint;

    // gradient of the height field, in world units, from three taps
    float e = 0.35 / scale;
    float h = triHeight(q, w);
    float hx = triHeight((p + vec3(e, 0.0, 0.0)) * scale, w);
    float hy = triHeight((p + vec3(0.0, e, 0.0)) * scale, w);
    float hz = triHeight((p + vec3(0.0, 0.0, e)) * scale, w);
    vec3 grad = vec3(hx - h, hy - h, hz - h) / max(e, 1e-5);

    float strength = uBumpStrength > 0.0 ? uBumpStrength : 0.6;
    // only the part across the surface bends it - along the normal is height,
    // not tilt, and adding it would swim the shading as the camera moves
    vec3 tangentialGrad = grad - n0 * dot(grad, n0);
    vec3 n = normalize(n0 - tangentialGrad * strength * 0.02);

    // wood scatters; the grain lets a little more light off the hard rings
    float rough = clamp(0.84 - 0.16 * h, 0.60, 0.94);
    FragColor = shadePbr(vWorldPos, n, albedo, rough, 0.0, 0.08);
}";

    /// <summary>Brushed metal with a tint: fine directional grooves (brush
    /// axis = local Y), bump, tight tinted highlight. Knobs: uMetalTint
    /// (zero = steel), uBrushScale (0 = 18), uBumpStrength (0 = 0.35).
    /// The magnets use it tinted; see Materials.BrushedMetal().</summary>
    public const string BrushedMetalFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec3 vObjPos;

uniform vec3 uMetalTint;       // zero vector = plain steel
uniform float uBrushScale;     // 0 = 18.0
uniform float uBumpStrength;   // 0 = 0.35
" + PbrLightUniforms + NoiseMetalBlock + PbrFunctions + @"
void main()
{
    float scale = uBrushScale > 0.0 ? uBrushScale : 18.0;
    vec3 p = vObjPos * scale;

    float h = metalHeight(p);
    float streak = fbm(vec3(p.x, p.y * 0.05, p.z));

    vec3 tint = dot(uMetalTint, uMetalTint) > 1e-6 ? uMetalTint : vec3(0.62, 0.64, 0.68);
    vec3 albedo = tint * (0.82 + 0.34 * h);

    float strength = uBumpStrength > 0.0 ? uBumpStrength : 0.35;
    vec3 n = bumpNormal(normalize(vNormal), p, h, 0.35, strength, 1);

    float rough = clamp(0.20 + 0.16 * streak, 0.12, 0.5);
    FragColor = shadePbr(vWorldPos, n, albedo, rough, 1.0, 1.0);
}";

    /// <summary>Depth-only vertex shader for the shadow pass: the same vertex
    /// format and the same uModel the lit pass uses, so a module renders its
    /// occluders with the calls it already has.</summary>
    /// <summary>
    /// The fresnel-rim glass (14.09, promoted from VacuumTube / BenchChamber /
    /// Schrödinger1D, which each carried a copy): an envelope that is dark
    /// face-on and lights up at its rim, drawn ADDITIVELY over whatever it
    /// contains. Uniforms: uModel, uView, uProj, uViewPos, uColor.
    /// </summary>
    /// <summary>
    /// Eclipse geometry for a lit surface (wave W2, 20.10): the uniforms of a
    /// spherical light (point or directional) and up to eight occluding
    /// spheres, the planar overlap of two angular discs, and `shadow(p, sunDir)`
    /// returning (light reaching the point, redness of the umbra of an occluder
    /// with an atmosphere). A line-for-line copy of
    /// MakarovPhysics.Numerics.Shadowing, pinned in ShadowingTests; pasted into
    /// CosmosPainter.Fragment and Globe.GlobeFragment by const concatenation.
    /// </summary>
    public const string EclipseShadow = """
        uniform float uPointLight, uLightRadius, uLightAngular;
        uniform vec3 uLightPos;
        uniform vec4 uOccluder[8];
        uniform float uOccluderAtmo[8];
        uniform int uOccluderCount;
        float covered(float l,float o,float s){
            if(l<=0.) return s<o?1.:0.;
            if(o<=0.||s>=l+o) return 0.;
            if(s<=abs(o-l)) return o>=l?1.:o*o/(l*l);
            float a=(s*s+l*l-o*o)/(2.*s),b=(s*s+o*o-l*l)/(2.*s);
            float area=l*l*acos(clamp(a/l,-1.,1.))-a*sqrt(max(0.,l*l-a*a))+o*o*acos(clamp(b/o,-1.,1.))-b*sqrt(max(0.,o*o-b*b));
            return area/(3.14159265*l*l);
        }
        vec2 shadow(vec3 p,vec3 sunDir){
            vec3 lightPos=uPointLight>.5?uLightPos:p+sunDir*1e4;
            float lightR=uPointLight>.5?uLightRadius:1e4*0.004652;
            float lit=1.,red=0.;
            for(int i=0;i<8;i++){
                if(i>=uOccluderCount) break;
                vec3 t=lightPos-p,o=uOccluder[i].xyz-p;float r=uOccluder[i].w;
                float dL=length(t),dO=length(o);
                float f=1.;
                if(dO<=r) f=0.;
                else if(dL>0.&&dO<dL){
                    float cosSep=dot(t,o)/(dL*dO);
                    if(cosSep>0.){
                        float aL=uLightAngular>0.?uLightAngular:asin(min(1.,lightR/dL));
                        float aO=asin(min(1.,r/dO));
                        float c=covered(aL,aO,acos(min(1.,cosSep)));
                        f=1.-c; red=max(red,uOccluderAtmo[i]*smoothstep(.6,1.,c));
                    }
                }
                lit*=f;
            }
            return vec2(lit,red);
        }
        """;

    public const string GlassRimVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vNormal;
out vec3 vWorld;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorld = world.xyz;
    vNormal = mat3(uModel) * aNormal;
    gl_Position = uProj * uView * world;
}";

    public const string GlassRimFragment = @"#version 330 core
in vec3 vNormal;
in vec3 vWorld;
uniform vec3 uViewPos;
uniform vec3 uColor;
out vec4 FragColor;
void main()
{
    vec3 n = normalize(vNormal);
    vec3 v = normalize(uViewPos - vWorld);
    float rim = pow(1.0 - abs(dot(n, v)), 2.2);
    FragColor = vec4(uColor * (0.06 + 0.8 * rim), 1.0);
}";

    public const string DepthVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;   // unused; the vertex format is shared

uniform mat4 uModel;
uniform mat4 uLightSpace;

void main()
{
    gl_Position = uLightSpace * uModel * vec4(aPos, 1.0);
}";

    /// <summary>Depth-only fragment shader: writes nothing but depth.</summary>
    public const string DepthFragment = @"#version 330 core
void main() { }";

    // ---- composable PBR chunks (03.08, the material library) ----
    // LitFragment is ASSEMBLED from these; WoodFragment and
    // BrushedMetalFragment reuse the same light uniforms and the same
    // shadePbr - the refactor is mechanical, the math is the pre-03.08
    // text verbatim.

    private const string LitFragmentHead = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;

uniform vec3 uObjectColor;

// Surface controls. Every module written before these existed leaves them at
// the GL default of 0, and 0 is mapped to the old plastic look - so the whole
// catalogue keeps rendering, and a module opts in by setting them.
uniform float uRoughness;   // 0 = use the default 0.55
uniform float uMetallic;    // 0 = dielectric (the old behaviour)

// Procedural bump (17.08): a value-noise perturbation of the normal, in world
// space, so every surface can carry micro-relief without a single texture.
// 0 = flat, which is the GL default - the whole catalogue keeps rendering
// exactly as before and a material opts in.
uniform float uBumpAmount;  // 0 = flat (default)
uniform float uBumpScale;   // 0 = default 14 (noise cells per world unit)
";

    public const string PbrLightUniforms = @"
uniform vec3 uLightDir;     // direction TOWARDS the light (normalized in the shader)
uniform vec3 uLightColor;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uSkyTint;     // 0 = use the default hemisphere strength
uniform float uEnvIntensity; // 0 = default 1.0; a space scene turns it down

// Shadowing. uShadowStrength = 0 (the GL default for an unset uniform) means
// there is no shadow map bound: the sampler is never touched, so every module
// that knows nothing about shadows keeps working unchanged.
// (No quotation marks in here - a lone one ends the C# verbatim string that
// carries this shader, and the compiler then reports a dozen errors in what
// looks like perfectly good GLSL.)
uniform float uShadowStrength;
uniform mat4 uLightSpace;
uniform sampler2D uShadowMap;

out vec4 FragColor;
";

    public const string PbrFunctions = @"
const float PI = 3.14159265359;

// GGX / Trowbridge-Reitz: the highlight shape real surfaces have - a tight
// core with a wide tail, instead of the single hard pow() lobe of Blinn-Phong.
float distributionGGX(float NdotH, float rough)
{
    float a = rough * rough;
    float a2 = a * a;
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 1e-7);
}

// Smith height-correlated visibility, already divided by 4 NdotL NdotV
float visibilitySmith(float NdotV, float NdotL, float rough)
{
    float a = rough * rough;
    float lv = NdotL * (NdotV * (1.0 - a) + a);
    float ll = NdotV * (NdotL * (1.0 - a) + a);
    return 0.5 / max(lv + ll, 1e-7);
}

vec3 fresnelSchlick(float VdotH, vec3 f0)
{
    float f = pow(clamp(1.0 - VdotH, 0.0, 1.0), 5.0);
    return f0 + (1.0 - f0) * f;
}

// The environment, evaluated analytically instead of sampled from a prefiltered
// cubemap. A real IBL probe would need render-to-cubemap convolution at start-up
// and five mip levels of storage per scene; this is the same idea in closed
// form - a sky gradient, a ground bounce and the sun disc - so a mirror shows
// where it is standing and a rough surface shows only the broad gradient.
// The lobe is widened with roughness, which is what prefiltering does.
vec3 envRadiance(vec3 dir, float rough, float skyTint)
{
    float up = dir.y * 0.5 + 0.5;

    vec3 zenith  = vec3(0.42, 0.55, 0.82);
    vec3 horizon = vec3(0.30, 0.34, 0.40);
    vec3 ground  = vec3(0.20, 0.18, 0.16);

    vec3 sky = mix(horizon, zenith, smoothstep(0.50, 1.0, up));
    vec3 base = mix(ground, sky, smoothstep(0.44, 0.56, up));
    base = base * skyTint + vec3(0.34) * (1.0 - skyTint);

    // the sun: a tight disc for a mirror, a broad wash for chalk
    float cosSpread = mix(0.9992, 0.55, rough);
    float sun = smoothstep(cosSpread, mix(0.99975, 0.92, rough), dot(dir, normalize(uLightDir)));
    return base + uLightColor * sun * mix(3.2, 0.35, rough);
}

// Karis' analytic fit to the split-sum environment BRDF: the scale and bias
// applied to F0 when a whole environment, rather than one light, is reflected.
// It replaces the 2D lookup texture the textbook formulation needs.
vec2 envBRDFApprox(float NdotV, float rough)
{
    vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
    vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
    vec4 r = rough * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    return vec2(-1.04, 1.04) * a004 + r.zw;
}

// 3x3 PCF with a slope-scaled bias: hard shadow-map edges look like a bug,
// and a flat bias either detaches the contact or stripes the lit surface.
float shadowFactor(vec3 worldPos, float NdotL)
{
    vec4 lp = uLightSpace * vec4(worldPos, 1.0);
    vec3 proj = lp.xyz / lp.w * 0.5 + 0.5;
    if (proj.z > 1.0 || proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0)
        return 1.0;

    float bias = max(0.0022 * (1.0 - NdotL), 0.0006);
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    float lit = 0.0;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            float d = texture(uShadowMap, proj.xy + vec2(x, y) * texel).r;
            lit += (proj.z - bias) > d ? 0.0 : 1.0;
        }
    }
    return lit / 9.0;
}

// The WHOLE shading path as one function, so a material shader (wood,
// brushed metal) reuses the exact BRDF the plain lit path has - one truth,
// no drift. skyTint / envIntensity defaults resolve HERE so every caller
// gets the same fallbacks; rough / metal arrive resolved (Lit maps its
// uniform zeros, a material computes real per-pixel values).
// specScale: 1.0 for everything that behaves like the old lit path; wood
// passes ~0.1, because a broad matte surface with a sliding environment
// highlight reads as lacquer however honest the noise is - Lambert diffuse
// is view-independent BY CONSTRUCTION, so killing the specular kills the
// shimmer deterministically (Michael, 03.08, second wood review)
vec4 shadePbr(vec3 worldPos, vec3 Nin, vec3 albedo, float rough, float metal, float specScale)
{
    float skyTint = uSkyTint > 0.0 ? uSkyTint : 0.55;
    float envIntensity = uEnvIntensity > 0.0 ? uEnvIntensity : 1.0;

    vec3 N = normalize(Nin);
    vec3 V = normalize(uViewPos - worldPos);
    vec3 L = normalize(uLightDir);
    vec3 H = normalize(L + V);

    // two-sided: revolutions and thin plates are seen from behind all the time
    if (dot(N, V) < 0.0) N = -N;

    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 1e-4);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);

    // metals tint their reflection with the albedo; dielectrics reflect 4% white
    vec3 f0 = mix(vec3(0.04), albedo, metal);
    vec3 diffuseAlbedo = albedo * (1.0 - metal);

    vec3 F = fresnelSchlick(VdotH, f0);
    float D = distributionGGX(NdotH, rough);
    float Vis = visibilitySmith(NdotV, NdotL, rough);

    vec3 specular = F * (D * Vis);
    vec3 kd = (vec3(1.0) - F) * (1.0 / PI);

    float shade = uShadowStrength > 0.0
        ? mix(1.0, shadowFactor(worldPos, NdotL), clamp(uShadowStrength, 0.0, 1.0))
        : 1.0;

    vec3 direct = (kd * diffuseAlbedo + specular * specScale) * uLightColor * NdotL * shade;

    // Hemisphere ambient instead of a flat term: a cool sky from above and a
    // warm bounce from below. Shapes that used to read as silhouettes now show
    // their form even where the key light does not reach.
    float up = N.y * 0.5 + 0.5;
    vec3 sky = vec3(0.55, 0.68, 0.92);
    vec3 ground = vec3(0.34, 0.30, 0.26);
    vec3 ambientLight = mix(ground, sky, up) * skyTint + vec3(1.0 - skyTint);

    // Image-based lighting, split-sum style. The diffuse half is the
    // hemisphere irradiance; the specular half is the environment seen along
    // the reflection vector, weighted by the analytic environment BRDF. A
    // metal has no diffuse albedo at all - without this reflection term it is
    // simply black, which is exactly how the first polished bob rendered.
    vec3 R = reflect(-V, N);
    vec3 prefiltered = envRadiance(R, rough, skyTint);
    vec2 ab = envBRDFApprox(NdotV, rough);
    vec3 specularIBL = prefiltered * (f0 * ab.x + vec3(ab.y));

    vec3 diffuseIBL = diffuseAlbedo * ambientLight;
    // Ambient FLOOR. Modules set values from 0.30 to 0.58 and the darkest of
    // them - QuantumSpin, Casimir, ChargedConductors and the rest of the
    // 'too dark' list - sit at the bottom of that range against a near-black
    // background, so an unlit face has almost nothing to show. Raising the
    // floor to 0.40 lifts the darkest third by up to a factor of 1.33 and
    // leaves everything already above it untouched, which is why this is a
    // max() and not a scale: a module that deliberately chose 0.58 keeps it.
    float ambientLevel = max(uAmbient, 0.40);
    vec3 ambient = (ambientLevel * diffuseIBL + specularIBL * specScale) * envIntensity;

    // a grazing sheen so metal keeps an edge against a dark background
    float rim = pow(1.0 - NdotV, 4.0) * (1.0 - rough) * 0.35;
    vec3 color = ambient + direct + f0 * rim * uLightColor * specScale;

    // gentle highlight rolloff: keeps a specular spike from clipping to a
    // white blob on its own, whether or not the module runs the post stack
    color = color / (1.0 + max(max(color.r, color.g), color.b) * 0.22);

    return vec4(color, 1.0);
}
";

    public const string LitFragment = LitFragmentHead + PbrLightUniforms
        + NoiseFunctionsCore + PbrFunctions + @"
void main()
{
    float rough = clamp(uRoughness > 0.0 ? uRoughness : 0.55, 0.045, 1.0);
    float metal = clamp(uMetallic, 0.0, 1.0);

    vec3 n = vNormal;
    if (uBumpAmount > 0.001)
    {
        // gradient of value noise by central differences: cheap, stable,
        // and in WORLD space so the relief does not swim when a part moves
        float s = uBumpScale > 0.0 ? uBumpScale : 14.0;
        vec3 p = vWorldPos * s;

        // LOD, the part a procedural bump does not get for free (22.08).
        // A texture has mipmaps; noise has nothing, so once one pixel covers
        // more than about half a noise cell the relief stops being detail
        // and becomes sampling error - the sandpaper crawl on a big floor
        // seen at a grazing angle. fwidth gives the world footprint of this
        // pixel; measured in cells, it says exactly when to let go. Near
        // surfaces have a tiny footprint and are untouched.
        vec3 fw = fwidth(vWorldPos);
        float cells = max(max(fw.x, fw.y), fw.z) * s;
        float lod = clamp(1.0 - (cells - 0.35) / 0.65, 0.0, 1.0);

        if (lod > 0.001)
        {
            const float e = 0.35;
            vec3 g = vec3(
                vnoise(p + vec3(e, 0, 0)) - vnoise(p - vec3(e, 0, 0)),
                vnoise(p + vec3(0, e, 0)) - vnoise(p - vec3(0, e, 0)),
                vnoise(p + vec3(0, 0, e)) - vnoise(p - vec3(0, 0, e)));
            n = normalize(normalize(n) + uBumpAmount * lod * g);
        }
    }

    FragColor = shadePbr(vWorldPos, n, uObjectColor, rough, metal, 1.0);
}";

    // Unlit: solid color. For grids, axes and debug lines.
    public const string UnlitVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal; // unused, but the vertex format is shared

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;

void main()
{
    gl_Position = uProj * uView * uModel * vec4(aPos, 1.0);
}";

    public const string UnlitFragment = @"#version 330 core
uniform vec3 uColor;
out vec4 FragColor;
void main() { FragColor = vec4(uColor, 1.0); }";

    /// <summary>Lit shading with a base colour map; pairs with TexturedMesh (location 2 = uv).</summary>
    public const string LitTexturedVertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vWorldPos;
out vec3 vNormal;
out vec2 vUv;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vUv = aUv;
    gl_Position = uProj * uView * world;
}";

    public const string LitTexturedFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vUv;
uniform sampler2D uTexture;
uniform vec3 uObjectColor;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uMatteSurface; // opt-in; default zero preserves existing materials
out vec4 FragColor;
void main()
{
    vec3 albedo = texture(uTexture, vUv).rgb * uObjectColor;
    vec3 n = normalize(vNormal);
    vec3 l = normalize(uLightDir);
    float diff = max(dot(n, l), 0.0);
    vec3 v = normalize(uViewPos - vWorldPos);
    vec3 h = normalize(l + v);
    float spec = pow(max(dot(n, h), 0.0), 48.0) * 0.25;

    // Hemisphere fill instead of a flat term. The flat version gave every
    // unlit surface the SAME grey - no shape at all wherever the key light did
    // not reach - and that is most of what reads as 'too dark': benches,
    // floors and stands are all on this path.
    //
    // Copying the PBR path's hemisphere verbatim would have made it worse, not
    // better: its ground colour is dark, and the measurement said the shadow
    // side would drop from 0.170 to 0.044. The numbers below were chosen
    // against three requirements - the shadow side no darker than before, a
    // visible top-to-bottom gradient where there was none, and the lit side
    // short of clipping. Measured result: shadow 0.170 -> 0.200, upward face
    // 0.375, so a 1.9x gradient appears and nothing gets dimmer.
    float up = n.y * 0.5 + 0.5;
    float fill = max(uAmbient, 0.50) * mix(0.80, 1.50, up);

    // a grazing sheen so an edge survives against a dark background
    float ndv = max(dot(n, normalize(uViewPos - vWorldPos)), 0.0);
    float rim = pow(1.0 - ndv, 4.0) * 0.12;

    vec3 lit = albedo * (fill + diff * uLightColor) + uLightColor * (spec + rim) * (1.0 - 0.94 * uMatteSurface);

    // gentle rolloff, matching the PBR path, so a bright bench does not clip
    lit = lit / (1.0 + max(max(lit.r, lit.g), lit.b) * 0.18);

    FragColor = vec4(lit, 1.0);
}";
}
