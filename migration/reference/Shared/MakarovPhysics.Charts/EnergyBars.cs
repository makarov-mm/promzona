using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;

namespace MakarovPhysics.Charts;

/// <summary>
/// The energy budget as bars: each term beside the others, with their sum
/// underneath.
///
/// The chart exists for one statement, that the total does not move while the
/// terms trade, and every decision here serves it. The scale is LINEAR and
/// SHARED by all the bars, because a term that is half of another has to be
/// drawn half as long or the trade cannot be read. It is sticky and quantized,
/// so the picture does not breathe while the physics wobbles. And the total is
/// drawn as the actual SUM of the terms rather than as a separate reading, so
/// if it visibly creeps, that is the integrator's error being shown rather
/// than hidden.
///
/// Terms may be negative: gravitational energy is measured from a zero someone
/// chose, and a bar chart that quietly dropped the sign would be lying about
/// what was chosen. The axis moves to the middle as soon as any term goes
/// negative, and stays there, since an axis that jumped about would be worse
/// than either.
/// </summary>
public sealed class EnergyBars : Widget
{
    private const float ValueWidth = 66f;

    /// <summary>Room reserved for the captions, logical pixels.</summary>
    public float LabelWidth = 52f;

    public string Title = string.Empty;
    public float Height = 132;
    public readonly List<EnergyChannel> Channels = [];

    /// <summary>Caption of the sum row; empty hides it.</summary>
    public string TotalName = string.Empty;

    public UiColor TotalColor = UiColor.FromBytes(226, 226, 236);
    public string Units = "J";
    public string Format = "0.##";
    private float _scale;
    private bool _signed;

    public override float PreferredHeight(float w) => Height;

    public override void Draw(Draw2D d)
    {
        UiRect r = Rect;
        d.Rect(r.X, r.Y, r.W, r.H, UiColor.FromBytes(18, 18, 21));
        d.RectOutline(r.X, r.Y, r.W, r.H, Theme.PanelBorder);

        if (Title.Length > 0) d.Text(r.X + 6, r.Y + 4, Title, Theme.TextDim);
        if (Channels.Count == 0) return;

        double total = 0, peak = 0;
        bool negative = false;

        foreach (EnergyChannel channel in Channels)
        {
            double value = channel.Read();
            if (!double.IsFinite(value)) continue;

            total += value;
            peak = Math.Max(peak, Math.Abs(value));
            if (value < 0) negative = true;
        }

        peak = Math.Max(peak, Math.Abs(total));
        _signed |= negative;

        bool showTotal = TotalName.Length > 0;
        int rows = Channels.Count + (showTotal ? 1 : 0);

        float top = r.Y + (Title.Length > 0 ? 21f : 6f);
        float rowHeight = Math.Max(11f, (r.Bottom - 6f - top) / rows);
        float barX = r.X + 6f + LabelWidth;
        float barWidth = Math.Max(10f, r.Right - 6f - ValueWidth - barX);
        float usable = _signed ? barWidth * 0.5f : barWidth;
        float zeroX = _signed ? barX + barWidth * 0.5f : barX;

        Rescale(peak, usable);

        if (_scale <= 0f)
        {
            return;
        }

        if (_signed)
        {
            d.Rect(zeroX, top, 1f, rows * rowHeight, Theme.PanelBorder);
        }

        float y = top;

        foreach (EnergyChannel channel in Channels)
        {
            DrawRow(d, channel.Name, channel.Read(), channel.Color, y, rowHeight, zeroX, usable);
            y += rowHeight;
        }

        if (!showTotal)
        {
            return;
        }

        d.Rect(r.X + 6f, y, r.W - 12f, 1f, Theme.PanelBorder);
        DrawRow(d, TotalName, total, TotalColor, y + 1f, rowHeight - 1f, zeroX, usable);
    }

    /// <summary>
    /// Refits the shared scale only when the picture has left its band. Fitting
    /// every frame would make all the bars breathe together whenever one term
    /// moved, which is exactly the comparison the chart is for.
    /// </summary>
    private void Rescale(double peak, float usable)
    {
        if (!(peak > 0) || !double.IsFinite(peak) || usable <= 0f) return;

        float longest = (float)peak * _scale;
        bool refit = _scale <= 0f || longest > usable || longest < usable * 0.35f;
        if (!refit) return;

        // The same 1-2-5 rounding the vector arrows use: one rule for every
        // scale in the application means a reader learns it once.
        float fitted = VectorScale.Fit((float)peak, usable);
        if (fitted > 0f) _scale = fitted;
    }

    private void DrawRow(
        Draw2D d, string name, double value, UiColor color,
        float y, float rowHeight, float zeroX, float usable)
    {
        float barHeight = Math.Max(4f, rowHeight - 4f);
        float centerY = y + (rowHeight - barHeight) * 0.5f;

        d.Text(Rect.X + 6f, y + (rowHeight - Theme.FontSize) * 0.5f - 1f, name, Theme.TextDim);

        if (double.IsFinite(value))
        {
            float length = Math.Clamp((float)Math.Abs(value) * _scale, 0f, usable);

            if (length >= 1f)
            {
                float x = value >= 0 ? zeroX : zeroX - length;
                d.Rect(x, centerY, length, barHeight, color);
            }
        }

        string text = double.IsFinite(value)
            ? value.ToString(Format) + (Units.Length > 0 ? " " + Units : string.Empty)
            : "--";

        float width = d.MeasureText(text);
        d.Text(Rect.Right - 6f - width, y + (rowHeight - Theme.FontSize) * 0.5f - 1f, text, Theme.Text);
    }
}
