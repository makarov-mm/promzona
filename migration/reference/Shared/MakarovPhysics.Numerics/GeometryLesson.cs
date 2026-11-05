namespace MakarovPhysics.Numerics;

public readonly record struct GeometryStroke(GeoPoint A,GeoPoint B,int Ink=0,bool Dashed=false);
public readonly record struct GeometryDisc(GeoPoint Centre,double Radius,int Ink=0);
public readonly record struct GeometryMark(GeoPoint Point,string Label,int Ink=1,int Handle=-1);
public readonly record struct GeometryFace(GeoPoint[] Points,int Ink);
public readonly record struct GeometryReading(string Label,double Value);

public abstract class GeometryLesson
{
    public GeometryParameter[] Parameters { get; protected set; } = [];
    public bool Edited { get; private set; }
    public int Revision { get; private set; }
    public double Clock { get; private set; }
    protected double V(int index) => Parameters[index].Value;

    public void Set(int index, double value, bool edit = true)
    {
        Parameters[index].Set(value);
        Edited |= edit;
        Revision++;
    }

    public virtual void Reset()
    {
        foreach (GeometryParameter p in Parameters)
        {
            p.Set(p.Initial);
        }

        Edited = false;
        Clock = 0;
        Revision++;
    }

    public virtual void Advance(double dt)
    {
        Clock += dt;
        GeometryParameter p = Parameters[0];

        if (p.Options is null)
        {
            Set(0, p.Initial + (p.Max - p.Min) * .18 * Math.Sin(Clock * .55), false);
        }
    }

    public abstract GeometryFrame Build();
    public abstract void Drag(int handle, GeoPoint point);

    public static GeoPoint Foot(GeoPoint p, GeoPoint a, GeoPoint b)
    {
        GeoPoint d = b - a;
        return a + d * (GeoPoint.Dot(p - a, d) / Math.Max(1e-20, GeoPoint.Dot(d, d)));
    }

    public static GeoPoint Intersection(GeoPoint a, GeoPoint u, GeoPoint b, GeoPoint v)
    {
        double d = GeoPoint.Cross(u, v);

        if (Math.Abs(d) < 1e-12)
        {
            throw new ArgumentException("Parallel construction lines.");
        }

        return a + u * (GeoPoint.Cross(b - a, v) / d);
    }

    public static double Area(IReadOnlyList<GeoPoint> p)
    {
        double a = p.Select((t, i) => GeoPoint.Cross(t, p[(i + 1) % p.Count])).Sum();
        return Math.Abs(a) / 2;
    }

    public static double Angle(GeoPoint a, GeoPoint o, GeoPoint b)
    {
        GeoPoint u = a - o;
        GeoPoint v = b - o;
        return Math.Atan2(Math.Abs(GeoPoint.Cross(u, v)), GeoPoint.Dot(u, v));
    }

    public static double Perimeter(IReadOnlyList<GeoPoint> p) => p.Select((t, i) => (t - p[(i + 1) % p.Count]).Length).Sum();

    public static GeoPoint[] Regular(int n, double radius, double phase = 0, GeoPoint centre = default) =>
    [
        .. Enumerable.Range(0, n).Select(i => centre + GeoPoint.Polar(phase + 2 * Math.PI * i / n, radius))
    ];

    protected static double Degrees(GeoPoint p) => ((Math.Atan2(p.Y, p.X) * 180 / Math.PI) % 360 + 360) % 360;
}
