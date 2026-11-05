using MakarovPhysics.Base;
using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>
/// y(x) chart with autoscale, grid and labels. A UI-layer widget -
/// added to a Panel like any other control.
/// </summary>
public class LineChart : Widget
{
    public readonly List<Series> Series = [];

    /// <summary>Series drawn as small SQUARE MARKERS instead of a polyline -
    /// scatter data: Poincaré sections, allowed lattice modes, sample dots.</summary>
    public readonly List<Series> Points = [];

    /// <summary>
    /// Frozen copies of the live curves, kept for comparison and drawn
    /// underneath them, oldest first.
    ///
    /// A saved curve without its caption is just a line, so each one carries
    /// the label the module gave it (the temperature, the angle, whatever was
    /// changed). And the frozen curves take part in to autoscale: a snapshot
    /// that got clipped the moment the parameter moved would be worse than no
    /// snapshot at all, because the eye would compare two curves drawn to
    /// different rulers.
    /// </summary>
    public readonly List<Series> Snapshots = [];

    /// <summary>How many frozen curves are kept before the oldest is dropped.</summary>
    public int MaxSnapshots = 3;

    public string Title = string.Empty;

    /// <summary>
    /// What the chart says while it has nothing to draw yet.
    ///
    /// A framed empty box reads as a broken chart, and several modules have an
    /// honest reason to stay empty for a while: the Doppler observer cannot
    /// report a frequency until a wavefront has actually reached it, and a
    /// spectrum cannot exist before its window has filled. The fix is NOT to
    /// draw something - a seeded curve would be an invention - but to say what
    /// is being waited for, so the reader knows the instrument is working and
    /// roughly how long the wait is.
    ///
    /// Set it per frame from the module (the text usually carries a countdown
    /// or a percentage); an empty string draws nothing, as before.
    /// </summary>
    public string EmptyHint = string.Empty;

    /// <summary>Chart height before the optional legend footer is added.</summary>
    public float Height = 140;
    /// <summary>Show named live, scatter and frozen series below the plot.</summary>
    public bool ShowLegend = true;
    /// <summary>Read the nearest real sample while the pointer is in the plot.</summary>
    public bool ShowCursor = true;
    private readonly ChartLegend _legend = new();
    private float _mouseX = float.NaN, _mouseY = float.NaN;
    private double _cursorX = double.NaN, _cursorY = double.NaN;
    private string _cursorXLabel = string.Empty, _cursorYLabel = string.Empty;
    private double _labelledMin = double.NaN, _labelledMax = double.NaN;
    private string _minLabel = string.Empty, _maxLabel = string.Empty;
    public bool AutoScale = true;
    public double MinX, MaxX = 1, MinY, MaxY = 1; // used when AutoScale = false

    /// <summary>Minimum autoscale span as a fraction of the value's own
    /// magnitude (0 = off). Set by modules that plot a conserved quantity, so
    /// numerical ripple is not blown up to full chart height.</summary>
    public double RelativeSpanFloor;

    public override float PreferredHeight(float w)
    {
        MeasureLegend(w, Ui?.Draw);
        return Height + _legend.Height;
    }

    /// <summary>Phase portraits draw only live trajectories and scatter data.</summary>
    protected virtual bool IncludesSnapshots => true;

    private void MeasureLegend(float width, Draw2D? d) =>
        _legend.Update(Series, Points, Snapshots, ShowLegend, IncludesSnapshots,
            width, Theme.FontSize, Ui?.Scale ?? 1f, d);

    /// <summary>The plot keeps its height when a measured legend is added.</summary>
    protected UiRect PlotRect => new(Rect.X + 6, Rect.Y + PlotTop,
        Math.Max(0, Rect.W - 12), Math.Max(0, Rect.H - 26 - _legend.Height));

    /// <summary>Shared frame, bounded title and measured legend footer.</summary>
    protected void DrawFrame(Draw2D d)
    {
        float previousHeight = _legend.Height;
        MeasureLegend(Rect.W, d);
        if (previousHeight != _legend.Height) RequestLayout();
        UiRect r = Rect;
        d.Rect(r.X, r.Y, r.W, r.H, UiColor.FromBytes(18, 18, 21));
        d.RectOutline(r.X, r.Y, r.W, r.H, Theme.PanelBorder);
        d.Text(r.X + 6, r.Y + 4, ChartLegend.Fit(Title, r.W - 12, Theme.FontSize, d), Theme.TextDim);
        if (_legend.Height == 0) return;

        float top = r.Y + r.H - _legend.Height;
        if (top < r.Y + PlotTop) return;
        d.Rect(r.X + 6, top, Math.Max(0, r.W - 12), 1, Theme.PanelBorder);
        foreach (var entry in _legend.Entries)
        {
            float x = r.X + 6 + entry.X, y = top + 4 + entry.Y;
            if (y + Theme.FontSize > r.Y + r.H) break;
            UiColor color = entry.Series.Color;
            float cy = y + Theme.FontSize * 0.5f;
            if (entry.Point) d.Rect(x + 5, cy - 2, 4, 4, color);
            else if (entry.Frozen)
            {
                color = color.WithAlpha(0.65f);
                d.Line(x, cy, x + 5, cy, color, 1.5f);
                d.Line(x + 9, cy, x + 14, cy, color, 1.5f);
            }
            else d.Line(x, cy, x + 14, cy, color, 2);
            d.Text(x + 20, y, entry.Text, entry.Frozen ? Theme.TextDim : Theme.Text);
        }
    }

