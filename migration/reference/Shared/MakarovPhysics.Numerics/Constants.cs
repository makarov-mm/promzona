namespace MakarovPhysics.Numerics;

/// <summary>Physical constants, SI (CODATA 2022).</summary>
public static class Constants
{
    public const double G0 = 9.80665; // standard gravity, m/s²
    public const double C = 299_792_458.0; // speed of light, m/s
    public const double E = 1.602176634e-19; // elementary charge, C
    public const double Kb = 1.380649e-23; // Boltzmann constant, J/K
    public const double Na = 6.02214076e23; // Avogadro constant, 1/mol
    public const double R = 8.31446261815324; // gas constant, J/(mol·K)
    public const double H = 6.62607015e-34; // Planck constant, J·s
    public const double Ke = 8.9875517923e9; // 1/(4πε₀), N·m²/C²
    public const double Me = 9.1093837015e-31; // electron mass, kg
    public const double Mp = 1.67262192369e-27; // proton mass, kg
    public const double Au = 1.495978707e11; // astronomical unit, m
    public const double MSun = 1.98892e30; // solar mass, kg
    public const double MEarth = 5.9722e24; // Earth mass, kg
}

/// <summary>Special functions used for analytic reference solutions.</summary>
public static partial class SpecialFunctions
{
    /// <summary>
    /// Exact simple-pendulum period T(theta0) via the arithmetic-geometric mean:
    /// T = T0 / AGM(1, cos(theta0/2)), T0 = 2*pi*sqrt(L/g).
    /// </summary>
    public static double PendulumPeriod(double length, double gravity, double amplitude)
    {
        if (!(length > 0.0) || !double.IsFinite(length))
            throw new ArgumentOutOfRangeException(nameof(length));
        if (!(gravity > 0.0) || !double.IsFinite(gravity))
            throw new ArgumentOutOfRangeException(nameof(gravity));
        if (!double.IsFinite(amplitude) || Math.Abs(amplitude) >= Math.PI)
            throw new ArgumentOutOfRangeException(nameof(amplitude), "Amplitude must satisfy |theta0| < PI.");

        double a = 1.0;
        double b = Math.Cos(0.5 * Math.Abs(amplitude));

        for (int i = 0; i < 60 && Math.Abs(a - b) > 1e-15; i++)
        {
            double arithmetic = 0.5 * (a + b);
            b = Math.Sqrt(a * b);
            a = arithmetic;
        }

        return 2.0 * Math.PI * Math.Sqrt(length / gravity) / (0.5 * (a + b));
    }

    /// <summary>Complete elliptic integral of the first kind K(m), where m = k^2.</summary>
    public static double EllipticK(double parameter)
    {
        if (!double.IsFinite(parameter) || parameter < 0.0 || parameter > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameter), "The elliptic parameter must be in [0, 1].");
        }

        if (Math.Abs(parameter - 1.0) < 0.0000001)
        {
            return double.PositiveInfinity;
        }

        double a = 1.0;
        double b = Math.Sqrt(1.0 - parameter);

        for (int i = 0; i < 60 && Math.Abs(a - b) > 1e-15; i++)
        {
            double arithmetic = 0.5 * (a + b);
            b = Math.Sqrt(a * b);
            a = arithmetic;
        }

        return Math.PI / (a + b);
    }

    /// <summary>Solves the elliptic Kepler equation M = E - e*sin(E) using Newton's method.</summary>
    public static double SolveKepler(double meanAnomaly, double eccentricity)
    {
        if (!double.IsFinite(meanAnomaly))
        {
            throw new ArgumentOutOfRangeException(nameof(meanAnomaly));
        }

        if (!double.IsFinite(eccentricity) || eccentricity < 0.0 || eccentricity >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(eccentricity), "Elliptic eccentricity must be in [0, 1).");
        }

        const double twoPi = 2.0 * Math.PI;
        double turns = Math.Floor((meanAnomaly + Math.PI) / twoPi);
        double reducedMeanAnomaly = meanAnomaly - turns * twoPi;

        double eccentricAnomaly = eccentricity < 0.8
            ? reducedMeanAnomaly
            : Math.CopySign(Math.PI, reducedMeanAnomaly == 0.0 ? 1.0 : reducedMeanAnomaly);

        for (int i = 0; i < 50; i++)
        {
            double function = eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - reducedMeanAnomaly;
            double derivative = 1.0 - eccentricity * Math.Cos(eccentricAnomaly);
            double step = function / derivative;
            eccentricAnomaly -= step;

            if (Math.Abs(step) < 1e-14)
                break;
        }

        return eccentricAnomaly + turns * twoPi;
    }
}
