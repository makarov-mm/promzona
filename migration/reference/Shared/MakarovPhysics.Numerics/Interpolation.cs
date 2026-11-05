namespace MakarovPhysics.Numerics;

/// <summary>
/// Natural cubic spline through (x[i], y[i]) with strictly increasing x:
/// C² everywhere, zero second derivative at the ends. This is the curve a
/// track editor wants (EnergyPark ramps, custom potentials, Brachistochrone
/// comparisons): smooth enough that the second derivative - and with it the
/// normal force - does not jump between segments. The tridiagonal system for
/// the second derivatives is solved once in the constructor by the Thomas
/// algorithm the library already has; evaluation is a binary search plus a
/// cubic, allocation-free.
/// </summary>
public sealed class CubicSpline
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _m; // second derivatives at the knots
    public int Count => _x.Length;
    public double MinX => _x[0];
    public double MaxX => _x[^1];

    public CubicSpline(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length) throw new ArgumentException("CubicSpline: x and y must have the same length.");
        if (x.Length < 2) throw new ArgumentException("CubicSpline: at least two points are required.");

        for (int i = 0; i < x.Length; i++)
        {
            if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]))
            {
                throw new ArgumentException($"CubicSpline: non-finite input at index {i}.");
            }

            if (i > 0 && !(x[i] > x[i - 1]))
            {
                throw new ArgumentException($"CubicSpline: x must be strictly increasing (x[{i - 1}] = {x[i - 1]:G6}, x[{i}] = {x[i]:G6}).");
            }
        }

        _x = (double[])x.Clone();
        _y = (double[])y.Clone();
        int n = x.Length;
        _m = new double[n];

        if (n == 2) return; // a straight segment: both second derivatives stay zero

        // Interior equations: h[i-1]·m[i-1] + 2(h[i-1]+h[i])·m[i] + h[i]·m[i+1] = 6·(Δ[i] − Δ[i-1]);
        // natural ends pin m[0] = m[n-1] = 0, so the system is (n−2)×(n−2).
        int k = n - 2;
        double[] a = new double[k], b = new double[k], c = new double[k], d = new double[k];
        double[] cw = new double[k], sol = new double[k];

        for (int i = 1; i <= k; i++)
        {
            double h0 = _x[i] - _x[i - 1];
            double h1 = _x[i + 1] - _x[i];
            a[i - 1] = h0;
            b[i - 1] = 2.0 * (h0 + h1);
            c[i - 1] = h1;
            d[i - 1] = 6.0 * ((_y[i + 1] - _y[i]) / h1 - (_y[i] - _y[i - 1]) / h0);
        }

        Thomas.Solve(a, b, c, d, cw, sol);
        for (int i = 0; i < k; i++) _m[i + 1] = sol[i];
    }

    private int Segment(double x)
    {
        // clamp instead of throwing: sliders and animations run slightly past
        // the knots every day, and linear extension of the end cubic is the
        // least surprising behaviour there
        if (x <= _x[0]) return 0;
        if (x >= _x[^1]) return _x.Length - 2;

        int lo = 0, hi = _x.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_x[mid] <= x) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>Spline value at x (the end cubics extend beyond the knots).</summary>
    public double Evaluate(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        int i = Segment(x);
        double h = _x[i + 1] - _x[i];
        double t = (x - _x[i]) / h, u = 1.0 - t;
        return u * _y[i] + t * _y[i + 1] + h * h / 6.0 * ((u * u * u - u) * _m[i] + (t * t * t - t) * _m[i + 1]);
    }

    /// <summary>First derivative dy/dx at x - slopes for tangents and normal forces.</summary>
    public double Derivative(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        int i = Segment(x);
        double h = _x[i + 1] - _x[i];
        double t = (x - _x[i]) / h, u = 1.0 - t;
        return (_y[i + 1] - _y[i]) / h + h / 6.0 * ((3.0 * t * t - 1.0) * _m[i + 1] - (3.0 * u * u - 1.0) * _m[i]);
    }

    /// <summary>Second derivative d²y/dx² at x - curvature, continuous by construction.</summary>
    public double SecondDerivative(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        int i = Segment(x);
        double h = _x[i + 1] - _x[i];
        double t = (x - _x[i]) / h;
        return (1.0 - t) * _m[i] + t * _m[i + 1];
    }
}