    /// <summary>Freezes the current curves under a caption.</summary>
    public void TakeSnapshot(string label)
    {
        foreach (Series live in Series)
        {
            if (live.Count < 2) continue;
            string caption = live.Name.Length > 0 ? $"{live.Name} · {label}" : label;
            var frozen = new Series(live.Count, caption, live.Color);

            for (int i = 0; i < live.Count; i++)
            {
                (double x, double y) = live.At(i);
                frozen.Add(x, y);
            }

            Snapshots.Add(frozen);
        }

        // Trim by whole snapshots: a chart with two live curves saves two
        // frozen ones at a time, and dropping half a pair would leave a
        // caption attached to a curve that is no longer there.
        int perShot = Math.Max(1, Series.Count(s => s.Count >= 2));
        while (Snapshots.Count > MaxSnapshots * perShot) Snapshots.RemoveAt(0);
    }

    public void ClearSnapshots() => Snapshots.Clear();

    /// <summary>Marker half-size for the Points series, logical pixels.</summary>
    protected const float MarkerHalf = 1.6f;

    /// <summary>Top edge of the plotting area: below the title strip.
    /// Public because <see cref="Histogram"/> lays out to the same strip.</summary>
    public const float PlotTop = 20f;

    /// <summary>
    /// True once anything at all can be drawn. A polyline needs two points; a
    /// scatter series shows from the first one.
    /// </summary>
    protected bool HasDrawableData
    {
        get
        {
            foreach (var s in Series) if (s.Count >= 2) return true;
            if (IncludesSnapshots)
                foreach (var s in Snapshots) if (s.Count >= 2) return true;
            foreach (var s in Points) if (s.Count >= 1) return true;
            return false;
        }
    }

    /// <summary>Centres <see cref="EmptyHint"/> in the plotting area.</summary>
    protected void DrawEmptyHint(Draw2D d)
    {
        if (EmptyHint.Length == 0) return;
        UiRect plot = PlotRect;
        if (plot.H < Theme.FontSize) return;
        string text = ChartLegend.Fit(EmptyHint, plot.W, Theme.FontSize, d);
        float x = plot.X + (plot.W - d.MeasureText(text)) * 0.5f;
        float y = plot.Y + (plot.H - Theme.FontSize) * 0.5f;
        d.Text(x, y, text, Theme.TextDim);
    }

    /// <summary>Draw every Points series as markers (shared by chart flavours).</summary>
    protected void DrawPoints(Draw2D d, double minX, double maxX, double minY, double maxY, float px, float py, float pw, float ph)
    {
        foreach (var s in Points)
        {
            for (int i = 0; i < s.Count; i++)
            {
                (double x, double y) = s.At(i);
                float sx = px + (float)ChartScale.Fraction(x, minX, maxX) * pw;
                float sy = py + ph - (float)ChartScale.Fraction(y, minY, maxY) * ph;
                if (sx < px || sx > px + pw || sy < py || sy > py + ph) continue;
                d.Rect(sx - MarkerHalf, sy - MarkerHalf, 2 * MarkerHalf, 2 * MarkerHalf, s.Color);
            }
        }
    }

    /// <summary>Extend the autoscale bounds with the Points series (min 1 point).</summary>
    protected void IncludePointBounds(ref double minX, ref double maxX, ref double minY, ref double maxY)
    {
        foreach ((double minX, double maxX, double minY, double maxY) b in from s in Points where s.Count >= 1 select s.Bounds())
        {
            minX = Math.Min(minX, b.minX); maxX = Math.Max(maxX, b.maxX);
            minY = Math.Min(minY, b.minY); maxY = Math.Max(maxY, b.maxY);
        }
    }

