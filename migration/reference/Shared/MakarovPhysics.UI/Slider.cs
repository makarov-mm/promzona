using System.Globalization;
using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// Float slider with a caption, value and units. Optional logarithmic
/// scale (for wide-range parameters).
/// </summary>
public class Slider : Widget
{
    public string Text;
    public double Min, Max;
    public string Units;

    /// <summary>Logarithmic mapping needs Min &gt; 0.</summary>
    public bool Logarithmic
    {
        get;
        set
        {
            if (value && Min <= 0)
            {
                throw new ArgumentException($"Slider '{Text}': logarithmic mode requires Min > 0 (Min = {Min}).");
            }

            field = value;
        }
    }

    public string Format = "0.###";
    public event Action<double>? Changed;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private bool _drag;

    /// <summary>
    /// Typing the number instead of aiming for it (27.08, asked for by a
    /// reader on release day: "is there a possibility to enter the values
    /// exactly? I mean not by sliders but by a textbox").
    ///
    /// Clicking the VALUE - the number at the right end of the caption row,
    /// not the track - turns it into a text box; Enter commits, Escape
    /// reverts, a click elsewhere commits. The track keeps working exactly as
    /// before, so nothing anyone already knows how to do changes.
    ///
    /// The editor is a plain TextField that is not in the widget tree: focus
    /// routing is by object, not by layout, so it only needs to exist. It
    /// carries Owner = this so a click on our own track does not count as
    /// "clicked outside" and cancel the edit that click just started.
    /// </summary>
    private TextField? _editor;

    private bool Editing => _editor is not null && Ui?.TextFocus == _editor;

    /// <summary>Width of the value text plus a little slack, measured on the
    /// last draw - the click test needs to know where the number is.</summary>
    private float _valueWidth;

    public Slider(string text, Func<double> get, Action<double> set, double min, double max, string units = "")
    {
        // A broken range turns Norm()/FromNorm() into NaN factories
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
        {
            throw new ArgumentException($"Slider '{text}': range [{min}; {max}] must be finite with max > min.");
        }

        Text = text;
        _get = get;
        _set = set;
        Min = min;
        Max = max;
        Units = units;
    }

    /// <summary>Set the value as a drag would - clamped to the range, through
    /// the setter, Changed raised with what the setter stored.</summary>
    public void SetValue(double v)
    {
        if (!double.IsFinite(v)) return;
        v = Math.Clamp(v, Min, Max);
        if (Logarithmic && v <= 0) return;
        _set(v);
        Changed?.Invoke(_get());
    }

    private int _captionRows = 1;
    public override float PreferredHeight(float w) => Theme.FontSize + Theme.ControlHeight + (_captionRows-1)*(Theme.FontSize+4);

    private double Norm(double v)
    {
        if (Logarithmic)
        {
            return Math.Log(v / Min) / Math.Log(Max / Min);
        }

        return (v - Min) / (Max - Min);
    }

    private double FromNorm(double t)
    {
        t = Math.Clamp(t, 0, 1);

        return Logarithmic
            ? Min * Math.Pow(Max / Min, t)
            : Min + (Max - Min) * t;
    }

    /// <summary>The getter is model state and may be corrupted
    /// (NaN, or ≤ 0 on a logarithmic scale). A poisoned value must neither
    /// produce NaN geometry nor be laundered back into the model.</summary>
    private bool IsValidValue(double v) => double.IsFinite(v) && (!Logarithmic || v > 0);

