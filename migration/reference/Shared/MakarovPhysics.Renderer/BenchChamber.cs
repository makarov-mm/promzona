namespace MakarovPhysics.Renderer;

/// <summary>
/// The lit laboratory enclosure for a 2-D box experiment (05.08, the "dark
/// frames" pass of Docs/VISUALS.md).
///
/// A whole family of modules - ideal gas, states of matter, heat engine,
/// Brownian motion, Drude conduction - is the same picture: particles inside a
/// square, historically drawn as a wireframe rectangle with additive dots on a
/// starfield. That frame measures under three per cent bright pixels: the
/// physics is honest and there is nothing to look at. The fix is not a brighter
/// palette, it is an OBJECT - a steel-framed chamber standing on an oak bench,
/// with a lit back panel the particles can be seen against.
///
/// This class owns that object so the modules do not each own a copy of it: the
/// bench, the back panel, the frame beams, the front glazing rails, the contact
/// shadow and the studio light. A module adds its own apparatus (a piston, a
/// gauge, a heater) through <see cref="Solid"/> and <see cref="Part"/>, which
/// shade under the SAME light - stating it once is a correctness rule, not a
/// convenience (see Materials, 03.08).
///
/// Convention: the experiment plane is z = 0 and the interior spans
/// [-Half, +Half] in x and y, so a module maps its own box onto that square and
/// nothing else needs to know the model's units. The depth is scenery: it makes
/// the frame an object instead of a rectangle, and a 2-D model never sees it.
///
/// GL resources are created in the constructor, so build it from Initialize (on
/// the render thread), never from a module constructor.
/// </summary>
/// <summary>Which side of the chamber a driven boundary sits on.</summary>
public enum ChamberEdge { Left, Right, Bottom, Top }

public sealed class BenchChamber : IDisposable
{
    /// <summary>Interior half-size of the chamber in world units.</summary>
    public float Half { get; init; } = 1.6f;

    /// <summary>Half-depth of the enclosure - scenery, see the class note.</summary>
    public float DepthHalf { get; init; } = 0.36f;

    /// <summary>Thickness of the frame beams.</summary>
    public float Beam { get; init; } = 0.13f;

    /// <summary>Bench slab centre; its top face meets the bottom beam.</summary>
    public float BenchY { get; init; } = -1.82f;

    /// <summary>Plain lit program - modules draw their own parts with it.</summary>
    public Shader Lit { get; }

    /// <summary>Procedural oak (Materials.Wood) - kept for anything that still
    /// wants a function, and as the fallback when the board cannot be loaded.</summary>
    public Shader Wood { get; }

    /// <summary>
    /// The scanned board (Textures/Wood, CC0). Set it and the bench top and
    /// its legs are drawn from the photograph instead of from the function:
    /// EIGHT modules stand on this chamber, so it arrives in all of them at
    /// once. Left null the chamber draws exactly as before.
    /// </summary>
    public TexturedSurface? Board { get; set; }

    /// <summary>Procedural brushed steel (Materials.BrushedMetal) - the frame.</summary>
    public Shader Metal { get; }

    public Mesh Cube { get; }
    public Mesh Cylinder { get; }

    // the contact shadow: its own little patch, drawn MULTIPLICATIVELY
    private readonly ColorLineMesh _shadow;
    private readonly List<TrailPoint> _shadowPts = [with(256)];
    private Mat4 _view, _proj;
    private Vec3 _eye;

    // The studio light of the whole family. One direction, one colour, one
    // ambient: two modules of the same family lit differently look like two
    // different laboratories.
    /// <summary>The family key for this frame, readable so a module drawing
    /// through ANOTHER lit path (RodChainScene has its own program) can state
    /// the same one. Two benches in the same laboratory lit differently look
    /// like two rooms.
    ///
    /// It takes the view matrix because since 23.08 the key is locked to the
    /// VIEWER, not to the world - see <see cref="KeyLight"/>. The old
    /// parameterless property could not be: it answered before the frame knew
    /// where the camera was.</summary>
    public static Vec3 StudioLightDir(Mat4 view) => KeyLight.FromView(view);
    public static Vec3 StudioLightColor => LightColor;
    public static float StudioAmbient => Ambient;
    public static float StudioSkyTint => SkyTint;

