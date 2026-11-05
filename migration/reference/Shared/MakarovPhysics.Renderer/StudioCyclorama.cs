namespace MakarovPhysics.Renderer;

/// <summary>
/// The STUDIO environment (18.09) - the fourth named backdrop, next to
/// Laboratory (<see cref="LabRoom"/>), DaySky (<see cref="SkyDome"/>) and
/// Space (<see cref="SpaceDome"/>).
///
/// It exists because of the screenshot review of 14.09: some thirty
/// non-cosmic modules stood in a STARFIELD. A table of dice, a board of
/// coins, a city of prime blocks - all of them floating in a galaxy,
/// because Space was the only backdrop that was not a laboratory. The
/// starfield is not neutral: it says "astronomy" in a module about
/// arithmetic, and it fights every scene whose subject is a small bright
/// object on a surface.
///
/// A cyclorama is what a photographer uses for exactly this: a backdrop
/// that CURVES into the floor so that no line marks where one ends and the
/// other begins, lit softly from above, with the object's own light
/// pooling on the floor around it. There is no horizon to read, so the eye
/// takes the object as the only thing in the frame - which is the whole
/// point of a table scene.
///
/// Shaded by ray intersection like the laboratory, not by direction like
/// the sky: the floor is at a FINITE height and the coving has a finite
/// radius, so both have to be hit, not sampled. One plane and one quarter
/// cylinder per ray, no loop.
///
/// HONEST ABOUT THE FLOOR. The review asked for "a floor with reflection".
/// This floor has a SHEEN, not a reflection: a backdrop cannot reflect the
/// scene, because it is drawn before the scene exists and knows nothing
/// about it. What it does is fade the backdrop's own light down into the
/// floor and add a soft pool under the origin, which is what a mirror
/// floor looks like from the distance these modules are viewed at. A real
/// reflection needs a second pass over the scene geometry and belongs to
/// the module, not here.
///
/// Adopted through <see cref="EnvironmentStage"/> with
/// <c>EnvironmentKind.Studio</c>.
/// </summary>
public sealed class StudioCyclorama : IDisposable
{
    private readonly Mesh _cube;
    private readonly Shader _shader;

    /// <summary>Where the floor lies. The table modules put their plinth at
    /// y = 0, so the floor sits a little below it by default.</summary>
    public float FloorY = -0.35f;

    /// <summary>Distance from the axis at which the backdrop stands. The
    /// coving eats the last <see cref="CoveRadius"/> of it.</summary>
    public float Radius = 9.0f;

    /// <summary>Radius of the curve that joins floor and wall. Zero would be
    /// a corner - and a corner is the line a cyclorama exists to remove.</summary>
    public float CoveRadius = 3.2f;

    /// <summary>The paper's colour at the top of the sweep. DARKER than the
    /// floor: a cyclorama is lit from in front and above, so its top is the
    /// darkest thing in the frame. The first version had this the other way
    /// round and the whole sweep landed in levels 31…50 of 255 - measured off
    /// the screenshots of 18.09, which is how it was found.</summary>
    public Vec3 BackdropColor = new(0.215f, 0.226f, 0.252f);

    /// <summary>The floor's colour directly under the light.</summary>
    public Vec3 FloorColor = new(0.265f, 0.277f, 0.305f);

    /// <summary>Where the soft box hangs. Normalised on use.</summary>
    public Vec3 LightDir = new(0.25f, 0.92f, 0.30f);

    /// <summary>How much light pools on the floor around the origin, 0..1.
    /// This is the sheen the review asked a reflection for.</summary>
    public float FloorSheen = 0.55f;

    /// <summary>Darkening towards the edges of the sweep, 0..1 - the reason a
    /// cyclorama reads as a room and not as flat paint.</summary>
    public float Falloff = 0.65f;

