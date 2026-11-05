namespace MakarovPhysics.Renderer;

/// <summary>
/// Reusable scene for pendulum-like experiments: a chain of joints rendered as
/// lit rods and bobs, plus an unlit fading trail. Used by Pendulum,
/// DoublePendulum, SpringPendulum and other rod-and-mass modules.
/// </summary>
public sealed class RodChainScene : IDisposable
{
    private const int TrailMeshCapacity = 2048;
    private readonly Mesh _cube;
    private readonly Mesh _tube;
    private readonly Mesh _sphere;
    private readonly Shader _lit;
    private Shader? _depth; // built on demand: only shadow-casting modules pay for it
    private Shader? _active; // the program DrawRod/DrawBob/DrawBox currently feed
    private readonly Shader _unlit;
    private ColorLineMesh? _trailMesh; // created lazily on the first trail

    // Directional light for the default lit shader (DefaultShaders).
    public Vec3 LightDir = new(0.4f, 0.85f, 0.55f); // direction TOWARDS the light

    /// <summary>
    /// Whether the key light is locked to the VIEWER instead of to the world
    /// (23.08). Fifty-six modules draw through this scene, and every one of
    /// them lit its apparatus from a constant world direction: orbit the
    /// camera and the highlight travelled with the object, which reads as a
    /// lamp bolted to the experiment. With this on - the default - the light
    /// comes from over the user's left shoulder whatever the camera does, and
    /// <see cref="LightDir"/> is used only as the fallback for a degenerate
    /// view matrix. See <see cref="KeyLight"/>.
    ///
    /// Turn it OFF in a scene whose light is a thing in the world: Pulleys and
    /// StaticElectricity stand under a <see cref="SkyDome"/> that draws the sun
    /// disk at this very direction, and a sun that follows the viewer across
    /// the sky is a worse bug than the one this fixes.
    /// </summary>
    public bool KeyFollowsCamera = true;

    /// <summary>
    /// The direction TOWARDS the light this frame - what <see cref="BeginLit"/>
    /// hands the shader. A module drawing part of the SAME scene through
    /// another program (a textured bench board, a stone, a crate) must pass
    /// this and not <see cref="LightDir"/>: the field is only the fallback, and
    /// a board lit from the world while the apparatus on it is lit from the
    /// viewer shows the seam between them.
    /// </summary>
    public Vec3 KeyDir(Mat4 view) => KeyFollowsCamera ? KeyLight.FromView(view) : LightDir;

    /// <summary>How much environment the surfaces reflect. 1 is a room with a
    /// sky and a floor; a scene set in space turns it down so its metal does
    /// not reflect a horizon that is not there.</summary>
    public float EnvIntensity = 1f;
    public Vec3 LightColor = new(1.22f, 1.18f, 1.10f);
    /// <summary>Raised from 0.2 on 10.08: thirty-five modules inherited the
    /// default and every one of them read as dusk on Michael's screenshots -
    /// the same complaint, module after module, was ONE constant.</summary>
    // 0.50 -> 0.58 (21.08, review): the scene-lit modules (charge-to-mass,
    // elastic rods, the coil bench...) all read brighter from one default;
    // modules that set their own value keep it.
    public float Ambient = 0.58f;
    public float RodThickness = 0.035f;

    public RodChainScene()
    {
        // Build every resource in locals first - if a later one
        // fails, everything already created is released instead of orphaned
        // (the fields are readonly, so the FieldTexture catch-Dispose pattern
        // does not apply directly)
        Mesh? cube = null, sphere = null, tube = null;
        Shader? lit = null, unlit = null;

        try
        {
            // chambered, not sharp: every rod, slab and stand in the suite is
            // drawn with this one mesh, so the bevel lands everywhere at once
            cube = Primitives.BeveledCube(0.04f);
            tube = Primitives.Cylinder(24, capped: true);
            sphere = Primitives.UvSphere(20, 28);
            lit = new Shader(DefaultShaders.LitVertex, DefaultShaders.LitFragment);
            unlit = new Shader(DefaultShaders.UnlitVertex, DefaultShaders.UnlitFragment);
        }
        catch
        {
            cube?.Dispose();
            tube?.Dispose();
            sphere?.Dispose();
            lit?.Dispose();
            unlit?.Dispose();
            throw;
        }

        _cube = cube;
        _tube = tube;
        _sphere = sphere;
        _lit = lit;
        _unlit = unlit;
    }

