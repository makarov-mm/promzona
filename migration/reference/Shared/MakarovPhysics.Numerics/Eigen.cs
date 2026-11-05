namespace MakarovPhysics.Numerics;

/// <summary>
/// Lowest eigenpairs of a real symmetric tridiagonal matrix
/// (diagonal d[0..n-1], off-diagonal e[0..n-2]): Sturm-sequence bisection for
/// the eigenvalues, inverse iteration with the Thomas solver for the vectors.
/// This is the general form of the machinery Schrodinger1D built for its
/// stationary levels - a discretized 1D Hamiltonian IS such a matrix - lifted
/// to the library so band structures, normal-mode problems and coupled
/// oscillator chains can share it.
/// </summary>
public static class SymmetricTridiagonalEigen
{
    /// <summary>
    /// Number of eigenvalues strictly below lambda: the count of sign
    /// agreements lost in the Sturm sequence of leading-minor ratios. O(n),
    /// numerically safe (the classic guard replaces a zero pivot with a tiny
    /// value of the correct sign).
    /// </summary>
    public static int CountBelow(double[] d, double[] e, double lambda)
    {
        Validate(d, e);
        if (!double.IsFinite(lambda)) throw new ArgumentOutOfRangeException(nameof(lambda));
        int count = 0;
        double q = d[0] - lambda;
        if (q < 0) count++;

        for (int i = 1; i < d.Length; i++)
        {
            if (q == 0) q = 1e-300;
            q = d[i] - lambda - e[i - 1] * e[i - 1] / q;
            if (q < 0) count++;
        }

        return count;
    }

    /// <summary>
    /// The k lowest eigenvalues, ascending, each bisected TOWARDS absolute
    /// accuracy tol within the Gershgorin bounds. tol is a target, not an
    /// unconditional guarantee: the loop is also capped by maxIterations, and
    /// when an absolute tol sits below the ULP at a large eigenvalue's own
    /// magnitude the cap is what returns - "as tight as this many halvings
    /// get" (review 09.08, §6). Cost O(k·n·log(range/tol)) - for the grids
    /// modules use (n ≤ a few thousand, k ≤ tens) this is instant.
    /// </summary>
    public static double[] LowestEigenvalues(double[] d, double[] e, int k, double tol = 1e-12, int maxIterations = 100)
    {
        Validate(d, e);
        if (k <= 0 || k > d.Length) throw new ArgumentOutOfRangeException(nameof(k), $"k must be in [1, {d.Length}].");
        if (!(tol > 0) || !double.IsFinite(tol)) throw new ArgumentOutOfRangeException(nameof(tol));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIterations);

        // Gershgorin: every eigenvalue lies within radius |e[i-1]| + |e[i]| of d[i]
        double lo = double.MaxValue, hi = double.MinValue;

        for (int i = 0; i < d.Length; i++)
        {
            double r = (i > 0 ? Math.Abs(e[i - 1]) : 0.0) + (i < d.Length - 1 ? Math.Abs(e[i]) : 0.0);
            lo = Math.Min(lo, d[i] - r);
            hi = Math.Max(hi, d[i] + r);
        }

        double[] result = new double[k];

        for (int j = 0; j < k; j++)
        {
            // eigenvalue #j (0-based) is the lambda where CountBelow crosses j → j+1.
            // Iteration-capped, not just tol-tested: an absolute tol can sit
            // below the ULP at a large eigenvalue's own magnitude (Schrodinger1D's
            // original bisection anchored its cutoff to 1 + |a| + |b| for exactly
            // this reason before this function existed to generalize it), and a
            // plain `while (b - a > tol)` would then never see the gap close -
            // this degrades gracefully to "as tight as this many halvings get",
            // the same idea AdaptiveSimpson's maxDepth already carries.
            double a = lo, b = hi;

            for (int it = 0; it < maxIterations && b - a > tol; it++)
            {
                double m = 0.5 * (a + b);

                if (CountBelow(d, e, m) <= j)
                {
                    a = m;
                }
                else
                {
                    b = m;
                }
            }

            result[j] = 0.5 * (a + b);
        }

