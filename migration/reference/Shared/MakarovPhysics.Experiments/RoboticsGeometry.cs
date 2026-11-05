using System.Numerics;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Machined details used by the five workshop exhibits.</summary>
public static class RoboticsGeometry
{
    public static readonly Vec3 Metal = new(.54f, .64f, .70f), Dark = new(.035f, .051f, .068f), White = new(.82f, .87f, .88f), Amber = new(.98f, .57f, .055f), Cyan = new(.025f, .69f, .81f);
    public static Vec3 V(Vector3 p) => new(p.X, p.Y, p.Z);
    public static void Box(DioramaGeometry g, Vector3 center, Vector3 half, Vec3 color, Quaternion rotation) => g.Box(V(center), V(half), color, V(Vector3.Transform(Vector3.UnitX, rotation)), V(Vector3.Transform(Vector3.UnitY, rotation)), V(Vector3.Transform(Vector3.UnitZ, rotation)));
    
    public static void Beam(DioramaGeometry g, Vec3 a, Vec3 b, float width, float depth, Vec3 color)
    {
        Vec3 up = (b - a).Normalized();
        Vec3 right = Vec3.Cross(Math.Abs(up.Y) > .9f ? Vec3.UnitZ : Vec3.UnitY, up).Normalized();
        Vec3 front = Vec3.Cross(right, up);
        g.Box((a + b) * .5f, new Vec3(width, (b - a).Length() * .5f, depth), color, right, up, front);
    }

    public static void Bolt(DioramaGeometry g, Vec3 point, Vec3 axis, float radius = .028f)
    {
        g.Material = SurfaceMaterial.Steel;
        g.Tube(point - axis * .012f, point + axis * .012f, radius, Metal);
        var across = Vec3.Cross(Math.Abs(axis.Y) > .9f ? Vec3.UnitZ : Vec3.UnitY, axis).Normalized();
        g.Material = SurfaceMaterial.Rubber;
        g.Tube(point + axis * .013f - across * radius * .55f, point + axis * .013f + across * radius * .55f, .004f, Dark);
    }

    public static void Hose(DioramaGeometry g, Vec3 a, Vec3 b, Vec3 bend, float radius = .014f)
    {
        g.Material = SurfaceMaterial.Rubber;
        Vec3 last = a;

        for (int i = 1; i <= 14; i++)
        {
            float t = i / 14f;
            Vec3 p = a * (1 - t) * (1 - t) + bend * 2 * t * (1 - t) + b * t * t;
            g.Tube(last, p, radius, Dark);
            last = p;
        }
    }

    public static void Wheel(DioramaGeometry g, Vec3 center, Vec3 axis, float radius, float width, double angle, Vec3 hub)
    {
        Vec3 u = Vec3.Cross(Math.Abs(axis.Y) > .9 ? Vec3.UnitZ : Vec3.UnitY, axis).Normalized();
        Vec3 v = Vec3.Cross(axis, u);
        g.Material = SurfaceMaterial.Rubber;
        MachineGeometry.Annulus(g, center, axis, radius * .60f, radius, width, 0, MathF.Tau, Dark, 40);

        for (int i = 0; i < 24; i++)
        {
            float a = (float)angle + i * MathF.Tau / 24;
            Vec3 radial = u * MathF.Cos(a) + v * MathF.Sin(a);
            Vec3 tangent = -u * MathF.Sin(a) + v * MathF.Cos(a);
            g.Box(center + radial * radius, new(width * .49f, .017f, radius * .055f), new Vec3(.075f, .085f, .093f), axis, radial, tangent);
        }

        g.Material = SurfaceMaterial.Steel;
        g.Tube(center - axis * width * .52f, center + axis * width * .52f, radius * .60f, hub);

        for (int j = -1; j <= 1; j += 2)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = (float)angle + i * MathF.Tau / 6;
                Vec3 d = u * MathF.Cos(a) + v * MathF.Sin(a);
                Bolt(g, center + axis * j * width * .54f + d * radius * .38f, axis * j, radius * .055f);
            }

