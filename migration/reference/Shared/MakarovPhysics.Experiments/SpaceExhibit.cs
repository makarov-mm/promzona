using System.Numerics;
using MakarovPhysics.Renderer;
using static MakarovPhysics.Experiments.CosmosScene;
using static MakarovPhysics.Experiments.SpaceHardware;
namespace MakarovPhysics.Experiments;

/// <summary>Opt-in engineering exhibits. Decorative fasteners and blankets have
/// no dynamics; every moving assembly receives its pose from the experiment.</summary>
public static class SpaceExhibit
{
    public static void Backdrop(CosmosScene scene, Vec3 eye, double time, float stars, bool earth=true, bool lunar=false, bool eclipse=false)
    {
        scene.HardwareShadows=true;
        scene.HardwareSun=new Vec3(-.58f,.63f,.52f).Normalized();
        scene.HardwareAmbient=eclipse?.13f:.30f; scene.HardwareFill=eclipse?.25f:.75f;
        scene.StarRadius=.28f;
        for(int i=0;i<(int)(4200*stars);i++)
        {
            var d=Direction(i,914);
            scene.Star(eye+d*1150,new Vec3(.72f,.81f,1)*(float)(.22+.70*Noise(i,917)));
        }
        if(!earth)return;
        // A celestial background anchored to viewing direction, not a nearby
        // prop. Lunar Earth subtends ~1.9 degrees; orbital view is illustrative.
        var direction=(lunar?new Vec3(-.62f,.045f,-.78f):new Vec3(.10f,-.70f,-.71f)).Normalized();
        scene.Body(eye+direction*980,lunar?16.24f:410,CosmicSurface.Ocean,new(.40f,.62f,.98f),
            .7+time*.0004,1.4,scene.HardwareSun,irradiance:eclipse?.13f:1);
    }

