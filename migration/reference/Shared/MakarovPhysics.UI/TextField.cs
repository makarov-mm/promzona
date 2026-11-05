using System.Globalization;
using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// Single-line text input. Click to focus; characters arrive through the
/// host's typed-character stream (layout and auto-repeat already applied),
/// with native IME composition handled by the Windows host. Enter commits
/// (fires <see cref="Committed"/>), Escape reverts to the pre-edit text,
/// clicking anywhere else commits too. The caret sits at the end of the
/// text - a deliberate v1 simplification for short property values.
/// </summary>
public class TextField(string label) : Widget
{
    public string Label = label;
    public string Placeholder = string.Empty;
    /// <summary>Maximum number of Unicode text elements, not UTF-16 units.</summary>
    public int MaxLength = 32;

    /// <summary>Fired when an edit is committed (Enter or focus loss) and the text changed.</summary>
    public event Action<string>? Committed;

    private readonly TextInputBuffer _buffer = new();
    public string Text => _buffer.Text;

    /// <summary>The widget this field belongs to when it is not laid out on
    /// its own - a Slider editing its value keeps one of these. Null for a
    /// field that lives in the widget tree like any other.</summary>
    public Widget? Owner { get; set; }
    private string _preEdit = string.Empty;
    public bool Focused => Ui?.TextFocus == this;

    /// <summary>External update (selection changed, value edited elsewhere).
    /// Ignored while the user is typing so their input is not clobbered.</summary>
    public void SetText(string text)
    {
        if (!Focused) _buffer.SetText(text);
    }

    public override float PreferredHeight(float w) => (Label.Length > 0 ? Theme.FontSize : 0) + Theme.ControlHeight;

    public UiRect InputBounds => new(Rect.X, Rect.Y + (Label.Length > 0 ? Theme.FontSize + 4 : 0),
        Rect.W, Theme.ControlHeight - 4);

    /// <summary>The same logical caret used by Draw and the native IME anchor.</summary>
    public UiRect CaretBounds(Draw2D d)
    {
        var box = InputBounds;
        return new UiRect(box.X + 6 + d.MeasureText(VisibleText(d)) + 1, box.Y + 3, 1.5f, box.H - 6);
    }

    private string VisibleText(Draw2D d)
    {
        bool placeholder = Text.Length == 0 && !Focused;
        string shown = placeholder ? Placeholder : Text;
        float avail = Math.Max(0, Rect.W - 12);
        if (placeholder) return TextLayout.Ellipsize(shown, avail, d.MeasureText);
        int[] starts = StringInfo.ParseCombiningCharacters(shown);
        int first = 0;
        while (first < starts.Length && d.MeasureText(shown[starts[first]..]) > avail) first++;
        return first < starts.Length ? shown[starts[first]..] : string.Empty;
    }

    public override void Draw(Draw2D d)
    {
        if (Label.Length > 0) d.Text(Rect.X, Rect.Y, Label, Theme.Text);
        float ty = InputBounds.Y;
        float h = InputBounds.H;
        d.Rect(Rect.X, ty, Rect.W, h, Focused ? Theme.ControlActive : Hovered ? Theme.ControlHover : Theme.ControlBackground);
        d.RectOutline(Rect.X, ty, Rect.W, h, Focused ? Theme.Accent : Theme.PanelBorder);

        // keep the tail visible when the text is wider than the box
        bool placeholder = Text.Length == 0 && !Focused;
        string shown = VisibleText(d);
        d.Text(Rect.X + 6, ty + (h - Theme.FontSize) / 2 - 1, shown, placeholder ? Theme.TextDim : Theme.Text);

        // caret blink from the shared UI clock (visual only - no physics involved)
        if (Focused && Environment.TickCount % 1000 < 600)
        {
            UiRect caret = CaretBounds(d);
            d.Rect(caret.X, caret.Y, caret.W, caret.H, Theme.Text);
        }
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;

        if (!Focused)
        {
            _preEdit = Text;
            Ui?.FocusText(this);
        }

        return true;
    }

    /// <summary>Typed-character routing (called by UiContext while focused).</summary>
    public void HandleChar(char c)
    {
        switch (c)
        {
            case '\b':
                _buffer.Backspace();
                break;

            case '\r' or '\n':
                _buffer.ResetPending();
                Ui?.BlurText(commit: true);
                break;

            default:
                _buffer.TypeChar(c, MaxLength);
                break;
        }
    }

    /// <summary>Focus is being released. commit=false reverts (Escape / UI rebuild).</summary>
    internal void OnBlur(bool commit)
    {
        _buffer.ResetPending();
        if (!commit)
        {
            _buffer.SetText(_preEdit);
            return;
        }

        if (Text != _preEdit) Committed?.Invoke(Text);
        _preEdit = Text;
    }
}
