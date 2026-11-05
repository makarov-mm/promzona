namespace MakarovPhysics.Renderer;
public static class FieldPalettes
{
    public static Vec3 Ocean(float t)
    {
        t = Math.Clamp(t, 0, 1);
        if (t < .30f) return Vec3.Lerp(new(.016f,.024f,.045f), new(.075f,.19f,.29f), t/.30f);
        if (t < .68f) return Vec3.Lerp(new(.075f,.19f,.29f), new(.20f,.62f,.66f), (t-.30f)/.38f);
        return Vec3.Lerp(new(.20f,.62f,.66f), new(.85f,.94f,.82f), (t-.68f)/.32f);
    }
}
