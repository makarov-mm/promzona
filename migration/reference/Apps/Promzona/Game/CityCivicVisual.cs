using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityCivicVisual
{
    public static void Building(DioramaGeometry g, CityModel.Obstacle b, int id, int lod, bool clinic)
    {
        if (clinic) return;
        float x = (float)b.X, z = (float)b.Z, h = (float)b.Height, hx = (float)b.HalfX, hz = (float)b.HalfZ;
        bool office = CityCivicSites.Office(b, id);
        if (CityCivicSites.BlankEnds(b) && (id % 7 == 0 || office))
        {
            int wall = 1 - CityCivicSites.WindowWall(b);
            float half = wall == 0 ? hx : hz, depth = wall == 0 ? hz : hx;
            float ph = Math.Min(h - 2.3f, Math.Min(5.2f, (half * 1.65f) * 1.5f)), pw = ph * 2 / 3;
            for (int side = office ? 1 : (id % 2 == 0 ? -1 : 1); side <= 1; side += 3)
            {
                Vec3 n = wall == 0 ? new(0, 0, side) : new(side, 0, 0), right = Vec3.Cross(Vec3.UnitY, n);
                // The +Z end may contain an entrance: leave canopy, plaque and notice board clear.
                float bottom = wall == 0 && side > 0 ? 4.1f : 1.6f;
                float height = Math.Min(ph, h - bottom - .3f), width = height * 2 / 3;
                Vec3 c = new Vec3(x, bottom + height * .5f, z) + n * (depth + .045f);
                int tile = 35 + (id / 3 + (side + 1) / 2) % 4;
                // Some ends retain the old commercial/satirical billboards; all use their native aspect.
                if (!office && id % 9 == 0 && side < 0) tile = (id / 9) % 10;
                CityArchitecture.Picture(g, c - right * width * .5f + Vec3.UnitY * height * .5f,
                    c + right * width * .5f + Vec3.UnitY * height * .5f,
                    c + right * width * .5f - Vec3.UnitY * height * .5f,
                    c - right * width * .5f - Vec3.UnitY * height * .5f, tile);
            }
        }

        if (!office || lod > 0) return;
        Vec3 p = new(x, 2.7f, z + hz + .17f);
        g.Material = SurfaceMaterial.Enamel;
        g.Box(p, new(Math.Min(hx - .2f, 2.6f), .38f, .06f), new(.30f, .065f, .055f));
        CityArchitecture.Text(g, p + new Vec3(0, .17f, .075f), "ВОЕНКОМАТ", Math.Min(hx * 1.7f, 4.8f),
            new(.91f, .82f, .59f));
        Vec3 board = new(x + Math.Min(hx - 1, 2.4f), 1.3f, z + hz + .09f);
        g.Material = SurfaceMaterial.CastMetal;
        g.Box(board, new(.55f, .67f, .045f), new(.16f, .18f, .16f));
        g.Material = SurfaceMaterial.Paper;
        for (int i = 0; i < 6; i++)
            g.Box(board + new Vec3((i % 2 - .5f) * .45f, (i / 2 - 1) * .39f, .052f), new(.18f, .15f, .008f),
                new(.75f, .72f, .58f));
        CityArchitecture.Text(g, board + new Vec3(0, .5f, .065f), "ПОВЕСТКИ", .85f, new(.18f, .15f, .12f));
    }

    public static void AirConditioner(DioramaGeometry g, Vec3 p, Vec3 right, Vec3 n, int lod)
    {
        // Under a window without a balcony. Unit width .76 m, height .43 m.
        Vec3 c = p - Vec3.UnitY * .87f + n * .23f;
        g.Material = SurfaceMaterial.Enamel;
        g.Box(c, new(.38f, .215f, .18f), new(.64f, .65f, .58f), right, Vec3.UnitY, n);
        if (lod != 0) return;
        g.Material = SurfaceMaterial.CastMetal;
        for (int i = 0; i < 7; i++)
            g.Tube(c + right * .12f + Vec3.UnitY * ((i - 3) * .045f) + n * .187f - right * .14f,
                c + right * .12f + Vec3.UnitY * ((i - 3) * .045f) + n * .187f + right * .14f, .008f,
                new(.25f, .29f, .28f));
        for (int s = -1; s <= 1; s += 2)
        {
            Vec3 a = c + right * (s * .27f) - Vec3.UnitY * .23f;
            g.Tube(a - n * .13f, a + n * .17f, .025f, new(.32f, .33f, .29f));
        }

        g.Material = SurfaceMaterial.Rubber;
        g.Tube(c - right * .34f, p - right * .5f - Vec3.UnitY * .5f + n * .08f, .025f, new(.55f, .53f, .44f));
    }
}
