namespace MakarovPhysics.Renderer;

/// <summary>
/// Builds the line geometry of a field arrow lattice. No GL here on purpose:
/// where the arrows go and how long they are is physics presentation, and it
/// is checked against analytic fields in SelfTest rather than by looking at
/// the screen. <see cref="FieldArrowGrid"/> is the thin layer that uploads
/// the result.
///
/// Buffers are allocated once for the given lattice size and refilled in
/// place, because a live field (a magnet falling through a coil) rebuilds
/// this every frame and per-frame garbage in the render loop is exactly the
/// kind of steady per-frame GC noise the UI layer is built to avoid.
/// </summary>
public sealed class FieldArrowGeometry
{
    /// <summary>Shaft plus two head barbs, drawn as GL.LINES pairs.</summary>
    public const int VerticesPerArrow = 6;

    private readonly int _columns;
    private readonly int _rows;
    private readonly Vec3[] _points;
    private readonly float[] _alongU;
    private readonly float[] _alongV;
    private readonly float[] _magnitudes;
    private readonly float[] _scratch;
    private readonly List<TrailPoint> _vertices;

    public FieldArrowGeometry(int columns, int rows)
    {
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));

        _columns = columns;
        _rows = rows;

        int cells = columns * rows;
        _points = new Vec3[cells];
        _alongU = new float[cells];
        _alongV = new float[cells];
        _magnitudes = new float[cells];
        _scratch = new float[cells];
        _vertices = [with(cells * VerticesPerArrow)];
    }

    public int Columns => _columns;

    public int Rows => _rows;

    /// <summary>Line vertices in GL.LINES order, valid until the next Build.</summary>
    public IReadOnlyList<TrailPoint> Vertices => _vertices;

    /// <summary>Arrows the last Build produced (cells with no field are skipped).</summary>
    public int ArrowCount { get; private set; }

    /// <summary>Reference magnitude the last Build scaled by (automatic or given).</summary>
    public float Reference { get; private set; }

    /// <summary>
    /// Samples the field over a rectangle of the plane and rebuilds the arrows.
    /// The rectangle is centered on the plane origin and measured in world
    /// units along its own axes.
    /// </summary>
    /// <param name="sample">
    /// Field vector at a world point. Only the part lying in the plane is
    /// drawn, which is the honest thing to show on a flat picture: a component
    /// sticking out of the plane would otherwise be silently rotated into it.
    /// </param>
    public void Build(
        in ScenePlane plane,
        float halfWidth, float halfHeight,
        Func<Vec3, Vec3> sample,
        in FieldArrowStyle style)
    {
        ArgumentNullException.ThrowIfNull(sample);

        _vertices.Clear();
        ArrowCount = 0;
        Reference = 0f;

        if (!plane.IsValid) return;
        if (!float.IsFinite(halfWidth) || !float.IsFinite(halfHeight)) return;
        if (!(halfWidth > 0f) || !(halfHeight > 0f)) return;

        float cellU = 2f * halfWidth / _columns;
        float cellV = 2f * halfHeight / _rows;
        float maxLength = MathF.Min(cellU, cellV) * Math.Clamp(style.MaxLengthFraction, 0.05f, 1f);

        SampleLattice(plane, halfWidth, halfHeight, cellU, cellV, sample);

        Reference = float.IsFinite(style.Reference) && style.Reference > 0f
            ? style.Reference
            : FieldArrowScale.Median(_scratch);

        EmitArrows(plane, maxLength, style);
    }

    private void SampleLattice(
        in ScenePlane plane, float halfWidth, float halfHeight,
        float cellU, float cellV, Func<Vec3, Vec3> sample)
    {
        for (int row = 0; row < _rows; row++)
        {
            for (int column = 0; column < _columns; column++)
            {
                int index = row * _columns + column;

                // Cell centers, not lattice corners: a lattice through the
                // origin puts a sample exactly on the point source that most
                // of these scenes have sitting there, where the field diverges
                // and takes the automatic reference with it.
                float u = -halfWidth + (column + 0.5f) * cellU;
                float v = -halfHeight + (row + 0.5f) * cellV;

                Vec3 point = plane.FromLocal(u, v);
                _points[index] = point;

                Vec3 field = sample(point);
                float alongU = Vec3.Dot(field, plane.AxisU);
                float alongV = Vec3.Dot(field, plane.AxisV);

                if (!float.IsFinite(alongU) || !float.IsFinite(alongV))
                {
                    alongU = 0f;
                    alongV = 0f;
                }

                _alongU[index] = alongU;
                _alongV[index] = alongV;
                _magnitudes[index] = MathF.Sqrt(alongU * alongU + alongV * alongV);
                _scratch[index] = _magnitudes[index];
            }
        }
    }

    private void EmitArrows(in ScenePlane plane, float maxLength, in FieldArrowStyle style)
    {
        for (int index = 0; index < _magnitudes.Length; index++)
        {
            float magnitude = _magnitudes[index];
            float length = FieldArrowScale.Length(magnitude, Reference, maxLength, style.MinimumLengthFraction);

            // Length is zero exactly when there is no direction to draw, so
            // magnitude is safe to divide by from here on.
            if (!(length > 0f)) continue;

            float unitU = _alongU[index] / magnitude;
            float unitV = _alongV[index] / magnitude;

            Vec3 along = plane.AxisU * unitU + plane.AxisV * unitV;
            Vec3 across = plane.AxisU * -unitV + plane.AxisV * unitU;

            Vec3 center = _points[index];
            Vec3 tail = center - along * (length * 0.5f);
            Vec3 tip = center + along * (length * 0.5f);

            float head = length * 0.32f;
            Vec3 barbBase = tip - along * head;
            Vec3 leftBarb = barbBase + across * (head * 0.5f);
            Vec3 rightBarb = barbBase - across * (head * 0.5f);

            Vec3 color = Vec3.Lerp(style.WeakColor, style.StrongColor,
                FieldArrowScale.Normalize(magnitude, Reference));

            _vertices.Add(new TrailPoint(tail, color));
            _vertices.Add(new TrailPoint(tip, color));
            _vertices.Add(new TrailPoint(tip, color));
            _vertices.Add(new TrailPoint(leftBarb, color));
            _vertices.Add(new TrailPoint(tip, color));
            _vertices.Add(new TrailPoint(rightBarb, color));
            ArrowCount++;
        }
    }
}
