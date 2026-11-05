namespace Promzona.Game;

public enum IndustrialKind { Hall, Chimney, Tank, Shed, PumpHouse, Fence, Crate, Wagon, Buffer }

/// <summary>The industrial district (29.10.2026, road revision 3): five block rows
/// at the north edge between the canals, with halls, tank farms, chimneys and
/// yards behind concrete fences, a garage cooperative, a market and a railway
/// spur that replaces one street row and ends at a buffer stop with three
/// parked wagons. Everything here is a pure function of the grid position, so
/// the layout generator and the renderer agree without a shared table, and the
/// seeded random stream of older revisions is never touched.</summary>
public static class CityIndustrial
{
    public const int Revision = 3, ZoneRows = 5;
    public static int ZoneRow => CityLayout.GridSize - 8;
    public static int RailRow => ZoneRow + 2;
    public static int ZoneWest => CityLayout.GridSize / 3 + 1;
    public static int ZoneEast => 2 * CityLayout.GridSize / 3;
    public static bool Zone(int i, int j) => j >= ZoneRow && j < ZoneRow + ZoneRows && i >= ZoneWest && i < ZoneEast;
    /// <summary>Two garage cooperatives and a market front the first row of the district.</summary>
    public static bool IsCooperative(int i, int j) => j == ZoneRow && (i == ZoneWest + 3 || i == ZoneEast - 3);
    public static (int I, int J) Market => (ZoneEast - 7, ZoneRow);
    public static double RailZ => CityTraffic.Coordinate(RailRow) + 2.0;
    public static double RailX0 => CityTraffic.Coordinate(ZoneWest) - 2;
    public static double RailX1 => CityTraffic.Coordinate(ZoneEast - 1) - 6;
    public const double WagonHalfX = 9.6, WagonHalfZ = 1.45, WagonHeight = 3.8;

    /// <summary>The horizontal street edges of the rail row between the canals are the spur, not a road.</summary>
    public static bool RailEdge(int i, int j, int d)
    {
        if (d % 2 == 0 || j != RailRow) return false;
        int from = d == 1 ? i : i - 1;
        return from >= ZoneWest && from < ZoneEast - 1;
    }

    public static int Variant(int i, int j) => (i * 3 + j * 5 + i / 2) % 4;

    /// <summary>The solid things of a zone block, in a fixed order the renderer repeats.</summary>
    public static IEnumerable<(CityModel.Obstacle Box, IndustrialKind Kind)> Obstacles(int i, int j)
    {
        double x = CityTraffic.BlockCentre(i), z = CityTraffic.BlockCentre(j);
        double fence = CityTraffic.Coordinate(j) + CityTraffic.Sidewalk + 1.3;
        bool special = IsCooperative(i, j) || (i, j) == Market;
        if (!special)
            switch (Variant(i, j))
            {
                case 0:
                    yield return (new(x, z + 1.5, 8.4, 6, 9 + (i % 3) * 1.5), IndustrialKind.Hall);
                    yield return (new(x + 6.6, z - 5.5, .9, .9, 24 + (j % 2) * 5), IndustrialKind.Chimney);
                    yield return (new(x - 5.3, fence, 3.3, .12, 2.2), IndustrialKind.Fence);
                    yield return (new(x + 5.3, fence, 3.3, .12, 2.2), IndustrialKind.Fence);
                    break;
                case 1:
                    for (int n = -1; n <= 1; n++) yield return (new(x + n * 5.6, z + 3, 2.5, 2.5, 7 + (n + 1) % 2 * 1.5), IndustrialKind.Tank);
                    yield return (new(x, z - 4.5, 2.5, 2, 3.2), IndustrialKind.PumpHouse);
                    yield return (new(x - 5.3, fence, 3.3, .12, 2.2), IndustrialKind.Fence);
                    yield return (new(x + 5.3, fence, 3.3, .12, 2.2), IndustrialKind.Fence);
                    break;
                case 2:
                    yield return (new(x, z + 2.5, 8.4, 4.5, 6), IndustrialKind.Shed);
                    yield return (new(x, z - 5.5, 5, 2, 3.5), IndustrialKind.Shed);
                    break;
                default:
                    yield return (new(x - 2.5, z + 2, 6, 6.5, 6.5), IndustrialKind.Hall);
                    for (int n = 0; n < 4; n++) yield return (new(x + 5.8 + n % 2 * 2.0, z - 6 + n / 2 * 2.2, .9, .9, 1 + n % 3 * .5), IndustrialKind.Crate);
                    yield return (new(x - 5.3, fence, 3.3, .12, 2.2), IndustrialKind.Fence);
                    yield return (new(x + 5.3, fence, 3.3, .12, 2.2), IndustrialKind.Fence);
                    break;
            }
        if (j != RailRow) yield break;
        double x0 = CityTraffic.Coordinate(i), x1 = x0 + CityTraffic.Spacing;
        for (int k = 0; k < 3; k++)
        {
            // One full-length car per streamed block: its collider and rigid
            // foundation must stay inside the same block as the visual owner.
            double wx = RailX1 - WagonHalfX - 2 - k * Math.Max(WagonHalfX * 2 + .6,CityTraffic.Spacing);
            if (wx >= x0 && wx < x1) yield return (new(wx, RailZ, WagonHalfX, WagonHalfZ, WagonHeight), IndustrialKind.Wagon);
        }
        if (RailX1 >= x0 && RailX1 < x1) yield return (new(RailX1 + .4, RailZ, .5, 1.3, 1.5), IndustrialKind.Buffer);
    }
}
