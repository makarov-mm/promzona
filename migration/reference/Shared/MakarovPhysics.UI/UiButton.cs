using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

public class UiButton(string text, Action? onClick = null) : Widget
{
    public string Text = text;

    /// <summary>
    /// Name of the icon file (without extension) drawn above the caption,
    /// turning the row into a tool tile. Empty keeps the plain text button, so
    /// nothing that already exists moves.
    /// </summary>
    public string IconName = string.Empty;

    /// <summary>Side of the icon box, logical pixels.</summary>
    public float IconSize = 22f;

    public event Action? Click;
    private bool _pressed;

    /// <summary>Persistent "this is the current mode" state for buttons that
    /// act as a switch (precision, scene, attractor). Every mode row in the
    /// suite used to be plain buttons, and the active choice was invisible -
    /// the review flagged it across a dozen modules. One-shot preset buttons
    /// simply never set this and look exactly as before.</summary>
    public bool Selected;

    /// <summary>true when this button is drawn as a tile with a pictogram.</summary>
    public bool HasIcon => IconName.Length > 0;

    public override float PreferredHeight(float w) => HasIcon ? Theme.ControlHeight + IconSize + 2f : Theme.ControlHeight;

    public override void Draw(Draw2D d)
    {
        UiColor bg = !Enabled ? Theme.ControlBackground.WithAlpha(0.5f)
            : _pressed ? Theme.ControlActive
            : Selected ? Theme.ControlActive
            : Hovered ? Theme.ControlHover
            : Theme.ControlBackground;

        d.Rect(Rect.X, Rect.Y, Rect.W, Rect.H, bg);
        d.RectOutline(Rect.X, Rect.Y, Rect.W, Rect.H, Selected ? Theme.Accent : Theme.PanelBorder);

        if (Selected)
        {
            d.Rect(Rect.X + 1, Rect.Y + Rect.H - 3, Rect.W - 2, 2, Theme.Accent);
        }

        UiColor ink = Enabled ? Theme.Text : Theme.Text.WithAlpha(0.5f);
        string caption = TextLayout.Ellipsize(Text, MathF.Max(0, Rect.W - 12), d.MeasureText);
        float tw = d.MeasureText(caption);

        if (!HasIcon)
        {
            d.Text(Rect.X + (Rect.W - tw) / 2, Rect.Y + (Rect.H - Theme.FontSize) / 2 - 1, caption, ink);
            return;
        }

        // Tile layout: pictogram over the caption, both centred. A missing icon
        // file leaves the caption where it is rather than shifting the row.
        float total = IconSize + 2f + Theme.FontSize;
        float top = Rect.Y + (Rect.H - total) / 2;

        d.DrawIcon(Rect.X + (Rect.W - IconSize) / 2, top, IconSize, IconSize, Ui?.Icons.Get(IconName), Enabled ? default : new UiColor(1f, 1f, 1f, 0.45f));
        d.Text(Rect.X + (Rect.W - tw) / 2, top + IconSize + 2f, caption, ink);
    }

    /// <summary>A click without a mouse: both handlers, in the order a real
    /// release runs them.</summary>
    public void Press()
    {
        if (!Enabled) return;
        onClick?.Invoke();
        Click?.Invoke();
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;
        _pressed = true;
        return true;
    }

    public override void OnMouseUp(float x, float y, MouseButton b)
    {
        if (_pressed && Rect.Contains(x, y))
        {
            onClick?.Invoke();
            Click?.Invoke();
        }

        _pressed = false;
    }
}
