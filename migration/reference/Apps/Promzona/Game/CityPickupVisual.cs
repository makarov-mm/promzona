using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
namespace Promzona.Game;

internal static class CityPickupVisual
{
    public static void Gun(DioramaGeometry g, CityWeaponId id, Vec3 o, Vec3 r, Vec3 f, float scale = 1, Vec3? vertical=null)
    {
        Vec3 up = vertical??Vec3.UnitY;
        Vec3 P(float x, float y, float z) => o + (r * x + up * y + f * z) * scale;

        void Box(float x, float y, float z, Vec3 size, Vec3 colour, SurfaceMaterial material)
        {
            g.Material = material;
            g.Box(P(x, y, z), size * scale, colour, r, up, f);
        }

        var metal = new Vec3(.16f, .18f, .19f);
        var dark = new Vec3(.08f, .09f, .085f);
        var wood = new Vec3(.34f, .16f, .07f);

        if(id==CityWeaponId.None)return;
        if(id==CityWeaponId.Flamethrower){
            g.Material=SurfaceMaterial.Steel;g.Tube(P(0,.05f,-.30f),P(0,.05f,.57f),.035f*scale,metal);
            g.Material=SurfaceMaterial.Enamel;g.Tube(P(0,-.15f,-.30f),P(0,.16f,-.30f),.10f*scale,new(.43f,.29f,.09f));
            Box(0,-.06f,.05f,new(.04f,.10f,.055f),dark,SurfaceMaterial.Polymer);
            g.Material=SurfaceMaterial.CastMetal;g.Tube(P(0,.05f,.45f),P(0,.05f,.64f),.055f*scale,dark);
        }
        else if(id==CityWeaponId.Bazooka){
            g.Material=SurfaceMaterial.Enamel;g.Tube(P(0,.08f,-.5f),P(0,.08f,.48f),.072f*scale,new(.23f,.28f,.14f));
            g.Material=SurfaceMaterial.Steel;g.Tube(P(0,.08f,-.57f),P(0,.08f,-.44f),.092f*scale,metal);
            g.Tube(P(0,.08f,.43f),P(0,.08f,.59f),.095f*scale,new(.34f,.36f,.18f));
            g.Ellipsoid(P(0,.08f,.65f),new Vec3(.085f,.085f,.19f)*scale,new(.34f,.36f,.18f),r,up,f);
            Box(0,-.08f,.02f,new(.033f,.13f,.05f),wood,SurfaceMaterial.Wood);
            Box(0,-.065f,-.24f,new(.035f,.11f,.05f),wood,SurfaceMaterial.Wood);
            Box(.09f,.14f,.1f,new(.025f,.07f,.13f),dark,SurfaceMaterial.CastMetal);
        }
        else if (id == CityWeaponId.Automatic)
        {
            Box(0, .04f, .08f, new(.045f, .055f, .21f), metal, SurfaceMaterial.CastMetal);
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Tube(P(0, .04f, .25f), P(0, .04f, .52f), .021f * scale, metal);
            Box(0, .025f, .24f, new(.05f, .045f, .10f), wood, SurfaceMaterial.Wood);
            Box(0, -.015f, -.24f, new(.046f, .065f, .12f), wood, SurfaceMaterial.Wood);
            Box(0, -.10f, -.06f, new(.033f, .10f, .045f), dark, SurfaceMaterial.Polymer);
            Box(0, -.12f, .12f, new(.035f, .12f, .053f), metal, SurfaceMaterial.CastMetal);
            Box(0, .108f, .30f, new(.012f, .023f, .012f), dark, SurfaceMaterial.CastMetal);
        }
        else
        {
            Box(0, .05f, .055f, new(.031f, .036f, .13f), metal, SurfaceMaterial.CastMetal);
            Box(0, -.035f, -.015f, new(.032f, .085f, .044f), dark, SurfaceMaterial.Polymer);
            Box(0, .092f, .15f, new(.008f, .01f, .01f), dark, SurfaceMaterial.CastMetal);
        }
    }

    public static void Draw(DioramaGeometry g, CityModel m, CityVisibility? visibility)
    {
        foreach (var p in m.Traffic.Pickups)
        {
            var centre = new Vec3((float)p.Position.X, .8f + (float)CityRoadLevels.Surface(p.Position.X,p.Position.Z,p.RoadLevel),
                (float)p.Position.Z);
            if ((g.Eye - centre).Length() > 135 || !(visibility?.Sphere(centre, 2) ?? true)) continue;
            float angle = (float)(m.Time * .8 + p.Id), bob = .07f * MathF.Sin((float)m.Time * 2 + p.Id);
            var r = new Vec3(MathF.Cos(angle), 0, -MathF.Sin(angle));
            var f = new Vec3(MathF.Sin(angle), 0, MathF.Cos(angle));
            var color = p.Kind == CityPickupKind.EnergyDrink ? new Vec3(.5f,1,.2f) : p.Kind == CityPickupKind.Pills ? new Vec3(.15f, .8f, .48f) : new Vec3(.9f, .64f, .18f);
            g.Material = SurfaceMaterial.Emissive;
            for (int n = 0; n < 24; n++)
            {
                float a = n * MathF.Tau / 24, b = (n + 1) * MathF.Tau / 24;
                var o = new Vec3(centre.X, centre.Y - .58f, centre.Z);
                g.Tube(o + new Vec3(MathF.Cos(a) * .70f, 0, MathF.Sin(a) * .70f),
                    o + new Vec3(MathF.Cos(b) * .70f, 0, MathF.Sin(b) * .70f), .018f, color);
            }

            if (p.Kind == CityPickupKind.Weapon)
            {
                Gun(g, p.Weapon, centre + new Vec3(0, bob, 0), r, f, p.Weapon == CityWeaponId.Pistol ? 2 : 1.4f);
                continue;
            }

            var at = centre + new Vec3(0, bob, 0);
            if(p.Kind==CityPickupKind.EnergyDrink){
                g.Material=SurfaceMaterial.BrushedAluminium;g.Tube(at-new Vec3(0,.22f,0),at+new Vec3(0,.22f,0),.13f,new(.7f,.75f,.72f));
                g.Material=SurfaceMaterial.Enamel;g.Tube(at-new Vec3(0,.17f,0),at+new Vec3(0,.17f,0),.134f,new(.25f,.65f,.08f));
                g.Material=SurfaceMaterial.Emissive;g.Tube(at+r*.14f-Vec3.UnitY*.09f,at+r*.14f+Vec3.UnitY*.09f,.026f,new(.9f,1,.5f));continue;
            }
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Box(at, new(.30f, .035f, .19f), new(.7f, .75f, .75f), r, Vec3.UnitY, f);
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z += 2)
            {
                g.Material = SurfaceMaterial.Enamel;
                var pos = at + r * (x * .18f) + f * (z * .085f) + Vec3.UnitY * .065f;
                g.Ellipsoid(pos, new(.055f, .045f, .055f), x == 0 ? new(.16f, .64f, .45f) : new(.94f, .90f, .79f));
            }
        }
    }

}
