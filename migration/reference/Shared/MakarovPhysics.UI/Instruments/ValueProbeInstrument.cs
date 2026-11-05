using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Probe: a crosshair the user parks anywhere in the scene, with a readout of
/// whatever the module measures at that point. One widget covers the whole
/// family the roadmap lists (field meter, thermometer, pressure gauge,
/// intensity meter, equipotential pen), because they differ only in the
/// sampling function and the caption, never in the interaction.
/// </summary>
public sealed class ValueProbeInstrument : InstrumentWidget
{
    private const float Reach = 13f;
    private readonly Func<Vec3, string> _sample;
    private float _x, _y;

    /// <param name="sample">
    /// Reads the module state at a world point on the measurement plane and
    /// returns the text to show. Formatting stays with the module: only it
    /// knows the quantity, its units and how many digits are honest.
    /// </param>
    public ValueProbeInstrument(Func<Vec3, string> sample)
    {
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        Units = string.Empty;
    }

    protected override int HandleCount => 1;

    protected override void GetHandle(int index, out float x, out float y)
    {
        x = _x;
        y = _y;
    }

    protected override void SetHandle(int index, float x, float y)
    {
        _x = x;
        _y = y;
    }

    protected override float BodyMargin => Reach;

    public override void PlaceDefault(float viewportWidth, float viewportHeight)
    {
        _x = viewportWidth * 0.42f;
        _y = viewportHeight * 0.45f;
        UpdateBounds();
    }

    /// <summary>Moves the crosshair (used by the host and by tests).</summary>
    public void SetTip(float x, float y)
    {
        _x = x;
        _y = y;
        UpdateBounds();
    }

    /// <summary>Current reading, or null when the probe cannot be placed in the scene.</summary>
    public string? Read() => TryPick(_x, _y, out Vec3 world) ? _sample(world) : null;

    protected override bool ContainsBody(float x, float y)
    {
        float dx = x - _x, dy = y - _y;
        return dx * dx + dy * dy <= Reach * Reach;
    }

    public override void Draw(Draw2D d)
    {
        var color = new UiColor(0.95f, 0.85f, 0.45f, 0.95f);

        d.Line(_x - Reach, _y, _x + Reach, _y, color, 1.6f);
        d.Line(_x, _y - Reach, _x, _y + Reach, color, 1.6f);
        d.RectOutline(_x - 4f, _y - 4f, 8f, 8f, color);

        string reading = Read() ?? "--";
        DrawReadout(d, _x + Reach + 4f, _y - Theme.FontSize, reading);
    }
}
