namespace MakarovPhysics.Numerics;

/// <summary>A body of the Solar System as the catalogue knows it. Masses in kg,
/// radii in km, rotation in hours (negative: retrograde), scale height in km,
/// orbital elements at J2000 - <see cref="A"/> in AU about the Sun or in km
/// about a planet, angles in degrees (<see cref="L"/> the mean longitude,
/// <see cref="LongitudeOfPerihelion"/> ϖ, <see cref="LongitudeOfNode"/> Ω).
/// <see cref="EpochKnown"/> is false where the phase at J2000 is not carried
/// (the moons and the small bodies: their elements are shape and tilt only).</summary>
public sealed record CelestialBody(
    string Name, string Kind, string? Parent,
    double MassKg, double RadiusKm, double BondAlbedo, double ObliquityDeg, double RotationHours, double ScaleHeightKm,
    double A, double E, double InclinationDeg, double L, double LongitudeOfPerihelion, double LongitudeOfNode, bool EpochKnown)
{
    /// <summary>Surface gravity G M / R², m/s².</summary>
    public double Gravity => SolarSystemCatalog.G * MassKg / (RadiusKm * 1000 * RadiusKm * 1000);
    /// <summary>Escape speed √(2 G M / R), m/s.</summary>
    public double EscapeSpeed => Math.Sqrt(2 * SolarSystemCatalog.G * MassKg / (RadiusKm * 1000));
    /// <summary>Mean density, kg/m³.</summary>
    public double Density => MassKg / (4.0 / 3 * Math.PI * Math.Pow(RadiusKm * 1000, 3));
    /// <summary>Semi-major axis in metres (AU about the Sun, km about a planet).</summary>
    public double SemiMajorMetres => Parent == "Sun" ? A * SolarSystemCatalog.Au : A * 1000;
}

/// <summary>
/// The Solar System in one table (S1 of ASTRONOMY-WOW-PLAN-2026-10-19, 20.10,
/// tail1530): the Sun, the eight planets, the Moon, Pluto, Ceres, Vesta, Bennu
/// and the large moons, with the numbers the astronomy modules used to keep as
/// their own constants. Sources: NASA planetary fact sheets; the planets'
/// J2000 elements are JPL's approximate Keplerian elements (Standish), held
/// fixed - good to arcminutes over decades, which is what a scene needs.
/// <see cref="State"/> gives the heliocentric position and velocity at a time
/// after J2000. Twin: Tools/twins/solar_system_catalog_twin.py.
/// </summary>
public static class SolarSystemCatalog
{
    public const double G = 6.67430e-11, Au = 149597870700, Year = 31557600, SunMass = 1.98847e30;

    public static readonly CelestialBody[] Bodies =
    [
        new("Sun", "Star", null, 1.98847e30, 695700, 0, 7.25, 609.12, 0, 0, 0, 0, 0, 0, 0, false),
        new("Mercury", "Mercury", "Sun", 3.3011e23, 2439.7, .088, .034, 1407.6, 0, .38709927, .20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593, true),
        new("Venus", "Venus", "Sun", 4.8675e24, 6051.8, .76, 177.4, -5832.5, 15.9, .72333566, .00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255, true),
        new("Earth", "Earth", "Sun", 5.9722e24, 6371.0, .306, 23.44, 23.9345, 8.5, 1.00000261, .01671123, -.00001531, 100.46457166, 102.93768193, 0, true),
        new("Mars", "Mars", "Sun", 6.4171e23, 3389.5, .25, 25.19, 24.6229, 11.1, 1.52371034, .09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891, true),
        new("Jupiter", "Jupiter", "Sun", 1.8982e27, 69911, .503, 3.13, 9.925, 27, 5.20288700, .04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909, true),
        new("Saturn", "Saturn", "Sun", 5.6834e26, 58232, .342, 26.73, 10.656, 59.5, 9.53667594, .05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448, true),
        new("Uranus", "Uranus", "Sun", 8.6810e25, 25362, .300, 97.77, -17.24, 27.7, 19.18916464, .04725744, .77263783, 313.23810451, 170.95427630, 74.01692503, true),
        new("Neptune", "Neptune", "Sun", 1.02413e26, 24622, .290, 28.32, 16.11, 19.7, 30.06992276, .00859048, 1.77004347, -55.12002969, 44.96476227, 131.78422574, true),
        new("Pluto", "Ice", "Sun", 1.303e22, 1188.3, .72, 122.5, -153.29, 50, 39.48211675, .24882730, 17.14001206, 238.92903833, 224.06891629, 110.30393684, true),
        new("Ceres", "Rock", "Sun", 9.3835e20, 469.7, .09, 4, 9.07, 0, 2.7691, .0785, 10.59, 0, 153.9, 80.3, false),
        new("Vesta", "Rock", "Sun", 2.59e20, 262.7, .18, 29, 5.342, 0, 2.3617, .0887, 7.14, 0, 254.5, 103.8, false),
        new("Bennu", "Rock", "Sun", 7.33e10, .245, .044, 178, 4.297, 0, 1.1264, .2037, 6.03, 0, 68.3, 2.06, false),
        new("Moon", "Moon", "Earth", 7.346e22, 1737.4, .11, 6.68, 655.72, 0, 384400, .0549, 5.145, 0, 0, 0, false),
        new("Io", "Rock", "Jupiter", 8.93e22, 1821.6, .52, 0, 42.46, 0, 421800, .0041, .05, 0, 0, 0, false),
        new("Europa", "Ice", "Jupiter", 4.80e22, 1560.8, .68, 0, 85.23, 0, 671100, .0094, .47, 0, 0, 0, false),
        new("Ganymede", "Ice", "Jupiter", 1.4819e23, 2634.1, .43, 0, 171.7, 0, 1070400, .0013, .2, 0, 0, 0, false),
        new("Callisto", "Rock", "Jupiter", 1.0759e23, 2410.3, .22, 0, 400.5, 0, 1882700, .0074, .19, 0, 0, 0, false),
        new("Titan", "Venus", "Saturn", 1.3452e23, 2574.7, .265, 0, 382.7, 40, 1221870, .0288, .35, 0, 0, 0, false),
        new("Enceladus", "Ice", "Saturn", 1.08e20, 252.1, .81, 0, 32.9, 0, 237948, .0047, .02, 0, 0, 0, false),
        new("Triton", "Ice", "Neptune", 2.139e22, 1353.4, .76, 0, -141.0, 0, 354759, .000016, 156.9, 0, 0, 0, false),
    ];

