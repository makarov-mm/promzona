using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// Shows a challenge: what is being asked, how close the attempt came, and by
/// how much it missed.
///
/// The residual line is the point of the widget. A card that only went green
/// or stayed grey would make the task a guessing game; printing "1.8 m short"
/// makes the next attempt a correction rather than another guess.
///
/// Both the title and the detail WRAP (05.09, external audit 3.6): the card
/// used to be two fixed lines and drew each in one line, so any task longer
/// than the panel is wide - most of them - was cut off at the right edge,
/// and a task the reader cannot finish reading is not a task. The wrap is
/// the same word-wrap UiLabel uses, measured in Draw and fed back to layout
/// through a line count, as UiLabel does.
/// </summary>
public sealed class ChallengeCard(Challenge challenge) : Widget
{
    private const float BarHeight = 6f;
    private readonly Challenge _challenge = challenge ?? throw new ArgumentNullException(nameof(challenge));
    public UiColor PendingColor = UiColor.FromBytes(120, 170, 230);
    public UiColor AchievedColor = SemanticColors.Event;
    public UiColor MissedColor = SemanticColors.Loss;
    private int _titleLines = 1, _detailLines = 1;
    private string? _wrapTitleOf, _wrapDetailOf;
    private float _wrapWidth = -1;
    private FontAtlas? _wrapAtlas;
    private string[] _titleWrapped = [], _detailWrapped = [];
    private float LineStep => Theme.FontSize + 4f;
    public override float PreferredHeight(float w) => 5f + _titleLines * LineStep + BarHeight + 4f + _detailLines * LineStep + 4f;

    private static string[] Wrap(Draw2D d, string text, float width)
    {
        if (text.Length == 0) return [];
        return [.. TextLayout.Wrap(text, width, d.MeasureText)];
    }

    public override void Draw(Draw2D d)
    {
        d.Rect(Rect.X, Rect.Y, Rect.W, Rect.H, UiColor.FromBytes(18, 18, 21));
        d.RectOutline(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBorder);

        ChallengeResult result = _challenge.Result;
        UiColor accent = result.State switch
        {
            ChallengeState.Achieved => AchievedColor,
            ChallengeState.Missed => MissedColor,
            _ => PendingColor,
        };

        float x = Rect.X + 6f;
        float y = Rect.Y + 5f;
        float width = Rect.W - 12f;
        bool atlasChanged = !ReferenceEquals(_wrapAtlas, d.Atlas);

        if (atlasChanged || !ReferenceEquals(_wrapTitleOf, _challenge.Title) || Math.Abs(_wrapWidth - width) > 0.5f)
        {
            _wrapTitleOf = _challenge.Title;
            _titleWrapped = Wrap(d, _challenge.Title, width);
        }

        if (atlasChanged || !ReferenceEquals(_wrapDetailOf, result.Detail) || Math.Abs(_wrapWidth - width) > 0.5f)
        {
            _wrapDetailOf = result.Detail;
            _detailWrapped = Wrap(d, result.Detail, width);
        }

        _wrapWidth = width;
        _wrapAtlas = d.Atlas;

        int titleLines = Math.Max(1, _titleWrapped.Length), detailLines = Math.Max(1, _detailWrapped.Length);

        if (titleLines != _titleLines || detailLines != _detailLines)
        {
            _titleLines = titleLines; _detailLines = detailLines;
            RequestLayout();
        }

        foreach (string line in _titleWrapped)
        {
            d.Text(x, y, line, accent); 
            y += LineStep;
        }

        if (_titleWrapped.Length == 0)
        {
            y += LineStep;
        }

        d.Rect(x, y, width, BarHeight, UiColor.FromBytes(38, 38, 44));
        float filled = width * (float)Math.Clamp(result.Progress, 0, 1);
        if (filled >= 1f) d.Rect(x, y, filled, BarHeight, accent);
        y += BarHeight + 4f;

        foreach (string line in _detailWrapped)
        {
            d.Text(x, y, line, Theme.TextDim); 
            y += LineStep;
        }
    }
}
