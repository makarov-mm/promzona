namespace MakarovPhysics.Numerics;

/// <summary>
/// Definite integrals of a smooth integrand. Three modules carry their own
/// composite Simpson today (the Bose integral in Blackbody, hydrostatic force
/// in Hydrostatics, the WKB barrier integral in Radioactivity); this is the
/// shared home so the next one does not write a fourth.
/// </summary>
public static class Quadrature
{
    /// <summary>
    /// Composite Simpson on [a, b] with n subintervals (n is rounded up to
    /// even - the rule needs paired panels). Fourth-order: doubling n cuts the
    /// error 16×. The right tool when the caller knows the resolution it
    /// wants; for "give me 8 digits and pick the points yourself" use
    /// <see cref="AdaptiveSimpson"/>.
    /// </summary>
    public static double Simpson(Func<double, double> f, double a, double b, int n = 256)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentOutOfRangeException(nameof(a), "Integration bounds must be finite.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);

        if (a == b) return 0.0;
        if ((n & 1) == 1) n++;

        double h = (b - a) / n;
        double sum = f(a) + f(b);

        for (int i = 1; i < n; i++)
        {
            sum += f(a + i * h) * ((i & 1) == 1 ? 4.0 : 2.0);
        }

        return sum * h / 3.0;
    }

    /// <summary>
    /// Adaptive Simpson to an absolute tolerance: each panel is compared with
    /// its two halves and split only where the integrand actually bends, so a
    /// Planck peak gets the points and its flat tail does not. The 1/15 factor
    /// is the standard Richardson estimate of the local error. Depth is capped
    /// so an accidental singularity degrades gracefully instead of recursing
    /// forever.
    /// </summary>
    public static double AdaptiveSimpson(Func<double, double> f, double a, double b, double tolerance = 1e-10, int maxDepth = 48)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (!double.IsFinite(a) || !double.IsFinite(b)) throw new ArgumentOutOfRangeException(nameof(a), "Integration bounds must be finite.");
        if (!(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);

        if (a == b) return 0.0;

        double fa = f(a), fb = f(b), m = 0.5 * (a + b), fm = f(m);
        double whole = (b - a) / 6.0 * (fa + 4.0 * fm + fb);
        return Recurse(f, a, b, fa, fm, fb, whole, tolerance, maxDepth);
    }

    private static double Recurse(Func<double, double> f, double a, double b, double fa, double fm, double fb, double whole, double tol, int depth)
    {
        double m = 0.5 * (a + b);
        double lm = 0.5 * (a + m), rm = 0.5 * (m + b);
        double flm = f(lm), frm = f(rm);
        double left = (m - a) / 6.0 * (fa + 4.0 * flm + fm);
        double right = (b - m) / 6.0 * (fm + 4.0 * frm + fb);
        double delta = left + right - whole;

        if (depth <= 0 || Math.Abs(delta) <= 15.0 * tol)
        {
            return left + right + delta / 15.0;
        }

        return Recurse(f, a, m, fa, flm, fm, left, 0.5 * tol, depth - 1) + 
               Recurse(f, m, b, fm, frm, fb, right, 0.5 * tol, depth - 1);
    }
}