    /// <param name="bobRadii">radius for joints[i] lives in bobRadii[i-1]; r ≤ 0 skips the bob</param>
    public void Render(
        Mat4 view, Mat4 proj, Vec3 viewPos,
        IReadOnlyList<Vec3> joints,
        IReadOnlyList<float> bobRadii,
        Vec3 rodColor, Vec3 bobColor,
        IReadOnlyList<TrailPoint>? trail = null,
        float rodRoughness = 0.55f, float rodMetallic = 0f,
        float bobRoughness = 0.55f, float bobMetallic = 0f)
    {
        // --- rods and bobs: the lit shader ---
        // The materials are parameters rather than a call before Render,
        // because Render begins with BeginLit and BeginLit resets the surface -
        // anything set outside would be wiped a line later.
        BeginLit(view, proj, viewPos);

        SetMaterial(rodRoughness, rodMetallic);
        for (int i = 0; i + 1 < joints.Count; i++)
        {
            DrawRod(joints[i], joints[i + 1], rodColor);
        }

        SetMaterial(bobRoughness, bobMetallic);
        for (int i = 1; i < joints.Count; i++)
        {
            float r = (bobRadii is not null && i - 1 < bobRadii.Count) ? bobRadii[i - 1] : 0.12f;

            if (r > 0f)
            {
                DrawBob(joints[i], r, bobColor);
            }
        }

        // Trail: ONE streaming line strip with per-vertex fading color.
        // (The previous per-point spheres cost a draw call each - up to 900
        // calls and a million triangles per frame just for a trail.) ---
        if (trail is { Count: > 1 })
        {
            GL.Enable(GL.DEPTH_TEST); // the trail must hide behind bobs and rods
            _trailMesh ??= new ColorLineMesh(TrailMeshCapacity);
            _trailMesh.Update(trail);
            _trailMesh.Draw(view, proj);
        }
    }

    /// <summary>
    /// The same fading line strip Render() draws, for modules that build
    /// their scene out of standalone DrawRod/DrawBob calls instead.
    ///
    /// Render() has drawn trails since the beginning, but only as part of its
    /// joints-and-rods shape, so a module laying out its own geometry had no
    /// way to draw one - and Lagrange, whose whole subject is the PATH a test
    /// particle takes around a libration point, showed a dot that sat there
    /// (09.08).
    ///
    /// Uses its own shader, so it may be called before BeginLit or after the
    /// lit draws; the depth test stays on, which is what makes the trail pass
    /// behind the bodies rather than over them.
    /// </summary>
    public void DrawTrail(IReadOnlyList<TrailPoint> trail, Mat4 view, Mat4 proj)
    {
        if (trail is not { Count: > 1 }) return;
        GL.Enable(GL.DEPTH_TEST);
        _trailMesh ??= new ColorLineMesh(TrailMeshCapacity);
        _trailMesh.Update(trail);
        _trailMesh.Draw(view, proj);
    }

