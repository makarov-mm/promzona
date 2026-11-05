using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

public class Checkbox(string text, bool value = false) : Widget
{
    public string Text = text;
    public bool Value = value;
    public event Action<bool>? Changed;

    private int _rows = 1;
    public override float PreferredHeight(float w) => MathF.Max(Theme.ControlHeight, _rows * (Theme.FontSize+4) + 6);

    public override void Draw(Draw2D d)
    {
        float s = 16;
        float cy = Rect.Y + 4;

        d.Rect(Rect.X, cy, s, s, Hovered ? Theme.ControlHover : Theme.ControlBackground);
        d.RectOutline(Rect.X, cy, s, s, Theme.PanelBorder);

        if (Value)
        {
            d.Rect(Rect.X + 4, cy + 4, s - 8, s - 8, Theme.Accent);
        }

        var lines = Overlays.SceneDashboard.Wrap(Text, MathF.Max(20, Rect.W - s - 8), d.MeasureText);

        if (_rows != lines.Count)
        {
            _rows = lines.Count;
            RequestLayout();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            d.Text(Rect.X + s + 8, Rect.Y + 4 + i * (Theme.FontSize + 4), lines[i], Theme.Text);
        }
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;
        SetValue(!Value);
        return true;
    }

    /// <summary>Set without a click; Changed is raised only when the value changes.</summary>
    public void SetValue(bool value)
    {
        if (Value == value) return;
        Value = value;
        Changed?.Invoke(Value);
    }
}
