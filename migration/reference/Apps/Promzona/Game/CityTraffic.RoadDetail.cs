namespace Promzona.Game;

public static class CityRoadDetail
{
    public static IEnumerable<CityPothole> Holes(int i, int j)
    {
        if (i < 0 || j < 0 || i >= CityTraffic.Nodes - 1 || j >= CityTraffic.Nodes - 1) yield break;
        uint state = unchecked((uint)(CityLayout.Current.Seed * 16777619 + i * 73856093 + j * 19349663)) | 1;

        for (int axis = 0; axis < 2; axis++)
        {
            if (!CityTraffic.HasRoad(i, j, axis)) continue;
            int count = Next() < .42 ? 0 : Next() < .68 ? 1 : 2;
            double last = -10;

            for (int k = 0; k < count; k++)
            {
                double along = 12 + Next() * 13;
                if (Math.Abs(along - last) < 3.5) continue;
                last = along;
                double across = -3.5 + Next() * 7;
                var point = new CityPoint(CityTraffic.Coordinate(i) + (axis == 0 ? across : along), CityTraffic.Coordinate(j) + (axis == 0 ? along : across));
                yield return new CityPothole(point, .48 + Next() * .65, (int)(Next() * 3));
            }
        }

        yield break;

        double Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state + .5) / 4294967296d;
        }
    }
}