    public override void Draw(Draw2D d)
    {
        DrawFrame(d);

        // Nothing to draw yet: say what is being waited for instead of leaving
        // an empty frame. Checked before the bounds, because a fixed-scale
        // chart (AutoScale = false) has valid bounds and no data at all.
        if (!HasDrawableData) { DrawEmptyHint(d); return; }

        double minX = MinX, maxX = MaxX, minY = MinY, maxY = MaxY;
        if (AutoScale)
        {
            minX = double.MaxValue; maxX = double.MinValue;
            minY = double.MaxValue; maxY = double.MinValue;

            foreach (var s in Series)
            {
                if (s.Count < 2) continue;
                var b = s.Bounds();
                minX = Math.Min(minX, b.minX); maxX = Math.Max(maxX, b.maxX);
                minY = Math.Min(minY, b.minY); maxY = Math.Max(maxY, b.maxY);
            }

            foreach (var s in Snapshots)
            {
                if (s.Count < 2) continue;
                var b = s.Bounds();
                minX = Math.Min(minX, b.minX); maxX = Math.Max(maxX, b.maxX);
                minY = Math.Min(minY, b.minY); maxY = Math.Max(maxY, b.maxY);
            }

            IncludePointBounds(ref minX, ref maxX, ref minY, ref maxY);
            if (minX > maxX) return;
            if (minX == maxX) (minX, maxX) = ChartScale.Expand(minX, maxX);

            // A flat trace: nothing to scale to. Numerical ripple in the
            // eleventh digit of a conserved quantity is flat too - zoomed to
            // the chart it printed "3.031456714862e+0" at both ends (external
            // audit 14.09) - so flatness is RELATIVE to the value. A flat
            // zero (a count that has not started) gets 0…1, not ±0.5 around
            // nothing.
            (minY, maxY) = ChartScale.Expand(minY, maxY, RelativeSpanFloor);
        }

        UiRect plot = PlotRect;
        float px = plot.X, py = plot.Y, pw = plot.W, ph = plot.H;
        if (pw <= 0 || ph <= 0 || minX >= maxX || minY >= maxY) return;

        // 4×4 grid
        for (int i = 1; i < 4; i++)
        {
            d.Rect(px, py + ph * i / 4f, pw, 1, UiColor.FromBytes(40, 40, 46));
            d.Rect(px + pw * i / 4f, py, 1, ph, UiColor.FromBytes(40, 40, 46));
        }

        // Frozen curves first and dimmed, so the live one always reads on top.
        for (int k = 0; k < Snapshots.Count; k++)
        {
            Series s = Snapshots[k];
            if (s.Count < 2) continue;
            float age = Snapshots.Count > 1 ? k / (float)(Snapshots.Count - 1) : 1f;
            UiColor color = s.Color.WithAlpha(0.26f + 0.20f * age);
            float gx = 0, gy = 0;

            for (int i = 0; i < s.Count; i++)
            {
                (double x, double y) = s.At(i);
                float sx = px + (float)ChartScale.Fraction(x, minX, maxX) * pw;
                float sy = py + ph - (float)ChartScale.Fraction(y, minY, maxY) * ph;
                sy = Math.Clamp(sy, py, py + ph);
                if (i > 0) d.Line(gx, gy, sx, sy, color, 1.2f);
                gx = sx; gy = sy;
            }
        }

        foreach (var s in Series)
        {
            if (s.Count < 2) continue;
            float lx = 0, ly = 0;

            for (int i = 0; i < s.Count; i++)
            {
                var (x, y) = s.At(i);
                float sx = px + (float)ChartScale.Fraction(x, minX, maxX) * pw;
                float sy = py + ph - (float)ChartScale.Fraction(y, minY, maxY) * ph;
                sy = Math.Clamp(sy, py, py + ph);
                if (i > 0) d.Line(lx, ly, sx, sy, s.Color, 1.5f);
                lx = sx; ly = sy;
            }
        }

        DrawPoints(d, minX, maxX, minY, maxY, px, py, pw, ph);

        // Y range labels. Formatted only when the SHOWN value moves: two fresh
        // strings per chart per frame is a steady allocation stream for text
        // that changes a few times a second at most. The same fix is already
        // in ModuleScreen for the clock and state labels.
        if (minY != _labelledMin || maxY != _labelledMax)
        {
            _labelledMin = minY;
            _labelledMax = maxY;
            // as many decimals as the SPAN needs: "1.001 / 1.001" at both ends
            // of a growing trace was the axis saying nothing (audit 07.09)
            (_minLabel, _maxLabel) = RangeLabels.Pair(minY, maxY);
        }

        // A small dark backing behind each range label: a series running
        // along the top or bottom edge used to strike the text through
        // (Predator-Prey, Electromagnet). Same fill as the chart body, so
        // the backing reads as a gap in the line, not as a box.
        var labelBg = UiColor.FromBytes(18, 18, 21);
        d.Rect(px + 1, py, d.MeasureText(_maxLabel) + 3, Theme.FontSize + 2, labelBg);
        d.Rect(px + 1, py + ph - Theme.FontSize - 1, d.MeasureText(_minLabel) + 3, Theme.FontSize + 2, labelBg);
        d.Text(px + 2, py, _maxLabel, Theme.TextDim);
        d.Text(px + 2, py + ph - Theme.FontSize, _minLabel, Theme.TextDim);

        DrawCursor(d, minX, maxX, minY, maxY);
    }

