namespace MakarovPhysics.Numerics;

/// <summary>
/// Dormand-Prince 5(4): the adaptive companion to the fixed-step
/// <see cref="Rk4"/>. Seven stages give a 5th-order solution and an embedded
/// 4th-order error estimate for free (FSAL: the last stage of an accepted step
/// is the first stage of the next). The step controller keeps the local error
/// per step under absTol + relTol·|y|, growing the step through quiet stretches
/// and shrinking it at close encounters - which is exactly the shape of a
/// Kepler flyby or a stiff spell in a chaotic attractor. Real-time modules
/// with a fixed frame budget should stay on RK4/Verlet; this class is for
/// reference trajectories, validation curves and offline sweeps where accuracy
/// is specified and time is not.
/// </summary>
public sealed class DormandPrince54
{
    // Butcher tableau (Dormand & Prince 1980, the RKDP pair inside every ode45)
    private const double C2 = 1.0 / 5, C3 = 3.0 / 10, C4 = 4.0 / 5, C5 = 8.0 / 9;
    private const double A21 = 1.0 / 5;
    private const double A31 = 3.0 / 40, A32 = 9.0 / 40;
    private const double A41 = 44.0 / 45, A42 = -56.0 / 15, A43 = 32.0 / 9;
    private const double A51 = 19372.0 / 6561, A52 = -25360.0 / 2187, A53 = 64448.0 / 6561, A54 = -212.0 / 729;
    private const double A61 = 9017.0 / 3168, A62 = -355.0 / 33, A63 = 46732.0 / 5247, A64 = 49.0 / 176, A65 = -5103.0 / 18656;

    // 5th-order weights (also row 7 of A: FSAL)
    private const double B1 = 35.0 / 384, B3 = 500.0 / 1113, B4 = 125.0 / 192, B5 = -2187.0 / 6784, B6 = 11.0 / 84;

    // error weights: b5 − b4 of the embedded pair
    private const double E1 = 71.0 / 57600, E3 = -71.0 / 16695, E4 = 71.0 / 1920, E5 = -17253.0 / 339200, E6 = 22.0 / 525, E7 = -1.0 / 40;

    private readonly double[] _k1, _k2, _k3, _k4, _k5, _k6, _k7, _tmp, _y5;
    private bool _haveK1; // FSAL: k1 of the next step is k7 of the accepted one

    public int Dimension => _k1.Length;

    /// <summary>Statistics of the last <see cref="Integrate"/> call.</summary>
    public int AcceptedSteps { get; private set; }
    public int RejectedSteps { get; private set; }

    public DormandPrince54(int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        _k1 = new double[dimension];
        _k2 = new double[dimension];
        _k3 = new double[dimension];
        _k4 = new double[dimension];
        _k5 = new double[dimension];
        _k6 = new double[dimension];
        _k7 = new double[dimension];
        _tmp = new double[dimension];
        _y5 = new double[dimension];
    }

