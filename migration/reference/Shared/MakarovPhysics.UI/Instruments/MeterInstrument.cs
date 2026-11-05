using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// A two-lead meter laid over the scene: drag the leads to where the probes
/// go, read what is between them.
///
/// Two leads rather than one, because that is what the quantity is. A
/// voltmeter does not measure the voltage AT a point - there is no such thing
/// without a reference - it measures the difference BETWEEN two points, and an
/// instrument with a single probe would quietly teach otherwise. The same
/// shape serves an ohmmeter and any other differential reading; what the pair
/// of positions means is the module's business, supplied as a callback, since
/// only the module knows what is under the probe.
///
/// A current CLAMP is deliberately not this shape: it reads through a single
/// conductor without breaking it, so it takes one lead and belongs on a
/// different widget. That difference is not a detail of packaging, it is why
/// putting an ammeter across a battery destroys it and putting a clamp round
/// the same wire does not.
/// </summary>
public sealed class MeterInstrument(Func<Vec3, Vec3, string?> read) : InstrumentWidget
{
    private const float BodyWidth = 78f;
    private const float BodyHeight = 34f;
    private readonly Func<Vec3, Vec3, string?> _read = read ?? throw new ArgumentNullException(nameof(read));
    private float _ax, _ay, _bx, _by;

    /// <summary>Caption above the reading, for example "V" or "A".</summary>
    public string Label = "";

    public UiColor LeadA = UiColor.FromBytes(232, 96, 84);
    public UiColor LeadB = UiColor.FromBytes(90, 130, 200);

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

    public override void PlaceDefault(float viewportWidth, float viewportHeight)
    {
        _ax = viewportWidth * 0.34f;
        _ay = viewportHeight * 0.62f;
        _bx = viewportWidth * 0.52f;
        _by = viewportHeight * 0.62f;
        UpdateBounds();
    }

    public override void Draw(Draw2D d)
    {
        if (!Visible) return;

        // The leads, each in its own colour: a differential reading has a sign,
        // and a reader who cannot tell the probes apart cannot interpret it.
        d.Line(_ax, _ay, _bx, _by, Theme.PanelBorder.WithAlpha(0.5f), 1.4f);
        DrawProbe(d, _ax, _ay, LeadA);
        DrawProbe(d, _bx, _by, LeadB);

        float cx = (_ax + _bx) * 0.5f;
        float cy = (_ay + _by) * 0.5f - BodyHeight - 8f;

        d.Rect(cx - BodyWidth / 2, cy, BodyWidth, BodyHeight, BodyColor);
        d.RectOutline(cx - BodyWidth / 2, cy, BodyWidth, BodyHeight, InkColor);

        string reading = "--";
        if (CanMeasure && TryPickHandle(0, out Vec3 a) && TryPickHandle(1, out Vec3 b))
        {
            reading = _read(a, b) ?? "--";
        }

        if (Label.Length > 0)
        {
            d.Text(cx - BodyWidth / 2 + 5f, cy + 3f, Label, InkColor);
        }

        float width = d.MeasureText(reading);
        d.Text(cx - width / 2, cy + BodyHeight - Theme.FontSize - 4f, reading, InkColor);
    }

    private static void DrawProbe(Draw2D d, float x, float y, UiColor color)
    {
        d.Rect(x - 2f, y - 9f, 4f, 18f, color);
        d.Rect(x - 5f, y - 12f, 10f, 4f, color);
    }
}