    /// <summary>
    /// Bind the lit shader and set the per-frame uniforms. Call before any
    /// standalone DrawRod/DrawBob calls made outside of Render().
    /// </summary>
    public void BeginLit(Mat4 view, Mat4 proj, Vec3 viewPos)
    {
        GL.Enable(GL.DEPTH_TEST); // defensive: a prior 2D pass may have disabled it
        _lit.Use();
        _lit.SetMatrix4("uView", view);
        _lit.SetMatrix4("uProj", proj);
        _lit.SetVec3("uLightDir", KeyDir(view));
        _lit.SetVec3("uLightColor", LightColor);
        _lit.SetVec3("uViewPos", viewPos);
        _lit.SetFloat("uAmbient", Ambient);
        // reset the surface every frame: otherwise the last material another
        // module set would leak in through the shared shader program
        _lit.SetFloat("uRoughness", 0.55f);
        _lit.SetFloat("uMetallic", 0f);
        _lit.SetFloat("uShadowStrength", 0f); // opt-in per frame, like the material
        _lit.SetFloat("uEnvIntensity", EnvIntensity);
    }

    /// <summary>Draw a single rod between two points (world space). BeginLit must be active.</summary>
    public void DrawRod(Vec3 a, Vec3 b, Vec3 color) => DrawRod(a, b, color, RodThickness);

    /// <summary>
    /// A round rod. Same orientation maths as DrawRod, but a cylinder instead
    /// of a bevelled cube: for a chain of pendulum links a box reads as a
    /// machined bar and is right, while a chemical bond drawn as a box reads
    /// as a girder - the flat faces catch the light in straight bands and the
    /// molecule looks welded together (08.08).
    /// </summary>
    public void DrawTube(Vec3 a, Vec3 b, Vec3 color, float thickness)
    {
        Vec3 d = b - a;
        float len = d.Length();
        if (len < 1e-6f) return;
        Vec3 dir = d * (1f / len);

        Mat4 rot;
        float c = dir.Y;

        if (c > 0.99999f)
        {
            rot = Mat4.Identity;
        }
        else if (c < -0.99999f)
        {
            rot = Mat4.Rotate(Vec3.UnitX, MathF.PI);
        }
        else
        {
            Vec3 axis = Vec3.Cross(Vec3.UnitY, dir).Normalized();
            rot = Mat4.Rotate(axis, MathF.Acos(Math.Clamp(c, -1f, 1f)));
        }

        Target.Use();
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", Mat4.Translate((a + b) * 0.5f) * rot * Mat4.Scale(thickness, len, thickness));
        _tube.Draw();
    }

    /// <summary>Draw a rod of a given thickness. The orientation handles ANY
    /// 3D direction (the previous Z-only rotation flattened out-of-plane
    /// segments - 3D coils rendered as a jumble, tilted posts leaned wrong).</summary>
    public void DrawRod(Vec3 a, Vec3 b, Vec3 color, float thickness)
    {
        Vec3 d = b - a;
        float len = d.Length();
        if (len < 1e-6f) return;
        Vec3 dir = d * (1f / len);

        // rotate the local +Y onto dir: axis = Y × dir, angle from the dot;
        // the antiparallel case flips around X
        Mat4 rot;
        float c = dir.Y; // dot(UnitY, dir)

        if (c > 0.99999f)
        {
            rot = Mat4.Identity;
        }
        else if (c < -0.99999f)
        {
            rot = Mat4.Rotate(Vec3.UnitX, MathF.PI);
        }
        else
        {
            Vec3 axis = Vec3.Cross(Vec3.UnitY, dir).Normalized();
            rot = Mat4.Rotate(axis, MathF.Acos(Math.Clamp(c, -1f, 1f)));
        }

        Vec3 mid = (a + b) * 0.5f;
        Mat4 model =
            Mat4.Translate(mid) *
            rot *
            Mat4.Scale(thickness, len, thickness);

        Target.Use(); // defensive: another mesh may have bound its own program
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", model);
        _cube.Draw();
    }

    /// <summary>The program the drawing helpers write to: the lit shader
    /// normally, the depth shader between BeginDepth and EndDepth. Uniforms
    /// that a program does not declare are silently ignored by GL, so the same
    /// DrawRod/DrawBob calls serve both passes.</summary>
    private Shader Target => _active ?? _lit;