    public StudioCyclorama()
    {
        Mesh? cube = null;
        Shader? shader = null;

        try
        {
            cube = Primitives.Cube();
            shader = new Shader(StudioVertexShader, StudioFragmentShader);
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

    /// <summary>Draw the sweep around the camera; state contract identical to
    /// the dome's - depth write off, depth test off, culling off because the
    /// camera is inside the cube, everything restored on the way out.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float farPlane)
    {
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Disable(GL.CULL_FACE);

        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        float scale = farPlane * 1.1f;
        _shader.SetMatrix4("uModel", Mat4.Translate(camPos) * Mat4.Scale(scale, scale, scale));
        _shader.SetVec3("uCamPos", camPos);
        _shader.SetVec3("uLightDir", LightDir);
        _shader.SetVec3("uBackdrop", BackdropColor);
        _shader.SetVec3("uFloor", FloorColor);

        // The cove may not eat more than the whole radius, and the floor may
        // not be above the camera's own height by more than a hair: both are
        // clamped here rather than trusted, because a module sets these as
        // plain fields every frame (EnvironmentStage forwards them) and a
        // single bad frame would paint the screen one flat colour.
        float radius = EffectiveRadius(Radius, camPos);
        float cove = EffectiveCove(CoveRadius, radius);
        _shader.SetFloat("uFloorY", FloorY);
        _shader.SetFloat("uRadius", radius);
        _shader.SetFloat("uCove", cove);
        _shader.SetFloat("uSheen", Math.Clamp(FloorSheen, 0f, 1f));
        _shader.SetFloat("uFalloff", Math.Clamp(Falloff, 0f, 1f));
        _cube.Draw();

        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    /// <summary>The radius the sweep is actually drawn at: the room ALWAYS
    /// contains the camera.
    ///
    /// A module may zoom out past the wall (MaxDistance runs to 26 in places),
    /// and a camera outside the cylinder turns the sweep inside out. It moved
    /// out of the shader on 18.09, when the Mac port would otherwise have
    /// carried a second copy of the rule in a second language: GLSL and MSL
    /// both run only on a GPU, and neither SelfTest nor XCTest can ask them
    /// anything. Here both hosts compute it in code and both pin it.</summary>
    public static float EffectiveRadius(float radius, Vec3 camPos)
    {
        float camRadial = MathF.Sqrt(camPos.X * camPos.X + camPos.Z * camPos.Z);
        return Math.Max(Math.Max(0.5f, radius), camRadial * 1.25f + 1.0f);
    }

    /// <summary>The cove may not eat more than the room, measured against the
    /// radius the sweep is actually drawn at.</summary>
    public static float EffectiveCove(float cove, float radius) =>
        Math.Clamp(cove, 0f, radius * 0.9f);

    public void Dispose()
    {
        _cube.Dispose();
        _shader.Dispose();
    }

    private const string StudioVertexShader = @"#version 330 core
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

    private const string StudioFragmentShader = @"#version 330 core
in vec3 vWorldPos;
out vec4 FragColor;
uniform vec3 uCamPos;
uniform vec3 uLightDir;
uniform vec3 uBackdrop;
uniform vec3 uFloor;
uniform float uFloorY;
uniform float uRadius;
uniform float uCove;
uniform float uSheen;
uniform float uFalloff;

/* A cyclorama in two surfaces along the ray - the floor plane below and the
   cylindrical wall around - joined by a quarter-round cove.

   EVERY TERM IS A FUNCTION OF THE SWEEP, and the sweep is continuous across
   the join. That is not tidiness, it is the whole requirement: the first
   version added a vertical smear on the FLOOR BRANCH ONLY, which jumped to
   zero the moment the ray hit the wall instead. Measured off the screenshots
   of 18.09: a step of 10 levels out of a band 19 levels wide - so the only
   thing the sweep actually drew was the seam it exists to remove. A branch
   here is a line on the screen. */

void main()
{
    vec3 d = normalize(vWorldPos - uCamPos);
    vec3 L = normalize(uLightDir);

    /* The room already contains the camera: the radius and the cove come in
       computed (StudioCyclorama.EffectiveRadius), because a rule that lives
       in a shader cannot be tested - and the Mac port would have carried a
       second copy of it in MSL. */
    float R = uRadius;
    float cove = uCove;

    float tFloor = d.y < -1e-5 ? (uFloorY - uCamPos.y) / d.y : 1e9;
    if (tFloor < 1e8 && length((uCamPos + d * tFloor).xz) > R)
    {
        tFloor = 1e9;                      /* floor beyond the wall is not floor */
    }

    float tWall = 1e9;
    float a = dot(d.xz, d.xz);
    if (a > 1e-9)
    {
        float b = dot(uCamPos.xz, d.xz);
        float c = dot(uCamPos.xz, uCamPos.xz) - R * R;
        float disc = b * b - a * c;
        if (disc >= 0.0)
        {
            float r = (-b + sqrt(disc)) / a;
            if (r > 0.0) tWall = r;
        }
    }

    float t = min(tFloor, tWall);
    float sweep, radial, h;
    vec3 inward;
    if (t > 1e8)
    {
        /* straight up: no azimuth exists, so take the top of the sweep with
           the height falloff already saturated - the limit of the wall */
        sweep = 1.0; radial = R; h = cove + R; inward = vec3(0.0, 0.0, -1.0);
    }
    else
    {
        vec3 hit = uCamPos + d * max(t, 0.0);
        radial = length(hit.xz);
        h = hit.y - uFloorY;
        sweep = tFloor <= tWall
            ? 0.5 * (1.0 - clamp(max(R - radial, 0.0) / cove, 0.0, 1.0))
            : 0.5 + 0.5 * clamp(h / cove, 0.0, 1.0);
        inward = radial > 1e-4 ? normalize(vec3(-hit.x, 0.0, -hit.z)) : vec3(0.0, 1.0, 0.0);
    }

    /* the normal turns through the cove with the sweep: up on the floor,
       horizontal on the wall, and every angle in between where the curve is */
    float phi = sweep * 1.5707963;
    vec3 n = normalize(vec3(0.0, cos(phi), 0.0) + sin(phi) * inward);

    float lambert = 0.60 + 0.40 * max(dot(n, L), 0.0);
    vec3 col = mix(uFloor, uBackdrop, smoothstep(0.0, 1.0, sweep)) * lambert;

    /* the pool of light under the middle of the table. A function of the
       RADIUS alone, so it is the same number on both sides of the join - and
       it has faded to nothing long before the cove. This is a sheen, not a
       reflection: the backdrop is drawn before the scene and knows nothing
       about it (see the class docs). */
    float pool = radial / 2.6;
    col += uFloor * (uSheen * 0.55 * exp(-pool * pool));

    /* one falloff outward, one upward; both continuous through the join
       because radial and h are */
    col *= 1.0 - uFalloff * 0.30 * smoothstep(0.0, R, radial);
    col *= 1.0 - 0.18 * clamp((h - cove) / R, 0.0, 1.0);

    FragColor = vec4(max(col, vec3(0.0)), 1.0);
}";
}
