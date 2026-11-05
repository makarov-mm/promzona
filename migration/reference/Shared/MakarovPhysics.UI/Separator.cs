namespace MakarovPhysics.UI;

public class Separator : Widget
{
    public override float PreferredHeight(float w) => 9;
    public override void Draw(Draw2D d) => d.Rect(Rect.X, Rect.Y + 4, Rect.W, 1, Theme.PanelBorder);
}