    /// <summary>
    /// Advance y from t to tEnd, choosing steps to keep the local error under
    /// absTol + relTol·|y| componentwise. Returns the number of accepted steps.
    /// maxSteps bounds the work: hitting it raises, because "the integrator
    /// silently gave up midway" must never look like a finished trajectory.
    /// </summary>
    public int Integrate(OdeRhs f, ref double t, double[] y, double tEnd, double relTol = 1e-9, double absTol = 1e-12, double? initialStep = null, int maxSteps = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(y);
        if (y.Length != Dimension) throw new ArgumentException($"State has {y.Length} components; the integrator was created for {Dimension}.", nameof(y));
        if (!double.IsFinite(t) || !double.IsFinite(tEnd)) throw new ArgumentOutOfRangeException(nameof(t), "Time bounds must be finite.");
        if (!(relTol > 0) || !(absTol > 0)) throw new ArgumentOutOfRangeException(nameof(relTol), "Tolerances must be positive.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSteps);
        AcceptedSteps = 0;
        RejectedSteps = 0;
        _haveK1 = false;
        double dir = Math.Sign(tEnd - t);
        if (dir == 0) return 0;
        double span = Math.Abs(tEnd - t);

        double h = initialStep is { } h0 && double.IsFinite(h0) && h0 > 0
            ? Math.Min(h0, span)
            : span / 100.0;

        h *= dir;
        int n = Dimension;

        for (int step = 0; step < maxSteps; step++)
        {
            if ((t - tEnd) * dir >= 0) return AcceptedSteps;

            // land exactly on tEnd instead of overshooting and interpolating back
            if ((t + h - tEnd) * dir > 0) h = tEnd - t;

            if (!_haveK1) f(t, y, _k1);

            for (int i = 0; i < n; i++) _tmp[i] = y[i] + h * A21 * _k1[i];
            f(t + C2 * h, _tmp, _k2);

            for (int i = 0; i < n; i++) _tmp[i] = y[i] + h * (A31 * _k1[i] + A32 * _k2[i]);
            f(t + C3 * h, _tmp, _k3);

            for (int i = 0; i < n; i++) _tmp[i] = y[i] + h * (A41 * _k1[i] + A42 * _k2[i] + A43 * _k3[i]);
            f(t + C4 * h, _tmp, _k4);

            for (int i = 0; i < n; i++) _tmp[i] = y[i] + h * (A51 * _k1[i] + A52 * _k2[i] + A53 * _k3[i] + A54 * _k4[i]);
            f(t + C5 * h, _tmp, _k5);

            for (int i = 0; i < n; i++) _tmp[i] = y[i] + h * (A61 * _k1[i] + A62 * _k2[i] + A63 * _k3[i] + A64 * _k4[i] + A65 * _k5[i]);
            f(t + h, _tmp, _k6);

            for (int i = 0; i < n; i++) _y5[i] = y[i] + h * (B1 * _k1[i] + B3 * _k3[i] + B4 * _k4[i] + B5 * _k5[i] + B6 * _k6[i]);
            f(t + h, _y5, _k7);

            // scaled error norm: err ≤ 1 means the step meets the tolerance
            double err = 0.0;

            for (int i = 0; i < n; i++)
            {
                double e = h * (E1 * _k1[i] + E3 * _k3[i] + E4 * _k4[i] + E5 * _k5[i] + E6 * _k6[i] + E7 * _k7[i]);
                double scale = absTol + relTol * Math.Max(Math.Abs(y[i]), Math.Abs(_y5[i]));
                double q = e / scale;
                err += q * q;
            }

            err = Math.Sqrt(err / n);

            if (double.IsNaN(err) || double.IsInfinity(err))
            {
                // a NaN state means the step blew up - halve and retry from k1
                RejectedSteps++;
                _haveK1 = false;
                h *= 0.5;

                if (Math.Abs(h) < 1e-14 * Math.Max(1.0, Math.Abs(t)))
                {
                    throw new ArithmeticException($"DormandPrince54: step underflow at t = {t:G8} (the right-hand side produced NaN).");
                }

                continue;
            }

            if (err <= 1.0)
            {
                t += h;
                Array.Copy(_y5, y, n);
                Array.Copy(_k7, _k1, n); // FSAL
                _haveK1 = true;
                AcceptedSteps++;
            }
            else
            {
                RejectedSteps++;
                _haveK1 = true; // k1 belongs to (t, y), both unchanged
            }

            // standard PI-flavoured update for a 5th-order pair, clamped so one
            // bad estimate cannot swing the step by orders of magnitude
            double factor = 0.9 * Math.Pow(Math.Max(err, 1e-10), -0.2);
            h *= Math.Clamp(factor, 0.2, 5.0);

            if (Math.Abs(h) < 1e-14 * Math.Max(1.0, Math.Abs(t)))
            {
                throw new ArithmeticException($"DormandPrince54: step underflow at t = {t:G8}; the problem may be stiff or singular here.");
            }
        }

        throw new ArithmeticException($"DormandPrince54: {maxSteps} steps were not enough to reach t = {tEnd:G8} (stopped at t = {t:G8}).");
    }
}
