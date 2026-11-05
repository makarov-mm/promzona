namespace MakarovPhysics.Renderer;

/// <summary>
/// The bench the pendulum modules stand on. They were three sticks and a ball
/// floating in a void: the physics was right and the picture said nothing
/// about where any of it was happening. This puts them in a laboratory - a
/// stand bolted to a table, a back wall with a scale, contact shadows, and an
/// angle arc drawn where the protractor would actually sit.
///
/// Everything is emitted as coloured triangles and lines with CPU lighting,
/// so it composes with the plain per-vertex-colour pipeline the pendulum
/// modules already use, and costs one extra draw call each.
/// </summary>
public sealed class LabBench : IDisposable
{
    private readonly ColorLineMesh _solid;
    private readonly ColorLineMesh _shadow;
    private readonly ColorLineMesh _addit;
    private readonly List<TrailPoint> _solidPts = [with(12000)];
    private readonly List<TrailPoint> _shadowPts = [with(2048)];
    private readonly List<TrailPoint> _additPts = [with(6000)];

    private static readonly Vec3 Key = new Vec3(0.40f, 0.78f, 0.48f).Normalized();

    /// <summary>The bench's OWN light, published so a module can hand it back
    /// to Draw for the wood. The furniture is CPU-lit from this direction, and
    /// a table top shaded by a different lamp than the table edge beside it is
    /// exactly the seam this avoids.</summary>
    public static Vec3 KeyDir => Key;
    public static Vec3 KeyColor => new(1.06f, 1.02f, 0.96f);
    // 0.46 -> 0.54 (21.08, review): every bench-lit module brightens with
    // one constant; the key light keeps its direction and colour.
    public const float KeyAmbient = 0.54f;

    /// <summary>
    /// The textured board for the table top, and the cube it is drawn on. Set
    /// it and the top stops being a flat brown box: SEVENTEEN modules stand on
    /// this bench, so the grain arrives in all of them from one place.
    /// Optional - without it the bench draws exactly as before.
    /// </summary>
    public TexturedSurface? Wood { get; set; }
    public Mesh? WoodMesh { get; set; }

    /// <summary>The plaster wall behind the bench. It is the largest surface
    /// in most of these scenes and it was one flat dark rectangle, which is
    /// what made the room read as a backdrop rather than as a room.</summary>
    public TexturedSurface? Wall { get; set; }

    // where BuildRoom put the top, so Draw can lay the boards on it
    private Vec3 _topCentre, _topHalf, _edgeCentre, _edgeHalf, _wallCentre, _wallHalf;
    private bool _hasTop;

    public LabBench()
    {
        ColorLineMesh? solid = null, shadow = null, addit = null;

        try
        {
            solid = new ColorLineMesh(12000) { Mode = GL.TRIANGLES };
            shadow = new ColorLineMesh(2048) { Mode = GL.TRIANGLES };
            addit = new ColorLineMesh(6000) { Mode = GL.LINES };
        }
        catch
        {
            solid?.Dispose();
            shadow?.Dispose();
            addit?.Dispose();
            throw;
        }

        _solid = solid;
        _shadow = shadow;
        _addit = addit;
    }

    private static Vec3 Shade(Vec3 n, Vec3 albedo, float ambient = 0.34f)
    {
        float k = MathF.Max(Vec3.Dot(n, Key), 0f);
        return albedo * (ambient + 0.78f * k);
    }

    private void Box(Vec3 centre, Vec3 half, Vec3 albedo)
    {
        Span<Vec3> normals =
        [
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        ];

        foreach (Vec3 n in normals)
        {
            // build the face frame from the normal
            Vec3 u = MathF.Abs(n.Y) > 0.5f ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
            Vec3 t1 = Vec3.Cross(n, u).Normalized();
            Vec3 t2 = Vec3.Cross(n, t1).Normalized();
            Vec3 e1 = new Vec3(t1.X * half.X, t1.Y * half.Y, t1.Z * half.Z);
            Vec3 e2 = new Vec3(t2.X * half.X, t2.Y * half.Y, t2.Z * half.Z);
            Vec3 c = centre + new Vec3(n.X * half.X, n.Y * half.Y, n.Z * half.Z);
            Vec3 col = Shade(n, albedo);
            Vec3 p00 = c - e1 - e2, p10 = c + e1 - e2, p11 = c + e1 + e2, p01 = c - e1 + e2;

            _solidPts.Add(new TrailPoint(p00, col));
            _solidPts.Add(new TrailPoint(p10, col));
            _solidPts.Add(new TrailPoint(p11, col));
            _solidPts.Add(new TrailPoint(p00, col));
            _solidPts.Add(new TrailPoint(p11, col));
            _solidPts.Add(new TrailPoint(p01, col));
        }
    }

