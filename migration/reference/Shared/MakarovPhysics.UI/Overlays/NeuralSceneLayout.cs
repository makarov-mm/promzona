using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Overlays;

/// <summary>One logical layout for the GL viewport, readouts and pointer input.
/// The network and the experiment remain visible together, including at high DPI.
///
/// Two arrangements (02.10, tail1359, audit of 01.10 frame 23 "the network is
/// too small against the background"): on a wide, tall area the readout cards
/// stand in a COLUMN at the right and the network takes the rest - a viewport
/// close to the network's own proportions, so the height-limited fit no longer
/// leaves half the width empty; on a short or narrow area the cards keep their
/// ROW under the network, as before. <see cref="Column"/> says which.</summary>
public readonly struct NeuralSceneLayout
{
    public readonly UiRect Network, Data;
    /// <summary>The cards stand in a column beside the network (wide and tall areas).</summary>
    public readonly bool Column;
    public NeuralSceneLayout(UiRect area, bool show3D)
    {
        Column = show3D && area.H >= 560 && area.W >= 1.9f * area.H;
        if (Column)
        {
            float dataWidth = Math.Clamp(area.W * .22f, 250, 320);
            Data = new UiRect(area.Right - dataWidth, area.Y + 24, dataWidth, area.H - 24);
            Network = new UiRect(area.X, area.Y + 24, MathF.Max(1, Data.X - 12 - area.X), MathF.Max(1, area.H - 36));
            return;
        }
        float dataHeight = show3D ? MathF.Min(area.H * .62f, Math.Clamp(area.H * .36f, 206, 270)) : area.H;
        Data = new UiRect(area.X, area.Bottom - dataHeight, area.W, dataHeight);
        Network = new UiRect(area.X, area.Y + 24, area.W, MathF.Max(1, Data.Y - area.Y - 36));
    }
    public UiRect Card(int column, int count)
    {
        if (Column)
        {
            // two lines of the readout paragraph fit above the first card
            float height = (Data.H - 50 - 10 * (count - 1)) / count;
            return new UiRect(Data.X, Data.Y + 50 + column * (height + 10), Data.W, height);
        }
        float width = (Data.W - 10 * (count - 1)) / count;
        return new UiRect(Data.X + column * (width + 10), Data.Y + 28, width, Data.H - 28);
    }
    public static UiRect Grid(UiRect card, float footer = 40)
    {
        float size = MathF.Max(8, MathF.Min(270, MathF.Min(card.W - 20, card.H - 48 - footer)));
        return new UiRect(card.X + (card.W - size) / 2, card.Y + 42, size, size);
    }
    public static void Ink(Draw2D d, UiRect box, List<List<Vec3>> strokes, float sourceSize)
    {
        d.Rect(box.X, box.Y, box.W, box.H, UiColor.FromBytes(8, 13, 22));
        d.RectOutline(box.X, box.Y, box.W, box.H, SceneDashboard.Muted);
        float scale = box.W / sourceSize;
        foreach (var stroke in strokes)
            for (int j = 0; j < stroke.Count; j++)
            {
                Vec3 a = stroke[Math.Max(0,j-1)], b = stroke[j];
                d.Circle(box.X+b.X*scale,box.Y+b.Y*scale,MathF.Max(2,4*scale),SceneDashboard.Ink);
                d.Line(box.X+a.X*scale,box.Y+a.Y*scale,box.X+b.X*scale,box.Y+b.Y*scale,SceneDashboard.Ink,MathF.Max(3,8*scale));
            }
    }
}
