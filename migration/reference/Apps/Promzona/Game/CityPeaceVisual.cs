using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Small bounded mission props; generated only for an active job and culled with the scene.</summary>
internal static class CityPeaceVisual
{
    public static void Draw(DioramaGeometry g,CityModel model,CityVisibility? visibility)
    {
        var t=model.Traffic;if(!t.PeaceJobActive)return;
        if(t.BroadcastCar is {Destroyed:false} car) Speaker(g,car,t.BroadcastAudible,model.Time,visibility);
        for(int i=0;i<t.PeaceSiteCount;i++)
        {
            var at=t.PeaceSite(i);bool done=i<t.PeaceSitesDone;
            if(!done&&i!=t.PeaceSitesDone||t.PeaceEscape&&!done)continue;
            Vec3 p=new((float)at.X,(float)CityTerrain.Ground(at),(float)at.Z);
            if((p-g.Eye).Length()>100||!(visibility?.Sphere(p+Vec3.UnitY,3)??true))continue;
            int start=g.SurfacePoints.Count;
            if(done&&t.JobKind==JobKind.Leaflets)
            {
                g.Material=SurfaceMaterial.Paper;
                for(int n=0;n<4;n++)
                {
                    var c=p+new Vec3(n*.08f-.1f,.241f+n*.005f,n*.03f);
                    g.Box(c,new(.15f,.003f,.21f),new(.88f,.87f,.76f));
                    g.Material=SurfaceMaterial.Enamel;
                    for(int line=0;line<4;line++)g.Box(c+new Vec3(0,.005f,-.12f+line*.07f),new(.095f,.001f,.008f),new(.13f,.24f,.19f));
                    g.Material=SurfaceMaterial.Paper;
                }
            }
            if(!done&&!t.PeaceEscape)
            {
                // Each endpoint follows the pavement height; a tilted street cannot bury the ring.
                g.Material=SurfaceMaterial.Emissive;Vec3 color=new(.45f,.85f,.6f);
                for(int n=0;n<48;n++)
                {
                    Vec3 Ring(double angle){var q=at+new CityPoint(Math.Sin(angle),Math.Cos(angle))*1.65;return new((float)q.X,(float)CityTerrain.Ground(q)+.30f,(float)q.Z);}
                    g.Rod(Ring(n*Math.Tau/48),Ring((n+.72)*Math.Tau/48),.025f,color);
                }
                float bob=.12f*MathF.Sin((float)model.Time*2);
                Vec3 tip=p+new Vec3(0,2.8f+bob,0);
                g.Rod(tip+new Vec3(-.25f,.35f,0),tip,.04f,color);g.Rod(tip+new Vec3(.25f,.35f,0),tip,.04f,color);
            }
            CityTerrainGeometry.Absolute(g,start);
        }
        g.Material=SurfaceMaterial.Automatic;
    }

    private static void Speaker(DioramaGeometry g,CityCar c,bool active,double time,CityVisibility? visibility)
    {
        var centre=new Vec3((float)c.Position.X,(float)(c.SurfaceHeight+c.Lift+c.Height),(float)c.Position.Z);
        if((g.Eye-centre).Length()>140||!(visibility?.Sphere(centre,3)??true))return;
        int start=g.SurfacePoints.Count;
        float roll=(float)(c.Roll+c.TerrainRoll),pitch=(float)(c.Pitch+c.TerrainPitch);
        float support=Math.Max(.7f,MathF.Abs(MathF.Sin(roll))*(float)c.HalfWidth+MathF.Abs(MathF.Sin(pitch))*(float)c.HalfLength+MathF.Abs(MathF.Cos(roll)*MathF.Cos(pitch))*.7f);
        var pose=Mat4.Translate(new((float)c.Position.X,(float)(c.SurfaceHeight+c.Lift)+support,(float)c.Position.Z))*Mat4.RotateY((float)c.Heading)*Mat4.RotateX(-pitch)*Mat4.RotateZ(-roll)*Mat4.Translate(new(0,-.7f,0));
        Vec3 P(float x,float y,float z)=>pose.TransformPoint(new(x,(float)c.Height+y,z));
        Vec3 right=(P(1,0,0)-P(0,0,0)).Normalized(),up=(P(0,1,0)-P(0,0,0)).Normalized(),forward=(P(0,0,1)-P(0,0,0)).Normalized();
        g.Material=SurfaceMaterial.CastMetal;
        foreach(float z in new[]{-.38f,.38f})g.Box(P(0,.045f,z),new(.59f,.045f,.045f),new(.16f,.19f,.18f),right,up,forward);
        for(int side=-1;side<=1;side+=2)
        {
            float x=side*.31f;
            g.Tube(P(x,.08f,0),P(x,.32f,0),.035f,new(.3f,.33f,.31f));
            g.Material=SurfaceMaterial.Enamel;
            // A horn with an actual flared opening, not a cylinder cap.
            for(int n=0;n<16;n++)
            {
                double a=n*Math.Tau/16,b=(n+1)*Math.Tau/16;
                Vec3 Rim(double angle,float radius,float z)=>P(x+MathF.Cos((float)angle)*radius,.34f+MathF.Sin((float)angle)*radius,z);
                g.Quad(Rim(a,.10f,-.23f),Rim(b,.10f,-.23f),Rim(b,.24f,.27f),Rim(a,.24f,.27f),new(.72f,.75f,.68f));
                g.Quad(Rim(a,.22f,.271f),Rim(b,.22f,.271f),Rim(b,.07f,-.12f),Rim(a,.07f,-.12f),new(.13f,.17f,.16f));
            }
            g.Tube(P(x,.34f,-.23f),P(x,.34f,-.12f),.095f,new(.1f,.13f,.12f));
        }
        g.Material=SurfaceMaterial.Enamel;
        g.Box(P(0,.17f,-.36f),new(.58f,.13f,.025f),new(.83f,.82f,.7f),right,up,forward);
        CityArchitecture.Text(g,P(0,.17f,-.39f),"НЕТ ВОЙНЕ",1.02f,new(.08f,.20f,.13f),-right,up);
        g.Material=active?SurfaceMaterial.Emissive:SurfaceMaterial.Polymer;
        g.Box(P(.51f,.17f,-.395f),new(.025f,.025f,.008f),active?new(.4f,1,.45f):new(.12f,.21f,.13f),right,up,forward);
        CityTerrainGeometry.Absolute(g,start);g.Material=SurfaceMaterial.Automatic;
    }
}
