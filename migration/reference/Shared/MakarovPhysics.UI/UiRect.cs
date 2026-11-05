namespace MakarovPhysics.UI;

public readonly struct UiRect(float x, float y, float w, float h)
{
    public readonly float X = x, Y = y, W = w, H = h;
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;
}
