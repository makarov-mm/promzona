using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Shapes the Engineering sites share: arrows with a head, cables that show the power they carry, ground
/// from a height function. Geometry only; nothing here enters a model.</summary>
public static class SiteGeometry
{
    /// <summary>A horizontal direction at angle t about a vertical axis, as <see cref="MachineGeometry.Annulus"/> measures it.</summary>
    public static Vec3 Around(float t) => new(-MathF.Cos(t), 0, MathF.Sin(t));

    /// <summary>A cone from the centre of its base to its apex.</summary>
    public static void Cone(DioramaGeometry g, Vec3 baseCentre, Vec3 apex, float radius, Vec3 colour, int sides = 16)
    {
        Vec3 d = apex - baseCentre;
        float h = d.Length();
        if (h < 1e-5f || radius < 1e-5f) return;
        Vec3 y = d / h, x = Vec3.Cross(MathF.Abs(y.Y) < .9f ? Vec3.UnitY : Vec3.UnitZ, y).Normalized(), z = Vec3.Cross(x, y);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            Vec3 r0 = x * MathF.Cos(a0) + z * MathF.Sin(a0), r1 = x * MathF.Cos(a1) + z * MathF.Sin(a1);
            Vec3 p0 = baseCentre + r0 * radius, p1 = baseCentre + r1 * radius;
            Vec3 n0 = (r0 * h + y * radius).Normalized(), n1 = (r1 * h + y * radius).Normalized();
            g.SurfaceTriangle(p1, p0, apex, n1, n0, (n0 + n1).Normalized(), colour);
            g.SurfaceTriangle(baseCentre, p0, p1, -y, -y, -y, colour);
        }
    }

    /// <summary>An arrow with a conical head; nothing when it is shorter than its head would be.</summary>
    public static void Arrow(DioramaGeometry g, Vec3 from, Vec3 to, float radius, Vec3 colour)
    {
        Vec3 d = to - from;
        float length = d.Length();
        if (length < radius * 3) return;
        Vec3 u = d / length;
        float head = MathF.Min(length * .45f, radius * 6);
        g.Tube(from, to - u * head, radius, colour);
        Cone(g, to - u * head, to, radius * 2.4f, colour);
    }

    /// <summary>The point at distance s along a polyline, and the direction there.</summary>
    public static (Vec3 Point, Vec3 Direction) Along(IReadOnlyList<Vec3> path, float s)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Vec3 d = path[i + 1] - path[i];
            float l = d.Length();
            if (l < 1e-6f) continue;
            if (s <= l || i + 2 == path.Count) return (path[i] + d * (Math.Min(s, l) / l), d / l);
            s -= l;
        }

        return (path[^1], Vec3.UnitX);
    }

    public static float Length(IReadOnlyList<Vec3> path)
    {
        float length = 0;
        for (int i = 0; i + 1 < path.Count; i++) length += (path[i + 1] - path[i]).Length();
        return length;
    }

    /// <summary>A cable or a pipe with pulses of light running along it the way the power flows: from the first point to
    /// the last for positive power, back for negative. The pulses run at one steady pace, so a changing power never makes
    /// them jump; they grow larger and brighter as the power nears <paramref name="full"/>. <paramref name="clock"/> is
    /// the time that moves them, s.</summary>
    public static void PowerLine(DioramaGeometry g, IReadOnlyList<Vec3> path, float radius, Vec3 sheath, double power, double full, double clock,
        Vec3 forward, Vec3 backward, SurfaceMaterial material = SurfaceMaterial.Rubber, float spacing = 0)
    {
        g.Material = material;
        LabGeometry.Sweep(g, path, radius, sheath, sides: 10);
        float share = (float)Math.Min(1, Math.Abs(power) / Math.Max(1e-9, full));
        if (share < .005f) return;
        if (spacing <= 0) spacing = radius * 20;
        float length = Length(path);
        float phase = (float)(clock * spacing * 1.2 % spacing);
        g.Material = SurfaceMaterial.Emissive;
        Vec3 colour = Vec3.Lerp(sheath, power >= 0 ? forward : backward, .45f + .55f * MathF.Sqrt(share));
        float size = radius * (1.1f + .5f * share);
        for (float s = phase; s < length; s += spacing)
        {
            var (p, u) = Along(path, power >= 0 ? s : length - s);
            g.Tube(p - u * radius * 2, p + u * radius * 2, size, colour);
        }
    }

    /// <summary>Ground over a rectangle from a height function, smooth-shaded, each triangle coloured by a function of
    /// its centre and its normal.</summary>
    public static void Terrain(DioramaGeometry g, float x0, float x1, float z0, float z1, int nx, int nz, Func<float, float, float> height,
        Func<Vec3, Vec3, Vec3> colour)
    {
        float dx = (x1 - x0) / nx, dz = (z1 - z0) / nz;
        var p = new Vec3[(nx + 1) * (nz + 1)];
        var n = new Vec3[p.Length];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                float x = x0 + i * dx, z = z0 + j * dz;
                p[i * (nz + 1) + j] = new(x, height(x, z), z);
                float hx = (height(x + dx * .5f, z) - height(x - dx * .5f, z)) / dx, hz = (height(x, z + dz * .5f) - height(x, z - dz * .5f)) / dz;
                n[i * (nz + 1) + j] = new Vec3(-hx, 1, -hz).Normalized();
            }

        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                int a = i * (nz + 1) + j, b = a + 1, c = a + nz + 2, d = a + nz + 1;
                Vec3 centre = (p[a] + p[b] + p[c] + p[d]) * .25f, normal = (n[a] + n[b] + n[c] + n[d]).Normalized();
                Vec3 col = colour(centre, normal);
                g.SurfaceTriangle(p[a], p[b], p[c], n[a], n[b], n[c], col);
                g.SurfaceTriangle(p[a], p[c], p[d], n[a], n[c], n[d], col);
            }
    }
}