    /// <summary>Fallback only: the chamber's key comes from the camera now
    /// (KeyLight), and this is what a degenerate view matrix falls back to.
    /// It is the constant the whole family was tuned under until 23.08.</summary>
    private static readonly Vec3 LightDir = new(0.36f, 0.84f, 0.42f);
    private static readonly Vec3 LightColor = new(1.34f, 1.29f, 1.19f);
    private const float Ambient = 0.62f; // 10.08: screenshots still read as dusk
    private const float SkyTint = 0.80f;

    public BenchChamber()
    {
        // Transactional construction (the Mesh rule): a half-built chamber
        // must not leak GL objects if the second shader fails to compile.
        Shader? lit = null, wood = null, metal = null;
        Mesh? cube = null, cylinder = null;
        ColorLineMesh? shadow = null;

        try
        {
            lit = new Shader(DefaultShaders.LitVertex, DefaultShaders.LitFragment);
            wood = Materials.Wood();
            metal = Materials.BrushedMetal();
            cube = Primitives.Cube();
            cylinder = Primitives.Cylinder(capped: true);
            shadow = new ColorLineMesh(ShadowCells * ShadowCells * 6) { Mode = GL.TRIANGLES };
        }
        catch
        {
            lit?.Dispose();
            wood?.Dispose();
            metal?.Dispose();
            cube?.Dispose();
            cylinder?.Dispose();
            shadow?.Dispose();
            throw;
        }

        Lit = lit;
        Wood = wood;
        Metal = metal;
        Cube = cube;
        Cylinder = cylinder;
        _shadow = shadow;
    }

    /// <summary>Outer half-size of the frame (interior plus one beam).</summary>
    public float Outer => Half + Beam;

    /// <summary>Per-frame camera and light for all three programs. Call once,
    /// before anything is drawn.</summary>
    public void SetCamera(Mat4 view, Mat4 proj, Vec3 eye)
    {
        // kept for the shadow pass, which draws with its own program-less batch
        _view = view;
        _proj = proj;
        _eye = eye;
        Vec3 key = KeyLight.FromView(view);
        ApplyStudioLight(Lit, view, proj, eye);
        Materials.ApplyCamera(Wood, view, proj, eye);
        Materials.ApplyLight(Wood, key, LightColor, Ambient, 1.0f, SkyTint);
        Materials.ApplyCamera(Metal, view, proj, eye);
        Materials.ApplyLight(Metal, key, LightColor, Ambient, 1.0f, SkyTint);
    }

    /// <summary>The studio light of the whole particles-in-a-box family, on any
    /// lit program. A module that keeps its own enclosure - the heat engine's
    /// cylinder is not a square chamber - calls this instead of writing its own
    /// light block, so the family reads as one laboratory rather than five rooms
    /// with different lamps.</summary>
    public static void ApplyStudioLight(Shader lit, Mat4 view, Mat4 proj, Vec3 eye)
    {
        lit.Use();
        lit.SetMatrix4("uView", view);
        lit.SetMatrix4("uProj", proj);
        lit.SetVec3("uLightDir", KeyLight.FromView(view));
        lit.SetVec3("uLightColor", LightColor);
        lit.SetVec3("uViewPos", eye);
        lit.SetFloat("uAmbient", Ambient);
        lit.SetFloat("uEnvIntensity", 1.0f);
        lit.SetFloat("uSkyTint", SkyTint);
    }

    /// <summary>A plainly coloured part under the chamber light.</summary>
    public void Solid(Mesh mesh, Mat4 model, Vec3 colour, float roughness = 0.6f, float metallic = 0f)
    {
        Lit.Use();
        Lit.SetVec3("uObjectColor", colour);
        Lit.SetFloat("uRoughness", roughness);
        Lit.SetFloat("uMetallic", metallic);
        Lit.SetMatrix4("uModel", model);
        mesh.Draw();
    }

