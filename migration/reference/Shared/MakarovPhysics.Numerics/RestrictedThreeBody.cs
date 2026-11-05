namespace MakarovPhysics.Numerics;

/// <summary>
/// The planar elliptic restricted three-body problem for test particles (ASTRONOMY-WOW-PLAN-2026-10-19 §4, 20.10,
/// tail1539): a star of mass 1 − μ at the origin of heliocentric coordinates, a planet of mass μ on a fixed Kepler
/// ellipse of semi-major axis 1 and mean motion 1 (perihelion at t = 0), and particles that feel both plus the
/// indirect term of the star's reflex. Units: the planet's semi-major axis, G(M + m) = 1, so the planet's period is
/// 2π. Particles are independent, so each is advanced on its own by a fixed-step KDK leapfrog over the planet's
/// precomputed positions - the integration parallelises per particle, and the twins vectorise across particles with
/// the same arithmetic in the same order (kirkwood_gaps_twin.py, trojan_asteroids_twin.py --tier-a).
/// </summary>
public sealed class RestrictedThreeBody
{
    public double Mu { get; }
    public double Eccentricity { get; }
    public int StepsPerOrbit { get; }
    public double Dt { get; }

    /// <summary>G M of the star alone, 1 − μ.</summary>
    public double StarMass => 1 - Mu;

    /// <summary>Hill radius of the planet at its semi-major axis, (μ/3)^(1/3).</summary>
    public double HillRadius => Math.Cbrt(Mu / 3);
    private readonly double[] _px, _py, _indirect;

    /// <summary>Number of steps the planet's ephemeris covers.</summary>
    public int Steps => _px.Length - 1;

    public RestrictedThreeBody(double mu, double eccentricity, int stepsPerOrbit, int steps)
    {
        Mu = mu;
        Eccentricity = eccentricity;
        StepsPerOrbit = stepsPerOrbit;
        Dt = Math.Tau / stepsPerOrbit;

        _px = new double[steps + 1];
        _py = new double[steps + 1];
        _indirect = new double[steps + 1];

        double cosFactor = Math.Sqrt(1 - eccentricity * eccentricity);

        for (int k = 0; k <= steps; k++)
        {
            double mean = (k * Dt) % Math.Tau;
            double anomaly = AstronomyScience.Kepler(mean, eccentricity);
            double px = Math.Cos(anomaly) - eccentricity, py = cosFactor * Math.Sin(anomaly);
            _px[k] = px; _py[k] = py;
            double pr2 = px * px + py * py, pr = Math.Sqrt(pr2);
            _indirect[k] = mu / (pr2 * pr);
        }
    }

    /// <summary>The planet's heliocentric position at a step.</summary>
    public (double X, double Y) Planet(int step) => (_px[step], _py[step]);

    /// <summary>Heliocentric state from planar elements about a centre of gravitational parameter gm: a, e, mean anomaly and the argument of perihelion.</summary>
    public static (double X, double Y, double VX, double VY) StateFromElements(double gm, double a, double e, double mean, double perihelion)
    {
        double anomaly = AstronomyScience.Kepler(mean, e), root = Math.Sqrt(1 - e * e);
        double xp = a * (Math.Cos(anomaly) - e), yp = a * root * Math.Sin(anomaly), r = a * (1 - e * Math.Cos(anomaly));
        double speed = Math.Sqrt(gm * a) / r, vxp = -speed * Math.Sin(anomaly), vyp = speed * root * Math.Cos(anomaly);
        double cw = Math.Cos(perihelion), sw = Math.Sin(perihelion);
        return (xp * cw - yp * sw, xp * sw + yp * cw, vxp * cw - vyp * sw, vxp * sw + vyp * cw);
    }

    /// <summary>Osculating heliocentric semi-major axis and eccentricity about the star alone (a is negative on a hyperbola).</summary>
    public (double A, double E) Elements(double x, double y, double vx, double vy)
    {
        double gm = StarMass, r = Math.Sqrt(x * x + y * y), v2 = vx * vx + vy * vy;
        double a = 1 / (2 / r - v2 / gm), h = x * vy - y * vx;
        double e = Math.Sqrt(Math.Max(0, 1 - h * h / (gm * a)));
        return (a, e);
    }

    /// <summary>Acceleration of a particle at a step: the star, the planet, and the indirect term.</summary>
    public void Accelerate(double x, double y, int step, out double ax, out double ay)
    {
        double gm = StarMass, px = _px[step], py = _py[step];
        double r2 = x * x + y * y, r = Math.Sqrt(r2), inv = gm / (r2 * r);
        ax = -x * inv; ay = -y * inv;
        double dx = x - px, dy = y - py, d2 = dx * dx + dy * dy, d = Math.Sqrt(d2), invd = Mu / (d2 * d);
        ax = ax - dx * invd; ay = ay - dy * invd;
        double indirect = _indirect[step];
        ax = ax - px * indirect; ay = ay - py * indirect;
    }

    /// <summary>Advances one particle from step <paramref name="from"/> to step <paramref name="to"/> by KDK leapfrog; the acceleration at <paramref name="from"/> comes in and the one at <paramref name="to"/> goes out.</summary>
    public void Advance(ref double x, ref double y, ref double vx, ref double vy, ref double ax, ref double ay, int from, int to)
    {
        double dt = Dt, half = dt / 2;
        for (int k = from; k < to; k++)
        {
            vx = vx + ax * half; vy = vy + ay * half;
            x = x + vx * dt; y = y + vy * dt;
            Accelerate(x, y, k + 1, out ax, out ay);
            vx = vx + ax * half; vy = vy + ay * half;
        }
    }

    /// <summary>Distance of a particle from the planet at a step.</summary>
    public double PlanetDistance(double x, double y, int step)
    {
        double dx = x - _px[step], dy = y - _py[step];
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Longitude of a particle measured from the planet, wrapped to (−π, π]: positive ahead of it.</summary>
    public double RelativeLongitude(double x, double y, int step)
    {
        double angle = Math.Atan2(y, x) - Math.Atan2(_py[step], _px[step]);
        if (angle > Math.PI) angle -= Math.Tau; else if (angle <= -Math.PI) angle += Math.Tau;
        return angle;
    }

    /// <summary>Jacobi constant of the circular problem (e = 0) in the barycentric rotating frame, C = x² + y² + 2(1 − μ)/r₁ + 2μ/r₂ − v²: a conserved quantity the leapfrog must keep.</summary>
    public double Jacobi(double x, double y, double vx, double vy, int step)
    {
        double px = _px[step], py = _py[step];
        // barycentre: μ along the planet; the planet's velocity on the unit circle
        double bx = x - Mu * px, by = y - Mu * py, bvx = vx - Mu * (-py), bvy = vy - Mu * px;
        double ux = bvx + by, uy = bvy - bx;
        double r1 = Math.Sqrt(x * x + y * y), r2 = PlanetDistance(x, y, step);
        return bx * bx + by * by + 2 * (1 - Mu) / r1 + 2 * Mu / r2 - (ux * ux + uy * uy);
    }
}
