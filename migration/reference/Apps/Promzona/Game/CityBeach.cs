using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityBeach
{
    public static void Draw(DioramaGeometry geometry, float x, float z, int i, int j)
    {
        // Keep equipment above the wet sand; four sides share the same distance to shore.
        float edge = (float)CityTraffic.LandEdge;
        x = Math.Clamp(x, -edge + 38, edge - 38);
        z = Math.Clamp(z, -edge + 38, edge - 38);

        if (CityWater.Depth(new CityPoint(x, z)) > .1) return;

        for (int k = -1; k <= 1; k++)
        {
            Vec3 at = new(x + k * 4, .03f, z + k % 2 * 3);
            int start = geometry.SurfacePoints.Count;
            geometry.Material = SurfaceMaterial.Paper;
            geometry.Box(at + new Vec3(0, .015f, 0), new Vec3(.65f, .015f, 1.1f), k == 0 ? new Vec3(.64f, .28f, .14f) : new Vec3(.23f, .49f, .54f));
            geometry.Material = SurfaceMaterial.BrushedAluminium;
            geometry.Tube(at, at + new Vec3(0, 2.15f, 0), .025f, new Vec3(.65f, .67f, .66f));

            for (int n = 0; n < 12; n++)
            {
                float a = n * MathF.Tau / 12, b = (n + 1) * MathF.Tau / 12;

                Vec3 c = at + new Vec3(0, 2.2f, 0),
                    p = at + new Vec3(MathF.Cos(a) * 1.4f, 1.85f, MathF.Sin(a) * 1.4f),
                    q = at + new Vec3(MathF.Cos(b) * 1.4f, 1.85f, MathF.Sin(b) * 1.4f);

                geometry.Material = SurfaceMaterial.Paper;
                geometry.Quad(c, p, q, c, n % 2 == 0 ? new Vec3(.85f, .65f, .25f) : new Vec3(.75f, .78f, .70f));
            }

            CityTerrainGeometry.Rigid(geometry, start, at.X, at.Z);
        }
    }
}