    public override void Draw(Draw2D d)
    {
        double v = _get();
        string valueText = IsValidValue(v) ? v.ToString(Format) + (Units.Length > 0 ? " " + Units : "") : "--";
        float reserve = MathF.Max(Editing ? 64 : 0, d.MeasureText(valueText)+12);
        var caption = Overlays.SceneDashboard.Wrap(Text, MathF.Max(24,Rect.W-reserve),d.MeasureText);
        if (_captionRows != caption.Count) { _captionRows=caption.Count; RequestLayout(); }
        for(int i=0;i<caption.Count;i++) d.Text(Rect.X,Rect.Y+i*(Theme.FontSize+4),caption[i],Theme.Text);

        if (!IsValidValue(v))
        {
            // show the invalid state honestly instead of drawing NaN rects
            d.Text(Rect.Right - d.MeasureText("--"), Rect.Y, "--", Theme.TextDim);
            float tyInvalid = Rect.Y + Theme.FontSize + 8 + (_captionRows-1)*(Theme.FontSize+4);
            d.Rect(Rect.X, tyInvalid, Rect.W, 4, Theme.SliderTrack);
            return;
        }

        string val = v.ToString(Format) + (Units.Length > 0 ? " " + Units : "");
        float vw = d.MeasureText(val);
        _valueWidth = vw + 8;

        if (Editing)
        {
            // the value row becomes the box; the track below stays live
            string typed = _editor!.Text;
            float bw = Math.Max(vw + 24, 64);
            float bx = Rect.Right - bw;
            d.Rect(bx, Rect.Y - 2, bw, Theme.FontSize + 4, Theme.ControlActive);
            d.RectOutline(bx, Rect.Y - 2, bw, Theme.FontSize + 4, Theme.Accent);
            float tw = d.MeasureText(typed);
            d.Text(bx + 4, Rect.Y, typed, Theme.Text);

            if (Environment.TickCount % 1000 < 600)
            {
                d.Rect(bx + 5 + tw, Rect.Y, 1.5f, Theme.FontSize, Theme.Text);
            }
        }
        else
        {
            d.Text(Rect.Right - vw, Rect.Y, val, Theme.TextDim);
        }

        float ty = Rect.Y + Theme.FontSize + 8 + (_captionRows-1)*(Theme.FontSize+4);
        d.Rect(Rect.X, ty, Rect.W, 4, Theme.SliderTrack);
        float t = (float)Math.Clamp(Norm(v), 0, 1);
        d.Rect(Rect.X, ty, Rect.W * t, 4, Theme.Accent);
        float hx = Rect.X + Rect.W * t;
        d.Rect(hx - 5, ty - 5, 10, 14, _drag || Hovered ? Theme.Accent : Theme.ControlActive);
    }

    private void Apply(float x)
    {
        double v = FromNorm((x - Rect.X) / Rect.W);
        _set(v);
        // report what the setter actually STORED (it may round or clamp) -
        // the wheel path already did; drag and wheel now agree
        Changed?.Invoke(_get());
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left)
        {
            return false;
        }

        // the caption row belongs to the number: click it to type one
        if (y < Rect.Y + Theme.FontSize + 2 && x > Rect.Right - _valueWidth)
        {
            BeginEdit();
            return true;
        }

        _drag = true;
        Apply(x);
        Ui?.Capture(this);
        return true;
    }

    private void BeginEdit()
    {
        if (Ui is null) return;
        double v = _get();
        if (!IsValidValue(v)) return;
        _editor ??= new TextField(Text) { Owner = this, MaxLength = 16 };
        _editor.Committed -= OnEditCommitted;
        _editor.Committed += OnEditCommitted;
        // the current value in the same format the caption shows, so what is
        // typed over is what was read
        _editor.SetText(v.ToString(Format));
        Ui.FocusText(_editor);
    }

    /// <summary>
    /// A typed value is CLAMPED to the range, not rejected: the reader asked
    /// for exact entry, and a silent refusal teaches nothing. Anything that
    /// is not a number at all leaves the value alone.
    ///
    /// Both the invariant and the current culture are tried, so a machine
    /// where the decimal separator is a comma accepts both "1,5" and "1.5" -
    /// the caption is formatted in the current culture, and refusing to read
    /// back what was just printed would be its own small betrayal.
    /// </summary>
    private void OnEditCommitted(string text) => CommitTypedValue(text);

    /// <summary>Apply a typed value as if it had been entered in the box.
    /// Public so the rules - clamping, both decimal separators, nonsense
    /// leaving the value alone - can be pinned without a mouse.</summary>
    public void CommitTypedValue(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v)
            &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            return;
        }

        if (!double.IsFinite(v)) return;
        v = Math.Clamp(v, Min, Max);
        if (Logarithmic && v <= 0) return;
        _set(v);
        Changed?.Invoke(_get());
    }

    public override void OnMouseMove(float x, float y)
    {
        if (_drag)
        {
            Apply(x);
        }
    }

    public override void OnMouseUp(float x, float y, MouseButton b)
    {
        _drag = false;
        Ui?.ReleaseCapture(this);
    }

    public override bool OnMouseWheel(int delta)
    {
        double v = _get();
        // with a corrupted current value, Norm(v) is NaN and
        // the old code wrote NaN back through the setter - the wheel became
        // the infection vector. Ignore the nudge; dragging still works (it
        // derives the value from the mouse position, not from the getter).
        if (!IsValidValue(v)) return true;
        double t = Math.Clamp(Norm(v) + delta * 0.02, 0, 1);
        _set(FromNorm(t));
        Changed?.Invoke(_get());
        return true;
    }
}
