using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Protractor: a vertex and two arms the user aims at whatever the angle is
/// between (an incline and the horizon, an incident ray and the normal, two
/// forces). The angle is computed in the plane's own coordinates rather than
/// from the screen positions, so an orbited camera reports the angle in the
/// scene instead of its foreshortened shadow.
/// </summary>
public sealed class ProtractorInstrument : InstrumentWidget
{
    private const int ArcSegments = 28;
    private float _vertexX, _vertexY;
    private float _firstX, _firstY;
    private float _secondX, _secondY;

    public ProtractorInstrument()
    {
        Units = "\u00B0";
        Format = "0.0";
    }

    protected override int HandleCount => 3;

    protected override void GetHandle(int index, out float x, out float y)
    {
        switch (index)
        {
            case 0: x = _vertexX; y = _vertexY; break;
            case 1: x = _firstX; y = _firstY; break;
            default: x = _secondX; y = _secondY; break;
        }
    }

    protected override void SetHandle(int index, float x, float y)
    {
        switch (index)
        {
            case 0: _vertexX = x; _vertexY = y; break;
            case 1: _firstX = x; _firstY = y; break;
            default: _secondX = x; _secondY = y; break;
        }
    }

    /// <summary>Places all three points at once (used by the host and by tests).</summary>
    public void SetPoints(float vertexX, float vertexY, float firstX, float firstY, float secondX, float secondY)
    {
        _vertexX = vertexX;
        _vertexY = vertexY;
        _firstX = firstX;
        _firstY = firstY;
        _secondX = secondX;
        _secondY = secondY;
        UpdateBounds();
    }

    public override void PlaceDefault(float viewportWidth, float viewportHeight)
    {
        float vx = viewportWidth * 0.30f;
        float vy = viewportHeight * 0.62f;
        float arm = MathF.Min(viewportWidth, viewportHeight) * 0.20f;
        SetPoints(vx, vy, vx + arm, vy, vx + arm * 0.71f, vy - arm * 0.71f);
    }

    /// <summary>Angle at the vertex in degrees (0..180). false when unmeasurable.</summary>
    public bool TryMeasure(out float degrees)
    {
        degrees = 0f;
        if (!TryPickHandle(0, out Vec3 vertex)
            || !TryPickHandle(1, out Vec3 first)
            || !TryPickHandle(2, out Vec3 second))
        {
            return false;
        }

        (float vertexU, float vertexV) = Plane.ToLocal(vertex);
        (float firstU, float firstV) = Plane.ToLocal(first);
        (float secondU, float secondV) = Plane.ToLocal(second);

        degrees = InstrumentGeometry.AngleDegrees(vertexU, vertexV, firstU, firstV, secondU, secondV);
        return float.IsFinite(degrees);
    }

    protected override bool ContainsBody(float x, float y)
    {
        const float reach = 5f;

        return 
            InstrumentGeometry.DistanceToSegment(x, y, _vertexX, _vertexY, _firstX, _firstY) <= reach
            ||
            InstrumentGeometry.DistanceToSegment(x, y, _vertexX, _vertexY, _secondX, _secondY) <= reach;
    }

    public override void Draw(Draw2D d)
    {
        var armColor = new UiColor(0.93f, 0.90f, 0.72f, 0.95f);

        d.Line(_vertexX, _vertexY, _firstX, _firstY, armColor, 2.5f);
        d.Line(_vertexX, _vertexY, _secondX, _secondY, armColor, 2.5f);

        DrawArc(d);

        bool measured = TryMeasure(out float degrees);
        DrawReadout(d, _vertexX + 12f, _vertexY + 12f, FormatValue(degrees, measured));
        DrawHandles(d);
    }

    private void DrawArc(Draw2D d)
    {
        float firstDx = _firstX - _vertexX, firstDy = _firstY - _vertexY;
        float secondDx = _secondX - _vertexX, secondDy = _secondY - _vertexY;

        float firstLength = MathF.Sqrt(firstDx * firstDx + firstDy * firstDy);
        float secondLength = MathF.Sqrt(secondDx * secondDx + secondDy * secondDy);
        if (!(firstLength > 1e-3f) || !(secondLength > 1e-3f)) return;

        float radius = MathF.Min(MathF.Min(firstLength, secondLength) * 0.42f, 70f);
        if (radius < 6f) return;

        float startAngle = MathF.Atan2(firstDy, firstDx);
        float endAngle = MathF.Atan2(secondDy, secondDx);

        // Sweep the short way around, which is the angle the reading names.
        float sweep = endAngle - startAngle;
        while (sweep > MathF.PI) sweep -= 2f * MathF.PI;
        while (sweep < -MathF.PI) sweep += 2f * MathF.PI;

        var arcColor = new UiColor(0.93f, 0.90f, 0.72f, 0.65f);
        float previousX = _vertexX + MathF.Cos(startAngle) * radius;
        float previousY = _vertexY + MathF.Sin(startAngle) * radius;

        for (int i = 1; i <= ArcSegments; i++)
        {
            float angle = startAngle + sweep * i / ArcSegments;
            float x = _vertexX + MathF.Cos(angle) * radius;
            float y = _vertexY + MathF.Sin(angle) * radius;
            d.Line(previousX, previousY, x, y, arcColor, 1.6f);
            previousX = x;
            previousY = y;
        }
    }
}
