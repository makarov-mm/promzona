using MakarovPhysics.Base;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Stopwatch the user drags onto the scene: a dial with Start/Stop, Lap and
/// Reset. It reads the module clock through a callback, so it freezes with the
/// experiment and follows the time scale without knowing anything about either.
/// </summary>
public sealed class StopwatchInstrument : InstrumentWidget
{
    private const float Width = 168f;
    private const float Height = 86f;
    private const float ButtonHeight = 22f;
    private const float Padding = 8f;

    private readonly Func<double> _simulationTime;
    private float _x, _y;

    /// <summary>Button captions; the host fills them from the translations.</summary>
    public string StartLabel = "Start";
    public string StopLabel = "Stop";
    public string LapLabel = "Lap";
    public string ResetLabel = "Reset";

    public StopwatchInstrument(Func<double> simulationTime)
    {
        _simulationTime = simulationTime ?? throw new ArgumentNullException(nameof(simulationTime));
        Units = "s";
    }

    public StopwatchState State { get; } = new();

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

    /// <summary>The whole panel is the body; the corner anchor is not a grab point.</summary>
    protected override float BodyMargin => 0f;

    public override void PlaceDefault(float viewportWidth, float viewportHeight)
    {
        _x = viewportWidth * 0.06f;
        _y = viewportHeight * 0.20f;
        UpdateBounds();
    }

    protected override void MeasureExtent(out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = _x;
        minY = _y;
        maxX = _x + Width;
        maxY = _y + Height;
    }

    /// <summary>Samples the module clock. InstrumentSet calls this every frame.</summary>
    public void Tick() => State.Sample(_simulationTime());

    public override void Draw(Draw2D d)
    {
        UpdateBounds();

        d.Rect(Rect.X, Rect.Y, Rect.W, Rect.H, new UiColor(0.05f, 0.06f, 0.08f, 0.90f));
        d.RectOutline(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBorder);

        string reading = State.Elapsed.ToString("0.00") + " " + Units;
        d.Text(Rect.X + Padding, Rect.Y + Padding, reading, State.Running ? Theme.Good : Theme.Text);

        float lapX = Rect.X + Padding + d.MeasureText(reading) + 10f;
        float lapY = Rect.Y + Padding;

        foreach (double lap in State.Laps)
        {
            d.Text(lapX, lapY, lap.ToString("0.00"), Theme.TextDim);
            lapY += Theme.FontSize + 1f;
        }

        RunRect(out UiRect run);
        LapRect(out UiRect lapRect);
        ResetRect(out UiRect reset);

        DrawButton(d, run, State.Running ? StopLabel : StartLabel);
        DrawButton(d, lapRect, LapLabel);
        DrawButton(d, reset, ResetLabel);
    }

    private void DrawButton(Draw2D d, UiRect rect, string text)
    {
        d.Rect(rect.X, rect.Y, rect.W, rect.H, Theme.ControlBackground);
        d.RectOutline(rect.X, rect.Y, rect.W, rect.H, Theme.PanelBorder);
        float width = d.MeasureText(text);
        d.Text(rect.X + (rect.W - width) * 0.5f, rect.Y + (rect.H - Theme.FontSize) * 0.5f - 1f, text, Theme.Text);
    }

    private void RunRect(out UiRect rect) => ButtonRect(0, out rect);

    private void LapRect(out UiRect rect) => ButtonRect(1, out rect);

    private void ResetRect(out UiRect rect) => ButtonRect(2, out rect);

    private void ButtonRect(int index, out UiRect rect)
    {
        const int count = 3;
        float spacing = 6f;
        float width = (Width - 2f * Padding - (count - 1) * spacing) / count;

        rect = new UiRect(
            _x + Padding + index * (width + spacing),
            _y + Height - Padding - ButtonHeight,
            width,
            ButtonHeight);
    }

    /// <summary>
    /// Buttons act on press, like the rest of the UI layer, and swallow the
    /// event so that hitting Lap does not also start dragging the dial.
    /// </summary>
    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;

        RunRect(out UiRect run);
        LapRect(out UiRect lap);
        ResetRect(out UiRect reset);

        if (run.Contains(x, y))
        {
            State.Toggle(); 
            return true;
        }

        if (lap.Contains(x, y))
        {
            State.Lap();
            return true;
        }

        if (reset.Contains(x, y))
        {
            State.Reset(); 
            return true;
        }

        return base.OnMouseDown(x, y, b);
    }
}