    public static void BoltCircle(SpaceHardware h,Vec3 p,Vec3 axis,float radius,int n,float size=.013f)
    {
        var u=Vec3.Cross(axis,MathF.Abs(axis.Y)>.9f?Vec3.UnitZ:Vec3.UnitY).Normalized();var v=Vec3.Cross(axis,u);
        for(int i=0;i<n;i++){float a=i*MathF.Tau/n;h.Ball(p+(u*MathF.Cos(a)+v*MathF.Sin(a))*radius,size,Metal);}
    }
    public static void Hatch(SpaceHardware h, Vec3 p, float radius)
    {
        h.Tube(p-new Vec3(0,0,.035f),p+new Vec3(0,0,.018f),radius,White,SurfaceMaterial.Ceramic);
        h.Ring(p,Vec3.UnitZ,radius*.83f,radius,.045f,Metal);
        h.Ring(p+new Vec3(0,0,.031f),Vec3.UnitZ,radius*.72f,radius*.77f,.012f,Dark);
        BoltCircle(h,p+new Vec3(0,0,.030f),Vec3.UnitZ,radius*.91f,12);
        h.Box(p+new Vec3(0,0,.054f),new(radius*.4f,.026f,.025f),Dark,SurfaceMaterial.Rubber);
    }
    public static void Blanket(SpaceHardware h,Vec3 p,float halfX,float halfY)
    {
        var g=h.G;g.Material=SurfaceMaterial.Foil;
        Vec3 Point(float u,float v)
        {
            float wrinkle=.006f*MathF.Sin(u*24+MathF.Sin(v*17))+.004f*MathF.Sin(v*38+u*11);
            float edge=MathF.Sin(u*MathF.PI)*MathF.Sin(v*MathF.PI);
            return h.At(p+new Vec3((u*2-1)*halfX,(v*2-1)*halfY,edge*(.014f+wrinkle)));
        }
        Vec3 Normal(float u,float v)=>Vec3.Cross(Point(u+.001f,v)-Point(u-.001f,v),Point(u,v+.001f)-Point(u,v-.001f)).Normalized();
        for(int i=0;i<36;i++)for(int j=0;j<28;j++)
        {
            float u=i/36f,v=j/28f,du=1/36f,dv=1/28f;
            var a=Point(u,v);var b=Point(u+du,v);var c=Point(u+du,v+dv);var d=Point(u,v+dv);
            var na=Normal(u,v);var nb=Normal(u+du,v);var nc=Normal(u+du,v+dv);var nd=Normal(u,v+dv);
            g.SurfaceTriangle(a,b,c,na,nb,nc,Gold);g.SurfaceTriangle(a,c,d,na,nc,nd,Gold);
        }
        for(int i=-1;i<=1;i+=2)
        {
            h.Tube(p+new Vec3(i*halfX,-halfY,.006f),p+new Vec3(i*halfX,halfY,.006f),.007f,Dark,SurfaceMaterial.Rubber);
            h.Tube(p+new Vec3(-halfX,i*halfY,.006f),p+new Vec3(halfX,i*halfY,.006f),.007f,Dark,SurfaceMaterial.Rubber);
        }
    }
    public static void Bus(SpaceHardware h, bool instruments=true)
    {
        h.FoilBox(Vec3.Zero,new(.43f,.34f,.47f));
        Blanket(h,new(0,0,.473f),.415f,.325f);
        // Overlapping blanket sections, seams, silver attachment tabs.
        for(int s=-1;s<=1;s+=2)
        {
            for(int j=0;j<3;j++)
            {
                h.Box(new(s*.436f,-.22f+j*.22f,0),new(.009f,.10f,.405f),j==1?Gold*.86f:Gold,SurfaceMaterial.Foil);
                for(int z=-1;z<=1;z+=2)h.Box(new(s*.449f,-.22f+j*.22f,z*.39f),new(.011f,.025f,.024f),White,SurfaceMaterial.Steel);
            }
            h.Box(new(s*.25f,-.353f,0),new(.12f,.014f,.39f),Dark,SurfaceMaterial.Rubber);
        }
        h.Box(new(0,.36f,0),new(.44f,.025f,.48f),White,SurfaceMaterial.Ceramic);
        for(int j=0;j<11;j++)h.Tube(new(-.37f+j*.074f,.390f,-.35f),new(-.37f+j*.074f,.390f,.10f),.008f,Metal);
        if(instruments)
        {
            h.Dish(new(.14f,.43f,.12f),.24f);
            h.Box(new(-.24f,.45f,-.25f),new(.095f,.075f,.115f),Dark);
            h.Tube(new(-.24f,.45f,-.25f),new(-.24f,.45f,-.44f),.065f,Metal);
            h.Tube(new(-.24f,.45f,-.435f),new(-.24f,.45f,-.452f),.050f,Blue,SurfaceMaterial.OpticalMirror);
            h.Tube(new(-.31f,.35f,.35f),new(-.31f,.91f,.35f),.009f,Metal);
            h.Ball(new(-.31f,.92f,.35f),.017f,Orange);
        }
        for(int s=-1;s<=1;s+=2)for(int z=-1;z<=1;z+=2)
        {
            var p=new Vec3(s*.41f,-.24f,z*.43f);
            h.Box(p,new(.065f,.065f,.065f),Dark);
            h.Tube(p,p+new Vec3(s*.13f,0,0),.026f,Metal);
            h.Ring(p+new Vec3(s*.14f,0,0),Vec3.UnitX,.012f,.031f,.018f,Metal);
        }
    }
    public static void SolarWing(SpaceHardware h, float length=1.6f, float halfWidth=.55f)
    {
        // Local X is the hinge axis's outward direction; three rigid leaves.
        for(int part=0;part<3;part++)
        {
            float span=length/3,x=(part+.5f)*span;
            h.Panel(new(x,0,0),span*.48f,halfWidth);
            for(int s=-1;s<=1;s+=2)
            {
                h.Tube(new(part*span,.012f,s*halfWidth),new((part+1)*span,.012f,s*halfWidth),.013f,Metal);
                h.Tube(new(part*span,-.044f,s*halfWidth),new((part+1)*span,-.044f,-s*halfWidth),.007f,Metal);
            }
            for(int k=0;k<12;k++)h.Tube(new(x-span*.44f,.047f,(k-5.5f)*halfWidth/6),new(x+span*.44f,.047f,(k-5.5f)*halfWidth/6),.0018f,new(.20f,.36f,.62f));
            h.Tube(new(part*span,0,-halfWidth*.75f),new(part*span,0,halfWidth*.75f),.025f,Dark);
        }
    }
    public static void Joint(CosmosScene s, Vec3 p, float radius=.12f)
    {
        var h=new SpaceHardware(s,p,Quaternion.Identity);
        h.Tube(new(0,0,-radius*.9f),new(0,0,radius*.9f),radius,White,SurfaceMaterial.Ceramic);
        h.Ring(new(0,0,radius*.92f),Vec3.UnitZ,radius*.4f,radius*.92f,.024f,Metal);
        h.Tube(new(0,0,radius*.94f),new(0,0,radius*1.10f),radius*.38f,Dark);
        BoltCircle(h,new(0,0,radius*1.05f),Vec3.UnitZ,radius*.73f,8,.007f);
    }
    public static void ArmBeam(CosmosScene s,Vec3 a,Vec3 b,float r)
    {
        var g=s.Geometry;g.Material=SurfaceMaterial.Ceramic;g.Tube(a,b,r,White);
        var d=(b-a).Normalized();var side=Vec3.Cross(d,Vec3.UnitZ).Normalized();
        g.Material=SurfaceMaterial.Steel;
        for(int i=1;i<=4;i++){float t=i/5f;g.Tube(a+(b-a)*(t-.018f),a+(b-a)*(t+.018f),r*1.08f,Metal);}
        g.Material=SurfaceMaterial.Rubber;g.Tube(a+side*r*1.13f,b+side*r*1.13f,.009f,Dark);
    }
    public static void Station(SpaceHardware h, bool compact=false)
    {
        float l=compact?1.15f:1.7f;
        h.Tube(new(-l,0,0),new(l,0,0),.54f,White,SurfaceMaterial.Ceramic);
        for(int j=-3;j<=3;j++)h.Ring(new(j*l/3,0,0),Vec3.UnitX,.529f,.558f,.035f,Metal);
        for(int j=-2;j<=2;j++)
        {
            h.Box(new(j*l/3,.515f,0),new(.18f,.014f,.17f),new(.63f,.68f,.73f),SurfaceMaterial.Steel);
            for(int s=-1;s<=1;s+=2)h.Tube(new(j*l/3-.13f,.42f,s*.34f),new(j*l/3+.13f,.42f,s*.34f),.012f,Gold);
        }
        h.Tube(new(0,0,.38f),new(0,0,.78f),.31f,White);
        h.Ring(new(0,0,.80f),Vec3.UnitZ,.215f,.32f,.12f,Metal);
        h.Ring(new(0,0,.865f),Vec3.UnitZ,.213f,.238f,.025f,Dark);
        BoltCircle(h,new(0,0,.88f),Vec3.UnitZ,.28f,12,.014f);
        for(int s=-1;s<=1;s+=2)
        {
            h.Tube(new(s*l,0,0),new(s*(l+1.6f),0,0),.055f,Metal);
            for(int k=0;k<5;k++)
            {
                float x=s*(l+.30f*k);
                for(int z=-1;z<=1;z+=2)
                {
                    h.Tube(new(x,-.16f,z*.15f),new(x+s*.3f,.16f,z*.15f),.014f,Metal);
                    h.Tube(new(x,.16f,z*.15f),new(x+s*.3f,-.16f,z*.15f),.014f,Metal);
                }
            }
            h.Panel(new(s*(l+.95f),0,0),.68f,1.40f);
            h.Box(new(s*(l+.30f),.22f,-.62f),new(.20f,.023f,.64f),White,SurfaceMaterial.Ceramic);
        }
        h.Dish(new(-l*.65f,.6f,0),.25f);
    }
    public static void Lander(SpaceHardware h,double throttle,double time)
    {
        h.Lander(throttle,time);
        Blanket(h,new(0,-.05f,.484f),.46f,.25f);
        // An ascent cabin, faceted thermal panels, pressure bottles, harness,
        // descent ladder and instrument optics distinguish an actual vehicle.
        for(int s=-1;s<=1;s+=2)
        {
            h.Box(new(s*.487f,-.05f,0),new(.012f,.23f,.40f),Gold*.8f,SurfaceMaterial.Foil);
            h.Tube(new(s*.38f,.29f,-.26f),new(s*.38f,.58f,-.26f),.09f,White);
            h.Ball(new(s*.38f,.59f,-.26f),.09f,White);
            h.Ball(new(s*.38f,.28f,-.26f),.09f,White);
            h.Box(new(s*.37f,.46f,.12f),new(.018f,.13f,.13f),Dark);
            h.Box(new(s*.39f,.46f,.12f),new(.007f,.09f,.095f),Blue,SurfaceMaterial.OpticalMirror);
            for(int z=-1;z<=1;z+=2)h.Ball(new(s*.49f,.15f,z*.46f),.044f,Metal);
        }
        h.Tube(new(-.13f,-.05f,.51f),new(-.13f,-1.04f,.84f),.016f,Metal);
        h.Tube(new(.13f,-.05f,.51f),new(.13f,-1.04f,.84f),.016f,Metal);
        for(int i=0;i<7;i++){float f=i/6f;h.Tube(new(-.13f,-.05f-f*.99f,.51f+f*.33f),new(.13f,-.05f-f*.99f,.51f+f*.33f),.012f,White);}
        for(int j=0;j<8;j++)h.Box(new((j-3.5f)*.085f,.69f,0),new(.027f,.012f,.22f),Metal,SurfaceMaterial.Steel);
        h.Tube(new(.19f,.70f,-.12f),new(.19f,1.05f,-.12f),.012f,Metal);
        h.Box(new(.19f,1.07f,-.12f),new(.09f,.055f,.07f),White);
        h.Ball(new(.19f,1.07f,-.048f),.037f,Blue,SurfaceMaterial.OpticalMirror);
    }