    /// <summary>Plain polished steel. The rule this exists for (frames 05.08):
    /// the procedural materials texture in OBJECT space, so a unit cube stretched
    /// 27:1 stretches the pattern 27:1 with it. On a chunky part (a gauge case, a
    /// magnet, a bench slab) that is invisible; on a frame beam it turns brushed
    /// steel into four wide bands and the chamber grows a wooden lintel. One
    /// scalar knob cannot fix it - uBrushScale multiplies all three axes at once,
    /// and the pitch a beam needs along its length is not the pitch it needs
    /// across its width. So the answer is not a compensation but a boundary:
    /// procedural materials go on roughly isotropic parts, long bars get this.
    /// (Same lesson as wood v2, 03.08, arrived at from the other side.)</summary>
    public void Steel(Mesh mesh, Mat4 model) => Solid(mesh, model, new Vec3(0.60f, 0.62f, 0.66f), 0.28f, 0.9f);

    /// <summary>A part in one of the procedural materials (Wood / Metal).</summary>
    public void Part(Shader material, Mesh mesh, Mat4 model)
    {
        material.Use();
        material.SetMatrix4("uModel", model);
        mesh.Draw();
    }

    /// <summary>Oak slab, two legs, and the contact shadow under the frame.
    /// Without the shadow the chamber floats a millimetre above its own bench,
    /// which is the cheapest tell that a scene was assembled, not built.</summary>
    public void DrawBench(float width = 5.9f, float depth = 2.2f, float shadowWidth = 0f, float shadowDepth = 0f, float centreX = 0f)
    {
        // 1.2 repeats per metre is a plank about 80 cm wide - the number every
        // bench in the suite uses, so the family reads as one workshop
        bool board = Board is not null && Board.Available;
        if (board) Board!.Begin(_view, _proj, _eye, KeyLight.FromView(_view), LightColor, Ambient, 1.0f, SkyTint);

        Mat4 topModel = Mat4.Translate(centreX, BenchY, 0.05f) * Mat4.Scale(width, 0.18f, depth);

        if (board)
        {
            Board!.Draw(Cube, topModel, 1.2f);
        }
        else
        {
            Part(Wood, Cube, topModel);
        }

        float legX = width / 2 - 0.70f;
        foreach (float x in new[] { -legX, legX })
        {
            Mat4 legModel = Mat4.Translate(centreX + x, BenchY - 0.62f, 0.05f) * Mat4.Scale(0.20f, 1.06f, 0.30f);

            if (board)
            {
                Board!.Draw(Cube, legModel, 1.2f);
            }
            else
            {
                Part(Wood, Cube, legModel);
            }
        }
        // The shadow defaults to the chamber's own footprint. A module whose
        // apparatus is not the square chamber passes its real footprint: sized
        // to something else, a hard dark rectangle on a bright bench stops
        // reading as a shadow and starts reading as a rubber mat (caught on the
        // heat-engine frame, 05.08).
        float sw = shadowWidth > 0 ? shadowWidth : 2 * Outer + 0.24f;
        float sd = shadowDepth > 0 ? shadowDepth : 1.15f;
        DrawContactShadow(sw, sd, centreX);
    }

    private const int ShadowCells = 8;

