namespace MakarovPhysics.UI;

public readonly struct GlyphInfo(float u0, float v0, float u1, float v1, float w, float h, float offsetX, float offsetY, float advance, int page = 0)
{
    public readonly float U0 = u0, V0 = v0, U1 = u1, V1 = v1;
    public readonly float W = w, H = h;
    public readonly float OffsetX = offsetX, OffsetY = offsetY;
    public readonly float Advance = advance;
    public readonly int Page = page;
}