    /// <summary>
    /// Finds the nearest visible sample in screen space. No interpolation or
    /// sorted-x assumption: phase loops, scatter and wrapped ring buffers all
    /// use the coordinates actually stored in Series. Ties follow draw order.
    /// </summary>
    protected (Series Series, double X, double Y, float ScreenX, float ScreenY)? NearestSample(
        float mouseX, float mouseY, UiRect plot, double minX, double maxX, double minY, double maxY)
    {
        (Series, double, double, float, float)? nearest = null;
        if (plot.W <= 0 || plot.H <= 0 || !plot.Contains(mouseX, mouseY)) return nearest;
        double best = double.PositiveInfinity;
        void Search(List<Series> curves, int minimum)
        {
            foreach (Series series in curves)
            {
                if (series.Count < minimum) continue;
                for (int i = 0; i < series.Count; i++)
                {
                    var (x, y) = series.At(i);
                    double fx = ChartScale.Fraction(x, minX, maxX), fy = ChartScale.Fraction(y, minY, maxY);
                    if (!double.IsFinite(fx) || !double.IsFinite(fy) || fx < 0 || fx > 1 || fy < 0 || fy > 1) continue;
                    float sx = plot.X + (float)fx * plot.W, sy = plot.Y + (1 - (float)fy) * plot.H;
                    double dx = sx - mouseX, dy = sy - mouseY, distance = dx * dx + dy * dy;
                    if (distance > best) continue;
                    best = distance;
                    nearest = (series, x, y, sx, sy);
                }
            }
        }
        if (IncludesSnapshots) Search(Snapshots, 2);
        Search(Series, 2);
        Search(Points, 1);
        return nearest;
    }

    public override void OnMouseMove(float x, float y) { _mouseX = x; _mouseY = y; }

    /// <summary>Readout stays inside the plot and leaves the parent's clip intact.</summary>
    protected void DrawCursor(Draw2D d, double minX, double maxX, double minY, double maxY)
    {
        if (!ShowCursor || !Hovered) return;
        UiRect plot = PlotRect;
        var nearest = NearestSample(_mouseX, _mouseY, plot, minX, maxX, minY, maxY);
        if (nearest is not { } sample) return;
        d.Line(sample.ScreenX, plot.Y, sample.ScreenX, plot.Y + plot.H, Theme.TextDim.WithAlpha(0.35f));
        d.Line(plot.X, sample.ScreenY, plot.X + plot.W, sample.ScreenY, Theme.TextDim.WithAlpha(0.35f));
        d.RectOutline(sample.ScreenX - 3, sample.ScreenY - 3, 6, 6, sample.Series.Color);

        if (_cursorX != sample.X || _cursorY != sample.Y)
        {
            _cursorX = sample.X; _cursorY = sample.Y;
            _cursorXLabel = "x = " + sample.X.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
            _cursorYLabel = "y = " + sample.Y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }
        bool named = !string.IsNullOrWhiteSpace(sample.Series.Name);
        float step = Theme.FontSize + 3, height = step * (named ? 3 : 2) + 8;
        if (plot.H < height || plot.W < 40) return;
        string name = ChartLegend.Fit(sample.Series.Name, plot.W - 12, Theme.FontSize, d);
        string xLabel = ChartLegend.Fit(_cursorXLabel, plot.W - 12, Theme.FontSize, d);
        string yLabel = ChartLegend.Fit(_cursorYLabel, plot.W - 12, Theme.FontSize, d);
        float width = Math.Min(plot.W, Math.Max(d.MeasureText(name), Math.Max(d.MeasureText(xLabel), d.MeasureText(yLabel))) + 12);
        float x = _mouseX + 12, y = _mouseY + 12;
        if (x + width > plot.X + plot.W) x = _mouseX - width - 12;
        if (y + height > plot.Y + plot.H) y = _mouseY - height - 12;
        x = Math.Clamp(x, plot.X, plot.X + plot.W - width);
        y = Math.Clamp(y, plot.Y, plot.Y + plot.H - height);
        d.Rect(x, y, width, height, UiColor.FromBytes(25, 27, 33));
        d.RectOutline(x, y, width, height, sample.Series.Color);
        float textY = y + 4;
        if (named) { d.Text(x + 6, textY, name, sample.Series.Color); textY += step; }
        d.Text(x + 6, textY, xLabel, Theme.Text);
        d.Text(x + 6, textY + step, yLabel, Theme.Text);
    }
}