    /// <summary>
    /// Begin the shadow pass: bind the depth-only program and the light matrix,
    /// then issue the SAME draw calls as the normal pass. Cull front faces
    /// while doing it - depth written by back faces puts the acne behind the
    /// surface, where it cannot be seen.
    /// </summary>
    public void BeginDepth(Mat4 lightSpace)
    {
        _depth ??= new Shader(DefaultShaders.DepthVertex, DefaultShaders.DepthFragment);
        _depth.Use();
        _depth.SetMatrix4("uLightSpace", lightSpace);
        _active = _depth;
        GL.Enable(GL.CULL_FACE);
        GL.CullFace(GL.FRONT);
    }

    /// <summary>End the shadow pass and go back to the lit program.</summary>
    public void EndDepth()
    {
        _active = null;
        GL.CullFace(GL.BACK);
    }

    /// <summary>
    /// Hand the lit pass a shadow map. Strength 0 turns shadowing off, which
    /// is also what every module that never calls this gets.
    /// </summary>
    public void UseShadowMap(ShadowMap map, Mat4 lightSpace, float strength = 0.75f, int unit = 3)
    {
        _lit.Use();
        _lit.SetMatrix4("uLightSpace", lightSpace);
        _lit.SetFloat("uShadowStrength", strength);
        _lit.SetInt("uShadowMap", unit);
        GL.ActiveTexture(GL.TEXTURE0 + (uint)unit);
        GL.BindTexture(GL.TEXTURE_2D, map.DepthTexture);
        GL.ActiveTexture(GL.TEXTURE0);
    }

    /// <summary>
    /// Surface material for everything drawn until the next call: roughness
    /// (0.05 mirror ... 1 chalk) and metalness (0 dielectric, 1 metal). The lit
    /// shader treats 0 as "unset" and falls back to the old plastic look, so a
    /// module that never calls this renders exactly as it always did.
    /// </summary>
    public void SetMaterial(float roughness, float metallic)
    {
        // Defensive Use(), like DrawBox/DrawBob/DrawRod: glUniform* writes to
        // the program that is CURRENTLY BOUND, while the location came from
        // _lit. Call this after some other mesh has bound its own shader - a
        // ColorLineMesh, a SphereSprites - and the driver is handed a location
        // belonging to one program and a different program to write it into,
        // which is GL_INVALID_OPERATION. Every other uniform-touching method on
        // this class already guarded against exactly that; this one did not,
        // and it is the only one modules call BETWEEN draws rather than right
        // after BeginLit.
        //
        // It cost a real GlCheck failure (03.08): BarWaves draws its bar as a
        // ColorLineMesh and then asks for MaterialPlastic before the marker
        // sphere, so the error fired in frame 0 on every run. Aurora has the
        // same shape guarded by `if (_model.Precipitated)`, so it fires only
        // once a particle actually reaches the atmosphere - a branch GlCheck's
        // eight frames never enter, which is precisely why fixing this here
        // rather than in the two call sites matters.
        _lit.Use();
        _lit.SetFloat("uRoughness", Math.Clamp(roughness, 0.045f, 1f));
        _lit.SetFloat("uMetallic", Math.Clamp(metallic, 0f, 1f));
        // flat by default: bump is part of the MATERIAL, and without this
        // reset the last bumpy part would smear its relief onto every draw
        // after it
        _lit.SetFloat("uBumpAmount", 0f);
        _lit.SetFloat("uBumpScale", 0f);
    }

    /// <summary>Material with procedural micro-relief (17.08): a world-space
    /// value-noise perturbation of the normal in the shared lit shader, so a
    /// surface can carry texture without a single texture map.</summary>
    public void SetMaterial(float roughness, float metallic, float bump, float bumpScale)
    {
        SetMaterial(roughness, metallic);   // binds _lit and sets the base pair
        _lit.Use();                          // defensive, same reason as above
        _lit.SetFloat("uBumpAmount", Math.Clamp(bump, 0f, 1.5f));
        _lit.SetFloat("uBumpScale", Math.Max(0f, bumpScale));
    }

