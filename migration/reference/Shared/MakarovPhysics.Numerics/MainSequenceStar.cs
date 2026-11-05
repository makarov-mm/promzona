namespace MakarovPhysics.Numerics;

/// <summary>
/// What a main-sequence star of a given mass looks like - the S2 sandbox
/// infrastructure (20.10.2026, ASTRONOMY-WOW-PLAN-2026-10-19): every module
/// that places a star by its mass (BinaryStarOrbits first; HabitableZone,
/// StarCluster, Kepler to follow) takes its luminosity, radius, effective
/// temperature and lifetime from here, so a 0.6 M☉ companion is the same
/// orange dwarf in every scene. Solar units throughout.
///
/// Luminosity: the piecewise mass-luminosity relation (0.23 M^2.3 below
/// 0.43 M☉, M^4 to 2 M☉, 1.4 M^3.5 to 55 M☉, 32000 M beyond); radius: M^0.8
/// below 1 M☉, M^0.57 above; temperature by Stefan-Boltzmann about the Sun
/// (5772 K). Valid as an illustration from the M dwarfs to ~20 M☉; the joins
/// of the luminosity law jump by a few percent, the scatter of the real
/// relation, and are kept as published. Twin: Tools/twins/main_sequence_star_twin.py.
/// </summary>
public static class MainSequenceStar
{
    /// <summary>Effective temperature of the Sun, K (IAU 2015 nominal).</summary>
    public const double SolarTemperature = 5772;

    /// <summary>Luminosity in L☉ of a star of <paramref name="mass"/> M☉.</summary>
    public static double Luminosity(double mass)
    {
        if (mass < .43) return .23 * Math.Pow(mass, 2.3);
        if (mass < 2) return Math.Pow(mass, 4);
        if (mass < 55) return 1.4 * Math.Pow(mass, 3.5);
        return 32000 * mass;
    }

    /// <summary>Radius in R☉ of a star of <paramref name="mass"/> M☉.</summary>
    public static double Radius(double mass) => mass < 1 ? Math.Pow(mass, .8) : Math.Pow(mass, .57);

    /// <summary>Effective temperature, K: T☉ (L / R²)^¼.</summary>
    public static double Temperature(double mass) => SolarTemperature * Math.Pow(Luminosity(mass) / (Radius(mass) * Radius(mass)), .25);

    /// <summary>Main-sequence lifetime, years: 10 Gyr × M / L.</summary>
    public static double Lifetime(double mass) => 1e10 * mass / Luminosity(mass);
}