    private void Cylinder(Vec3 centre, float radius, float halfHeight, Vec3 albedo, int segments = 16)
    {
        for (int s = 0; s < segments; s++)
        {
            float a0 = MathF.Tau * s / segments, a1 = MathF.Tau * (s + 1) / segments;
            var n0 = new Vec3(MathF.Cos(a0), 0, MathF.Sin(a0));
            var n1 = new Vec3(MathF.Cos(a1), 0, MathF.Sin(a1));

            Vec3 b0 = centre + n0 * radius - new Vec3(0, halfHeight, 0);
            Vec3 b1 = centre + n1 * radius - new Vec3(0, halfHeight, 0);
            Vec3 t0 = b0 + new Vec3(0, 2 * halfHeight, 0);
            Vec3 t1 = b1 + new Vec3(0, 2 * halfHeight, 0);

            Vec3 c0 = Shade(n0, albedo), c1 = Shade(n1, albedo);
            _solidPts.Add(new TrailPoint(b0, c0));
            _solidPts.Add(new TrailPoint(b1, c1));
            _solidPts.Add(new TrailPoint(t1, c1));
            _solidPts.Add(new TrailPoint(b0, c0));
            _solidPts.Add(new TrailPoint(t1, c1));
            _solidPts.Add(new TrailPoint(t0, c0));

            // cap
            Vec3 capCol = Shade(new Vec3(0, 1, 0), albedo);
            _solidPts.Add(new TrailPoint(centre + new Vec3(0, halfHeight, 0), capCol));
            _solidPts.Add(new TrailPoint(t0, capCol));
            _solidPts.Add(new TrailPoint(t1, capCol));
        }
    }

