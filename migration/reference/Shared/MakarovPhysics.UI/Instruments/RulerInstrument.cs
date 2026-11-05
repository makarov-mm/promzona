using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Ruler laid over the scene: drag it by the body, drag either end to aim it,
/// read the distance between the ends in world units.
///
/// The ticks are placed at round world positions and projected back to screen
/// one by one, not spaced evenly in pixels. Under perspective that difference
/// is the whole point: the far end of the same ruler really is finer than the
/// near end, and a ruler that pretended otherwise would be lying about the
/// scene it is lying on.
/// </summary>
public sealed class RulerInstrument : InstrumentWidget
{
    /// <summary>
    /// Body width in logical pixels. Seen over a 3D scene the first value read
    /// as a plank lying across the experiment rather than as an instrument
    /// held over it: an overlay that hides more of the physics than it
    /// measures is the wrong trade.
    /// </summary>
    private const float Thickness = 17f;
    private const int MaxTicks = 400;
    private float _ax, _ay, _bx, _by;
    protected override int HandleCount => 2;

    protected override void GetHandle(int index, out float x, out float y)
    {
        if (index == 0)
        {
            x = _ax; 
            y = _ay;
        }
        else
        {
            x = _bx;
            y = _by;
        }
    }

    protected override void SetHandle(int index, float x, float y)
    {
        if (index == 0)
        {
            _ax = x; 
            _ay = y;
        }
        else
        {
            _bx = x;
            _by = y;
        }
    }

    protected override float BodyMargin => Thickness * 0.5f + 6f;

    /// <summary>Places both ends at once (used by the host and by tests).</summary>
    public void SetEndpoints(float ax, float ay, float bx, float by)
    {
        _ax = ax;
        _ay = ay;
        _bx = bx;
        _by = by;
        UpdateBounds();
    }

    public override void PlaceDefault(float viewportWidth, float viewportHeight)
    {
        float y = viewportHeight * 0.76f;
        SetEndpoints(viewportWidth * 0.16f, y, viewportWidth * 0.5f, y);
    }

    /// <summary>Distance between the ends in world units. false when unmeasurable.</summary>
    public bool TryMeasure(out float length)
    {
        length = 0f;
        if (!TryPickHandle(0, out Vec3 a) || !TryPickHandle(1, out Vec3 b)) return false;
        length = (b - a).Length();
        return float.IsFinite(length);
    }

    protected override bool ContainsBody(float x, float y) => InstrumentGeometry.DistanceToSegment(x, y, _ax, _ay, _bx, _by) <= Thickness * 0.5f + 3f;

    public override void Draw(Draw2D d)
    {
        float dx = _bx - _ax;
        float dy = _by - _ay;
        float screenLength = MathF.Sqrt(dx * dx + dy * dy);

        if (!(screenLength > 1e-3f) || !float.IsFinite(screenLength))
        {
            DrawHandles(d);
            return;
        }

        float alongX = dx / screenLength, alongY = dy / screenLength;
        float acrossX = -alongY, acrossY = alongX;
        float half = Thickness * 0.5f;

        d.Line(_ax, _ay, _bx, _by, BodyColor, Thickness);
        d.Line(_ax + acrossX * half, _ay + acrossY * half, _bx + acrossX * half, _by + acrossY * half, EdgeColor);
        d.Line(_ax - acrossX * half, _ay - acrossY * half, _bx - acrossX * half, _by - acrossY * half, EdgeColor);

        float worldLength = 0f;
        bool measured = false;

        if (TryPickHandle(0, out Vec3 worldA) && TryPickHandle(1, out Vec3 worldB))
        {
            worldLength = (worldB - worldA).Length();
            measured = float.IsFinite(worldLength) && worldLength > 0f;

            if (measured)
            {
                DrawScale(d, worldA, worldB, worldLength, screenLength, acrossX, acrossY, half);
            }
        }

        string reading = FormatValue(worldLength, measured);
        DrawReadout(d, (_ax + _bx) * 0.5f - 24f, (_ay + _by) * 0.5f - half - 24f, reading);
        DrawHandles(d);
    }

    private void DrawScale(Draw2D d, Vec3 worldA, Vec3 worldB, float worldLength, float screenLength, float acrossX, float acrossY, float half)
    {
        // the scale is graduated in READING units (UnitsPerWorld), the ticks
        // placed back in world length along the ruler
        double unitLength = worldLength * UnitsPerWorld;
        double pixelsPerUnit = screenLength / unitLength;
        double step = InstrumentGeometry.NiceStepAtLeast(unitLength, pixelsPerUnit, 7f);
        if (step <= 0) return;
        int count = (int)Math.Floor(unitLength / step + 1e-6);
        if (count is < 1 or > MaxTicks) return;
        int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)), 0, 3);
        string labelFormat = "0." + new string('0', decimals);
        bool labelsFit = step * pixelsPerUnit * 5 > 30;
        Vec3 direction = worldB - worldA;

        for (int i = 0; i <= count; i++)
        {
            double distance = i * step;
            Vec3 point = worldA + direction * (float)(distance / unitLength);
            if (!Projection.TryWorldToScreen(point, out float x, out float y)) continue;

            bool major = i % 5 == 0;
            float height = major ? half * 0.75f : half * 0.42f;

            d.Line(x - acrossX * half, y - acrossY * half,
                   x - acrossX * (half - height), y - acrossY * (half - height),
                   InkColor);

            if (!major || !labelsFit) continue;

            string label = distance.ToString(labelFormat);
            float width = d.MeasureText(label);
            d.Text(x - width * 0.5f, y - acrossY * half * 0.1f - Theme.FontSize * 0.5f, label, InkColor);
        }
    }
}