    /// <summary>Ready-made surfaces, so the modules speak in materials rather
    /// than in numbers.</summary>
    public void MaterialSteel() => SetMaterial(0.30f, 1.0f);
    public void MaterialBrushedMetal() => SetMaterial(0.52f, 1.0f);
    public void MaterialPaintedMetal() => SetMaterial(0.38f, 0.0f);

    /// <summary>Wool: as rough as a surface gets, and no specular at all.
    /// A knitted jumper drawn with the same 0.45 roughness as a metal plate
    /// is a coloured slab, which is exactly what the static-electricity
    /// scene looked like (09.08).</summary>
    public void MaterialWool() => SetMaterial(0.98f, 0.0f);

    /// <summary>Painted plaster: matte, but not as dead as cloth - a wall
    /// still has a faint sheen when the light rakes it.</summary>
    public void MaterialPlaster() => SetMaterial(0.86f, 0.0f);

    /// <summary>Rubber: matte body with a tight highlight, which is what
    /// makes a balloon read as a balloon rather than a red sphere.</summary>
    public void MaterialRubber() => SetMaterial(0.30f, 0.0f);

    /// <summary>Bare copper: the one thing in a schoolroom electrostatics
    /// scene that IS a metal, and it should look like it.</summary>
    public void MaterialCopper() => SetMaterial(0.30f, 1.0f);
    public void MaterialPlastic() => SetMaterial(0.55f, 0.0f);
    public void MaterialChalk() => SetMaterial(0.92f, 0.0f);

    // Bumped presets (17.08): the flat catalogue read as untextured plastic
    // on every close-up. Each carries relief tuned to what the surface IS.
    public void MaterialCastIron() => SetMaterial(0.72f, 0.35f, 0.45f, 22f);
    public void MaterialRoughCeramic() => SetMaterial(0.34f, 0.0f, 0.14f, 18f);
    public void MaterialHammeredMetal() => SetMaterial(0.42f, 1.0f, 0.30f, 9f);
    public void MaterialConcrete() => SetMaterial(0.90f, 0.0f, 0.55f, 26f);

    /// <summary>An axis-aligned box with independent half extents: rods have a
    /// square cross-section by construction, so thin WIDE plates (ground
    /// platforms, floor slabs) were impossible without this - the seismic
    /// tower's ground "slab" rendered as a giant cube for exactly that
    /// reason. BeginLit must be active.</summary>
    public void DrawBox(Vec3 center, Vec3 half, Vec3 color)
    {
        Mat4 model = Mat4.Translate(center) * Mat4.Scale(half.X * 2f, half.Y * 2f, half.Z * 2f);
        Target.Use();   // defensive: another mesh may have bound its own program
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", model);
        _cube.Draw();
    }

    /// <summary>An ORIENTED box: centre, half extents and a rotation matrix -
    /// what rigid-body wreckage renders with. BeginLit must be active.</summary>
    public void DrawOrientedBox(Vec3 center, Vec3 half, Mat4 rotation, Vec3 color)
    {
        Mat4 model = Mat4.Translate(center) * rotation * Mat4.Scale(half.X * 2f, half.Y * 2f, half.Z * 2f);
        Target.Use();   // defensive: another mesh may have bound its own program
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", model);
        _cube.Draw();
    }

    /// <summary>An ellipsoid: a sphere with independent radii. A balloon is
    /// not a ball, and a squashed sphere is the whole difference between
    /// something that reads as rubber and something that reads as a marble.
    /// BeginLit must be active.</summary>
    public void DrawEllipsoid(Vec3 centre, Vec3 radii, Vec3 color)
    {
        Target.Use();
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", Mat4.Translate(centre) * Mat4.Scale(radii.X, radii.Y, radii.Z));
        _sphere.Draw();
    }