    /// <summary>The contact shadow, MULTIPLIED onto the bench instead of laid
    /// on it as a dark slab. Two frames taught this: a flat opaque rectangle
    /// darkens by a fixed amount regardless of what is under it and keeps a
    /// razor edge, which the eye reads as a rubber mat, not as a shadow. A
    /// multiply pass with a soft falloff darkens the wood grain THROUGH itself
    /// and fades to nothing at the rim, which is what contact shadows do.
    /// (Same technique as LabBench; the batch is separate, because sharing one
    /// with the furniture would put the furniture under multiplicative blending
    /// and smear the whole bench.)</summary>
    public void DrawContactShadow(float width, float depth, float centreX = 0f, float centreZ = 0.02f)
    {
        const float darkest = 0.45f; // 1 = no darkening (see ShadowTintAt)
        float y = BenchY + 0.093f;
        _shadowPts.Clear();

        for (int i = 0; i < ShadowCells; i++)
        {
            for (int j = 0; j < ShadowCells; j++)
            {
                float u0 = i / (float)ShadowCells, u1 = (i + 1) / (float)ShadowCells;
                float v0 = j / (float)ShadowCells, v1 = (j + 1) / (float)ShadowCells;
                Vec3 a = ShadowVertex(u0, v0, width, depth, centreX, centreZ, y);
                Vec3 b = ShadowVertex(u1, v0, width, depth, centreX, centreZ, y);
                Vec3 c = ShadowVertex(u1, v1, width, depth, centreX, centreZ, y);
                Vec3 d = ShadowVertex(u0, v1, width, depth, centreX, centreZ, y);
                Vec3 ca = ShadowTint(u0, v0, darkest), cb = ShadowTint(u1, v0, darkest);
                Vec3 cc = ShadowTint(u1, v1, darkest), cd = ShadowTint(u0, v1, darkest);
                // wound a-d-c / a-c-b: normal UP, so the patch survives the
                // host's face culling without the pass having to switch it off
                _shadowPts.Add(new TrailPoint(a, ca));
                _shadowPts.Add(new TrailPoint(d, cd));
                _shadowPts.Add(new TrailPoint(c, cc));
                _shadowPts.Add(new TrailPoint(a, ca));
                _shadowPts.Add(new TrailPoint(c, cc));
                _shadowPts.Add(new TrailPoint(b, cb));
            }
        }

        _shadow.Update(_shadowPts);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ZERO, GL.SRC_COLOR); // dst *= src: a darkening pass
        GL.DepthMask(false);
        _shadow.Draw(_view, _proj);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    private static Vec3 ShadowVertex(float u, float v, float w, float d, float cx, float cz, float y) => new(cx + (u - 0.5f) * w, y, cz + (v - 0.5f) * d);

    private static Vec3 ShadowTint(float u, float v, float darkest)
    {
        float k = ShadowTintAt(u, v, darkest);
        return new Vec3(k, k, k);
    }

    /// <summary>White at the rim (multiply by 1 = no change), darkest in the
    /// middle, smoothstep between - the falloff IS the shadow. Public because
    /// it is the one part of the patch a test can hold without a GL context,
    /// and the rim being exactly 1 is what keeps the shadow from having an
    /// edge.</summary>
    public static float ShadowTintAt(float u, float v, float darkest = 0.45f)
    {
        float du = 1f - MathF.Abs(u - 0.5f) * 2f;
        float dv = 1f - MathF.Abs(v - 0.5f) * 2f;
        float t = MathF.Min(1f, du * 1.35f) * MathF.Min(1f, dv * 1.35f);
        t = t * t * (3f - 2f * t);
        return 1f - (1f - darkest) * t;
    }

    /// <summary>Back panel, three steel beams and the front glazing rails.
    /// <paramref name="leftWall"/> paints the LEFT beam - the wall a thermostat
    /// model rethermalises against - so a boundary condition can be seen instead
    /// of being announced by a lamp bolted on somewhere else. Null leaves it
    /// steel like the rest.
    /// <paramref name="rightGap"/> pushes the right beam outward, for chambers
    /// where a piston travels up to the frame line and two coplanar steel faces
    /// would z-fight exactly where the eye is looking.</summary>
    public void DrawFrame(Vec3? leftWall = null, float rightGap = 0f, Vec3? backColour = null)
    {
        float outer = Outer;
        float d = 2 * DepthHalf + 0.06f;

        // the particles need something to be seen AGAINST: on a starfield a slow
        // (cool, dark) particle is a dark dot on a dark sky
        Solid(Cube, Mat4.Translate(0, 0, -DepthHalf - 0.06f) * Mat4.Scale(2 * outer, 2 * outer, 0.10f), backColour ?? new Vec3(0.50f, 0.52f, 0.55f), 0.92f);

        Steel(Cube, Mat4.Translate(0, outer - Beam / 2, 0) * Mat4.Scale(2 * outer, Beam, d));
        Steel(Cube, Mat4.Translate(0, -(outer - Beam / 2), 0) * Mat4.Scale(2 * outer, Beam, d));
        Steel(Cube, Mat4.Translate(outer - Beam / 2 + rightGap, 0, 0) * Mat4.Scale(Beam, 2 * Half, d));

        if (leftWall is { } wall)
        {
            Solid(Cube, Mat4.Translate(-(outer - Beam / 2), 0, 0) * Mat4.Scale(Beam, 2 * Half, d), wall, 0.45f);
        }
        else
        {
            Steel(Cube, Mat4.Translate(-(outer - Beam / 2), 0, 0) * Mat4.Scale(Beam, 2 * Half, d));
        }

        // glazing rails: they give the open face an edge and read as a case
        foreach (float y in (float[])[outer - Beam - 0.02f, -(outer - Beam - 0.02f)])
        {
            Steel(Cube, Mat4.Translate(0, y, DepthHalf + 0.02f) * Mat4.Scale(2 * Half, 0.05f, 0.05f));
        }
    }

