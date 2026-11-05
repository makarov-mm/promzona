namespace Promzona.Game;

/// <summary>Stable reuse of existing dry building footprints; no road or collision changes.</summary>
public static class CityCivicSites
{
    // Both public buildings face +Z. Layout, signs and the release/healing area
    // measure from this footprint, so resizing a building also moves its doorway.
    internal static CityModel.Obstacle Building(int i, int j) =>
        new(CityTraffic.BlockCentre(i), CityTraffic.BlockCentre(j), 9, 8, 8);

    internal static CityPoint Entrance(int i, int j)
    {
        var building = Building(i, j);
        const double doorwayClearance = 1.25;
        return new(building.X, building.Z + building.HalfZ + doorwayClearance);
    }

    public static bool BlankEnds(CityModel.Obstacle b) => b.Height >= 6 && b.HalfX >= 2.8 && b.HalfZ >= 2.8;
    public static int WindowWall(CityModel.Obstacle b) => b.HalfX >= b.HalfZ ? 0 : 1;

    public static bool Residential(CityModel.Obstacle b)
    {
        int i = Math.Clamp((int)Math.Floor(b.X / CityTraffic.Spacing + (CityTraffic.Nodes - 1) * .5), 0, CityTraffic.Nodes - 2);
        int j = Math.Clamp((int)Math.Floor(b.Z / CityTraffic.Spacing + (CityTraffic.Nodes - 1) * .5), 0, CityTraffic.Nodes - 2);
        return !CityLayout.Current.Industrial(i, j) && !CityTraffic.IsClinic(i, j) && !CityTraffic.IsStation(i, j);
    }

    public static bool Office(CityModel.Obstacle b, int id) => id % 113 == 19 && b.Height is >= 6 and <= 24 && b is { HalfX: >= 3.3, HalfZ: >= 3.3 } && Residential(b);
    public static IEnumerable<(int Id, CityModel.Obstacle Building)> Offices() => CityModel.Blocks.Select((b, id) => (Id: id, Building: b)).Where(v => Office(v.Building, v.Id));
}