    // Baked terrain around a flat landing site. The level central area matches
    // the model's leg-plane contact; distant relief is scenery, not collision.
    public static IReadOnlyList<SurfaceVertex> LandingGround()
    {
        var g=new DioramaGeometry {CaptureSurface=true,Material=SurfaceMaterial.Stone};
        float H(float x,float z)
        {
            float r=MathF.Sqrt(x*x+z*z),ramp=Math.Clamp((r-7)/13,0,1);
            return ramp*ramp*(1.3f*MathF.Sin(x*.15f)*MathF.Cos(z*.11f)+.5f*MathF.Sin(x*.37f+z*.23f));
        }
        Vec3 Point(float x,float z)=>new(x,H(x,z),z);
        Vec3 Normal(float x,float z)=>new Vec3(H(x-.1f,z)-H(x+.1f,z),.2f,H(x,z-.1f)-H(x,z+.1f)).Normalized();
        for(int i=0;i<160;i++)for(int j=0;j<160;j++)
        {
            float x=-160+i*2,z=-160+j*2;var a=Point(x,z);var b=Point(x,z+2);var c=Point(x+2,z+2);var d=Point(x+2,z);
            var color=new Vec3(.43f,.426f,.416f)*(float)(.86+.10*Math.Sin(x*.13)*Math.Cos(z*.18));
            g.SurfaceTriangle(a,b,c,Normal(x,z),Normal(x,z+2),Normal(x+2,z+2),color);
            g.SurfaceTriangle(a,c,d,Normal(x,z),Normal(x+2,z+2),Normal(x+2,z),color);
        }
        for(int i=0;i<400;i++)
        {
            float x=(float)(Noise(i,61)*180-90),z=(float)(Noise(i,62)*180-90);
            if(x*x+z*z<90)continue;
            float rad=(float)(.10+Noise(i,63)*.9);
            g.Ellipsoid(Point(x,z),new(rad,rad*.62f,rad*.8f),new(.29f,.29f,.28f));
        }
        return g.SurfacePoints.ToArray();
    }

