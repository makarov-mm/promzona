namespace MakarovPhysics.Numerics;

/// <summary>
/// The singular value decomposition of any m × n matrix (m ≥ n) by one-sided
/// Jacobi (Hestenes): pairs of COLUMNS are rotated until every pair is
/// orthogonal; then σⱼ is the length of column j, uⱼ the column over its
/// length, and V collects the rotations. Sorted so σ descends. No pivoting,
/// no bidiagonalisation - for a 64 × 64 picture it converges in a dozen
/// sweeps and the whole thing is forty lines that a twin can transcribe
/// line for line: `Tools/twins/singular_values_twin.py` (jacobi_svd) does,
/// and its pins are bit-for-bit. Matrix3.Svd is the 3 × 3 special case by a
/// different route (Jacobi on AᵀA); the two agree to 1e−12 and that is
/// tested.
/// </summary>
public static class JacobiSvd
{
    /// <summary>U as m × n (columns uⱼ), σ descending, V as n × n (columns vⱼ):
    /// A = Σⱼ σⱼ·uⱼ·vⱼᵀ. A zero σ leaves its u column zero.</summary>
    public static (double[,] U, double[] Sigma, double[,] V) Decompose(double[,] a)
    {
        int m = a.GetLength(0), n = a.GetLength(1);
        if (m < n) throw new ArgumentException("JacobiSvd wants m >= n: transpose first.", nameof(a));
        var cols = new double[n][];
        var v = new double[n][];

        for (int j = 0; j < n; j++)
        {
            cols[j] = new double[m];

            for (int i = 0; i < m; i++)
            {
                cols[j][i] = a[i, j];
            }

            v[j] = new double[n];
            v[j][j] = 1;
        }

        for (int sweep = 0; sweep < 60; sweep++)
        {
            int rotated = 0;

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double[] cp = cols[p], cq = cols[q];
                    double alpha = 0, beta = 0, gamma = 0;

                    for (int i = 0; i < m; i++)
                    {
                        alpha += cp[i] * cp[i];
                        beta += cq[i] * cq[i];
                        gamma += cp[i] * cq[i];
                    }

                    if (Math.Abs(gamma) <= 1e-15 * Math.Sqrt(alpha * beta) || Math.Abs(gamma) < 1e-300) continue;
                    rotated++;
                    double zeta = (beta - alpha) / (2 * gamma);
                    double t = Math.CopySign(1.0, zeta) / (Math.Abs(zeta) + Math.Sqrt(1 + zeta * zeta));
                    double c = 1 / Math.Sqrt(1 + t * t), sn = c * t;

                    for (int i = 0; i < m; i++)
                    {
                        double x = cp[i], y = cq[i];
                        cp[i] = c * x - sn * y;
                        cq[i] = sn * x + c * y;
                    }

                    double[] vp = v[p], vq = v[q];

                    for (int i = 0; i < n; i++)
                    {
                        double x = vp[i], y = vq[i];
                        vp[i] = c * x - sn * y;
                        vq[i] = sn * x + c * y;
                    }
                }
            }

            if (rotated == 0) break;
        }

        var sigma = new double[n];

        for (int j = 0; j < n; j++)
        {
            double s = 0;

            for (int i = 0; i < m; i++)
            {
                s += cols[j][i] * cols[j][i];
            }

            sigma[j] = Math.Sqrt(s);
        }

        int[] order = new int[n];

        for (int j = 0; j < n; j++)
        {
            order[j] = j;
        }

        Array.Sort(order, (x, y) => sigma[y].CompareTo(sigma[x]));
        var u = new double[m, n];
        var vv = new double[n, n];
        var sorted = new double[n];

        for (int k = 0; k < n; k++)
        {
            int j = order[k];
            sorted[k] = sigma[j];

            for (int i = 0; i < m; i++)
            {
                u[i, k] = sigma[j] > 1e-300 ? cols[j][i] / sigma[j] : 0.0;
            }

            for (int i = 0; i < n; i++)
            {
                vv[i, k] = v[j][i];
            }
        }

        return (u, sorted, vv);
    }

    /// <summary>Σⱼ σⱼ·uⱼ·vⱼᵀ over the first k: the best rank-k approximation.</summary>
    public static double[,] Rebuild(double[,] u, double[] sigma, double[,] v, int k)
    {
        int m = u.GetLength(0), n = v.GetLength(0);
        var r = new double[m, n];
        k = Math.Clamp(k, 0, sigma.Length);

        for (int j = 0; j < k; j++)
        {
            double s = sigma[j];

            for (int i = 0; i < m; i++)
            {
                double su = s * u[i, j];

                for (int l = 0; l < n; l++)
                {
                    r[i, l] += su * v[l, j];
                }
            }
        }

        return r;
    }

    /// <summary>√(Σσⱼ², j ≥ k) / √(Σσⱼ²): the Frobenius error of the rank-k
    /// truncation, relative - Eckart–Young, in closed form.</summary>
    public static double RelativeError(double[] sigma, int k)
    {
        double total = 0, rest = 0;

        for (int j = 0; j < sigma.Length; j++)
        {
            total += sigma[j] * sigma[j];
            if (j >= k) rest += sigma[j] * sigma[j];
        }

        return total > 0 ? Math.Sqrt(rest) / Math.Sqrt(total) : 0.0;
    }

    public static double Frobenius(double[,] a)
    {
        double s = a.Cast<double>().Sum(x => x * x);
        return Math.Sqrt(s);
    }

    public static int Rank(double[] sigma, double relEps = 1e-9)
    {
        if (sigma.Length == 0 || sigma[0] <= 0) return 0;
        return sigma.Count(s => s > relEps * sigma[0]);
    }
}
