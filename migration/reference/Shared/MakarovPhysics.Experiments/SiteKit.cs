using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>
/// Small pieces the sites of the third Engineering and Earth batch (30.10.2026) share: a frustum for horns, shades
/// and funnels, a polyline of rods for profiles drawn in the scene, a glowing bulb, and the bookkeeping of the twelve
/// local lamps of <see cref="OutdoorLighting"/>. Presentation only.
/// </summary>
public static class SiteKit
{
    /// <summary>The side of a truncated cone from the circle (a, ra) to the circle (b, rb), outward normals.</summary>
    public static void Frustum(DioramaGeometry g, Vec3 a, Vec3 b, float ra, float rb, Vec3 color, int sides = 24)
    {
        Vec3 axis = (b - a).Normalized();
        Vec3 u = MathF.Abs(axis.Y) < .9f ? Vec3.Cross(axis, Vec3.UnitY).Normalized() : Vec3.Cross(axis, Vec3.UnitX).Normalized();
        Vec3 v = Vec3.Cross(axis, u);
        for (int i = 0; i < sides; i++)
        {
            float t0 = MathF.Tau * i / sides, t1 = MathF.Tau * (i + 1) / sides;
            Vec3 d0 = u * MathF.Cos(t0) + v * MathF.Sin(t0), d1 = u * MathF.Cos(t1) + v * MathF.Sin(t1);
            g.Quad(a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb, color);
        }
    }

    /// <summary>Rods through the points, as a drawn line.</summary>
    public static void Polyline(DioramaGeometry g, IReadOnlyList<Vec3> points, float radius, Vec3 color)
    {
        for (int i = 1; i < points.Count; i++) g.Rod(points[i - 1], points[i], radius, color);
    }

    /// <summary>A bulb that glows in the emission pass.</summary>
    public static void Bulb(DioramaGeometry g, Vec3 centre, float radius, Vec3 color)
    {
        var m = g.Material;
        g.Material = SurfaceMaterial.Emissive;
        g.Ellipsoid(centre, new Vec3(radius, radius, radius), color);
        g.Material = m;
    }

    /// <summary>Sets one of the twelve local lamps; a zero direction makes it shine all round, a direction makes it
    /// a spot with the shader's cone.</summary>
    public static void Lamp(OutdoorLighting l, int index, Vec3 position, Vec3 color, Vec3 direction = default)
    {
        if (index < 0 || index >= l.Positions.Length) return;
        l.Positions[index] = position;
        l.Colors[index] = color;
        l.Directions[index] = direction;
    }

    /// <summary>Switches every lamp off.</summary>
    public static void Dark(OutdoorLighting l)
    {
        Array.Clear(l.Colors);
        Array.Clear(l.Directions);
    }

    /// <summary>A smooth-shaded strip of the surface z = h(x) between x0 and x1 at a fixed width in z, for profiles of
    /// terrain, water and sand seen in section; the colour may vary along it.</summary>
    public static void Strip(DioramaGeometry g, IReadOnlyList<float> xs, IReadOnlyList<float> ys, float z0, float z1, Func<int, Vec3> color)
    {
        for (int i = 1; i < xs.Count; i++)
        {
            Vec3 a = new(xs[i - 1], ys[i - 1], z0), b = new(xs[i], ys[i], z0), c = new(xs[i], ys[i], z1), d = new(xs[i - 1], ys[i - 1], z1);
            g.Quad(a, d, c, b, color(i));
        }
    }

    /// <summary>A vertical section face below a profile: from y = floor up to the profile, at a fixed z.</summary>
    public static void Section(DioramaGeometry g, IReadOnlyList<float> xs, IReadOnlyList<float> ys, float floor, float z, Func<int, Vec3> color, bool front = true)
    {
        for (int i = 1; i < xs.Count; i++)
        {
            Vec3 a = new(xs[i - 1], floor, z), b = new(xs[i], floor, z), c = new(xs[i], ys[i], z), d = new(xs[i - 1], ys[i - 1], z);
            if (front) g.Quad(a, b, c, d, color(i)); else g.Quad(a, d, c, b, color(i));
        }
    }
}
