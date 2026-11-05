using MakarovPhysics.Base;
using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>
/// Phase portrait: Series hold (x, y) STATE pairs (θ, ω) instead of a time
/// series. Differences from the base chart: the trail FADES with age (the
/// newest end is bright, history dims away - the eye follows the current
/// state), and a wrap-around jump (θ re-entering at −π after a full turn)
/// BREAKS the polyline instead of slashing a line across the whole plot.
/// </summary>
public class PhasePortrait : LineChart
{
    /// <summary>A segment longer than this fraction of the axis range is a
    /// wrap-around artefact, not a trajectory - skip it.</summary>
    public double JumpBreakFraction = 0.45;

    /// <summary>Alpha of the oldest trail point (the newest is fully opaque).</summary>
    public float TailAlpha = 0.06f;

    protected override bool IncludesSnapshots => false;

    public PhasePortrait()
    {
        Title = "Phase portrait";
        Height = 170;
    }

    // A stationary origin must remain in the centre of a phase portrait.
    internal static (double Min, double Max) StateRange(double min, double max) =>
        min == 0 && max == 0 ? ChartScale.Expand(-0.5, 0.5) : ChartScale.Expand(min, max);

    // Compare dimensionless coordinates: subtracting raw extreme states can overflow.
    internal static bool Connects(double x0, double y0, double x1, double y1, double limit) =>
        double.IsFinite(x0) && double.IsFinite(y0) && double.IsFinite(x1) && double.IsFinite(y1)
        && Math.Abs(x1 - x0) < limit && Math.Abs(y1 - y0) < limit;

    public override void Draw(Draw2D d)
    {
        DrawFrame(d);

        if (!HasDrawableData)
        {
            DrawEmptyHint(d); 
            return;
        }

        double minX = MinX, maxX = MaxX, minY = MinY, maxY = MaxY;
        if (AutoScale)
        {
            minX = double.MaxValue; maxX = double.MinValue;
            minY = double.MaxValue; maxY = double.MinValue;

            foreach (var s in Series)
            {
                if (s.Count < 2) continue;
                (double minX, double maxX, double minY, double maxY) b = s.Bounds();
                minX = Math.Min(minX, b.minX); maxX = Math.Max(maxX, b.maxX);
                minY = Math.Min(minY, b.minY); maxY = Math.Max(maxY, b.maxY);
            }

            IncludePointBounds(ref minX, ref maxX, ref minY, ref maxY);
            if (minX > maxX) return;
            (minX, maxX) = StateRange(minX, maxX);
            (minY, maxY) = StateRange(minY, maxY);
        }

        UiRect plot = PlotRect;
        float px = plot.X, py = plot.Y, pw = plot.W, ph = plot.H;
        if (pw <= 0 || ph <= 0 || minX >= maxX || minY >= maxY) return;

        // axis cross through the origin (if it is in view) - orbits circle it
        if (minX < 0 && maxX > 0)
        {
            d.Rect(px + (float)ChartScale.Fraction(0, minX, maxX) * pw, py, 1, ph, UiColor.FromBytes(48, 48, 56));
        }

        if (minY < 0 && maxY > 0)
        {
            d.Rect(px, py + ph - (float)ChartScale.Fraction(0, minY, maxY) * ph, pw, 1, UiColor.FromBytes(48, 48, 56));
        }


        foreach (var s in Series)
        {
            if (s.Count < 2) continue;
            float lx = 0, ly = 0;
            double lastX = double.NaN, lastY = double.NaN;

            for (int i = 0; i < s.Count; i++)
            {
                var (x, y) = s.At(i);
                double fx = ChartScale.Fraction(x, minX, maxX), fy = ChartScale.Fraction(y, minY, maxY);
                if (!double.IsFinite(fx) || !double.IsFinite(fy))
                {
                    lastX = lastY = double.NaN;
                    continue;
                }
                float sx = px + (float)Math.Clamp(fx, 0, 1) * pw;
                float sy = py + ph - (float)Math.Clamp(fy, 0, 1) * ph;
                sx = Math.Clamp(sx, px, px + pw);
                sy = Math.Clamp(sy, py, py + ph);
                if (Connects(lastX, lastY, fx, fy, JumpBreakFraction))
                {
                    float age = (float)i / s.Count;              // 0 oldest ... 1 newest
                    float alpha = TailAlpha + (1f - TailAlpha) * age * age;
                    d.Line(lx, ly, sx, sy, s.Color.WithAlpha(alpha), 1.5f);
                }
                lx = sx; ly = sy;
                lastX = fx; lastY = fy;
            }
        }

        // scatter overlays (Poincaré sections) go on top of the fading trails
        DrawPoints(d, minX, maxX, minY, maxY, px, py, pw, ph);

        d.Text(px + 2, py, RangeLabels.Format(maxY, minY), Theme.TextDim);
        d.Text(px + 2, py + ph - Theme.FontSize, RangeLabels.Format(minY, maxY), Theme.TextDim);
        DrawCursor(d, minX, maxX, minY, maxY);
    }
}