    /// <summary>A lit back wall on its own, for a module whose enclosure is NOT
    /// a square chamber (the heat engine's cylinder) but which still needs
    /// something for the experiment to be seen against. Same reasoning as the
    /// chamber's own back panel: on a dark room a dark particle is invisible,
    /// and half the colour scale stops meaning anything.</summary>
    /// The colour is DARKER than the chamber's own back panel by default: that
    /// one is a small square seen through a frame, this one is a standing sheet
    /// several units wide, and at the same lightness it takes over the frame
    /// (heat-engine frame, 05.08).
    public void DrawBackPanel(float width, float height, float z, float centreY = 0f,
        Vec3 colour = default, float centreX = 0f)
    {
        Vec3 c = colour.LengthSquared() > 1e-6f ? colour : new Vec3(0.33f, 0.35f, 0.38f);
        Solid(Cube, Mat4.Translate(centreX, centreY, z) * Mat4.Scale(width, height, 0.10f), c, 0.92f);
    }

    /// <summary>Two slanted highlight streaks across the opening, appended as
    /// additive triangles. There is no glass mesh - transparency ordering costs
    /// more than it buys here - and the streaks are what a viewer reads as glass
    /// anyway.</summary>
    public void AddGlassSheen(List<TrailPoint> quads, float brightness = 1f)
    {
        Vec3 sheen = new Vec3(0.12f, 0.14f, 0.17f) * brightness;
        float z = DepthHalf + 0.03f;

        AddQuad4(quads,
            new Vec3(-Half + 0.10f, Half, z),
            new Vec3(-Half + 0.45f, Half, z),
            new Vec3(-Half + 0.05f, -Half, z),
            new Vec3(-Half - 0.30f, -Half, z),
            sheen);

        AddQuad4(quads,
            new Vec3(-Half + 0.62f, Half, z),
            new Vec3(-Half + 0.76f, Half, z),
            new Vec3(-Half + 0.36f, -Half, z),
            new Vec3(-Half + 0.22f, -Half, z),
            sheen * 0.7f);
    }

    /// <summary>Feet lifting the chamber off the bench and a heater tray in the
    /// gap: the apparatus for a model whose thermostat drives the WHOLE sample
    /// (a Berendsen or Andersen bath) rather than one wall. Pass null for the
    /// element colour when the run is insulated - an un-driven bath must look
    /// un-driven, or the scene teaches that energy is being pumped in when the
    /// model is conserving it. Needs a chamber built with a lower BenchY: the
    /// gap is what is left between the bottom beam and the bench top.</summary>
    public void DrawHeatBath(Vec3? element)
    {
        float top = -Outer; // underside of the bottom beam
        float benchTop = BenchY + 0.09f;
        float gap = top - benchTop;
        if (gap <= 0.02f) return; // no room: chamber sits flat

        foreach (float x in (float[])[-(Outer - 0.22f), Outer - 0.22f])
        {
            Part(Metal, Cube, Mat4.Translate(x, benchTop + gap / 2, 0) * Mat4.Scale(0.16f, gap, 2 * DepthHalf));
        }

        Steel(Cube, Mat4.Translate(0, benchTop + 0.05f, 0) * Mat4.Scale(2 * Half * 0.78f, 0.10f, 2 * DepthHalf));

        if (element is { } c)
        {
            Solid(Cube, Mat4.Translate(0, benchTop + 0.13f, 0) * Mat4.Scale(2 * Half * 0.70f, 0.06f, 2 * DepthHalf * 0.8f), c, 0.45f);
        }
    }

