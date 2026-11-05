using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>Histogram with an optional theoretical curve on top, both
/// normalized to their own maxima (shape comparison, e.g. a measured speed
/// distribution against Maxwell).</summary>
public class Histogram : Widget
{
    public double[] Bins = [];
    public double BinMin, BinMax = 1;
    public Func<double, double>? TheoryCurve;   // e.g. the Maxwell distribution
    public float Height = 140;
    public string Title = string.Empty;

    /// <summary>What the histogram says before any count has landed in a bin -
    /// same reasoning as <see cref="LineChart.EmptyHint"/>.</summary>
    public string EmptyHint = string.Empty;

    public override float PreferredHeight(float w) => Height;

    public override void Draw(Draw2D d)
    {
        var r = Rect;
        d.Rect(r.X, r.Y, r.W, r.H, UiColor.FromBytes(18, 18, 21));
        d.RectOutline(r.X, r.Y, r.W, r.H, Theme.PanelBorder);
        if (Title.Length > 0) d.Text(r.X + 6, r.Y + 4, Title, Theme.TextDim);
        double max = 0;

        foreach (double b in Bins)
        {
            max = Math.Max(max, b);
        }

        if (Bins.Length == 0 || max <= 0)
        {
            if (EmptyHint.Length > 0)
            {
                float hx = r.X + Math.Max(6f, (r.W - d.MeasureText(EmptyHint)) * 0.5f);
                float hy = r.Y + LineChart.PlotTop + (r.H - LineChart.PlotTop - 6f - Theme.FontSize) * 0.5f;
                d.Text(hx, hy, EmptyHint, Theme.TextDim);
            }
            return;
        }

        float px = r.X + 6, py = r.Y + LineChart.PlotTop;
        float pw = r.W - 12, ph = r.H - 26;
        float bw = pw / Bins.Length;

        for (int i = 0; i < Bins.Length; i++)
        {
            float bh = (float)(Bins[i] / max) * ph;
            d.Rect(px + i * bw, py + ph - bh, MathF.Max(1, bw - 1), bh, Theme.Accent.WithAlpha(0.7f));
        }

        // theory curve, normalized to its own maximum over [BinMin; BinMax]:
        // both shapes peak at full height, so the eye compares FORMS, which is
        // what a Maxwell-distribution overlay is for
        if (TheoryCurve is not null && BinMax > BinMin)
        {
            const int samples = 96;
            Span<float> values = stackalloc float[samples];
            float theoryMax = 0;

            for (int i = 0; i < samples; i++)
            {
                double v = BinMin + (BinMax - BinMin) * i / (samples - 1);
                double y = TheoryCurve(v);
                values[i] = double.IsFinite(y) && y > 0 ? (float)y : 0f;
                theoryMax = MathF.Max(theoryMax, values[i]);
            }

            if (theoryMax > 0)
            {
                var curveColor = new UiColor(1f, 1f, 1f, 0.85f);
                float prevX = px, prevY = py + ph - values[0] / theoryMax * ph;
                for (int i = 1; i < samples; i++)
                {
                    float cx = px + pw * i / (samples - 1);
                    float cy = py + ph - values[i] / theoryMax * ph;
                    d.Line(prevX, prevY, cx, cy, curveColor);
                    prevX = cx;
                    prevY = cy;
                }
            }
        }
    }
}