    public static IReadOnlyList<SurfaceVertex> MiningGround()
    {
        var g=new DioramaGeometry {CaptureSurface=true,Material=SurfaceMaterial.Stone};
        // One closed surface, smoothly merging a local level work area into
        // the irregular body. Only the central 6x6 patch is a model collider.
        Vec3 Pnt(double u,double v)
        {
            double wave=1+.10*Math.Sin(3*u)*Math.Pow(Math.Cos(v),2)+.06*Math.Sin(7*u+v)*Math.Cos(v);
            double x=6.5*Math.Cos(v)*Math.Cos(u)*wave,z=5.6*Math.Cos(v)*Math.Sin(u)*wave;
            double y=-3.8+4.0*Math.Sin(v)*wave;
            if(v>0)
            {
                double edge=Math.Clamp((Math.Max(Math.Abs(x),Math.Abs(z))-3)/1.25,0,1);
                edge=edge*edge*(3-2*edge);
                y=Math.Min(0,y)*edge;
            }
            return P(x,y,z);
        }
        Vec3 Normal(double u,double v)
        {
            if(Math.Abs(v)>Math.PI/2-.0001)return new(0,v>0?1:-1,0);
            return Vec3.Cross(Pnt(u,v+.0001)-Pnt(u,v-.0001),Pnt(u+.0001,v)-Pnt(u-.0001,v)).Normalized();
        }
        const int nx=192,ny=96;double du=Math.Tau/nx,dv=Math.PI/ny;
        for(int j=0;j<ny;j++)for(int k=0;k<nx;k++)
        {
            double u=k*du,v=-Math.PI/2+j*dv;
            var a=Pnt(u,v);var b=Pnt(u+du,v);var c=Pnt(u+du,v+dv);var d=Pnt(u,v+dv);
            var na=Normal(u,v);var nb=Normal(u+du,v);var nc=Normal(u+du,v+dv);var nd=Normal(u,v+dv);
            var color=new Vec3(.32f,.30f,.27f);
            g.SurfaceTriangle(a,d,c,na,nd,nc,color);g.SurfaceTriangle(a,c,b,na,nc,nb,color);
        }
        for(int i=0;i<105;i++)
        {
            float x=(float)(Noise(i,12)*5.8-2.9),z=(float)(Noise(i,13)*5.8-2.9);
            if(x*x+z*z<2.8)continue;
            float size=(float)(.04+.16*Noise(i,14));g.Ellipsoid(new(x,-.015f,z),new(size,size*.6f,size*.8f),new(.21f,.21f,.205f));
        }
        return g.SurfacePoints.ToArray();
    }
}
