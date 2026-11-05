using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Smooth open tubes and cups. Parallel-transport frames avoid
/// twisting seams when the tube bends through vertical and horizontal.</summary>
public static class LabGeometry
{
    public static void Sweep(DioramaGeometry g, IReadOnlyList<Vec3> path, float radius, Vec3 color, bool inside = false, int sides = 32)
    {
        if (path.Count < 2) return;
        var points = new Vec3[path.Count * sides];
        var normals = new Vec3[points.Length];
        Vec3 x = Vec3.UnitX;

        for (int i = 0; i < path.Count; i++)
        {
            Vec3 tangent = (path[Math.Min(path.Count - 1, i + 1)] - path[Math.Max(0, i - 1)]).Normalized();
            x -= tangent * Vec3.Dot(x, tangent);

            if (x.Length() < .1f)
                x = Vec3.Cross(tangent, Math.Abs(tangent.Y) < .9f ? Vec3.UnitY : Vec3.UnitZ);

            x = x.Normalized();
            var z = Vec3.Cross(x, tangent).Normalized();

            for (int j = 0; j < sides; j++)
            {
                float a = j * MathF.Tau / sides;
                Vec3 n = x * MathF.Cos(a) + z * MathF.Sin(a);
                int k = i * sides + j;
                points[k] = path[i] + n * radius;
                normals[k] = inside ? -n : n;
            }
        }

        void Triangle(int a, int b, int c) => g.SurfaceTriangle(points[a], points[b], points[c], normals[a], normals[b], normals[c], color);

        for (int i = 0; i < path.Count - 1; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = i * sides + j, b = (i + 1) * sides + j, c = (i + 1) * sides + (j + 1) % sides, d = i * sides + (j + 1) % sides;
                if (!inside)
                {
                    Triangle(a, b, c);
                    Triangle(a, c, d);
                }
                else
                {
                    Triangle(a, c, b);
                    Triangle(a, d, c);
                }
            }
        }
    }

    public static void Cup(DioramaGeometry g, Vec3 bottom, float radius, float height)
    {
        Vec3 top = bottom + new Vec3(0, height, 0), white = new(.9f, .96f, 1);
        Sweep(g, [bottom, top], radius, white, false, 64);
        Sweep(g, [bottom + new Vec3(0, .025f, 0), top], radius - .025f, white, true, 64);
        HorizontalRing(g, top, radius - .0125f, .0125f, white);
        g.Tube(bottom, bottom + new Vec3(0, .025f, 0), radius, white);
    }

    public static void HorizontalRing(DioramaGeometry g, Vec3 centre, float radius, float wire, Vec3 color)
    {
        var path = new Vec3[65];

        for (int i = 0; i < path.Length; i++)
        {
            float a = i * MathF.Tau / 64;
            path[i] = centre + new Vec3(radius * MathF.Cos(a), 0, radius * MathF.Sin(a));
        }

        Sweep(g, path, wire, color, false, 12);
    }

    public static void Dashed(DioramaGeometry g, Vec3 a, Vec3 b, float radius, Vec3 color, int count = 8)
    {
        for (int i = 0; i < count; i++)
        {
            g.Tube(a + (b - a) * (i / (float)count), a + (b - a) * ((i + .52f) / count), radius, color);
        }
    }
}
