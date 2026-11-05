namespace Promzona.Game;

/// <summary>Shared shoreline/river/deck definition for generation, navigation,
/// contacts and rendering. River beds remain distinct from bridge deck support.</summary>
public static class CityWater
{
    public const double Level = -2.8;
    public static double BankHalf => 18 - CityTraffic.RoadHalf - 2.8;
    public static double WaterHalf => BankHalf - 1.55;

    // The deck joins the INNER edge of the quayside street, not its centre.
    // Railings stop at the bank so the riverside footpath stays connected.
    public static double DeckHalf => 18 - CityTraffic.RoadHalf;
    public static double RailHalf => BankHalf;
    public static double DeckSide => CityLayout.WideRoads ? 10.3 : 7.8;

    // Geometry-only predicate is safe during layout generation (no Current recursion).
    public static bool UrbanSite(CityPoint p, double radius = 0) =>
        Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) + radius <= CityTraffic.Coordinate(CityTraffic.Nodes - 1) + 7 &&
        RiverDistance(p.X) > 18 + radius;

    public static double RiverDistance(double x)
    {
        if (!CityLayout.Rivers)
        {
            return double.PositiveInfinity;
        }

        return Math.Min(
            Math.Abs(x - (CityTraffic.Coordinate(CityTraffic.Nodes / 3) + 18)), 
            Math.Abs(x - (CityTraffic.Coordinate(2 * CityTraffic.Nodes / 3) + 18)));
    }

    public static bool Bridge(double x, double z)
    {
        if (!CityLayout.Rivers || RiverDistance(x) > DeckHalf) return false;
        int row = (int)Math.Round((z - CityTraffic.Coordinate(0)) / 36);
        return CityLayout.BridgeRow(row) && Math.Abs(z - CityTraffic.Coordinate(row)) < (CityLayout.WideRoads ? 10.55 : 8.05);
    }

    public static double Deck(double x, double z) => 2.2 + 1.5 * Math.Pow(Math.Cos(Math.PI * Math.Min(1, RiverDistance(x) / DeckHalf) * .5), 2);

    public static double Depth(CityPoint p) => Bridge(p.X, p.Z) ? 0 : Math.Max(0, Level - CityTerrain.Current.BedHeight(p.X, p.Z));

    public static bool Wet(CityPoint p) => Depth(p) > .02;
}