        return result;
    }

    /// <summary>
    /// Normalized eigenvector for an already-computed eigenvalue: inverse
    /// iteration on (A − λI) with a small shift so the shifted matrix is
    /// invertible. Two to three iterations from a deterministic start converge
    /// to machine precision when lambda is accurate; the deterministic seed
    /// keeps SelfTest reproducible.
    ///
    /// For a CLUSTERED or degenerate spectrum this single-vector call cannot
    /// tell the members of the cluster apart - inverse iteration converges to
    /// whatever mix of the cluster the shift favours (external review 09.08:
    /// diag(0, 1e-12) returned the SAME vector for both eigenvalues). Use
    /// <see cref="Eigenvectors"/>, which orthogonalizes each vector against
    /// the ones already found.
    /// </summary>
    public static double[] Eigenvector(double[] d, double[] e, double lambda, int iterations = 4) => Eigenvectors(d, e, [lambda], iterations)[0];

    /// <summary>
    /// Normalized eigenvectors for a set of already-computed eigenvalues, in
    /// the given order. Inverse iteration per vector, and after every solve
    /// the vector is orthogonalized (modified Gram-Schmidt) against all the
    /// vectors already found - so a clustered or exactly degenerate spectrum
    /// yields an orthonormal basis of the cluster instead of one direction
    /// repeated (the 09.08 review's diag(0, 1e-12) case: |dot| was 0.9998,
    /// the twin of this scheme measures 1e-19). The shift starts at
    /// scale·1e-13 and is bumped ×100 when the shifted solve comes back
    /// non-finite, so an eigenvalue that is exact to the last bit cannot make
    /// the tridiagonal solve singular.
    /// </summary>
    public static double[][] Eigenvectors(double[] d, double[] e, double[] lambdas, int iterations = 4)
    {
        Validate(d, e);
        ArgumentNullException.ThrowIfNull(lambdas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        if (lambdas.Any(lambda => !double.IsFinite(lambda)))
        {
            throw new ArgumentOutOfRangeException(nameof(lambdas));
        }

        int n = d.Length;

        // scale of the matrix, for a shift that is tiny yet not denormal
        double scale = 0.0;

        for (int i = 0; i < n; i++)
        {
            scale = Math.Max(scale, Math.Abs(d[i]));
        }

        for (int i = 0; i < n - 1; i++)
        {
            scale = Math.Max(scale, Math.Abs(e[i]));
        }

        double baseShift = Math.Max(scale, 1.0) * 1e-13;

        var a = new double[n];
        var b = new double[n];
        var c = new double[n];
        var rhs = new double[n];
        var cw = new double[n];

        // deterministic pseudo-random start: no eigenvector of a physical
        // Hamiltonian is orthogonal to it in practice
        var rng = new Xoshiro256ss(12345);
        var found = new double[lambdas.Length][];

        for (int j = 0; j < lambdas.Length; j++)
        {
            double lambda = lambdas[j];
            double shift = baseShift;
            double[] v = new double[n];

            for (int i = 0; i < n; i++)
            {
                v[i] = rng.NextDouble() - 0.5;
            }

            OrthogonalizeAgainst(v, found, j);
            Normalize(v);

            for (int it = 0; it < iterations; it++)
            {
                // a lambda exact to the last bit makes (A − λI + shift)
                // near-singular: Thomas throws on an exactly-zero pivot and
                // overflows to Inf just above it - either way, bump the shift
                double[] x = v;

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        a[i] = i > 0 ? e[i - 1] : 0.0;
                        b[i] = d[i] - lambda + shift;
                        c[i] = i < n - 1 ? e[i] : 0.0;
                        rhs[i] = v[i];
                    }

                    x = new double[n];

                    try
                    {
                        Thomas.Solve(a, b, c, rhs, cw, x);
                        if (AllFinite(x)) break;
                    }
                    catch (ArgumentException)
                    {
                        // zero pivot: fall through to the shift bump
                    }

                    shift *= 100;
                }

                v = x;
                OrthogonalizeAgainst(v, found, j);

                if (!TryNormalize(v))
                {
                    // the solve landed INSIDE the span of the found vectors
                    // (a cluster): restart from fresh noise outside that span
                    for (int i = 0; i < n; i++) v[i] = rng.NextDouble() - 0.5;
                    OrthogonalizeAgainst(v, found, j);
                    Normalize(v);
                }
            }

            // sign convention: the largest-magnitude component is positive, so
            // the same physics always produces the same picture
            int imax = 0;

            for (int i = 1; i < n; i++)
            {
                if (Math.Abs(v[i]) > Math.Abs(v[imax]))
                {
                    imax = i;
                }
            }

            if (v[imax] < 0)
            {
                for (int i = 0; i < n; i++)
                {
                    v[i] = -v[i];
                }
            }

            found[j] = v;
        }

        return found;
    }

    /// <summary>Modified Gram-Schmidt of v against the first `count` rows.</summary>
    private static void OrthogonalizeAgainst(double[] v, double[][] basis, int count)
    {
        for (int j = 0; j < count; j++)
        {
            double[] u = basis[j];
            double dot = v.Select((t, i) => t * u[i]).Sum();

            for (int i = 0; i < v.Length; i++)
            {
                v[i] -= dot * u[i];
            }
        }
    }

    private static bool AllFinite(double[] v) => v.All(double.IsFinite);

    /// <summary>Normalize in place; false when the vector has collapsed.</summary>
    private static bool TryNormalize(double[] v)
    {
        double s = Math.Sqrt(v.Sum(t => t * t));

        if (s <= 0 || !double.IsFinite(s))
        {
            return false;
        }

        for (int i = 0; i < v.Length; i++)
        {
            v[i] /= s;
        }

        return true;
    }

    private static void Normalize(double[] v)
    {
        double s = v.Sum(t => t * t);
        s = Math.Sqrt(s);

        if (s <= 0 || !double.IsFinite(s))
        {
            throw new ArithmeticException("Eigenvector iteration collapsed to zero.");
        }

        for (int i = 0; i < v.Length; i++)
        {
            v[i] /= s;
        }
    }

    private static void Validate(double[] d, double[] e)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(e);
        if (d.Length == 0) throw new ArgumentException("Eigen: empty matrix.");
        if (e.Length < d.Length - 1) throw new ArgumentException($"Eigen: off-diagonal needs at least {d.Length - 1} entries, got {e.Length}.");
    }
}
