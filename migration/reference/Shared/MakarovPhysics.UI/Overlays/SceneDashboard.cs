using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Overlays;

/// <summary>Screen-space experiment readouts. Geometry and input share logical
/// coordinates, including DPI and the host's reserved caption area.</summary>
public sealed class SceneDashboard(IModuleContext context, Action<Draw2D, UiRect> draw) : Widget
{
    public static readonly UiColor Ink = UiColor.FromBytes(229, 237, 247);
    public static readonly UiColor Muted = UiColor.FromBytes(157, 176, 198);
    public static readonly UiColor Blue = UiColor.FromBytes(100, 187, 246);
    public static readonly UiColor Gold = UiColor.FromBytes(250, 196, 105);
    public static readonly UiColor Green = UiColor.FromBytes(106, 219, 176);
    public static readonly UiColor Red = UiColor.FromBytes(245, 125, 139);
    public Func<UiRect>? InputBounds;
    public Action<float, float, bool>? Stroke;
    private bool _dragging, _inside;

    public UiRect Bounds
    {
        get
        {
            float scale = context.GetService<UiContext>()?.Scale ?? 1f;
            float w = context.ViewportWidth / scale, h = context.ViewportHeight / scale;
            var insets = context.GetService<SceneInsets>();
            float top = MathF.Max(68, h * (insets?.Top ?? 0) + 12);
            return new UiRect(16, top, MathF.Max(1, w - 32), MathF.Max(1, h * (1 - (insets?.Bottom ?? 0)) - top - 16));
        }
    }

    public override void Draw(Draw2D d) { if (Shown) draw(d, Bounds); }
    public override Widget? HitTest(float x, float y) => Shown && InputBounds?.Invoke().Contains(x, y) == true ? this : null;
    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left || InputBounds?.Invoke().Contains(x, y) != true) return false;
        _dragging = _inside = true; Ui?.Capture(this); Stroke?.Invoke(x, y, true); return true;
    }
    public override void OnMouseMove(float x, float y)
    {
        if (!_dragging) return;
        bool inside = InputBounds?.Invoke().Contains(x, y) == true;
        if (inside) Stroke?.Invoke(x, y, !_inside);
        _inside = inside;
    }
    public override void OnMouseUp(float x, float y, MouseButton b) { _dragging = _inside = false; Ui?.ReleaseCapture(this); }

    public static void Card(Draw2D d, UiRect r, UiColor accent)
    {
        d.Rect(r.X, r.Y, r.W, r.H, UiColor.FromBytes(19, 26, 37));
        d.RectOutline(r.X, r.Y, r.W, r.H, UiColor.FromBytes(51, 66, 85));
        d.Rect(r.X, r.Y, r.W, 3, accent);
    }

    public static List<string> Wrap(string text, float width, Func<string,float> measure) =>
        TextLayout.Wrap(text, width, measure);

    public static float Paragraph(Draw2D d, string text, float x, float y, float width, UiColor color)
    {
        foreach(string line in Wrap(text,width,d.MeasureText)) { d.Text(x,y,line,color); y+=19; }
        return y;
    }

    public static UiRect GridBounds(UiRect card, float maxSize = 300)
    {
        float size = MathF.Max(8, MathF.Min(maxSize, MathF.Min(card.W - 24, card.H - 104)));
        return new UiRect(card.X + (card.W - size) / 2, card.Y + 58, size, size);
    }

    public static void Pixels(Draw2D d, UiRect r, int side, Func<int, double> value, UiColor color, Func<int, bool>? wrong = null)
    {
        float cell = r.W / side, gap = cell > 12 ? 2 : 1;
        for (int i = 0; i < side * side; i++)
        {
            float v = (float)Math.Clamp(value(i), 0, 1);
            var c = new UiColor(.10f + (color.R - .10f) * v, .14f + (color.G - .14f) * v, .20f + (color.B - .20f) * v, 1);
            float x = r.X + i % side * cell, y = r.Y + i / side * cell;
            d.Rect(x, y, cell - gap, cell - gap, c);
            if (wrong?.Invoke(i) == true) d.RectOutline(x, y, cell - gap, cell - gap, Red, MathF.Min(2, cell / 5));
        }
    }

    public static void Matrix(Draw2D d, UiRect r, double[,] a, string title, UiColor accent)
    {
        Card(d, r, accent);
        d.Text(r.X + 12, r.Y + 12, title, accent);
        float cell = (r.W - 28) / 3;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                string s = a[i, j].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                d.Text(r.X + 14 + j * cell + (cell - d.MeasureText(s)) / 2, r.Y + 40 + i * 23, s, Ink);
            }
        d.Line(r.X + 9, r.Y + 36, r.X + 9, r.Y + 106, accent);
        d.Line(r.X + r.W - 9, r.Y + 36, r.X + r.W - 9, r.Y + 106, accent);
    }
}
