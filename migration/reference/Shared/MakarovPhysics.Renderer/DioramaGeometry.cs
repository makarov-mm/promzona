namespace MakarovPhysics.Renderer;
/// <summary>World-shaded faceted solids, streamed in one draw. Twin of
/// Mac/Sources/MPRender/DioramaGeometry.swift; dimensions are radii / half extents.</summary>
public sealed class DioramaGeometry
{
    public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    public readonly List<TrailPoint> Points = new(160_000);
    // Opt-in surface stream: legacy modules retain their exact CPU-lit geometry.
    public bool CaptureSurface;
    /// <summary>Opt-in edge chamfers on metal and painted housings. Outside
    /// dimensions stay unchanged; collision and numerical geometry is independent.</summary>
    public bool BevelBoxes;
    /// <summary>Fine edge radii and round small shafts for close inspection; visual only.</summary>
    public bool PrecisionFinish;
    public SurfaceMaterial Material = SurfaceMaterial.Automatic;
    public readonly List<SurfaceVertex> SurfacePoints = new();
    public Vec3 Light = new Vec3(0.42f, 0.72f, 0.38f).Normalized();
    public float Daylight = 1, Night;
    public Vec3 Eye, Horizon = new(0.82f, 0.88f, 0.86f);
    public bool Haze;
    public void Clear()
    {
        Points.Clear();
        SurfacePoints.Clear();
        Material = SurfaceMaterial.Automatic;
    }

    public void Triangle(Vec3 a, Vec3 b, Vec3 c, Vec3 color)
    {
        Vec3 n = Vec3.Cross(b - a, c - a).Normalized();
        if (Vec3.Dot(n, n) <= 0.5f)
            return;
        float diffuse = MathF.Max(0, Vec3.Dot(n, Light));
        float sky = MathF.Max(0, n.Y) * 0.12f;
        float intensity = 0.20f + Daylight * (0.34f + 0.48f * diffuse + sky);
        Vec3 lit = color * intensity;
        lit = new Vec3(lit.X * (1 + 0.06f * diffuse), lit.Y, lit.Z * (1 + 0.10f * (1 - diffuse)));
        lit = new Vec3(lit.X * (1 - 0.25f * Night), lit.Y * (1 - 0.18f * Night), lit.Z * (1 + 0.10f * Night));
        if (Haze)
        {
            float d = ((a + b + c) / 3 - Eye).Length();
            float t = Math.Clamp((d - 300) / 680, 0, 1), f = t * t * (3 - 2 * t);
            lit = lit * (1 - f) + Horizon * f;
        }

        Points.Add(new(a, lit));
        Points.Add(new(b, lit));
        Points.Add(new(c, lit));
        if (CaptureSurface)
        {
            SurfacePoints.Add(new(a, n, color, a, Material));
            SurfacePoints.Add(new(b, n, color, b, Material));
            SurfacePoints.Add(new(c, n, color, c, Material));
        }
    }

    private void SmoothTriangle(Vec3 a, Vec3 b, Vec3 c, Vec3 na, Vec3 nb, Vec3 nc, Vec3 localA, Vec3 localB, Vec3 localC, Vec3 color)
    {
        int previous = SurfacePoints.Count;
        Triangle(a, b, c, color);
        if (!CaptureSurface || SurfacePoints.Count != previous + 3)
            return;
        SurfacePoints[previous] = new(a, na, color, localA, Material);
        SurfacePoints[previous + 1] = new(b, nb, color, localB, Material);
        SurfacePoints[previous + 2] = new(c, nc, color, localC, Material);
    }

    public void SurfaceTriangle(Vec3 a, Vec3 b, Vec3 c, Vec3 na, Vec3 nb, Vec3 nc, Vec3 color) => SmoothTriangle(a, b, c, na, nb, nc, a, b, c, color);
    private static readonly (Vec3 a, Vec3 b, Vec3 c)[] SmoothShell = BuildSmoothShell(20, 12);
    private static readonly (Vec3 a, Vec3 b, Vec3 c)[] SmallShell = BuildSmoothShell(8, 6);
    private static (Vec3, Vec3, Vec3)[] BuildSmoothShell(int slices, int rings)
    {
        var triangles = new List<(Vec3, Vec3, Vec3)>();
        Vec3 P(int longitude, int latitude)
        {
            float a = longitude * MathF.Tau / slices, b = -MathF.PI / 2 + latitude * MathF.PI / rings;
            return new(MathF.Cos(b) * MathF.Cos(a), MathF.Sin(b), MathF.Cos(b) * MathF.Sin(a));
        }

        for (int j = 0; j < rings; j++)
            for (int i = 0; i < slices; i++)
            {
                Vec3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                if (j > 0)
                    triangles.Add((a, c, b));
                if (j < rings - 1)
                    triangles.Add((a, d, c));
            }

        return triangles.ToArray();
    }

