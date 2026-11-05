using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>A short description with explicit access to details and equations.
/// The header hides the whole card; only that choice is persisted by the host.</summary>
public sealed class InfoOverlay : Widget
{
    public string Title = string.Empty;
    public string Summary = string.Empty;
    public string Text = string.Empty;
    public string MoreText = string.Empty;
    public string LessText = string.Empty;
    public bool Collapsed;
    public bool DetailsExpanded { get; private set; }
    public event Action<bool>? CollapsedChanged;
    public float PanelWidth = 420;
    public float AnchorX = 8;
    public float AnchorBottom = 600;
    public float MaxHeight = 560;
    private const float Pad = 10f;
    public int SummaryLineLimit = 3;
    private int SummaryLines => Math.Clamp(SummaryLineLimit,1,3);
    private int _wrappedLimit;
    private float _scroll, _bodyVisible, _bodyTotal;
    private readonly List<string> _lines = [];
    private string _wrappedText = "\u0000", _wrappedTitle = "\u0000", _wrappedFullText = "\u0000";
    private float _wrappedWidth = -1, _wrappedFontSize = -1;
    private FontAtlas? _wrappedAtlas;
    private Func<string, float>? _wrappedMeasure;
    private bool _wrappedDetails, _hasDetails;
    private string _header = string.Empty, _footer = string.Empty;
    private UiRect _footerRect;
    private float HeaderH => Theme.FontSize + 12;
    private float LineH => Theme.FontSize + 4;

    /// <summary>Measure before the scene so its camera can reserve the actual
    /// card height. No GL calls; deterministic metrics can be used in tests.</summary>
    public void Measure(Func<string, float> measureText)
    {
        float width = MathF.Max(60f, PanelWidth), textW = width - 2 * Pad - 6;
        string brief = string.IsNullOrWhiteSpace(Summary) ? Text : Summary;
        string shownText = DetailsExpanded && !string.IsNullOrWhiteSpace(Text) ? Text : brief;
        string header = (Collapsed ? "\u25B8 " : "\u25BE ") + Title;

        // The Draw2D object survives atlas replacement. Comparing only the
        // delegate would keep old line breaks after a language/DPI change.
        FontAtlas? atlas = measureText.Target is Draw2D drawing ? drawing.Atlas : measureText.Target as FontAtlas ?? Ui?.Font;

        if (_wrappedText != shownText || _wrappedFullText != Text || _wrappedTitle != header
            || MathF.Abs(_wrappedWidth - textW) > 0.5f
            || !ReferenceEquals(_wrappedAtlas, atlas) || _wrappedMeasure != measureText
            || _wrappedLimit != SummaryLines || _wrappedFontSize != Theme.FontSize || _wrappedDetails != DetailsExpanded)
        {
            WrapText(shownText, textW, measureText);
            _hasDetails = !string.IsNullOrWhiteSpace(Text) && (Text != brief || _lines.Count > SummaryLines);
            if (!DetailsExpanded && _lines.Count > SummaryLines)
            {
                _lines.RemoveRange(SummaryLines, _lines.Count - SummaryLines);
                _lines[^1] = FitText(_lines[^1] + "\u2026", textW, measureText);
            }
            _header = FitText(header, textW, measureText);
            _wrappedText = shownText;
            _wrappedFullText = Text;
            _wrappedTitle = header;
            _wrappedWidth = textW;
            _wrappedFontSize = Theme.FontSize;
            _wrappedAtlas = atlas;
            _wrappedMeasure = measureText;
            _wrappedLimit = SummaryLines;
            _wrappedDetails = DetailsExpanded;
            _scroll = 0;
        }

        _footer = FitText(DetailsExpanded ? LessText : MoreText, textW, measureText);
        float footerH = !Collapsed && _hasDetails ? HeaderH : 0;
        _bodyTotal = _lines.Count * LineH + Pad * 0.8f;
        float cap = MathF.Max(HeaderH + footerH + LineH, MaxHeight);
        float h = Collapsed ? HeaderH : MathF.Min(HeaderH + _bodyTotal + footerH, cap);
        _bodyVisible = Collapsed ? 0 : h - HeaderH - footerH;
        _scroll = Math.Clamp(_scroll, 0, MathF.Max(0, _bodyTotal - _bodyVisible));
        Rect = new UiRect(AnchorX, AnchorBottom - h, Collapsed ? MathF.Min(width, measureText(_header) + 2 * Pad) : width, h);
        _footerRect = new UiRect(Rect.X, Rect.Y + h - footerH, Rect.W, footerH);
    }

    public override void Draw(Draw2D d)
    {
        Measure(d.MeasureText);
        d.Rect(Rect.X, Rect.Y, Rect.W, Rect.H, new UiColor(0.030f, 0.036f, 0.052f, 0.80f));
        d.RectOutline(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBorder.WithAlpha(0.55f));
        d.Text(Rect.X + Pad, Rect.Y + 6, _header, new UiColor(1f, 1f, 1f, Hovered ? 1f : 0.94f));
        if (Collapsed) return;
        var body = new UiRect(Rect.X, Rect.Y + HeaderH, Rect.W, _bodyVisible);
        float maxScroll = MathF.Max(0, _bodyTotal - _bodyVisible);
        bool scrolls = maxScroll > 0.5f;
        if (scrolls) d.SetClip(body);
        float y = body.Y + 1 - _scroll;

        foreach (string line in _lines)
        {
            if (y + LineH >= body.Y && y <= body.Y + body.H)
            {
                d.Text(Rect.X + Pad, y, line, new UiColor(1f, 1f, 1f, 0.88f));
            }

            y += LineH;
        }

        if (scrolls)
        {
            d.SetClip(null);
            float track = MathF.Max(0, body.H - 2 * Pad);
            float thumb = MathF.Min(track, MathF.Max(12f, track * _bodyVisible / _bodyTotal));
            float top = body.Y + Pad + (track - thumb) * (_scroll / maxScroll);
            d.Rect(Rect.X + Rect.W - 6, body.Y + Pad, 3, track, new UiColor(1f, 1f, 1f, 0.10f));
            d.Rect(Rect.X + Rect.W - 6, top, 3, thumb, new UiColor(1f, 1f, 1f, 0.45f));
        }

        if (_hasDetails)
        {
            d.Rect(_footerRect.X + Pad, _footerRect.Y, _footerRect.W - 2 * Pad, 1, Theme.PanelBorder.WithAlpha(0.55f));
            d.Text(_footerRect.X + Pad, _footerRect.Y + 6, _footer, Theme.Accent);
        }
    }

    public override bool OnMouseWheel(int delta)
    {
        if (!Collapsed) _scroll = Math.Clamp(_scroll - delta * 3 * LineH, 0, MathF.Max(0, _bodyTotal - _bodyVisible));
        return true; // reading the card must never zoom the scene behind it
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left || !Rect.Contains(x, y)) return true;

        if (Collapsed || y <= Rect.Y + HeaderH)
        {
            Collapsed = !Collapsed;
            CollapsedChanged?.Invoke(Collapsed);
        }
        else if (_hasDetails && _footerRect.Contains(x, y))
        {
            DetailsExpanded = !DetailsExpanded;
            _scroll = 0;
        }

        return true;
    }

    private void WrapText(string text, float width, Func<string, float> measureText)
    {
        _lines.Clear();
        _lines.AddRange(TextLayout.Wrap(text, width, measureText));
    }

    private static string FitText(string text, float width, Func<string, float> measureText) => TextLayout.Ellipsize(text, width, measureText);
}