    public static CelestialBody Find(string name)
    {
        foreach (var b in Bodies) if (b.Name == name) return b;
        throw new KeyNotFoundException($"{name} is not in the Solar System catalogue.");
    }

    /// <summary>The eight planets in order from the Sun.</summary>
    public static readonly string[] Planets = ["Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"];

    /// <summary>Orbital period about the parent, years: 2π √(a³ / G (M_parent + m)).</summary>
    public static double PeriodYears(CelestialBody body)
    {
        if (body.Parent is null) return 0;
        double a = body.SemiMajorMetres, m = Find(body.Parent).MassKg + body.MassKg;
        return 2 * Math.PI * Math.Sqrt(a * a * a / (G * m)) / Year;
    }

    /// <summary>
    /// Heliocentric ecliptic position (AU) and velocity (AU/yr) of a body
    /// orbiting the Sun, <paramref name="years"/> after J2000, from its
    /// elements held fixed: M = L − ϖ + n t, Kepler's equation, the true
    /// anomaly, r = a (1 − e cos E), then the rotations by ω = ϖ − Ω, I and Ω.
    /// </summary>
    public static (double X, double Y, double Z, double Vx, double Vy, double Vz) State(CelestialBody body, double years)
    {
        if (body.Parent != "Sun") throw new ArgumentException("State is heliocentric: the body must orbit the Sun.", nameof(body));
        double a = body.A, e = body.E, inc = body.InclinationDeg * Math.PI / 180;
        double L = body.L * Math.PI / 180, pomega = body.LongitudeOfPerihelion * Math.PI / 180, node = body.LongitudeOfNode * Math.PI / 180;
        double n = 2 * Math.PI / PeriodYears(body);
        double mean = L - pomega + n * years, ecc = mean;
        for (int k = 0; k < 30; k++) ecc -= (ecc - e * Math.Sin(ecc) - mean) / (1 - e * Math.Cos(ecc));
        double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(ecc / 2), Math.Sqrt(1 - e) * Math.Cos(ecc / 2));
        double r = a * (1 - e * Math.Cos(ecc));
        double omega = pomega - node;
        double mu = G * (SunMass + body.MassKg) / (Au * Au * Au) * Year * Year;
        double vk = Math.Sqrt(mu / (a * (1 - e * e)));
        double px = r * Math.Cos(nu), py = r * Math.Sin(nu), vx = -vk * Math.Sin(nu), vy = vk * (e + Math.Cos(nu));
        double cw = Math.Cos(omega), sw = Math.Sin(omega), ci = Math.Cos(inc), si = Math.Sin(inc), co = Math.Cos(node), so = Math.Sin(node);
        (double X, double Y, double Z) Rotate(double x, double y)
        {
            double xw = cw * x - sw * y, yw = sw * x + cw * y;
            double xi = xw, yi = ci * yw, zi = si * yw;
            return (co * xi - so * yi, so * xi + co * yi, zi);
        }
        var p = Rotate(px, py);
        var v = Rotate(vx, vy);
        return (p.X, p.Y, p.Z, v.X, v.Y, v.Z);
    }
}
