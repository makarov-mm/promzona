using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// Text label. '\n' is a REAL line break: Draw2D.Text feeds the atlas one
/// glyph at a time, U+000A has no glyph, so before 03.08 every multi-line
/// info label in the catalogue (nine modules build them) rendered as ONE
/// clipped line with the fallback '?' where the breaks were - seen live on
/// the FractalFields screenshot. The label owns its line layout the same way
/// InfoOverlay owns its word wrap; the draw step and PreferredHeight use the
/// same constants, so the layout engine and the pen cannot disagree.
/// A single-line label keeps the EXACT height it always had (FontSize + 6):
/// every panel in the catalogue is laid out against that number.
/// </summary>
public class UiLabel(string text) : Widget
{
    public string Text = text;
    public bool Dim;

    /// <summary>
    /// Fold a line that does not fit the panel onto the next one (07.08).
    ///
    /// On by default, and that changes NOTHING for a label that already fits:
    /// it still measures one line and keeps the exact height every panel in
    /// the catalogue is laid out against. It changes the labels that were
    /// already broken - the info and hint lines running under the right edge
    /// of the panel, cut mid-word, in a good third of the shipped modules
    /// ("...(the Gascheau rat", "...tick, ms:", "physic").
    /// </summary>
    public bool Wrap = true;

    // Text, width and atlas determine wrapping. The atlas changes when the
    // language or display DPI changes, even when the text and width do not.
    private string? _splitOf;
    private float _splitWidth = -1;
    private FontAtlas? _splitAtlas;
    private string[] _lines = [];
    private int _laidOutLines = 1;

    /// <summary>Plain '\n' split, for a label with wrapping switched off.
    /// Re-splits when the STRING ITSELF changes - a readout updated in place
    /// every frame must not keep drawing the text it had at build time.</summary>
    private string[] Lines()
    {
        if (!ReferenceEquals(_splitOf, Text) || _splitWidth >= 0)
        {
            _splitOf = Text;
            _splitWidth = -1;
            _splitAtlas = null;
            _lines = Text.Split('\n');
        }

        return _lines;
    }

    /// <summary>Split on '\n' first, then fold each paragraph to the width.
    /// Needs a Draw2D because only the atlas knows how wide a string is, so it
    /// runs in Draw; layout uses the count the previous frame produced and is
    /// asked to run again when that count changes.</summary>
    private string[] WrappedLines(Draw2D d, float width)
    {
        if (ReferenceEquals(_splitOf, Text) && Math.Abs(_splitWidth - width) < 0.5f
            && ReferenceEquals(_splitAtlas, d.Atlas)) return _lines;

        _splitOf = Text;
        _splitWidth = width;
        _splitAtlas = d.Atlas;
        _lines = [.. TextLayout.Wrap(Text, width, d.MeasureText)];
        return _lines;
    }

    /// <summary>Advance between baselines of a multi-line label. Slightly
    /// tighter than the first line's padded height - continuation lines of
    /// one readout belong together. Instance property: Theme itself is one
    /// (it resolves through the widget's UiContext).</summary>
    private float LineStep => Theme.FontSize + 4;

    /// <summary>Lines the text asks for OUTRIGHT, from its '\n' alone. Counted
    /// rather than split: this runs in layout, every frame, for every label.</summary>
    private int HardLineCount => 1 + Text.Count(c => c == '\n');

    public override float PreferredHeight(float w)
    {
        // Wrapping cannot be measured here - no Draw2D, no atlas - so the
        // count from the last Draw stands in for it, and Draw asks for another
        // layout pass when it disagrees.
        //
        // But the EXPLICIT breaks are known without measuring anything, and
        // they must not wait for a frame: taking only the drawn count made a
        // three-line readout ask for one line's height until it had been drawn
        // once, which is the 03.08 multi-line fix undone (caught by its own
        // test, "three lines add two line steps: got 20, expected 56").
        int lines = Wrap ? Math.Max(HardLineCount, _laidOutLines) : HardLineCount;
        return Theme.FontSize + 6 + (lines - 1) * LineStep;
    }

    public override void Draw(Draw2D d)
    {
        UiColor colour = Dim ? Theme.TextDim : Theme.Text;
        string[] lines = Wrap ? WrappedLines(d, Rect.W) : Lines();

        if (Wrap && lines.Length != _laidOutLines)
        {
            _laidOutLines = lines.Length;
            RequestLayout();
        }

        float y = Rect.Y + 2;

        foreach (string line in lines)
        {
            d.Text(Rect.X, y, line, colour);
            y += LineStep;
        }
    }
}
