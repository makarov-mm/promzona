using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Seeded botanical geometry, built once with the site. Real branch and
/// leaf silhouettes rather than solid canopy spheres. Scale is render-space
/// height; all vegetation is decorative and kept away from working envelopes.</summary>
public static class LivingLandscape
{
    public static void Tree(DioramaGeometry g, Vec3 foot, float height, int seed)
    {
        var random = new Random(seed);
        var bark = new Vec3(.26f, .20f, .13f);
        var crown = new Vec3(.22f, .34f, .105f);
        Vec3 trunk = foot + new Vec3(height * .035f, height * .57f, -height * .02f);
        Branch(foot, trunk, height * .033f);

        // Buttress roots, with a tapered tip in the soil.
        for (int k = 0; k < 5; k++)
        {
            float a = k * MathF.Tau / 5;
            SiteGeometry.Cone(g, foot + Vec3.UnitY * (height * .08f), foot + new Vec3(MathF.Cos(a) * height * .13f, .002f, MathF.Sin(a) * height * .13f), height * .025f, bark, 8);
        }

        for (int branch = 0; branch < 9; branch++)
        {
            float angle = branch * 2.39996f + R() * .35f;
            float level = .4f + branch * .045f;
            Vec3 start = Vec3.Lerp(foot, trunk, level / .57f);
            Vec3 radial = new(MathF.Cos(angle), 0, MathF.Sin(angle));
            Vec3 end = foot + radial * (height * (.23f + R() * .12f)) + Vec3.UnitY * (height * (level + .19f));
            Branch(start, end, height * (.009f + R() * .004f));

            for (int twig = 0; twig < 4; twig++)
            {
                Vec3 tip = end + Unit() * (height * .13f) + Vec3.UnitY * (height * .06f);
                Branch(end, tip, height * .0035f);

                // Hundreds of separate, softly folded, lanceolate leaves make
                // porous crowns. The seed and cost are independent of frames.
                for (int leaf = 0; leaf < 23; leaf++)
                {
                    Vec3 p = tip + Unit() * (height * (.06f + R() * .12f));
                    float size = height * (.041f + R() * .028f);
                    Vec3 along = Unit(), side = Vec3.Cross(along, Vec3.UnitY).Normalized();
                    if (side.Length() < .1f) side = Vec3.UnitX;
                    Vec3 n = Vec3.Cross(side, along).Normalized();
                    Vec3 a = p - along * size, b = p + side * (size * .5f), c = p + along * size, d = p - side * (size * .5f);
                    Vec3 ridge = p + n * (size * .12f);
                    float shade = .72f + .48f * R() + .12f * (p.Y - foot.Y) / height;
                    g.Material = SurfaceMaterial.Foliage;
                    g.SurfaceTriangle(a, b, ridge, n, n, n, crown * shade);
                    g.SurfaceTriangle(b, c, ridge, n, n, n, crown * shade);
                    g.SurfaceTriangle(c, d, ridge, n, n, n, crown * shade);
                    g.SurfaceTriangle(d, a, ridge, n, n, n, crown * shade);
                }
            }
        }

        return;

        float R() => (float)random.NextDouble();

        void Branch(Vec3 a, Vec3 b, float radius)
        {
            g.Material = SurfaceMaterial.Bark;
            g.Tube(a, b, radius, bark);
        }

        Vec3 Unit()
        {
            float a = R() * MathF.Tau, y = R() * 2 - 1, r = MathF.Sqrt(1 - y * y);
            return new Vec3(r * MathF.Cos(a), y, r * MathF.Sin(a));
        }
    }

    /// <summary>Small bent blade silhouettes scattered on an arbitrary terrain.
    /// The caller's mask excludes paths, concrete, water and apparatus.</summary>
    public static void Grass(DioramaGeometry g, float x0, float x1, float z0, float z1, int count, int seed, Func<float, float, float> height, Func<float, float, bool> allowed, float bladeHeight = .09f)
    {
        var random = new Random(seed);
        g.Material = SurfaceMaterial.GrassBlade;

        for (int i = 0; i < count; i++)
        {
            float x = x0 + (x1 - x0) * R(), z = z0 + (z1 - z0) * R();
            if (!allowed(x, z)) continue;
            Vec3 root = new(x, height(x, z) + .002f, z);
            float h = bladeHeight * (.45f + R()), a = R() * MathF.Tau, w = h * .09f;
            Vec3 side = new(MathF.Cos(a) * w, 0, MathF.Sin(a) * w);
            Vec3 tip = root + new Vec3(h * .26f, h, h * .12f), mid = root + new Vec3(h * .07f, h * .55f, h * .03f);
            Vec3 n = Vec3.Cross(side, tip - root).Normalized();
            Vec3 c = new Vec3(.29f, .40f, .13f) * (.68f + .52f * R());
            int first = g.SurfacePoints.Count;
            g.SurfaceTriangle(root - side, root + side, mid + side * .45f, n, n, n, c);
            g.SurfaceTriangle(root - side, mid + side * .45f, mid - side * .45f, n, n, n, c);
            g.SurfaceTriangle(mid - side * .45f, mid + side * .45f, tip, n, n, n, c * 1.10f);

            for (int j = first; j < g.SurfacePoints.Count; j++)
            {
                var v = g.SurfacePoints[j];
                g.SurfacePoints[j] = v with { Local = v.Position - root };
            }
        }

        return;

        float R() => (float)random.NextDouble();
    }

    public static void MeadowSite(DioramaGeometry g, string id)
    {
        // Only flat parts outside each module's physical and visual envelopes.
        if (id == "EngWindFarm")
        {
            Grass(g, -23, 23, -15, 13, 34000, 141, (x, z) => Meadow.Height(x, z), (x, z) => MathF.Abs(z - 1.25f) > .28f && (MathF.Abs(x) > 1.4f || z > 1.8f || z < -4.7f) && MathF.Abs(z) > .3f, .11f);

            for (int k = 0; k < 16; k++)
            {
                float x = -23 + k * 3.1f, z = -13.5f - 1.4f * MathF.Sin(k * 2.1f);
                Tree(g, new Vec3(x, Meadow.Height(x, z), z), .9f + .3f * (k % 3), 210 + k);
            }
        }
        else if (id is "EngCompressedAirStorage" or "EngCoolingTower" or "EngSolarFurnace" or "EngFerrisWheel" or "EngTelegraph")
        {
            float back = id switch
            {
                "EngSolarFurnace" => -30,
                "EngCoolingTower" => -12,
                _ => -10
            };

            for (int k = 0; k < 7; k++)
            {
                float x = -12 + k * 4, z = back - (k % 2) * 2;
                Tree(g, new Vec3(x, Meadow.Height(x, z), z), 1.2f + (k % 3) * .25f, 330 + k);
            }

            Grass(g, -18, 18, back - 5, back + 2, 7000, 42, (x, z) => Meadow.Height(x, z), (_, _) => true, .10f);
        }
    }
}
