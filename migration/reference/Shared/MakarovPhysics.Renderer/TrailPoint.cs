namespace MakarovPhysics.Renderer;

public readonly struct TrailPoint(Vec3 pos, Vec3 color)
{
    public readonly Vec3 Pos = pos;
    public readonly Vec3 Color = color;
}
