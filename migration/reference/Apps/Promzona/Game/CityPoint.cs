using System.Text.Json.Serialization;

namespace Promzona.Game;

/// <summary>Planar actors on a directed lane/sidewalk graph. Metres and seconds;
/// physical footprints, route-following drivers and signal rules.
/// The planar game model is not a certified road-vehicle simulation.</summary>
public readonly record struct CityPoint(double X, double Z)
{
    public static CityPoint operator +(CityPoint a, CityPoint b) => new(a.X + b.X, a.Z + b.Z);
    public static CityPoint operator -(CityPoint a, CityPoint b) => new(a.X - b.X, a.Z - b.Z);
    public static CityPoint operator *(CityPoint a, double b) => new(a.X * b, a.Z * b);
    // A derived value: without the attribute every saved point (routes, marks,
    // cash, actor fields) carried a third number that the loader ignored.
    [JsonIgnore]
    public double Length => Math.Sqrt(X * X + Z * Z);
    public static double Dot(CityPoint a, CityPoint b) => a.X * b.X + a.Z * b.Z;
}