    /// <summary>
    /// The room: a table top under the apparatus, a back wall with a faint
    /// height scale, and the stand that carries the pivot - a foot on the
    /// table, an upright column and a horizontal arm ending at the pivot.
    /// </summary>
    /// <param name="stand">Build the pendulum stand (foot, column, arm,
    /// pivot boss). Default true - the pendulums that always had it are
    /// unchanged; an experiment with its own apparatus (a clamped string,
    /// a free-standing rig) passes false and gets just the room.</param>
    /// <summary>
    /// depth is the table's half-extent in Z. The default 0.72 reproduces
    /// the historical bench byte for byte (the old back wall constant -0.62
    /// was exactly depth - 0.10); a module whose motion sweeps a circle -
    /// the ball on the spinning arm was the first - passes its orbit radius
    /// so the table carries the orbit and the wall stands behind it instead
    /// of slicing through (11.08).
    /// </summary>
    public void BuildRoom(Vec3 pivot, float tableY, float reach, bool stand = true, float depth = 0.72f)
    {
        _solidPts.Clear();
        _shadowPts.Clear();
        _additPts.Clear();

        // The wall stands BEHIND the experiment, and perspective makes a thing
        // further away cover less screen: at the closest camera the wall needs
        // to be about 25 % wider than the swing merely to sit behind it. The
        // old 1.45 factor covered ±1.20 of swing for a 1 m pendulum, so a bob
        // at 108° crossed the edge and hung against the empty backdrop.
        float halfX = MathF.Max(1.9f, reach * 1.90f);
        float backZ = -(depth - 0.10f);

        // table top and its front edge. With a wood material set they are
        // drawn as BOARDS in Draw instead of as two brown boxes here, which is
        // why the geometry is remembered rather than emitted.
        _topCentre = new Vec3(0, tableY - 0.035f, 0.05f);
        _topHalf = new Vec3(halfX, 0.035f, depth);
        _edgeCentre = new Vec3(0, tableY - 0.10f, depth);
        _edgeHalf = new Vec3(halfX, 0.03f, 0.02f);
        _hasTop = true;

        if (Wood is null || WoodMesh is null || !Wood.Available)
        {
            Box(_topCentre, _topHalf, new Vec3(0.30f, 0.26f, 0.22f));
            Box(_edgeCentre, _edgeHalf, new Vec3(0.38f, 0.33f, 0.27f));
        }

        // back wall
        // The HEIGHT has to follow the length too, which it did not: a bob on a
        // 3 m rod swung 1.4 m above a wall that was always the same 2.8 m tall.
        // A pendulum reaches `reach` above its pivot at 180°, and the pivot is
        // at y = 0, so the wall must clear that with the same margin.
        // The wall runs from the table (y = tableY, itself −reach−0.55 or so) up
        // past the pivot to the bob's highest point (y = +reach). Its HALF is
        // therefore half of that whole span, plus margin - working from the
        // reach alone left a 1.5 m pendulum with its top edge below the bob.
        float spanTop = reach + 0.45f;
        float wallHalfY = MathF.Max(1.55f, (spanTop - tableY) * 0.5f);
        _wallCentre = new Vec3(0, tableY + wallHalfY, backZ - 0.04f);
        _wallHalf = new Vec3(halfX, wallHalfY, 0.04f);
        if (Wall is null || WoodMesh is null || !Wall.Available)
        {
            Box(_wallCentre, _wallHalf, new Vec3(0.135f, 0.145f, 0.165f));
        }

        // the stand: foot, column, arm, and the pivot boss the rod hangs from
        if (stand)
        {
            Box(new Vec3(pivot.X - reach * 0.92f, tableY + 0.02f, 0f), new Vec3(0.16f, 0.022f, 0.16f), new Vec3(0.26f, 0.28f, 0.32f));
            Cylinder(new Vec3(pivot.X - reach * 0.92f, (tableY + pivot.Y) * 0.5f, 0f), 0.028f, (pivot.Y - tableY) * 0.5f, new Vec3(0.42f, 0.45f, 0.50f));
            Box(new Vec3(pivot.X - reach * 0.46f, pivot.Y + 0.035f, 0f), new Vec3(reach * 0.46f, 0.022f, 0.03f), new Vec3(0.42f, 0.45f, 0.50f));
            Cylinder(new Vec3(pivot.X, pivot.Y, 0f), 0.030f, 0.045f, new Vec3(0.55f, 0.58f, 0.64f), 14);
        }

        // a height scale on the wall: ten faint rungs, brighter every fifth
        var tick = new Vec3(0.30f, 0.33f, 0.38f);
        for (int i = 1; i <= 12; i++)
        {
            float y = tableY + i * 0.22f;
            float len = i % 5 == 0 ? 0.13f : 0.07f;
            Vec3 c = tick * (i % 5 == 0 ? 1.6f : 1.0f);
            _additPts.Add(new TrailPoint(new Vec3(-halfX + 0.04f, y, backZ), c));
            _additPts.Add(new TrailPoint(new Vec3(-halfX + 0.04f + len, y, backZ), c));
        }
    }

    /// <summary>
    /// A soft round contact shadow on the table, offset away from the key
    /// light. Multiplicative, so it darkens the table instead of painting a
    /// grey disc on it.
    /// </summary>
    public void AddShadow(Vec3 worldPos, float tableY, float radius)
    {
        float fade = Math.Clamp(1f - (worldPos.Y - tableY) / 2.2f, 0.15f, 1f);
        float r = radius * (1f + (worldPos.Y - tableY) * 0.35f);
        var centre = new Vec3(1f - 0.55f * fade, 1f - 0.55f * fade, 1f - 0.50f * fade);
        var rim = new Vec3(1f, 1f, 1f);
        var c = new Vec3(worldPos.X - 0.12f, tableY + 0.004f, worldPos.Z + 0.10f);

        const int fan = 12;
        for (int k = 0; k < fan; k++)
        {
            float a0 = MathF.Tau * k / fan, a1 = MathF.Tau * (k + 1) / fan;
            _shadowPts.Add(new TrailPoint(c, centre));
            _shadowPts.Add(new TrailPoint(new Vec3(c.X + r * MathF.Cos(a0), c.Y, c.Z + r * MathF.Sin(a0)), rim));
            _shadowPts.Add(new TrailPoint(new Vec3(c.X + r * MathF.Cos(a1), c.Y, c.Z + r * MathF.Sin(a1)), rim));
        }
    }

