namespace MakarovPhysics.Numerics;

/// <summary>
/// The other half of SpecialFunctions (the pendulum period and the elliptic
/// integral live in Constants.cs): the functions the statistics modules share, each written out
/// and each pinned in SelfTest against an independent source - the
/// twins' math.erfc, printed tables of Student's t, Γ at half-integers.
///
/// erfc: Maclaurin series below |x| = 2.5, modified-Lentz continued fraction
/// above; relative error 3e-12 at the join, 1e-15 elsewhere. Φ = ½ erfc(−z/√2).
/// ln Γ: Lanczos, g = 7, nine terms. I_x(a, b): the regularized incomplete
/// beta by Lentz's continued fraction (the Numerical Recipes form), with the
/// symmetric switch at x > (a + 1)/(a + b + 2). Student's t: its CDF is
/// 1 − ½ I_x(ν/2, ½) with x = ν/(ν + t²); quantiles by bisection. The
/// tables come out of the code, not the other way round.
/// </summary>
public static partial class SpecialFunctions
{
    public static double Erfc(double x)
    {
        double ax = Math.Abs(x), r;

        if (ax < 2.5)
        {
            double term = ax, sum = ax, x2 = ax * ax;

            for (int k = 1; k < 200; k++)
            {
                term *= -x2 / k;
                double add = term / (2 * k + 1);
                sum += add;
                if (Math.Abs(add) < 1e-17 * Math.Abs(sum)) break;
            }

            r = 1 - 2 / Math.Sqrt(Math.PI) * sum;
        }
        else
        {
            const double tiny = 1e-300;
            double f = ax, c = ax, d = 0;

            for (int k = 1; k < 300; k++)
            {
                double a = k * 0.5;
                d = ax + a * d; if (Math.Abs(d) < tiny) d = tiny; d = 1 / d;
                c = ax + a / c; if (Math.Abs(c) < tiny) c = tiny;
                double delta = c * d;
                f *= delta;
                if (Math.Abs(delta - 1) < 1e-16) break;
            }

            r = Math.Exp(-ax * ax) / (Math.Sqrt(Math.PI) * f);
        }

        return x < 0 ? 2 - r : r;
    }

    /// <summary>Standard normal density.</summary>
    public static double NormalPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);

    /// <summary>Φ(z) = ½ erfc(−z/√2).</summary>
    public static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2));

    /// <summary>z with Φ(z) = p, by bisection to 1e-13.</summary>
    public static double NormalQuantile(double p)
    {
        double lo = -10, hi = 10;

        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (NormalCdf(mid) < p) lo = mid; else hi = mid;
            if (hi - lo < 1e-13) break;
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>ln Γ(z), Lanczos (g = 7, n = 9); reflection below ½.</summary>
    public static double LogGamma(double z)
    {
        double[] g =
        [
            0.99999999999980993, 
            676.5203681218851, 
            -1259.1392167224028, 
            771.32342877765313,
            -176.61502916214059, 
            12.507343278686905, 
            -0.13857109526572012, 
            9.9843695780195716e-6, 
            1.5056327351493116e-7
        ];

        if (z < 0.5)
        {
            return Math.Log(Math.PI / Math.Sin(Math.PI * z)) - LogGamma(1 - z);
        }

        z -= 1;
        double x = g[0];

        for (int i = 1; i < 9; i++)
        {
            x += g[i] / (z + i);
        }

        double t = z + 7.5;
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(x);
    }

    /// <summary>Regularized incomplete beta I_x(a, b).</summary>
    public static double IncompleteBeta(double a, double b, double x)
    {
        switch (x)
        {
            case <= 0:
                return 0;
            case >= 1:
                return 1;
        }

        double lnBeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        double front = Math.Exp(Math.Log(x) * a + Math.Log(1 - x) * b - lnBeta);

        if (x < (a + 1) / (a + b + 2))
        {
            return front * BetaContinuedFraction(a, b, x) / a;
        }

        return 1 - front * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const double tiny = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < tiny) d = tiny;
        d = 1 / d;
        double h = d;

        for (int m = 1; m <= 300; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny; d = 1 / d;
            c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny; d = 1 / d;
            c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < 1e-15) break;
        }

        return h;
    }

    /// <summary>Student's t CDF with ν degrees of freedom.</summary>
    public static double StudentCdf(double t, int nu)
    {
        double x = nu / (nu + t * t);
        double ib = IncompleteBeta(nu / 2.0, 0.5, x);
        return t >= 0 ? 1 - 0.5 * ib : 0.5 * ib;
    }

    /// <summary>t with F(t) = p (p ≥ ½), by bisection to 1e-12.</summary>
    public static double StudentQuantile(double p, int nu)
    {
        double lo = 0, hi = 1000;

        for (int i = 0; i < 300; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (StudentCdf(mid, nu) < p) lo = mid; else hi = mid;
            if (hi - lo < 1e-12) break;
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>Student's density.</summary>
    public static double StudentPdf(double t, int nu)
    {
        double lnC = LogGamma((nu + 1) / 2.0) - LogGamma(nu / 2.0) - 0.5 * Math.Log(nu * Math.PI);
        return Math.Exp(lnC - (nu + 1) / 2.0 * Math.Log(1 + t * t / nu));
    }
}
