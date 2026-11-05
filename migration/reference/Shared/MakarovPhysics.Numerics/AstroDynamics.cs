namespace MakarovPhysics.Numerics;

/// <summary>Explicit teaching atmosphere profiles. Density is exponential, not
/// a weather/thermosphere model; the celestial body's mass and radius use S1.</summary>
public readonly record struct FlightWorld(
    string Name,
    double SurfaceDensity,
    double DensityHeight,
    double EntryHeight,
    double EntrySpeed,
    double HeatingCoefficient)
{
    public CelestialBody Body => SolarSystemCatalog.Find(Name)!;
    public double Radius => Body.RadiusKm * 1000;
    public double Mu => SolarSystemCatalog.G * Body.MassKg;
    public double Density(double altitude) => SurfaceDensity * Math.Exp(-Math.Max(0, altitude) / DensityHeight);

    public static readonly FlightWorld[] Entries =
    [
        new("Earth", 1.225, 7200, 110000, 7600, 1.83e-4),
        new("Mars", .020, 11100, 100000, 5500, 1.90e-4),
        new("Venus", 65, 15900, 180000, 11000, 1.90e-4),
        new("Titan", 5.3, 40000, 800000, 6000, 1.74e-4)
    ];
}
