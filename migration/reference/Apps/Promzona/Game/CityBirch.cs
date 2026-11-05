using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Baked silver birch with a tapered white stem, curved limbs and
/// hanging leafy branchlets. No per-frame geometry or alpha overdraw.</summary>
internal static class CityBirch
{
    private static void Stem(DioramaGeometry g, Vec3 a, Vec3 b, float ra, float rb, Vec3 color, int sides)
    {
        Vec3 axis = (b - a).Normalized(),
            u = Vec3.Cross(axis, Math.Abs(axis.Y) > .9f ? Vec3.UnitX : Vec3.UnitY).Normalized(),
            v = Vec3.Cross(axis, u);
        for (int n = 0; n < sides; n++)
        {
            float t = n * MathF.Tau / sides, s = (n + 1) * MathF.Tau / sides;
            Vec3 p = u * MathF.Cos(t) + v * MathF.Sin(t), q = u * MathF.Cos(s) + v * MathF.Sin(s);
            g.SurfaceTriangle(a + p * ra, a + q * ra, b + q * rb, p, q, q, color);
            g.SurfaceTriangle(a + p * ra, b + q * rb, b + p * rb, p, q, p, color);
        }
    }

    private static void Leaf(DioramaGeometry g, Vec3 p, Vec3 axis, float size, float angle, Vec3 tint)
    {
        Vec3 r = Vec3.Cross(axis, new Vec3(MathF.Cos(angle), .6f, MathF.Sin(angle))).Normalized() * size * .65f;
        Vec3 a = axis.Normalized() * size, b = p + r, c = p + a, d = p - r;
        // Broad, pointed birch leaves, with a raised central vein and two faces.
        g.Triangle(p - a, b, c, tint);
        g.Triangle(p - a, c, d, tint * .9f);
        g.Triangle(p - a, c, b, tint * .87f);
        g.Triangle(p - a, d, c, tint * .82f);
    }

    public static void Draw(DioramaGeometry g, Vec3 p, float h, int lod)
    {
        float phase = p.X * .173f + p.Z * .131f;

        Vec3 Trunk(float t) => p + new Vec3(MathF.Sin(t * 1.7f + phase) * h * .025f * t, h * t,
            MathF.Cos(phase + t) * h * .025f * t);

        g.Material = (SurfaceMaterial)370;
        for (int n = 0; n < 6; n++)
            Stem(g, Trunk(n / 6f), Trunk((n + 1) / 6f), h * (.023f - .019f * n / 6), h * (.023f - .019f * (n + 1) / 6),
                new(.78f, .78f, .72f), lod == 0 ? 9 : 6);
        int limbs = lod == 0 ? 16 : lod == 1 ? 10 : 6,
            twigs = lod == 0 ? 5 : lod == 1 ? 4 : 3,
            leaves = lod == 0 ? 8 : 5;
        for (int n = 0; n < limbs; n++)
        {
            float t = .36f + .57f * n / limbs, a = phase + n * 2.399963f;
            Vec3 dir = new(MathF.Cos(a), 0, MathF.Sin(a)), side = new(-dir.Z, 0, dir.X);
            float reach = h * (.18f + .13f * MathF.Sin(MathF.PI * (n + .8f) / limbs));
            Vec3 root = Trunk(t),
                bend = root + dir * reach * .50f + Vec3.UnitY * h * .11f,
                end = root + dir * reach + Vec3.UnitY * h * .04f;
            g.Material = (SurfaceMaterial)370;
            Stem(g, root, bend, h * .010f, h * .005f, new(.66f, .66f, .59f), 5);
            g.Material = SurfaceMaterial.Bark;
            Stem(g, bend, end, h * .005f, .009f, new(.30f, .25f, .17f), 5);
            for (int k = 0; k < twigs; k++)
            {
                Vec3 from = Vec3.Lerp(bend, end, (k + .4f) / twigs),
                    tip = from + dir * h * .08f + side * ((k % 2 == 0 ? 1 : -1) * h * (.055f + .035f * (k % 3))) -
                          Vec3.UnitY * h * (.14f + .035f * (k % 3));
                g.Material = SurfaceMaterial.Bark;
                Stem(g, from, tip, .009f, .003f, new(.27f, .24f, .15f), 4);
                g.Material = SurfaceMaterial.Foliage;
                for (int l = 0; l < leaves; l++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float f = (l + .35f) / leaves, ang = a + sign * 1.1f + l * .3f;
                    float seed = n * 19.31f + k * 7.71f + l * 3.91f + sign;
                    Vec3 scatter = new(MathF.Sin(seed) * .18f, MathF.Cos(seed * 1.7f) * .17f,
                        MathF.Cos(seed * .91f) * .18f);
                    Vec3 c = Vec3.Lerp(from, tip, f) + side * (sign * (.055f + .10f * MathF.Sin(f * MathF.PI))) +
                             scatter;
                    float size = (lod == 0 ? .11f : lod == 1 ? .16f : .22f) *
                                 (1 + .25f * MathF.Sin(n * 13 + k * 7 + l));
                    Vec3 axis = new(MathF.Cos(seed * 1.3f), .2f + MathF.Sin(seed) * .7f, MathF.Sin(seed * 1.3f));
                    Leaf(g, c, axis, size, ang, new(.28f + .035f * (k % 3), .40f + .025f * (l % 3), .13f));
                }
            }
        }
    }
}
