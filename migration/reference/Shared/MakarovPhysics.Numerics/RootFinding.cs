namespace MakarovPhysics.Numerics;

/// <summary>
/// One-dimensional root finding on a bracketing interval.
/// The workhorse is Brent's method: inverse quadratic interpolation and the
/// secant step where they behave, bisection whenever they do not, so it is
/// never slower than bisection and usually superlinear. This is the library
/// home for the ad-hoc bisections modules keep writing (band edges in
/// Kronig-Penney, turning points, lens equations).
/// </summary>
public static class RootFinding
{
    /// <summary>
    /// The gap between 1.0 and the next representable double, 2.22e-16.
    ///
    /// NOT <c>double.Epsilon</c>, which in .NET is the smallest positive
    /// SUBNORMAL - 4.94e-324, eight orders of magnitude smaller than anything
    /// useful as a relative tolerance. Brent's tolerance term
    /// <c>2*eps*|b| + xTol/2</c> was written with double.Epsilon and therefore
    /// contributed exactly nothing: the relative part underflowed to zero and
    /// only the caller's absolute xTol survived. Harmless while roots are
    /// O(1) - the default xTol of 1e-12 dominates anyway - but for a root near
    /// 1e6 the method was being asked to resolve 5e-13, which is a thousandth
    /// of one ULP there, so it could only ever exhaust its iterations.
    /// </summary>
    public const double MachineEpsilon = 2.220446049250313e-16;