            g.Material = SurfaceMaterial.Enamel;
            g.Tube(center + axis * j * width * .53f, center + axis * j * width * .58f, radius * .19f, Amber);
        }
    }

    public static void Plinth(DioramaGeometry g, float x, float z, Vec3 accent)
    {
        g.Material = SurfaceMaterial.Stone;
        g.Box(new Vec3(0, -.12f, 0), new Vec3(x, .12f, z), new Vec3(.105f, .145f, .175f));
        g.Material = SurfaceMaterial.Steel;

        for (int s = -1; s <= 1; s += 2)
        {
            g.Box(new Vec3(0, -.16f, s * z), new Vec3(x, .06f, .025f), Metal);
            g.Box(new Vec3(s * x, -.16f, 0), new Vec3(.025f, .06f, z), Metal);
        }

        g.Material = SurfaceMaterial.Emissive;
        g.Box(new Vec3(0, -.075f, z + .006f), new Vec3(x * .84f, .009f, .005f), accent * .65f);
        g.Material = SurfaceMaterial.Rubber;

        for (int a = -1; a <= 1; a += 2)
        {
            for (int b = -1; b <= 1; b += 2)
            {
                g.Box(new Vec3(a * (x - .2f), -.29f, b * (z - .2f)), new Vec3(.16f, .055f, .14f), Dark);
            }
        }
    }

    public static void Hazard(DioramaGeometry g, Vec3 a, Vec3 b, float width)
    {
        Vec3 d = b - a;
        Vec3 u = d.Normalized();
        Vec3 cross = Vec3.Cross(Vec3.UnitY, u);
        g.Material = SurfaceMaterial.Flat;

        for (int i = 0; i < 24; i++)
        {
            g.Box(a + d * ((i + .5f) / 24), new Vec3(d.Length() / 48, .003f, width), i % 2 == 0 ? Amber : Dark, u, Vec3.UnitY, cross);
        }
    }

    public static void Cylinder(DioramaGeometry g, Vec3 a, Vec3 b, float barrel, float fraction, Vec3 color)
    {
        Vec3 d = b - a;
        g.Material = SurfaceMaterial.Enamel;
        g.Tube(a, a + d * fraction, barrel, color);
        g.Material = SurfaceMaterial.Steel;
        g.Tube(a + d * (fraction - .02f), b, barrel * .45f, White);
        MachineGeometry.Annulus(g, a + d * fraction, d.Normalized(), barrel * .48f, barrel * 1.04f, .035f, 0, MathF.Tau, Metal, 24);
        g.Ellipsoid(a, new Vec3(barrel * 1.2f, barrel * 1.2f, barrel * 1.2f), Metal);
        g.Ellipsoid(b, new Vec3(barrel * .85f, barrel * .85f, barrel * .85f), Metal);
    }

    public static void Gear(DioramaGeometry g, Vec3 c, Vec3 axis, float radius, float depth, int teeth, double angle, Vec3 color)
    {
        Vec3 u = Vec3.Cross(Math.Abs(axis.Y) > .9f ? Vec3.UnitZ : Vec3.UnitY, axis).Normalized();
        Vec3 v = Vec3.Cross(axis, u);
        MachineGeometry.Annulus(g, c, axis, radius * .25f, radius * .88f, depth, 0, MathF.Tau, color, 64);

        for (int i = 0; i < teeth; i++)
        {
            float a = (float)(angle + i * Math.Tau / teeth);
            Vec3 n = u * MathF.Cos(a) + v * MathF.Sin(a);
            Vec3 t = -u * MathF.Sin(a) + v * MathF.Cos(a);
            g.Box(c + n * radius * .93f, new(radius * .10f, radius * .75f / teeth, depth * .5f), color, n, t, axis);
        }
    }

    public static void BevelGear(DioramaGeometry g, Vec3 c, Vec3 axis, double angle, Vec3 color)
    {
        Vec3 u = Vec3.Cross(Math.Abs(axis.Y) > .9f ? Vec3.UnitZ : Vec3.UnitY, axis).Normalized();
        Vec3 v = Vec3.Cross(axis, u);
        const int slices = 160;

        for (int i = 0; i < slices; i++)
        {
            float a = (float)(angle + i * Math.Tau / slices), b = (float)(angle + (i + 1) * Math.Tau / slices);
            float ra = .50f + (i % 8 is 2 or 3 or 4 or 5 ? .025f : -.015f), rb = .50f + ((i + 1) % 8 is 2 or 3 or 4 or 5 ? .025f : -.015f);
            Vec3 na = u * MathF.Cos(a) + v * MathF.Sin(a);
            Vec3 nb = u * MathF.Cos(b) + v * MathF.Sin(b);
            g.Quad(c + axis * .20f + na * ra * .4f, c + axis * .50f + na * ra, c + axis * .50f + nb * rb, c + axis * .20f + nb * rb * .4f, color);
            g.Quad(c + axis * .50f + na * ra, c + axis * .56f + na * ra, c + axis * .56f + nb * rb, c + axis * .50f + nb * rb, color);
            g.Quad(c + axis * .56f + na * .13f, c + axis * .56f + na * ra, c + axis * .56f + nb * rb, c + axis * .56f + nb * .13f, color);
        }
    }
}
