namespace Promzona.Game;

/// <summary>A stop is stored at its roadside sign. The shelter extends behind it along -X.</summary>
internal static class BusStopFixture
{
    internal const string Model = "outdated_soviet_bus_stop";
    internal const float Width = 2.95f, Height = 2.4f, Depth = 1.12f;
    private const double CentreOffset = -.8;
    internal static CityPoint Centre(CityPoint sign) => new(sign.X + CentreOffset, sign.Z);
    internal static ImportedSite Imported(CityPoint sign) => new(Model, Centre(sign), Width, Height, Depth, MathF.PI / 2);

    // Reservation leaves room for passengers and furniture clearance around the shelter.
    internal const double ReservedHalfX = 1.65, ReservedHalfZ = 2.25;
    internal static readonly CityPoint WalkHalfSize = new(.7, 1.75);
    internal static CityPoint WalkCentre(CityPoint sign) => new(sign.X + (CentreOffset - .2), sign.Z);

    internal static CityPoint RearWall(CityPoint centre) => new(centre.X - .48, centre.Z);
    internal static CityPoint Bench(CityPoint centre) => new(centre.X - .25, centre.Z);
    internal static readonly CityPoint WallHalfSize = new(.10, 1.48);
    internal static readonly CityPoint BenchHalfSize = new(.34, 1.12);
    internal const double BenchHeight = .85;

    internal static bool Occupied(CityPoint centre, CityPoint point, double radius)
    {
        var wall = point - RearWall(centre);
        var seat = point - Bench(centre);
        const double walkingWallMargin = .04;
        return (Math.Abs(wall.X) < WallHalfSize.X + walkingWallMargin + radius && Math.Abs(wall.Z) < WallHalfSize.Z + radius)
            || (Math.Abs(seat.X) < BenchHalfSize.X + radius && Math.Abs(seat.Z) < BenchHalfSize.Z + radius);
    }
}
