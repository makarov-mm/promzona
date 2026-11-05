using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI;

/// <summary>
/// A numbered colour scale: the reference-tool colorbar (Ansys/CFD screenshots,
/// 21.08) as a panel widget. A horizontal gradient strip sampled from a caller
/// ramp, with the MINIMUM and MAXIMUM values printed at its ends and an
/// optional unit - so a field view stops being "warmer is more" and becomes a
/// measurement. Min/Max are mutable every frame (adaptive scales stay honest:
/// the label follows the actual normalization, not a wish), and the ramp is
/// whatever function the module actually colours its field with - the strip
/// is drawn BY the same code that paints the pixels, so it cannot lie.
/// </summary>
public class ColorScale(string label, Func<float, Vec3> ramp) : Widget
{
    /// <summary>The colormap, t in [0,1] → linear RGB in [0,1]. REQUIRED to
    /// be the very function the view uses.</summary>
    public Func<float, Vec3> Ramp = ramp;

    public string Label = label;
    public double Min;
    public double Max = 1;
    public string Unit = string.Empty;

    /// <summary>Number format for the end labels.</summary>
    public string Format = "0.##";

    private const int Steps = 32;
    private const float BarHeight = 12f;

    public override float PreferredHeight(float w)
        => Theme.FontSize + 4  // the label line
         + BarHeight + 3       // the strip
         + Theme.FontSize + 2; // the numbers line

    public override void Draw(Draw2D d)
    {
        float x = Rect.X, w = Rect.W;
        float y = Rect.Y;

        d.Text(x, y, Label, Theme.Text);
        y += Theme.FontSize + 4;

        // the strip: Steps quads sampling the ramp at their centres
        for (int i = 0; i < Steps; i++)
        {
            float t = (i + 0.5f) / Steps;
            Vec3 c = Ramp(t);

            UiColor col = UiColor.FromBytes(
                (byte)Math.Clamp(c.X * 255f, 0f, 255f),
                (byte)Math.Clamp(c.Y * 255f, 0f, 255f),
                (byte)Math.Clamp(c.Z * 255f, 0f, 255f));

            float x0 = x + w * i / Steps;
            float x1 = x + w * (i + 1) / Steps;
            d.Rect(x0, y, x1 - x0 + 0.5f, BarHeight, col);
        }

        d.RectOutline(x, y, w, BarHeight, Theme.PanelBorder);
        y += BarHeight + 3;

        // the numbers: min left-aligned, max right-aligned, unit after max
        string minText = Min.ToString(Format);
        string maxText = Max.ToString(Format) + (Unit.Length > 0 ? " " + Unit : string.Empty);
        d.Text(x, y, minText, Theme.TextDim);
        float maxW = d.MeasureText(maxText);
        d.Text(x + w - maxW, y, maxText, Theme.TextDim);
    }
}
