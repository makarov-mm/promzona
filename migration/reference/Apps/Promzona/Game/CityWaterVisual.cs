using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityWaterVisual
{
    public static void Canal(DioramaGeometry g, int i, int j, int lod)
    {
        int start = g.SurfacePoints.Count;
        float x = (float)CityTraffic.Coordinate(i), z = (float)CityTraffic.Coordinate(j);
        float edge = (float)CityTraffic.LandEdge;
        float z0 = Math.Max(-edge, z), z1 = Math.Min(edge, z + 36);
        if (z1 <= z0) return;
        float bank = (float)CityWater.BankHalf, water = (float)CityWater.WaterHalf;
        g.Material = (SurfaceMaterial)301;

        for (float a = x; a < x + 36; a++)
        {
            for (float b = z0; b < z1; b++)
            {
                g.Material = Math.Abs(a + .5f - x - 18) < bank 
                    ? (SurfaceMaterial)301 
                    : Math.Abs(a + .5f - x - 18) < 18 - CityTraffic.RoadHalf 
                        ? SurfaceMaterial.Paving 
                        : (SurfaceMaterial)300;

                g.Quad(P(a, b), P(a, b + 1), P(a + 1, b + 1), P(a + 1, b), new Vec3(.46f, .43f, .32f));
                continue;

                Vec3 P(float xx, float zz) => new(xx, (float)CityTerrain.Current.BedHeight(xx, zz), zz);
            }
        }

        g.Material = (SurfaceMaterial)360;

        // The water is a continuous surface above a real river bed, not a hanging slab.
        g.Quad(
            new Vec3(x + 18 - water, -2.8f, z0), 
            new Vec3(x + 18 - water, -2.8f, z1), 
            new Vec3(x + 18 + water, -2.8f, z1),
            new Vec3(x + 18 + water, -2.8f, z0), 
            new Vec3(.09f, .24f, .25f));

        for (int row = j; row <= j + 1; row++)
        {
            if (CityLayout.BridgeRow(row))
            {
                float rz = (float)CityTraffic.Coordinate(row),
                    lo = Math.Max(z0, rz - (CityLayout.WideRoads ? 10.5f : 8)),
                    hi = Math.Min(z1, rz + (CityLayout.WideRoads ? 10.5f : 8));

                if (hi <= lo) continue;

                for (float a = x + 18 - (float)CityWater.DeckHalf; a < x + 18 + (float)CityWater.DeckHalf - .0001f;)
                {
                    float next = Math.Min(x + 18 + (float)CityWater.DeckHalf, MathF.Floor(a + .0001f) + 1);
                    float y0 = (float)CityWater.Deck(a, rz), y1 = (float)CityWater.Deck(next, rz);

                    g.Material = (SurfaceMaterial)300;
                    g.Quad(new Vec3(a, y0, lo), new Vec3(a, y0, hi), new Vec3(next, y1, hi), new Vec3(next, y1, lo), new Vec3(.18f, .19f, .19f));
                    g.Material = SurfaceMaterial.Concrete;

                    foreach (int side in new[] { -1, 1 })
                    {
                        float az = rz + side * (CityLayout.WideRoads ? 10.3f : 7.8f);
                        if (az < lo || az > hi) continue;

                        g.Quad(new(a, y0, az), new(next, y1, az), new(next, y1 - .65f, az), new(a, y0 - .65f, az), new(.35f, .36f, .34f));
                        g.Material = SurfaceMaterial.Paving;
                        float inner = rz + side * (CityLayout.WideRoads ? 7.4f : 4.9f);
                        g.Quad(new(a, y0 + .16f, Math.Min(inner, az)), new(a, y0 + .16f, Math.Max(inner, az)), new(next, y1 + .16f, Math.Max(inner, az)), new(next, y1 + .16f, Math.Min(inner, az)), new(.47f, .45f, .40f));
                        g.Material = SurfaceMaterial.CastMetal;
                        float railStart = Math.Max(a, x + 18 - (float)CityWater.RailHalf), railEnd = Math.Min(next, x + 18 + (float)CityWater.RailHalf);

                        if (railEnd > railStart)
                        {
                            g.Tube(
                                new Vec3(railStart, (float)CityWater.Deck(railStart, rz) + 1.1f, az),
                                new Vec3(railEnd, (float)CityWater.Deck(railEnd, rz) + 1.1f, az), .06f,
                                new Vec3(.18f, .25f, .24f));
                        }

                        if (Math.Abs(a - x - 18) < CityWater.RailHalf && ((int)a) % 3 == 0)
                        {
                            g.Tube(new Vec3(a, y0 + .15f, az), new Vec3(a, y0 + 1.1f, az), .045f, new Vec3(.18f, .25f, .24f));
                        }
                    }

                    if ((int)a % 5 < 3 && rz >= lo && rz <= hi)
                    {
                        g.Material = SurfaceMaterial.Flat;
                        g.Quad(
                            new Vec3(a, y0 + .02f, rz - .035f), 
                            new Vec3(a, y0 + .02f, rz + .035f),
                            new Vec3(next, y1 + .02f, rz + .035f), 
                            new Vec3(next, y1 + .02f, rz - .035f),
                            new Vec3(.71f, .65f, .43f));
                    }

                    a = next;
                }
            }
        }

        CityTerrainGeometry.Absolute(g, start);
    }
}