    private static readonly (Vec3 a, Vec3 b, Vec3 c)[] Shell = BuildShell();
    private static (Vec3, Vec3, Vec3)[] BuildShell()
    {
        var triangles = new List<(Vec3, Vec3, Vec3)>();
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.Tau / 8, b = (i + 1) * MathF.Tau / 8;
            Vec3 p = new(MathF.Cos(a) * 0.84f, -0.54f, MathF.Sin(a) * 0.84f);
            Vec3 q = new(MathF.Cos(b) * 0.84f, -0.54f, MathF.Sin(b) * 0.84f);
            Vec3 u = new(p.X, 0.54f, p.Z), v = new(q.X, 0.54f, q.Z);
            triangles.Add((new(0, -1, 0), p, q));
            triangles.Add((p, u, v));
            triangles.Add((p, v, q));
            triangles.Add((u, new(0, 1, 0), v));
        }

        return triangles.ToArray();
    }

    public void Ellipsoid(Vec3 center, Vec3 radii, Vec3 color) => Ellipsoid(center, radii, color, Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);
    public void Ellipsoid(Vec3 center, Vec3 radii, Vec3 color, Vec3 right, Vec3 up, Vec3 forward)
    {
        Vec3 x = right * radii.X, y = up * radii.Y, z = forward * radii.Z;
        Vec3 Point(Vec3 p) => center + x * p.X + y * p.Y + z * p.Z;
        if (!CaptureSurface)
        {
            foreach (var(a, b, c)in Shell)
                Triangle(Point(a), Point(b), Point(c), color);
            return;
        }

        Vec3 Normal(Vec3 p) => (right * (p.X / Math.Max(1e-7f, radii.X)) + up * (p.Y / Math.Max(1e-7f, radii.Y)) + forward * (p.Z / Math.Max(1e-7f, radii.Z))).Normalized();
        Vec3 Local(Vec3 p) => new(p.X * radii.X, p.Y * radii.Y, p.Z * radii.Z);
        foreach (var(a, b, c)in Math.Max(radii.X, Math.Max(radii.Y, radii.Z)) < .10f ? SmallShell : SmoothShell)
            SmoothTriangle(Point(a), Point(b), Point(c), Normal(a), Normal(b), Normal(c), Local(a), Local(b), Local(c), color);
    }

    public void Rod(Vec3 a, Vec3 b, float radius, Vec3 color)
    {
        Vec3 d = b - a;
        float length = d.Length();
        if (length <= 0.00001f)
            return;
        Vec3 y = d / length;
        Vec3 x = Vec3.Cross(MathF.Abs(y.Y) < 0.9f ? Vec3.UnitY : Vec3.UnitZ, y).Normalized();
        Ellipsoid((a + b) / 2, new(radius, length / 2 + radius * 0.2f, radius), color, x, y, Vec3.Cross(x, y));
    }

    public void Tube(Vec3 a, Vec3 b, float radius, Vec3 color)
    {
        Vec3 d = b - a;
        float length = d.Length();
        if (length <= 0.00001f)
            return;
        Vec3 y = d / length, x = Vec3.Cross(MathF.Abs(y.Y) < 0.9f ? Vec3.UnitY : Vec3.UnitZ, y).Normalized();
        Vec3 z = Vec3.Cross(x, y);
        // Sub-pixel wires need smooth normals, not a large radial mesh.
        int segments = CaptureSurface ? (PrecisionFinish ? (radius > .12f ? 40 : radius > .02f ? 24 : 16) : (radius > .12f ? 40 : radius > .02f ? 12 : 8)) : 8;
        for (int i = 0; i < segments; i++)
        {
            float t = i * MathF.Tau / segments, u = (i + 1) * MathF.Tau / segments;
            Vec3 p = (x * MathF.Cos(t) + z * MathF.Sin(t)) * radius, q = (x * MathF.Cos(u) + z * MathF.Sin(u)) * radius;
            if (!CaptureSurface)
            {
                Triangle(a + p, b + p, b + q, color);
                Triangle(a + p, b + q, a + q, color);
                Triangle(a, a + p, a + q, color);
                Triangle(b, b + q, b + p, color);
            }
            else
            {
                Vec3 np = p / radius, nq = q / radius;
                Vec3 lp = new(MathF.Cos(t) * radius, 0, MathF.Sin(t) * radius), lq = new(MathF.Cos(u) * radius, 0, MathF.Sin(u) * radius), top = new(0, length, 0);
                SmoothTriangle(a + p, b + p, b + q, np, np, nq, lp, lp + top, lq + top, color);
                SmoothTriangle(a + p, b + q, a + q, np, nq, nq, lp, lq + top, lq, color);
                SmoothTriangle(a, a + p, a + q, -y, -y, -y, Vec3.Zero, lp, lq, color);
                SmoothTriangle(b, b + q, b + p, y, y, y, top, lq + top, lp + top, color);
            }
        }
    }

    private static readonly (int, int, int, int)[] BoxFaces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 4, 7, 3), (1, 2, 6, 5), (3, 7, 6, 2), (0, 1, 5, 4)];
    public void Box(Vec3 center, Vec3 half, Vec3 color) => Box(center, half, color, Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);
    public void Box(Vec3 center, Vec3 half, Vec3 color, Vec3 right, Vec3 up, Vec3 forward)
    {
        float smallest = Math.Min(half.X, Math.Min(half.Y, half.Z));
        if (BevelBoxes && CaptureSurface && smallest > (PrecisionFinish ? .002f : .018f) &&
            Material is SurfaceMaterial.Enamel or SurfaceMaterial.Steel or SurfaceMaterial.Brass or SurfaceMaterial.CastMetal or SurfaceMaterial.MachinedMetal or SurfaceMaterial.Polymer or SurfaceMaterial.BrushedAluminium or SurfaceMaterial.GlazedTile or SurfaceMaterial.LacqueredWood)
        {
            ChamferBox(center, half, color, right, up, forward, Math.Min(.035f, smallest * .12f));
            return;
        }
        Vec3 x = right * half.X, y = up * half.Y, z = forward * half.Z;
        Vec3[] p = [center - x - y - z, center + x - y - z, center + x + y - z, center - x + y - z, center - x - y + z, center + x - y + z, center + x + y + z, center - x + y + z];
        foreach (var(a, b, c, d)in BoxFaces)
        {
            if (!CaptureSurface)
            {
                Triangle(p[a], p[b], p[c], color);
                Triangle(p[a], p[c], p[d], color);
            }
            else
            {
                Vec3 n = Vec3.Cross(p[b] - p[a], p[c] - p[a]).Normalized();
                Vec3 Local(Vec3 v)
                {
                    var r = v - center;
                    return new(Vec3.Dot(r, right), Vec3.Dot(r, up), Vec3.Dot(r, forward));
                }

                SmoothTriangle(p[a], p[b], p[c], n, n, n, Local(p[a]), Local(p[b]), Local(p[c]), color);
                SmoothTriangle(p[a], p[c], p[d], n, n, n, Local(p[a]), Local(p[c]), Local(p[d]), color);
            }
        }
    }

    private void ChamferBox(Vec3 center, Vec3 half, Vec3 color, Vec3 right, Vec3 up, Vec3 forward, float radius)
    {
        Vec3 World(Vec3 p) => center + right*p.X + up*p.Y + forward*p.Z;
        Vec3 Axis(int axis) => axis==0 ? Vec3.UnitX : axis==1 ? Vec3.UnitY : Vec3.UnitZ;
        float H(int axis) => axis==0 ? half.X : axis==1 ? half.Y : half.Z;
        void Tri(Vec3 a, Vec3 b, Vec3 c, Vec3 normal)
        {
            if(Vec3.Dot(Vec3.Cross(b-a,c-a),normal)<0)(b,c)=(c,b);
            Vec3 n=(right*normal.X+up*normal.Y+forward*normal.Z).Normalized();
            SmoothTriangle(World(a),World(b),World(c),n,n,n,a,b,c,color);
        }
        void Face(Vec3 a,Vec3 b,Vec3 c,Vec3 d,Vec3 n){Tri(a,b,c,n);Tri(a,c,d,n);}
        for(int axis=0;axis<3;axis++)
        {
            int j=(axis+1)%3,k=(axis+2)%3;
            Vec3 u=Axis(j)*(H(j)-radius),v=Axis(k)*(H(k)-radius);
            foreach(float sign in new[]{-1f,1f})
            { Vec3 n=Axis(axis)*sign,c=n*H(axis);Face(c-u-v,c+u-v,c+u+v,c-u+v,n); }
        }
        for(int axis=0;axis<3;axis++)
        {
            int j=(axis+1)%3,k=(axis+2)%3;Vec3 along=Axis(axis)*(H(axis)-radius);
            foreach(float sj in new[]{-1f,1f})foreach(float sk in new[]{-1f,1f})
            {
                Vec3 u=Axis(j)*sj,v=Axis(k)*sk;
                Vec3 a=u*H(j)+v*(H(k)-radius),b=u*(H(j)-radius)+v*H(k);
                Face(a-along,b-along,b+along,a+along,(u+v).Normalized());
            }
        }
        foreach(float sx in new[]{-1f,1f})foreach(float sy in new[]{-1f,1f})foreach(float sz in new[]{-1f,1f})
        {
            Vec3 c=new(sx*(half.X-radius),sy*(half.Y-radius),sz*(half.Z-radius));
            Tri(c+Vec3.UnitX*(sx*radius),c+Vec3.UnitY*(sy*radius),c+Vec3.UnitZ*(sz*radius),new(sx,sy,sz));
        }
    }
}
