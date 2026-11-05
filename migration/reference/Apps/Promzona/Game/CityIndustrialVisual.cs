using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Rendering of the industrial district: the halls, tanks, fences and
/// wagons of CityIndustrial.Obstacles, the gravel, the rails and the level
/// crossings. Kept apart from the layout data so the test project, which has no
/// renderer, can build the district.</summary>
internal static class CityIndustrialVisual
{
    private static Vec3 Ground(double x, double z, float y = 0) => new((float)x, (float)CityTerrain.Ground(x, z) + y, (float)z);

    /// <summary>Gravel instead of a lawn or a courtyard tree.</summary>
    public static void Ground(DioramaGeometry g, float x, float z)
    {
        g.Material = SurfaceMaterial.Stone;
        CityTerrainGeometry.Top(g, x, z, 8.9f, 8.9f, .16f, new(.33f, .32f, .29f));
    }

    public static void Block(DioramaGeometry g, int i, int j, int lod)
    {
        foreach (var (b, kind) in CityIndustrial.Obstacles(i, j)) Draw(g, b, kind, i + j, lod);
        if (j == CityIndustrial.RailRow) Rail(g, i, lod);
        if (j == CityIndustrial.RailRow - 1) Corridor(g, i, true);
    }

    private static void Draw(DioramaGeometry g, CityModel.Obstacle b, IndustrialKind kind, int salt, int lod)
    {
        int start = g.SurfacePoints.Count;
        float x = (float)b.X, z = (float)b.Z, hx = (float)b.HalfX, hz = (float)b.HalfZ, h = (float)b.Height;
        float y = (float)CityTerrain.Ground(b.X, b.Z);
        Vec3 c = new(x, y, z);
        switch (kind)
        {
            case IndustrialKind.Hall:
            {
                g.Material = SurfaceMaterial.Concrete;
                Vec3 wall = salt % 2 == 0 ? new(.62f, .60f, .54f) : new(.55f, .57f, .53f);
                g.Box(c + new Vec3(0, h * .5f, 0), new(hx, h * .5f, hz), wall);
                g.Material = SurfaceMaterial.CastMetal;
                g.Box(c + new Vec3(0, h + .25f, 0), new(hx + .25f, .25f, hz + .25f), new(.30f, .31f, .30f));
                // The gate and the clerestory strip on the road side.
                g.Material = SurfaceMaterial.Enamel;
                g.Box(c + new Vec3(-hx * .35f, 2.2f, -hz - .04f), new(2.4f, 2.2f, .05f), new(.18f, .22f, .26f));
                g.Material = SurfaceMaterial.ArchitecturalGlass;
                g.Box(c + new Vec3(0, h - 1.6f, -hz - .03f), new(hx - .8f, .55f, .03f), new(.35f, .45f, .5f));
                g.Box(c + new Vec3(0, h - 1.6f, hz + .03f), new(hx - .8f, .55f, .03f), new(.35f, .45f, .5f));
                if (lod == 0)
                {
                    g.Material = SurfaceMaterial.CastMetal;
                    for (int n = 1; n < 6; n++) g.Box(c + new Vec3(0, n * h / 6, -hz - .06f), new(hx, .015f, .02f), new(.4f, .4f, .38f));
                    g.Tube(c + new Vec3(hx - .6f, .2f, hz - .3f), c + new Vec3(hx - .6f, h + .4f, hz - .3f), .09f, new(.35f, .34f, .3f));
                }
                break;
            }
            case IndustrialKind.Chimney:
            {
                g.Material = SurfaceMaterial.Brick;
                g.Tube(c, c + new Vec3(0, h, 0), hx, new(.46f, .25f, .18f));
                g.Material = SurfaceMaterial.Enamel;
                g.Tube(c + new Vec3(0, h - 3, 0), c + new Vec3(0, h - 2, 0), hx + .03f, new(.9f, .9f, .86f));
                g.Tube(c + new Vec3(0, h - 1, 0), c + new Vec3(0, h, 0), hx + .03f, new(.75f, .2f, .15f));
                break;
            }
            case IndustrialKind.Tank:
            {
                g.Material = SurfaceMaterial.BrushedAluminium;
                g.Tube(c, c + new Vec3(0, h, 0), hx, new(.66f, .64f, .58f));
                g.Ellipsoid(c + new Vec3(0, h, 0), new(hx, .6f, hx), new(.6f, .58f, .52f));
                if (lod == 0)
                {
                    g.Material = SurfaceMaterial.CastMetal;
                    g.Tube(c + new Vec3(hx, .5f, 0), c + new Vec3(hx + .4f, .5f, 0), .12f, new(.32f, .3f, .28f));
                    g.Tube(c + new Vec3(hx, .5f, 0), c + new Vec3(hx, h - .4f, 0), .04f, new(.32f, .3f, .28f));
                }
                break;
            }
            case IndustrialKind.Shed:
            {
                g.Material = SurfaceMaterial.CastMetal;
                g.Box(c + new Vec3(0, h * .5f, 0), new(hx, h * .5f, hz), new(.44f, .50f, .48f));
                g.Box(c + new Vec3(0, h + .2f, 0), new(hx + .35f, .2f, hz + .35f), new(.26f, .27f, .27f));
                g.Material = SurfaceMaterial.Enamel;
                g.Box(c + new Vec3(0, 1.6f, -hz - .04f), new(Math.Min(2.2f, hx - .5f), 1.6f, .05f), new(.2f, .2f, .22f));
                if (hx > 6) CityArchitecture.Text(g, c + new Vec3(0, h - .6f, -hz - .08f), "СКЛАД", 4, new(.88f, .84f, .6f), new Vec3(-1, 0, 0));
                break;
            }
            case IndustrialKind.PumpHouse:
            {
                g.Material = SurfaceMaterial.Brick;
                g.Box(c + new Vec3(0, h * .5f, 0), new(hx, h * .5f, hz), new(.5f, .3f, .22f));
                g.Material = SurfaceMaterial.CastMetal;
                g.Tube(c + new Vec3(0, h * .6f, hz), c + new Vec3(0, h * .6f, hz + 5.5f), .14f, new(.35f, .35f, .33f));
                break;
            }
            case IndustrialKind.Fence:
            {
                g.Material = SurfaceMaterial.Concrete;
                g.Box(c + new Vec3(0, h * .5f, 0), new(hx, h * .5f, hz), new(.56f, .56f, .52f));
                if (lod == 0)
                    for (float px = -hx; px <= hx; px += 2.5f)
                        g.Box(c + new Vec3(px, h * .5f + .15f, 0), new(.14f, h * .5f + .15f, hz + .06f), new(.48f, .48f, .45f));
                break;
            }
            case IndustrialKind.Crate:
            {
                g.Material = SurfaceMaterial.Wood;
                g.Box(c + new Vec3(0, h * .5f, 0), new(hx, h * .5f, hz), new(.52f, .40f, .22f));
                break;
            }
            case IndustrialKind.Wagon:
            {
                if(CityImportedModels.Available.Contains("metro_2033_train"))break;
                g.Material = SurfaceMaterial.CastMetal;
                g.Box(c + new Vec3(0, .95f, 0), new(hx - .2f, .18f, hz - .15f), new(.18f, .18f, .17f));
                for (int n = -1; n <= 1; n += 2) for (int side = -1; side <= 1; side += 2)
                    g.Tube(c + new Vec3(n * (hx - 2.2f) - .12f, .45f, side * (hz - .35f)), c + new Vec3(n * (hx - 2.2f) + .12f, .45f, side * (hz - .35f)), .45f, new(.12f, .12f, .12f));
                g.Material = SurfaceMaterial.Enamel;
                Vec3 body = salt % 3 == 0 ? new(.45f, .22f, .16f) : salt % 3 == 1 ? new(.32f, .36f, .30f) : new(.40f, .30f, .20f);
                g.Box(c + new Vec3(0, 1.15f + (h - 1.15f) * .5f, 0), new(hx, (h - 1.15f) * .5f, hz), body);
                g.Box(c + new Vec3(0, h + .12f, 0), new(hx + .1f, .12f, hz + .1f), new(.22f, .2f, .18f));
                if (lod == 0)
                {
                    g.Material = SurfaceMaterial.CastMetal;
                    for (int n = 0; n < 7; n++) g.Box(c + new Vec3(-hx + .6f + n * (hx * 2 - 1.2f) / 6, 1.15f + (h - 1.15f) * .5f, -hz - .03f), new(.05f, (h - 1.15f) * .5f - .1f, .03f), new(.25f, .22f, .2f));
                    g.Box(c + new Vec3(0, 2.4f, -hz - .05f), new(1.2f, .9f, .03f), new(.14f, .12f, .11f));
                    for (int n = -1; n <= 1; n += 2) g.Tube(c + new Vec3(n * hx, .95f, 0), c + new Vec3(n * (hx + .4f), .95f, 0), .11f, new(.2f, .2f, .2f));
                }
                break;
            }
            case IndustrialKind.Buffer:
            {
                g.Material = SurfaceMaterial.CastMetal;
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Tube(c + new Vec3(-.3f, 0, side * .75f), c + new Vec3(-.3f, 1.25f, side * .75f), .08f, new(.28f, .28f, .26f));
                    g.Tube(c + new Vec3(.3f, 0, side * .75f), c + new Vec3(-.3f, 1.25f, side * .75f), .07f, new(.28f, .28f, .26f));
                }
                g.Box(c + new Vec3(-.3f, 1.0f, 0), new(.12f, .22f, 1.0f), new(.3f, .3f, .28f));
                g.Material = SurfaceMaterial.Enamel;
                g.Box(c + new Vec3(-.3f, 1.55f, 0), new(.03f, .3f, .9f), new(.8f, .12f, .1f));
                CityArchitecture.Text(g, c + new Vec3(-.34f, 1.65f, 0), "ТУПИК", 1.5f, new(.95f, .95f, .9f), new Vec3(0, 0, 1));
                break;
            }
        }
        // These positions already include the obstacle's terrain datum. The
        // district-wide conform pass must not add it a second time.
        CityTerrainGeometry.Absolute(g, start);
    }

    /// <summary>The gravel strip of the spur in both blocks beside it.</summary>
    private static void Corridor(DioramaGeometry g, int i, bool south)
    {
        double x0 = Math.Max(CityIndustrial.RailX0 - 4, CityTraffic.Coordinate(i)), x1 = Math.Min(CityIndustrial.RailX1 + 4, CityTraffic.Coordinate(i) + CityTraffic.Spacing);
        if (x1 <= x0) return;
        double zc = CityTraffic.Coordinate(CityIndustrial.RailRow);
        float hz = (float)(CityTraffic.RoadHalf * .5), z = (float)(south ? zc - CityTraffic.RoadHalf * .5 : zc + CityTraffic.RoadHalf * .5);
        g.Material = SurfaceMaterial.Stone;
        CityTerrainGeometry.Top(g, (float)((x0 + x1) * .5), z, (float)((x1 - x0) * .5), hz, .05f, new(.30f, .29f, .27f));
    }

    /// <summary>Two rails on sleepers following the ground, level crossings where streets cross the spur.</summary>
    private static void Rail(DioramaGeometry g, int i, int lod)
    {
        Corridor(g, i, false);
        int start = g.SurfacePoints.Count;
        double x0 = Math.Max(CityIndustrial.RailX0, CityTraffic.Coordinate(i)), x1 = Math.Min(CityIndustrial.RailX1, CityTraffic.Coordinate(i) + CityTraffic.Spacing);
        if (x1 <= x0) return;
        double step = lod == 0 ? 1.2 : lod == 1 ? 2.4 : 4.8;
        g.Material = SurfaceMaterial.Wood;
        for (double x = x0 + .6; x < x1; x += step)
            g.Box(Ground(x, CityIndustrial.RailZ, .09f), new(.12f, .07f, 1.25f), new(.30f, .24f, .17f));
        g.Material = SurfaceMaterial.MachinedMetal;
        for (double x = x0; x < x1; x += 3)
        {
            double xb = Math.Min(x1, x + 3);
            for (int side = -1; side <= 1; side += 2)
                g.Tube(Ground(x, CityIndustrial.RailZ + side * .76, .2f), Ground(xb, CityIndustrial.RailZ + side * .76, .2f), .045f, new(.55f, .55f, .52f));
        }
        if (lod >= 2) { CityTerrainGeometry.Absolute(g, start); return; }
        // Level crossing posts where a vertical street crosses the spur.
        double node = CityTraffic.Coordinate(i);
        if (CityTraffic.HasRoad(i, CityIndustrial.RailRow, 0) || CityTraffic.HasRoad(i, CityIndustrial.RailRow, 2))
            for (int side = -1; side <= 1; side += 2)
            {
                double px = node + side * (CityTraffic.RoadHalf + .6);
                for (int n = 0; n < 2; n++)
                {
                    double pz = CityIndustrial.RailZ + (n == 0 ? -3.2 : 3.2);
                    g.Material = SurfaceMaterial.Enamel;
                    for (int band = 0; band < 5; band++)
                        g.Box(Ground(px, pz, .3f + band * .5f + .25f), new(.07f, .25f, .07f), band % 2 == 0 ? new(.85f, .15f, .12f) : new(.92f, .92f, .88f));
                    g.Box(Ground(px, pz, 3.1f), new(.45f, .45f, .03f), new(.92f, .92f, .88f), new Vec3(1, 1, 0) * .7071f, new Vec3(-1, 1, 0) * .7071f, Vec3.UnitZ);
                    g.Box(Ground(px, pz, 3.1f), new(.4f, .06f, .035f), new(.85f, .15f, .12f), new Vec3(1, 1, 0) * .7071f, new Vec3(-1, 1, 0) * .7071f, Vec3.UnitZ);
                    g.Box(Ground(px, pz, 3.1f), new(.06f, .4f, .035f), new(.85f, .15f, .12f), new Vec3(1, 1, 0) * .7071f, new Vec3(-1, 1, 0) * .7071f, Vec3.UnitZ);
                }
            }
        // Corridor remains relative; sleepers, rails and crossing posts above
        // were explicitly sampled at their world terrain heights.
        CityTerrainGeometry.Absolute(g, start);
    }
}
