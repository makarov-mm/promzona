using MakarovPhysics.UI;

namespace MakarovPhysics.Experiments;

internal sealed class RoboticsRow : UiPanel
{
    private void Fit(float width) => Horizontal = Children.OfType<UiButton>().All(b => (Ui?.Font is not null ? Ui.Draw.MeasureText(b.Text) : b.Text.Length * 8) + 12 <= (width - 2 * Theme.Padding - Theme.Spacing) / 2);
    public override float PreferredHeight(float width)
    {
        Fit(width);
        return base.PreferredHeight(width);
    }

    public override void Layout()
    {
        Fit(Rect.W);
        base.Layout();
    }
}

