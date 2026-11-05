namespace Promzona.Game;

public sealed class CityPath
{
    private readonly CityPoint[] _points;
    private readonly double[] _arc;
    // The four Bezier handles when the path was built from them: a save stores
    // those instead of the 65 sampled points (format 3) and rebuilds the same curve.
    private readonly CityPoint[]? _control;
    public bool IsBezier => _control is not null;
    public double Length => _arc[^1];

    public CityPath(CityPoint a, CityPoint b) 
        : this(a, a + (b - a) * (1d / 3), a + (b - a) * (2d / 3), b)
    {
    }

    public CityPath(CityPoint a, CityPoint b, CityPoint c, CityPoint d)
    {
        _control = [a, b, c, d];
        _points = new CityPoint[65];
        _arc = new double[65];

        for (int i = 0; i < 65; i++)
        {
            double u = i / 64d, v = 1 - u;
            _points[i] = a * (v * v * v) + b * (3 * v * v * u) + c * (3 * v * u * u) + d * (u * u * u);

            if (i > 0)
            {
                _arc[i] = _arc[i - 1] + (_points[i] - _points[i - 1]).Length;
            }
        }
    }

    public CityPoint[] ExportPoints() => (CityPoint[])_points.Clone();
    /// <summary>The compact form for a save: the handles of a Bezier path, the sampled points otherwise.</summary>
    public CityPoint[] ExportCompact() => (CityPoint[])(_control ?? _points).Clone();
    internal static CityPath Restore(CityPoint[] points, bool bezier = false) =>
        bezier && points.Length == 4 ? new(points[0], points[1], points[2], points[3]) : new(points);

    private CityPath(CityPoint[] points)
    {
        _points = points;
        _arc = new double[points.Length];

        for (int i = 1; i < points.Length; i++)
        {
            _arc[i] = _arc[i - 1] + (points[i] - points[i - 1]).Length;
        }
    }

    public static CityPath Turnaround(CityPoint centre, CityPoint f,double lane=CityTraffic.Lane)
    {
        var r = CityTraffic.Right(f);
        List<CityPoint> points = [];

        CityPoint a = centre - f * CityTraffic.StopLine + r * lane,
            b = centre - f * 8 + r * lane,
            c = centre - f * 5 + r * 5,
            d = centre - f * 3 + r * 5;

        Curve(a, b, c, d);

        for (int i = 1; i <= 128; i++)
        {
            double t = i * Math.PI / 128;
            points.Add(centre - f * 3 + r * (5 * Math.Cos(t)) + f * (5 * Math.Sin(t)));
        }

        Curve(centre - f * 3 - r * 5, centre - f * 5 - r * 5, centre - f * 6.5 - r * lane, centre - f * 9 - r * lane);
        return new CityPath([.. points]);

        void Curve(CityPoint p, CityPoint q, CityPoint v, CityPoint w)
        {
            for (int i = points.Count == 0 ? 0 : 1; i <= 64; i++)
            {
                double t = i / 64d, u = 1 - t;
                points.Add(p * (u * u * u) + q * (3 * u * u * t) + v * (3 * u * t * t) + w * (t * t * t));
            }
        }
    }

    public double ClosestDistance(CityPoint position)
    {
        double best = double.PositiveInfinity, along = 0;

        for (int i = 1; i < _points.Length; i++)
        {
            CityPoint a = _points[i - 1];
            CityPoint d = _points[i] - a;
            double t = Math.Clamp(CityPoint.Dot(position - a, d) / Math.Max(1e-12, CityPoint.Dot(d, d)), 0, 1);
            CityPoint e = position - (a + d * t);
            double score = CityPoint.Dot(e, e);

            if (score < best)
            {
                best = score;
                along = _arc[i - 1] + (_arc[i] - _arc[i - 1]) * t;
            }
        }

        return along;
    }

    public (CityPoint Point, double Heading) Sample(double distance)
    {
        distance = Math.Clamp(distance, 0, Length);
        int at = Array.BinarySearch(_arc, distance);
        if (at < 0) at = ~at;
        at = Math.Clamp(at, 1, _points.Length - 1);
        double f = (distance - _arc[at - 1]) / Math.Max(1e-12, _arc[at] - _arc[at - 1]);
        CityPoint d = _points[at] - _points[at - 1];
        return (_points[at - 1] + d * f, Math.Atan2(d.X, d.Z));
    }
}
