using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Sectioned annuli and instrument faces shared by the three machine exhibits.</summary>
public static class MachineGeometry
{
    public static void Annulus(DioramaGeometry g, Vec3 c, Vec3 axis, float inner, float outer, float depth, float start, float span, Vec3 color, int segments)
    {
        axis = axis.Normalized();
        Vec3 u = Vec3.Cross(Math.Abs(axis.Y) < .9f ? Vec3.UnitY : Vec3.UnitZ, axis).Normalized();
        Vec3 v = Vec3.Cross(axis, u), z = axis * (depth * .5f);
        Vec3 P(float r, float t) => c + (u * MathF.Cos(t) + v * MathF.Sin(t)) * r;

        for (int i = 0; i < segments; i++)
        {
            float a = start + span * i / segments, b = start + span * (i + 1) / segments;
            Vec3 ia = P(inner, a), ib = P(inner, b), oa = P(outer, a), ob = P(outer, b);
            Vec3 na = (oa - c).Normalized(), nb = (ob - c).Normalized();

            g.SurfaceTriangle(oa - z, ob - z, ob + z, na, nb, nb, color);
            g.SurfaceTriangle(oa - z, ob + z, oa + z, na, nb, na, color);
            g.SurfaceTriangle(ib - z, ia - z, ia + z, -nb, -na, -na, color);
            g.SurfaceTriangle(ib - z, ia + z, ib + z, -nb, -na, -nb, color);
            g.Quad(ia + z, oa + z, ob + z, ib + z, color);
            g.Quad(ib - z, ob - z, oa - z, ia - z, color);

            if (i == 0) g.Quad(ia - z, oa - z, oa + z, ia + z, color);
            if (i == segments - 1) g.Quad(ib + z, ob + z, ob - z, ib - z, color);
        }
    }

    public static void Dial(DioramaGeometry g, Vec3 c, float radius, double fraction, Vec3 accent)
    {
        g.Material = SurfaceMaterial.Steel;
        Annulus(g, c, Vec3.UnitZ, radius * .9f, radius, .055f, 0, MathF.Tau, new Vec3(.60f, .67f, .71f), 48);
        g.Material = SurfaceMaterial.Ceramic;
        g.Tube(c - Vec3.UnitZ * .025f, c, radius * .90f, new Vec3(.82f, .85f, .80f));
        g.Material = SurfaceMaterial.Flat;

        for (int k = 0; k <= 20; k++)
        {
            float a = (float)(Math.PI * (1.25 - 1.5 * k / 20d));
            Vec3 d = new(MathF.Cos(a), MathF.Sin(a), 0);
            g.Tube(c + d * radius * (k % 5 == 0 ? .66f : .76f) + Vec3.UnitZ * .007f, c + d * radius * .85f + Vec3.UnitZ * .007f, radius * .013f, new Vec3(.09f, .13f, .16f));
        }

        float angle = (float)(Math.PI * (1.25 - 1.5 * Math.Clamp(fraction, 0, 1)));
        g.Tube(c + Vec3.UnitZ * .02f, c + new Vec3(MathF.Cos(angle), MathF.Sin(angle), 0) * radius * .73f + Vec3.UnitZ * .02f, radius * .027f, accent);
        g.Ellipsoid(c + Vec3.UnitZ * .02f, new Vec3(radius * .09f, radius * .09f, .02f), new Vec3(.12f, .15f, .18f));
    }
}
