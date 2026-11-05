using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Reusable apparatus primitives; no model state or time evolution.</summary>
public static class ExhibitGeometry
{
    public static Vec3 V(double x, double y, double z = 0) => new((float)x, (float)y, (float)z);

    public static void Ring(DioramaGeometry g, Vec3 center, float radius, Vec3 color, float tube = .015f, int segments = 48)
    {
        for (int i = 0; i < segments; i++)
        {
            float a = i * MathF.Tau / segments, b = (i + 1) * MathF.Tau / segments;
            g.Tube(center + new Vec3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0), center + new Vec3(MathF.Cos(b) * radius, MathF.Sin(b) * radius, 0), tube, color);
        }
    }

    public static void Spring(DioramaGeometry g, Vec3 a, Vec3 b, float radius, Vec3 color, int turns = 12)
    {
        Vec3 y = (b - a).Normalized();
        Vec3 x = Vec3.Cross(y, Math.Abs(y.Y) < .9 ? Vec3.UnitY : Vec3.UnitX).Normalized();
        Vec3 z = Vec3.Cross(y, x);
        Vec3 old = a;
        int n = turns * 12;

        for (int i = 1; i <= n; i++)
        {
            float t = (float)i / n, phase = t * MathF.Tau * turns;
            Vec3 p = a + (b - a) * t + (x * MathF.Cos(phase) + z * MathF.Sin(phase)) * (radius * MathF.Min(1, t * 12) * MathF.Min(1, (1 - t) * 12));
            g.Tube(old, p, .014f, color);
            old = p;
        }
    }

    public static void Arrow(DioramaGeometry g, Vec3 a, Vec3 b, Vec3 color, float radius = .022f)
    {
        Vec3 d = b - a;
        float length = d.Length();
        if (length < .001f) return;
        Vec3 u = d / length;
        Vec3 side = Vec3.Cross(u, Math.Abs(u.Z) < .9 ? Vec3.UnitZ : Vec3.UnitY).Normalized();
        g.Tube(a, b, radius, color);
        float head = Math.Min(.18f, length * .28f);
        g.Tube(b, b - u * head + side * head * .55f, radius, color);
        g.Tube(b, b - u * head - side * head * .55f, radius, color);
    }

    public static void Gear(DioramaGeometry g, Vec3 center, float radius, double phase, Vec3 color, int teeth = 20)
    {
        g.Tube(center - new Vec3(0, 0, .06f), center + new Vec3(0, 0, .06f), radius * .84f, color);

        for (int i = 0; i < teeth; i++)
        {
            float a = (float)(phase + i * Math.Tau / teeth);
            Vec3 radial = new(MathF.Cos(a), MathF.Sin(a), 0), tangent = new(-radial.Y, radial.X, 0);
            g.Box(center + radial * radius * .94f, new Vec3(.09f, radius * .10f, .08f), color, tangent, radial, Vec3.UnitZ);
        }

        g.Tube(center - new Vec3(0, 0, .10f), center + new Vec3(0, 0, .11f), radius * .15f, new(.7f, .75f, .72f));
        g.Tube(center + new Vec3(0, 0, .095f), center + V(Math.Cos(phase) * radius * .68, Math.Sin(phase) * radius * .68, .095), .025f, new(.96f, .66f, .20f));
    }

    public static void Polyline(DioramaGeometry g, IReadOnlyList<Vec3> points, Vec3 color, float radius = .014f)
    {
        for (int i = 1; i < points.Count; i++)
        {
            g.Tube(points[i - 1], points[i], radius, color);
        }
    }
}