    /// <summary>
    /// Root of f on [a, b] with f(a) and f(b) of opposite signs (an endpoint
    /// where f is exactly zero is accepted as the answer). Converges to
    /// |x − root| ≤ xTol + a few ulps; throws when the interval does not
    /// bracket a root, because silently returning an endpoint is how a wrong
    /// resonance frequency ends up on a chart.
    /// </summary>
    public static double Brent(Func<double, double> f, double a, double b,
        double xTol = 1e-12, int maxIterations = 128)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentOutOfRangeException(nameof(a), "Interval endpoints must be finite.");
        if (!(xTol > 0) || !double.IsFinite(xTol)) throw new ArgumentOutOfRangeException(nameof(xTol));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIterations);

        double fa = f(a), fb = f(b);
        if (fa == 0) return a;
        if (fb == 0) return b;
        // Infinity is checked alongside NaN: a sign is still well defined at an
        // infinite endpoint, so the bracket test would wave it through, and the
        // interpolation would then compute fb/fa = Inf/Inf = NaN two lines later
        // and blame the function. Reject it where the cause is still visible.
        if (!double.IsFinite(fa) || !double.IsFinite(fb))
            throw new ArgumentException($"Brent: f({a:G6}) = {fa:G6} and f({b:G6}) = {fb:G6} must both be finite.");
        if (Math.Sign(fa) == Math.Sign(fb))
            throw new ArgumentException($"Brent: f({a:G6}) = {fa:G6} and f({b:G6}) = {fb:G6} do not bracket a root.");

        // the largest |f| the CALLER handed us, kept for the final sanity check
        double endpointScale = Math.Max(Math.Abs(fa), Math.Abs(fb));

        // b is kept as the best estimate, a as its counterpart, c as the previous b
        if (Math.Abs(fa) < Math.Abs(fb)) { (a, b) = (b, a); (fa, fb) = (fb, fa); }
        double c = a, fc = fa, d = b - a, e = d;

        for (int i = 0; i < maxIterations; i++)
        {
            if (Math.Sign(fb) == Math.Sign(fc)) { c = a; fc = fa; d = e = b - a; }
            if (Math.Abs(fc) < Math.Abs(fb)) { a = b; b = c; c = a; fa = fb; fb = fc; fc = fa; }

            double tol = 2.0 * MachineEpsilon * Math.Abs(b) + 0.5 * xTol;
            double m = 0.5 * (c - b);
            if (Math.Abs(m) <= tol || fb == 0) return Verified(b, fb, endpointScale, a, c, "Brent");

            if (Math.Abs(e) >= tol && Math.Abs(fa) > Math.Abs(fb))
            {
                // interpolation: secant on two points, inverse quadratic on three
                double s = fb / fa, p, q;

                if (a == c)
                {
                    p = 2.0 * m * s;
                    q = 1.0 - s;
                }
                else
                {
                    double qa = fa / fc, r = fb / fc;
                    p = s * (2.0 * m * qa * (qa - r) - (b - a) * (r - 1.0));
                    q = (qa - 1.0) * (r - 1.0) * (s - 1.0);
                }

                if (p > 0) q = -q; else p = -p;

                // accept only when the step stays inside the bracket and shrinks
                if (2.0 * p < Math.Min(3.0 * m * q - Math.Abs(tol * q), Math.Abs(e * q)))
                {
                    e = d; d = p / q;
                }
                else
                {
                    d = m; e = m;   // interpolation misbehaved - bisect
                }
            }
            else
            {
                d = m; e = m;
            }

            a = b; fa = fb;
            b += Math.Abs(d) > tol ? d : (m > 0 ? tol : -tol);
            fb = f(b);

            // Infinity as well as NaN: an infinite value has a usable sign, so
            // the bracket logic sails past it and the interpolation then works
            // out fb/fa = Inf/Inf = NaN a few lines later and blames whatever
            // it touched next.
            if (!double.IsFinite(fb))
            {
                throw new ArithmeticException($"Brent: f({b:G6}) evaluated to {fb:G6}; the function must be finite across the bracket.");
            }
        }

        // Falling out of the loop means the budget ran out. Before calling that
        // a failure, honour the same stopping rule the loop uses at its top: an
        // exact hit or a bracket already inside the tolerance on the FINAL step
        // is a converged answer that simply had no turn left to report itself.
        if (fb == 0 || Math.Abs(0.5 * (c - b)) <= 2.0 * MachineEpsilon * Math.Abs(b) + 0.5 * xTol)
        {
            return Verified(b, fb, endpointScale, a, c, "Brent");
        }

        // Otherwise: returning the last iterate here made a failed solve
        // indistinguishable from a successful one - the caller gets a number
        // either way and has no way to ask. That is the one thing this library
        // says it does not do (Docs/LIBRARIES.md: an exception rather than
        // quiet garbage).
        throw new InvalidOperationException(
            $"Brent: no convergence in {maxIterations} iterations on the remaining bracket " +
            $"[{Math.Min(b, c):G6}, {Math.Max(b, c):G6}]; last estimate {b:G6}, f = {fb:G6}.");
    }

    /// <summary>
    /// Plain bisection - half the speed of Brent but immune to any f shape.
    /// Kept public for callers that prefer the guarantee over the iterations
    /// (e.g. scanning for a sign change first, then polishing).
    /// </summary>
    public static double Bisect(Func<double, double> f, double a, double b, double xTol = 1e-12, int maxIterations = 200)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentOutOfRangeException(nameof(a), "Interval endpoints must be finite.");
        if (!(xTol > 0) || !double.IsFinite(xTol)) throw new ArgumentOutOfRangeException(nameof(xTol));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIterations);

        double fa = f(a), fb = f(b);
        if (fa == 0) return a;
        if (fb == 0) return b;
        if (!double.IsFinite(fa) || !double.IsFinite(fb))
            throw new ArgumentException($"Bisect: f({a:G6}) = {fa:G6} and f({b:G6}) = {fb:G6} must both be finite.");
        if (Math.Sign(fa) == Math.Sign(fb))
            throw new ArgumentException($"Bisect: f({a:G6}) = {fa:G6} and f({b:G6}) = {fb:G6} do not bracket a root.");

        double endpointScale = Math.Max(Math.Abs(fa), Math.Abs(fb));
        double last = fa;

        for (int i = 0; i < maxIterations && Math.Abs(b - a) > xTol; i++)
        {
            double m = 0.5 * (a + b), fm = f(m);

            if (!double.IsFinite(fm))
            {
                throw new ArithmeticException($"Bisect: f({m:G6}) evaluated to {fm:G6}; the function must be finite across the bracket.");
            }

            last = fm;
            if (fm == 0) return m;
            if (Math.Sign(fm) == Math.Sign(fa)) { a = m; fa = fm; }
            else b = m;
        }

        // the loop also exits on iteration count, which is NOT success
        if (Math.Abs(b - a) > xTol)
        {
            throw new InvalidOperationException(
                $"Bisect: no convergence in {maxIterations} iterations; bracket is still " +
                $"[{a:G6}, {b:G6}], width {Math.Abs(b - a):G6} against xTol {xTol:G6}.");
        }

        return Verified(0.5 * (a + b), last, endpointScale, a, b, "Bisect");
    }

    /// <summary>
    /// A sign change is not a root. 1/(x − 0.5) on [0, 1] has finite endpoints
    /// of opposite sign and no root at all: bisection walks straight into the
    /// pole and both methods used to hand back 0.4999999999995 as the answer,
    /// with |f| there equal to 2.2e12. Checking finiteness at each evaluation
    /// does not catch it, because the pole itself is approached but never
    /// landed on exactly.
    ///
    /// What separates the two cases is what |f| DID: converging on a root
    /// drives it to zero, converging on a pole drives it up without bound. So
    /// the result is rejected when |f| there exceeds the largest |f| the caller
    /// handed in as a bracket endpoint - scale-free, and impossible for a real
    /// root to trip, since |f| = 0 there.
    /// </summary>
    private static double Verified(double root, double froot, double endpointScale,
                                   double lo, double hi, string method)
    {
        if (Math.Abs(froot) > endpointScale)
            throw new ArithmeticException(
                $"{method}: converged to {root:G6} where |f| = {Math.Abs(froot):G6}, which is larger than at the " +
                $"bracket endpoints ({endpointScale:G6}) - that is a discontinuity, not a root. " +
                $"The bracket [{Math.Min(lo, hi):G6}, {Math.Max(lo, hi):G6}] must contain no pole.");
        return root;
    }
}
