using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>Numeric field with ± buttons. v1: no free keyboard input.</summary>
public class NumericField(string text, Func<double> get, Action<double> set, double min, double max, double step, string units = "") : Widget
{
    public string Text = text;
    public double Min = min, Max = max, Step = step;
    public string Units = units;
    public string Format = "0.###";
    public event Action<double>? Changed;
    public override float PreferredHeight(float w) => Theme.FontSize + Theme.ControlHeight;

    public override void Draw(Draw2D d)
    {
        d.Text(Rect.X, Rect.Y, Text, Theme.Text);
        float ty = Rect.Y + Theme.FontSize + 4;
        float h = Theme.ControlHeight - 4;
        d.Rect(Rect.X, ty, Rect.W, h, Theme.ControlBackground);
        d.RectOutline(Rect.X, ty, Rect.W, h, Theme.PanelBorder);
        string val = get().ToString(Format) + (Units.Length > 0 ? " " + Units : "");
        d.Text(Rect.X + 6, ty + (h - Theme.FontSize) / 2 - 1, val, Theme.Text);
        d.Text(Rect.Right - 34, ty + (h - Theme.FontSize) / 2 - 1, "−", Theme.TextDim);
        d.Text(Rect.Right - 16, ty + (h - Theme.FontSize) / 2 - 1, "+", Theme.TextDim);
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;
        double v = get();

        if (x > Rect.Right - 22)
        {
            v += Step;
        }
        else if (x > Rect.Right - 40)
        {
            v -= Step;
        }
        else
        {
            return true;
        }

        v = Math.Clamp(v, Min, Max);
        set(v);
        Changed?.Invoke(get()); // the stored value, not the raw request
        return true;
    }

    public override bool OnMouseWheel(int delta)
    {
        double v = Math.Clamp(get() + delta * Step, Min, Max);
        set(v);
        Changed?.Invoke(get());
        return true;
    }
}
