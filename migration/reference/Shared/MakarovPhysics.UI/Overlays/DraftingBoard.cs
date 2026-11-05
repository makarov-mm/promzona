namespace MakarovPhysics.UI.Overlays;

/// <summary>Quiet, high-contrast drafting sheet for exact constructions - on
/// the same dark navy as every other 2D sheet of the suite (29.09: the cream
/// paper of 37 modules stood out against the rest of the carousel, so the
/// sheet went dark and kept its inks as accents). Screen-space spacing and
/// ink widths stay readable when the experiment changes scale.</summary>
public static class DraftingBoard
{
    public static readonly UiColor Paper = UiColor.FromBytes(17, 25, 38);
    public static readonly UiColor Ink = UiColor.FromBytes(229, 237, 247);
    public static readonly UiColor Muted = UiColor.FromBytes(157, 176, 198);
    public static readonly UiColor Blue = UiColor.FromBytes(100, 187, 246);
    public static readonly UiColor Gold = UiColor.FromBytes(250, 196, 105);
    public static readonly UiColor Green = UiColor.FromBytes(106, 219, 176);
    public static readonly UiColor Red = UiColor.FromBytes(245, 125, 139);

    public static void Draw(Draw2D d, UiRect r, float grid = 32)
    {
        d.Rect(r.X + 4, r.Y + 5, r.W, r.H, UiColor.FromBytes(5, 8, 12));
        d.Rect(r.X, r.Y, r.W, r.H, Paper);
        var gridInk = UiColor.FromBytes(27, 37, 52);
        for (float x = r.X + grid; x < r.Right; x += grid) d.Line(x, r.Y, x, r.Bottom, gridInk);
        for (float y = r.Y + grid; y < r.Bottom; y += grid) d.Line(r.X, y, r.Right, y, gridInk);
        d.RectOutline(r.X, r.Y, r.W, r.H, UiColor.FromBytes(51, 66, 85));
    }

    public static void Tag(Draw2D d, float x, float y, string text, UiColor ink)
    {
        d.Rect(x - 4, y - 2, d.MeasureText(text) + 8, 22, Paper);
        d.Text(x, y, text, ink);
    }

    public static void Pin(Draw2D d, float x, float y, UiColor ink, bool draggable = false)
    {
        d.Circle(x + 1, y + 2, 6, UiColor.FromBytes(5, 8, 12), 24);
        d.Circle(x, y, 5, ink, 24);
        d.Circle(x - 1, y - 1, 1.6f, UiColor.Lerp(ink, UiColor.FromBytes(255, 255, 255), .6f), 12);
        if (draggable) d.CircleOutline(x, y, 10, ink, 1, 32);
    }

    public static void Metric(Draw2D d, UiRect r, string label, string value, UiColor ink)
    {
        SceneDashboard.Card(d, r, ink);
        d.Text(r.X + 12, r.Y + 10, label, SceneDashboard.Muted);
        d.Text(r.X + 12, r.Y + 35, value, SceneDashboard.Ink);
    }
}
