using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityAmenitiesVisual
{
    // The GLBs provide the detailed models. Keep the missing-asset silhouette
    // recognisable from its service face, using the same dimensions as the GLB.
    private static void DrawFoodFallback(DioramaGeometry g, FoodSite food)
    {
        var fixture = food.Fixture;
        var half = new Vec3(fixture.Width / 2, fixture.Height / 2, fixture.Depth / 2);
        var centre = new Vec3((float)food.Position.X, .24f + half.Y, (float)food.Position.Z);
        var face = centre + new Vec3(0, 0, half.Z);
        int start = g.SurfacePoints.Count;
        g.Material = SurfaceMaterial.Enamel;
        g.Box(centre, half, food.Shawarma ? new(.31f, .36f, .31f) : new(.55f, .12f, .065f));
        g.Material = SurfaceMaterial.Polymer;
        g.Box(face, new(half.X * .72f, half.Y * .42f, .02f), new(.065f, .10f, .10f));
        if (food.Shawarma)
        {
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Box(face - new Vec3(0, half.Y * .42f, 0), new(half.X * .78f, .045f, .15f), new(.64f, .64f, .56f));
        }
        CityTerrainGeometry.Rigid(g, start, food.Position.X, food.Position.Z);
    }

    private static void DrawGarage(DioramaGeometry g, GarageSite garage, int lod)
    {
        const float halfWidth = (float)GarageSite.HalfX, halfDepth = (float)GarageSite.HalfZ;
        const float wallHeight = (float)GarageSite.WallHeight, roofHalfHeight = .1f;
        const float doorHalfWidth = 2.3f, doorHalfHeight = 1.45f, sill = .1f;
        const float doorTop = sill + 2 * doorHalfHeight;
        var basePoint = new Vec3((float)garage.Position.X, .2f, (float)garage.Position.Z);
        int start = g.SurfacePoints.Count;

        g.Material = SurfaceMaterial.Concrete;
        g.Box(basePoint + new Vec3(0, wallHeight / 2, 0), new(halfWidth, wallHeight / 2, halfDepth), new(.52f, .50f, .44f));
        g.Material = SurfaceMaterial.CastMetal;
        g.Box(basePoint + new Vec3(0, wallHeight + roofHalfHeight, 0), new(halfWidth + .15f, roofHalfHeight, halfDepth + .15f), new(.30f, .30f, .28f));
        g.Box(Front(sill + doorHalfHeight, .03f), new(doorHalfWidth, doorHalfHeight, .04f), new(.36f, .40f, .38f));
        if (lod == 0)
        {
            for (int slat = 0; slat < 6; slat++)
                g.Box(Front(.35f + slat * .45f, .06f), new(doorHalfWidth, .012f, .02f), new(.22f, .24f, .23f));

            g.Material = SurfaceMaterial.Rubber;
            const float tyreThickness = .28f;
            for (int tyre = 1; tyre <= 3; tyre++)
            {
                var hub = basePoint + new Vec3(halfWidth + .55f, tyre * tyreThickness, -halfDepth + 1.2f);
                g.Tube(hub, hub + new Vec3(0, .001f, 0), .32f, new(.08f, .08f, .08f));
            }
        }

        g.Material = SurfaceMaterial.Enamel;
        g.Box(Front(doorTop + .1f, .05f), new(2.6f, .3f, .03f), new(.14f, .18f, .30f));
        CityArchitecture.Text(g, Front(doorTop + .2f, .09f), "АВТОСЕРВИС", doorHalfWidth * 2, new(.92f, .86f, .55f), new Vec3(-1, 0, 0));
        CityTerrainGeometry.Rigid(g, start, garage.Position.X, garage.Position.Z);

        Vec3 Front(float height, float outward) => basePoint + new Vec3(0, height, -halfDepth - outward);
    }

    private static void DrawMarketStall(DioramaGeometry g, MarketSite stall, int lod)
    {
        const float halfWidth = (float)MarketSite.HalfX, halfDepth = (float)MarketSite.HalfZ;
        const float counterHeight = .85f, counterHalfThickness = .06f, parcelHalfHeight = .12f;
        const float postHeight = 2.35f, canopyHalfThickness = .03f;
        const int stripeCount = 6;
        const float stripeWidth = (2 * halfWidth - .2f) / stripeCount;
        var basePoint = new Vec3((float)stall.Position.X, .2f, (float)stall.Position.Z);
        int start = g.SurfacePoints.Count;

        g.Material = SurfaceMaterial.Wood;
        g.Box(basePoint + new Vec3(0, counterHeight, 0), new(halfWidth, counterHalfThickness, halfDepth), new(.45f, .32f, .18f));
        g.Box(basePoint + new Vec3(0, .42f, -halfDepth + .1f), new(halfWidth, .42f, .05f), new(.38f, .27f, .15f));
        g.Material = SurfaceMaterial.CastMetal;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int end = -1; end <= 1; end += 2)
            {
                var foot = basePoint + new Vec3(side * (halfWidth - .08f), 0, end * (halfDepth - .08f));
                g.Tube(foot, foot + new Vec3(0, postHeight, 0), .025f, new(.3f, .3f, .3f));
            }
        }

        g.Material = SurfaceMaterial.Polymer;
        Vec3 stripe = stall.Id % 2 == 0 ? new(.85f, .25f, .2f) : new(.2f, .35f, .75f);
        for (int n = 0; n < stripeCount; n++)
        {
            float x = -halfWidth + .1f + (n + .5f) * stripeWidth;
            g.Box(basePoint + new Vec3(x, postHeight + canopyHalfThickness, 0), new(stripeWidth / 2, canopyHalfThickness, halfDepth + .25f),
                  n % 2 == 0 ? stripe : new(.92f, .92f, .88f));
        }
        if (lod == 0)
        {
            g.Material = SurfaceMaterial.Paper;
            for (int n = 0; n < 4; n++)
                g.Box(basePoint + new Vec3(-halfWidth + .45f + n * .7f, counterHeight + counterHalfThickness + parcelHalfHeight, .15f),
                      new(.22f, parcelHalfHeight, .18f), new(.62f + n * .07f, .55f, .38f));
        }
        CityTerrainGeometry.Rigid(g, start, stall.Position.X, stall.Position.Z);
    }

    public static void Block(DioramaGeometry g, int i, int j, int lod)
    {
        double x = CityTraffic.Coordinate(i), z = CityTraffic.Coordinate(j);
        foreach (var food in CityAmenities.Food())
        {
            if (CityTraffic.InBlock(food.Position, x, z) && !CityImportedModels.Available.Contains(food.Fixture.Model))
                DrawFoodFallback(g, food);
        }

        foreach (var garage in CityAmenities.Garages())
        {
            if (CityTraffic.InBlock(garage.Position, x, z)) DrawGarage(g, garage, lod);
        }

        foreach (var stall in CityAmenities.Markets())
        {
            if (CityTraffic.InBlock(stall.Position, x, z)) DrawMarketStall(g, stall, lod);
        }

        if (!CityImportedModels.Available.Contains(BusStopFixture.Model))
        {
            foreach (var sign in CityAmenities.Stops())
                if (CityTraffic.InBlock(sign, x - CityTraffic.Spacing / 2, z)) DrawStopFallback(g, sign);
        }
    }

    private static void DrawStopFallback(DioramaGeometry g, CityPoint sign)
    {
        int start = g.SurfacePoints.Count;
        var seat = BusStopFixture.Bench(BusStopFixture.Centre(sign));
        const float pavement = .15f, seatHalfDepth = .40f, seatHalfHeight = .07f, seatTop = .79f;
        const float seatHalfLength = BusStopFixture.Width / 2 - .025f;
        float seatCentre = seatTop - seatHalfHeight;
        var pole = new Vec3((float)sign.X, pavement, (float)sign.Z);
        var bench = new Vec3((float)seat.X, pavement, (float)seat.Z);

        g.Material = SurfaceMaterial.Enamel;
        g.Tube(pole, pole + new Vec3(0, 3.15f, 0), .055f, new(.4f, .48f, .5f));
        var panel = pole + new Vec3(0, 2.9f, 0);
        g.Box(panel, new(.045f, .35f, .28f), new(.1f, .27f, .60f));
        g.Box(panel + new Vec3(.05f, .02f, 0), new(.015f, .15f, .19f), new(.9f, .9f, .82f));

        g.Material = SurfaceMaterial.Wood;
        g.Box(bench + new Vec3(0, seatCentre, 0), new(seatHalfDepth, seatHalfHeight, seatHalfLength), new(.3f, .20f, .105f));
        g.Box(bench + new Vec3(-seatHalfDepth + .04f, seatTop + .33f, 0), new(.05f, .32f, seatHalfLength), new(.3f, .20f, .105f));
        g.Material = SurfaceMaterial.CastMetal;
        for (int side = -1; side <= 1; side += 2)
            g.Box(bench + new Vec3(0, seatCentre / 2, side), new(.3f, seatCentre / 2, .06f), new(.16f, .20f, .18f));

        CityTerrainGeometry.Rigid(g, start, sign.X, sign.Z);
    }

    private static Vec3 Ground(CityPoint p, float y = .3f) => new((float)p.X, (float)CityTerrain.Ground(p) + y, (float)p.Z);

    public static void Dynamic(DioramaGeometry g, CityModel m, CityVisibility? visibility)
    {
        CityTraffic t = m.Traffic;
        g.Material = SurfaceMaterial.Emissive;

        foreach (FoodSite s in t.FoodSites)
        {
            CityPoint p = s.Interaction;
            if (!Visible(p)) continue;
            Vec3 color = s.Shawarma ? new Vec3(1, .62f, .18f) : new Vec3(.45f, .85f, .3f);

            for (int n = 0; n < 32; n++)
            {
                double a = n * Math.Tau / 32, b = (n + 1) * Math.Tau / 32;
                g.Tube(Ground(p + new CityPoint(Math.Cos(a), Math.Sin(a)) * .85), Ground(p + new CityPoint(Math.Cos(b), Math.Sin(b)) * .85), .025f, color);
            }
        }

        foreach (var (i, j) in CityTraffic.ClinicBlocks)
            DrawEntrance(CityAmenities.ClinicEntrance(i, j), new(.15f, .52f, 1));
        DrawEntrance(CityAmenities.StationEntrance(), new(.42f, .48f, .62f));

        void DrawEntrance(CityPoint p, Vec3 color)
        {
            if (!Visible(p)) return;
            const double half = CityAmenities.ClinicHalf;
            var a = Ground(new(p.X - half, p.Z - half));
            var b = Ground(new(p.X + half, p.Z - half));
            var c = Ground(new(p.X + half, p.Z + half));
            var d = Ground(new(p.X - half, p.Z + half));
            g.Tube(a, b, .04f, color);
            g.Tube(b, c, .04f, color);
            g.Tube(c, d, .04f, color);
            g.Tube(d, a, .04f, color);
        }

        bool Visible(CityPoint p, float radius = 3) => (Ground(p) - g.Eye).Length() < 115 && (visibility?.Sphere(Ground(p), radius) ?? true);
    }
}
