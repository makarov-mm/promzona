namespace MakarovPhysics.Numerics;

public static class Thomas
{
    /// <summary>
    /// Thomas algorithm: tridiagonal system a[i]·x[i-1] + b[i]·x[i] + c[i]·x[i+1] = d[i].
    /// Arrays of length n; a[0] and c[n-1] are ignored. d and the work buffer cw are
    /// overwritten; the answer is in x. For Crank-Nicolson schemes (heat, Schrödinger 1D).
    /// </summary>
    public static void Solve(double[] a, double[] b, double[] c, double[] d, double[] cw, double[] x)
    {
        int n = b.Length;

        if (n == 0)
        {
            throw new ArgumentException("Thomas: empty system.");
        }

        if (a.Length < n || c.Length < n || d.Length < n || cw.Length < n || x.Length < n)
        {
            throw new ArgumentException("Thomas: all arrays must have length >= b.Length.");
        }

        const double eps = 1e-300; // pivot guard: diagonal dominance is the caller's job

        if (Math.Abs(b[0]) < eps)
        {
            throw new ArgumentException("Thomas: zero pivot at row 0.");
        }

        cw[0] = c[0] / b[0];
        d[0] /= b[0];

        for (int i = 1; i < n; i++)
        {
            double piv = b[i] - a[i] * cw[i - 1];

            if (Math.Abs(piv) < eps)
            {
                throw new ArgumentException($"Thomas: zero pivot at row {i}.");
            }

            double m = 1.0 / piv;
            cw[i] = c[i] * m;
            d[i] = (d[i] - a[i] * d[i - 1]) * m;
        }

        x[n - 1] = d[n - 1];

        for (int i = n - 2; i >= 0; i--)
        {
            x[i] = d[i] - cw[i] * x[i + 1];
        }
    }
}