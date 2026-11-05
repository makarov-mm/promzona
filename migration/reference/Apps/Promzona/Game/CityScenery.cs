namespace Promzona.Game;

public static class CityScenery
{
    public static IEnumerable<(CityPoint Position, double Radius)> Trees()
    {
        for (int j = 0; j < CityTraffic.Nodes - 1; j++)
        for (int i = 0; i < CityTraffic.Nodes - 1; i++)
        {
            if (CityLayout.CanalColumn(i)||CityRoadLevels.ReservedBlock(i,j)) continue;
            double x = CityTraffic.BlockCentre(i), z = CityTraffic.BlockCentre(j);

            if (CityLayout.Current.Park(i, j))
            {
                for (int n = 0; n < 8; n++)
                {
                    var tree = new CityPoint(x + 8 * Math.Cos(n * Math.Tau / 8), z + 8 * Math.Sin(n * Math.Tau / 8));

                    if (CityAmenities.TreeClear(tree))
                    {
                        yield return (tree, .24);
                    }
                }
            }
            else if ((i + j) % 6 != 0 && CityAmenities.TreeClear(new CityPoint(x, z)))
            {
                yield return (new CityPoint(x, z), .20);
            }

            for (int d = 0; d < 2; d++)
            {
                if (!CityTraffic.HasRoad(i, j, d))
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        CityPoint f = CityTraffic.Forward(d);
                        CityPoint p = new CityPoint(x - 18, z - 18) + f * 18 + CityTraffic.Right(f) * (side * 3.8);

                        if (CityWater.UrbanSite(p, .8) && CityAmenities.TreeClear(p))
                        {
                            yield return (p, .24);
                        }
                    }
                }
            }
        }
    }
}
