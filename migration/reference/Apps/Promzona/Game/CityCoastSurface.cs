using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Coastal tiles share the road's actual footprint; sand and asphalt
/// partition the ground rather than being competing, coplanar overlays.</summary>
internal static class CityCoastSurface
{
    public static void Draw(DioramaGeometry g, float x0, float x1, float z0, float z1)
    {
        float low = (float)CityTraffic.Coordinate(0), high = -low, r = (float)CityTraffic.RoadHalf;
        float[] xs = Cuts(x0, x1);
        float[] zs = Cuts(z0, z1);

        for (int i = 0; i < xs.Length - 1; i++)
        {
            for (int j = 0; j < zs.Length - 1; j++)
            {
                float x = (xs[i] + xs[i + 1]) * .5f, z = (zs[j] + zs[j + 1]) * .5f;
                bool road = CityRoadSurface.Contains(new CityPoint(x, z));
                g.Material = (SurfaceMaterial)(road ? 300 : 301);
                CityTerrainGeometry.Top(g, x, z, (xs[i + 1] - xs[i]) * .5f, (zs[j + 1] - zs[j]) * .5f, 0, road ? new Vec3(.14f, .15f, .15f) : new Vec3(.69f, .59f, .39f));
            }
        }

        return;

        float[] Cuts(float a, float b)
        {
            var cuts = new List<float> { a, b };

            for (int n = Math.Max(0, (int)Math.Floor((a - low - r) / 36));
                 n <= Math.Min(CityTraffic.Nodes - 1, (int)Math.Ceiling((b - low + r) / 36));
                 n++)
            {
                cuts.AddRange(new[] { low + n * 36 - r, low + n * 36, low + n * 36 + r }.Where(v => v > a && v < b));
            }

            return [.. cuts.Distinct().Order()];
        }
    }

    public static void Mark(DioramaGeometry g, Vec3 centre, Vec3 half)
    {
        if (!CityRoadSurface.Rectangle(centre.X, centre.Z, half.X, half.Z)) return;
        g.Material=(SurfaceMaterial)302;g.Box(centre, half, new Vec3(.61f, .60f, .51f));
    }
}
