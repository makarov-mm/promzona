using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>
/// A colour scale with its ends labelled.
///
/// A field painted in colour without one of these is decoration: the reader
/// sees that something is redder over there and cannot say what by. The bar
/// costs a strip of panel and turns the whole picture into a measurement.
///
/// The gradient is sampled from the same function the scene uses, passed in
/// rather than reimplemented, because a legend that disagreed with the thing
/// it explains would be worse than none at all.
/// </summary>
public sealed class ColorScaleBar(Func<float, UiColor> palette) : Widget
{
    private const int Steps = 48;
    private const float BarHeight = 12f;
    private readonly Func<float, UiColor> _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    public string Title = string.Empty;

    /// <summary>Text under the left and right ends, in the scene's own units.</summary>
    public string LowLabel = string.Empty;
    public string HighLabel = string.Empty;
    public override float PreferredHeight(float w) => (Title.Length > 0 ? Theme.FontSize + 4f : 0) + BarHeight + Theme.FontSize + 8f;

    public override void Draw(Draw2D d)
    {
        float y = Rect.Y + 2f;

        if (Title.Length > 0)
        {
            d.Text(Rect.X + 2f, y, Title, Theme.TextDim);
            y += Theme.FontSize + 4f;
        }

        float x = Rect.X + 2f;
        float width = Rect.W - 4f;
        float step = width / Steps;

        for (int i = 0; i < Steps; i++)
        {
            // The +1 covers the seam: adjacent strips that merely touch leave a
            // hairline of background between them at fractional widths.
            d.Rect(x + i * step, y, step + 1f, BarHeight, _palette(i / (Steps - 1f)));
        }

        d.RectOutline(x, y, width, BarHeight, Theme.PanelBorder);
        y += BarHeight + 2f;

        if (LowLabel.Length > 0)
        {
            d.Text(x, y, LowLabel, Theme.TextDim);
        }

        if (HighLabel.Length > 0)
        {
            float textWidth = d.MeasureText(HighLabel);
            d.Text(x + width - textWidth, y, HighLabel, Theme.TextDim);
        }
    }
}