    /// <summary>Heat bleeding off a driven edge into the box: ONE additive band,
    /// bright at the boundary and fading to nothing inward. It used to be three
    /// stacked bands of constant colour, and on the BOTTOM edge (a heat bath
    /// under the chamber) the outermost band's hard upper edge read as a liquid
    /// level - a glowing floor is heat, a flat line across the box is water.
    /// Per-vertex colour makes the gradient free.
    ///
    /// Called only while the bath is actually driving: an insulated boundary
    /// glows exactly nothing, and a scene that glows anyway teaches the
    /// opposite.</summary>
    public void AddEdgeHeat(List<TrailPoint> quads, Vec3 heat, ChamberEdge edge = ChamberEdge.Left, float reach = 0.42f)
    {
        const float z = 0.02f;
        Vec3 fade = Vec3.Zero;

        switch (edge)
        {
            case ChamberEdge.Left:
                AddQuadShaded(quads, new Vec3(-Half, -Half, z), new Vec3(-Half + reach, -Half, z), new Vec3(-Half + reach, Half, z), new Vec3(-Half, Half, z), fade, heat, fade, heat);
                break;

            case ChamberEdge.Right:
                AddQuadShaded(quads, new Vec3(Half - reach, -Half, z), new Vec3(Half, -Half, z), new Vec3(Half, Half, z), new Vec3(Half - reach, Half, z), fade, heat, heat, fade);
                break;

            case ChamberEdge.Bottom:
                AddQuadShaded(quads, new Vec3(-Half, -Half, z), new Vec3(Half, -Half, z), new Vec3(Half, -Half + reach, z), new Vec3(-Half, -Half + reach, z), heat, heat, fade, fade);
                break;

            default:
                AddQuadShaded(quads, new Vec3(-Half, Half - reach, z), new Vec3(Half, Half - reach, z), new Vec3(Half, Half, z), new Vec3(-Half, Half, z), fade, fade, heat, heat);
                break;
        }
    }

    /// <summary>An axis-aligned quad in the plane z, wound for the camera side.</summary>
    public static void AddQuad(List<TrailPoint> quads, float x0, float y0, float x1, float y1, float z, Vec3 colour) => AddQuad4(quads, new Vec3(x0, y0, z), new Vec3(x1, y0, z), new Vec3(x1, y1, z), new Vec3(x0, y1, z), colour);

    /// <summary>Quad a→b→c→d with a colour PER CORNER - the batch already
    /// carries per-vertex colour, so a gradient costs nothing and is the
    /// difference between a glow and a painted stripe.</summary>
    public static void AddQuadShaded(List<TrailPoint> quads, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 ca, Vec3 cb, Vec3 cc, Vec3 cd)
    {
        quads.Add(new TrailPoint(a, ca));
        quads.Add(new TrailPoint(b, cb));
        quads.Add(new TrailPoint(c, cc));
        quads.Add(new TrailPoint(a, ca));
        quads.Add(new TrailPoint(c, cc));
        quads.Add(new TrailPoint(d, cd));
    }

    /// <summary>Quad a→b→c→d, counter-clockwise seen from +Z (the camera side).</summary>
    public static void AddQuad4(List<TrailPoint> quads, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 colour)
    {
        quads.Add(new TrailPoint(a, colour));
        quads.Add(new TrailPoint(b, colour));
        quads.Add(new TrailPoint(c, colour));
        quads.Add(new TrailPoint(a, colour));
        quads.Add(new TrailPoint(c, colour));
        quads.Add(new TrailPoint(d, colour));
    }

    public void Dispose()
    {
        _shadow.Dispose();
        Lit.Dispose();
        Board?.Dispose();
        Wood.Dispose();
        Metal.Dispose();
        Cube.Dispose();
        Cylinder.Dispose();
    }
}