    /// <summary>The protractor: a graduated arc at the pivot, the swept angle
    /// filled in, and the plumb line the angle is measured from.</summary>
    public void AddProtractor(Vec3 pivot, float radius, double theta, Vec3 accent)
    {
        var faint = new Vec3(0.26f, 0.29f, 0.34f);

        // the plumb line
        _additPts.Add(new TrailPoint(pivot, faint * 1.3f));
        _additPts.Add(new TrailPoint(pivot - new Vec3(0, radius * 1.12f, 0), faint * 1.3f));

        // graduations every ten degrees over ±90°
        for (int deg = -90; deg <= 90; deg += 10)
        {
            float a = (float)(deg * Math.PI / 180.0);
            float inner = deg % 30 == 0 ? radius * 0.90f : radius * 0.955f;
            var dir = new Vec3(MathF.Sin(a), -MathF.Cos(a), 0f);
            Vec3 c = deg % 30 == 0 ? faint * 1.7f : faint;
            _additPts.Add(new TrailPoint(pivot + dir * inner, c));
            _additPts.Add(new TrailPoint(pivot + dir * radius, c));
        }

        // the swept sector, from the plumb line to the current angle
        int steps = Math.Max(2, (int)(Math.Abs(theta) * 26) + 2);
        for (int i = 0; i < steps; i++)
        {
            double a0 = theta * i / steps, a1 = theta * (i + 1) / steps;
            var d0 = new Vec3((float)Math.Sin(a0), (float)-Math.Cos(a0), 0f);
            var d1 = new Vec3((float)Math.Sin(a1), (float)-Math.Cos(a1), 0f);
            _additPts.Add(new TrailPoint(pivot + d0 * radius, accent * 0.85f));
            _additPts.Add(new TrailPoint(pivot + d1 * radius, accent * 0.85f));
        }
    }

    /// <summary>A free line in the additive pass (velocity arrows, markers).</summary>
    public void AddLine(Vec3 a, Vec3 b, Vec3 colour)
    {
        _additPts.Add(new TrailPoint(a, colour));
        _additPts.Add(new TrailPoint(b, colour));
    }

    /// <summary>Draw the bench: solids first (depth on), then the additive
    /// overlay which never writes depth.</summary>
    /// <summary>
    /// Draw, with the camera the wood needs. A bench two or three metres wide
    /// wants boards about 80 cm across, which is 1.2 repeats per metre - the
    /// same number the other benches in the suite use, so a reader moving
    /// between modules sees one workshop rather than several.
    /// </summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, Vec3 lightDir, Vec3 lightColor, float ambient)
    {
        if (_hasTop && Wood is not null && WoodMesh is not null && Wood.Available)
        {
            Wood.Begin(view, proj, eye, lightDir, lightColor, ambient);
            Wood.Draw(WoodMesh, Mat4.Translate(_topCentre) * Mat4.Scale(_topHalf * 2f), 1.2f);
            Wood.Draw(WoodMesh, Mat4.Translate(_edgeCentre) * Mat4.Scale(_edgeHalf * 2f), 1.2f);
        }

        if (_hasTop && Wall is not null && WoodMesh is not null && Wall.Available)
        {
            // Plaster, and DARKENED to the tint the flat wall had: a lab wall
            // behind a lit bench is in shade, and a bright white one would
            // pull the eye off the apparatus. The texture is there for the
            // grain, not for the brightness.
            Wall.Begin(view, proj, eye, lightDir, lightColor, ambient);
            Wall.Draw(WoodMesh, Mat4.Translate(_wallCentre) * Mat4.Scale(_wallHalf * 2f), 0.8f, new Vec3(0.30f, 0.32f, 0.36f));
        }

        Draw(view, proj);
    }

    public void Draw(Mat4 view, Mat4 proj)
    {
        _solid.Update(_solidPts);
        _shadow.Update(_shadowPts);
        _addit.Update(_additPts);

        GL.Enable(GL.DEPTH_TEST);
        GL.Disable(GL.CULL_FACE);

        // 1) the room, opaque. (Sharing a batch with the shadows would have
        //    put the furniture under multiplicative blending and turned the
        //    whole bench into a dark smear - the same trap that swallowed the
        //    hydrostatics glass.)
        _solid.Draw(view, proj);

        // 2) contact shadows: multiply, so they darken whatever they land on
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ZERO, GL.SRC_COLOR);
        GL.DepthMask(false);
        _shadow.Draw(view, proj);

        // 3) overlays: additive, still no depth writes
        GL.BlendFunc(GL.ONE, GL.ONE);
        _addit.Draw(view, proj);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.CULL_FACE);
    }

    public void Dispose()
    {
        // the bench owns whatever was handed to it: a module that sets Wood
        // and WoodMesh should not have to remember to release them too
        Wood?.Dispose();
        Wall?.Dispose();
        WoodMesh?.Dispose();
        _solid.Dispose();
        _shadow.Dispose();
        _addit.Dispose();
    }
}