    /// <summary>
    /// Draw any mesh the caller owns under the scene's light, with its own
    /// model matrix. The primitive set here is fixed (rod, box, sphere), so a
    /// solid the caller built itself - a cone, a torus, a lathe profile - had
    /// no way in and was faked out of rods instead. A rod is a solid of its
    /// own thickness: on the cloth's cone the 36 lateral rods sat ON the
    /// collider surface and stuck 35 mm out of a sheet resting 15 mm off it.
    /// BeginLit must be active.
    /// </summary>
    public void DrawMesh(Mesh mesh, Mat4 model, Vec3 color)
    {
        Target.Use();   // defensive: another mesh may have bound its own program
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", model);
        mesh.Draw();
    }

    /// <summary>Draw a single sphere (world space). BeginLit must be active.</summary>
    public void DrawBob(Vec3 pos, float r, Vec3 color)
    {
        Target.Use();   // defensive: another mesh may have bound its own program
        Target.SetVec3("uObjectColor", color);
        Target.SetMatrix4("uModel", Mat4.Translate(pos) * Mat4.Scale(r, r, r));
        _sphere.Draw();
    }

    /// <summary>
    /// Fills dst (length = segments + 1) with a planar zig-zag between a and b -
    /// the standard spring/coil visual. side defines the zig-zag plane.
    /// </summary>
    public static void BuildZigzag(Vec3 a, Vec3 b, Vec3 side, float amplitude, Vec3[] dst)
    {
        int segments = dst.Length - 1;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float offset = (i == 0 || i == segments) ? 0f : (i % 2 == 0 ? amplitude : -amplitude);
            dst[i] = a + (b - a) * t + side * offset;
        }
    }

    /// <summary>
    /// Fills dst (length = segments + 1) with a 3D HELIX between a and b - a
    /// real coil spring instead of the flat zigzag. The radial envelope eases
    /// in and out so the wire meets both anchors on the axis. The helix keeps
    /// its number of turns while the ends move, so it visibly stretches and
    /// compresses like a physical spring.
    /// </summary>
    public static void BuildCoil(Vec3 a, Vec3 b, float radius, int turns, Vec3[] dst)
    {
        int segments = dst.Length - 1;
        Vec3 axis = b - a;
        float length = axis.Length();
        Vec3 dir = length > 1e-6f ? axis * (1f / length) : Vec3.UnitX;
        // a stable perpendicular frame around the axis
        Vec3 reference = MathF.Abs(dir.Y) < 0.9f ? Vec3.UnitY : Vec3.UnitX;
        Vec3 u = Vec3.Cross(dir, reference).Normalized();
        Vec3 v = Vec3.Cross(dir, u);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * turns * 2f * MathF.PI;
            // ease the radius to zero over the first/last ~15% of the wire
            float envelope = MathF.Min(1f, MathF.Min(t, 1f - t) / 0.15f);
            dst[i] = a + dir * (length * t) + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * (radius * envelope);
        }
    }

    /// <summary>Draw a coil spring between two anchors as a smooth helix of
    /// thin wire segments. BeginLit must be active. scratch must hold
    /// segments + 1 points (the caller owns it to avoid per-frame garbage).</summary>
    public void DrawSpring(Vec3 a, Vec3 b, float radius, int turns, Vec3 color, Vec3[] scratch)
    {
        BuildCoil(a, b, radius, turns, scratch);
        const float wire = 0.016f;

        for (int i = 0; i + 1 < scratch.Length; i++)
        {
            DrawRod(scratch[i], scratch[i + 1], color, wire);
        }
    }

    public void Dispose()
    {
        _trailMesh?.Dispose();
        _trailMesh = null;
        _cube.Dispose();
        _tube.Dispose();
        _sphere.Dispose();
        _lit.Dispose();
        _depth?.Dispose();
        _unlit.Dispose();
    }
}
