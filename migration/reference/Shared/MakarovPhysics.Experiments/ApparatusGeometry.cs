using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Modelled instrument parts, including real mesh digits on pendants.</summary>
public static class ApparatusGeometry
{
    public static void Ring(DioramaGeometry g, Vec3 c, Vec3 axis, float radius, float wire, Vec3 color, int segments = 48)
    {
        axis = axis.Normalized();
        Vec3 u = Vec3.Cross(axis, Math.Abs(axis.Y) < .9 ? Vec3.UnitY : Vec3.UnitX).Normalized(), v = Vec3.Cross(axis, u);
        Vec3[] path = new Vec3[segments + 1];
        for (int j = 0; j <= segments; j++)
        {
            float a = j * MathF.Tau / segments;
            path[j] = c + radius * (u * MathF.Cos(a) + v * MathF.Sin(a));
        }

        LabGeometry.Sweep(g, path, wire, color, sides: 8);
    }

    public static void Lathe(DioramaGeometry g, Vec3 c, IReadOnlyList<(float Y, float Radius)> profile, Vec3 color, int sides = 64)
    {
        for (int i = 0; i < profile.Count - 1; i++)
        {
            var a = profile[i];
            var b = profile[i + 1];
            float dy = b.Y - a.Y, dr = b.Radius - a.Radius;
            for (int j = 0; j < sides; j++)
            {
                float t = j * MathF.Tau / sides, q = (j + 1) * MathF.Tau / sides;
                Vec3 N(float angle) => new Vec3(dy * MathF.Cos(angle), -dr, dy * MathF.Sin(angle)).Normalized();
                Vec3 P(float y, float r, float angle) => c + new Vec3(r * MathF.Cos(angle), y, r * MathF.Sin(angle));
                Vec3 p0 = P(a.Y, a.Radius, t), p1 = P(b.Y, b.Radius, t), p2 = P(b.Y, b.Radius, q), p3 = P(a.Y, a.Radius, q);
                g.SurfaceTriangle(p0, p1, p2, N(t), N(t), N(q), color);
                g.SurfaceTriangle(p0, p2, p3, N(t), N(q), N(q), color);
            }
        }
    }

    public static void Hemisphere(DioramaGeometry g, Vec3 c, float r, Vec3 color)
    {
        var profile = new List<(float, float)>();
        for (int i = 0; i <= 12; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 24;
            profile.Add((r * MathF.Sin(a), Math.Max(.0001f, r * MathF.Cos(a))));
        }

        profile.Add((0, .0001f));
        Lathe(g, c, profile, color, 40);
    }

    public static void Number(DioramaGeometry g, Vec3 c, int value, float size, Vec3 color, Vec3 right, Vec3 up)
    {
        string s = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int[] masks = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
        (float, float, float, float)[] lines = [(-.32f, .5f, .32f, .5f), (.32f, .5f, .32f, 0), (.32f, 0, .32f, -.5f), (-.32f, -.5f, .32f, -.5f), (-.32f, 0, -.32f, -.5f), (-.32f, .5f, -.32f, 0), (-.32f, 0, .32f, 0)];
        for (int i = 0; i < s.Length; i++)
        {
            Vec3 p = c + right * ((i - (s.Length - 1) * .5f) * size * .94f);
            for (int k = 0; k < 7; k++)
                if ((masks[s[i] - '0'] & (1 << k)) != 0)
                {
                    var(a, b, d, e) = lines[k];
                    g.Tube(p + right * (a * size) + up * (b * size), p + right * (d * size) + up * (e * size), size * .044f, color);
                }
        }
    }
}
