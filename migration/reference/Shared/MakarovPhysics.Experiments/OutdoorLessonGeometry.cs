using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;

/// <summary>Presentation-only landscapes with explicit clear corridors for
/// the seven outdoor lessons. Heights never change a model's contact plane.</summary>
public static class OutdoorLessonGeometry
{
    public static void Build(DioramaGeometry g,string id)
    {
        float floor=id switch {"ProjectileMotion"=>-3.85f,"EnergyPark"=>-.39f,"Relativity"=>-.205f,"Bicycle"=>-.13f,_=>0};
        if(id=="BridgeFlutter") { River(g);return; }
        float size=id=="ProjectileMotion"?6000:180;
        float Height(float x,float z) => floor+(id=="ProjectileMotion"?0:Meadow.Height(x,z));
        Field(g,-size,size,-size,size,80,Height);
        bool Allowed(float x,float z) => id switch {
            "Bicycle"=>Math.Abs(z)>3.2f,
            "EnergyPark"=>Math.Abs(z)>3.3f || Math.Abs(x)>12,
            "Relativity"=>Math.Abs(z)>5.2f,
            "ProjectileMotion"=>Math.Abs(z)>24 || x< -20,
            "Betz"=>x*x+z*z>100,
            _=>false};
        float span=id=="ProjectileMotion"?100:28;
        LivingLandscape.Grass(g,-span,span,-span,span,9000,1504,Height,Allowed,id=="ProjectileMotion"?.35f:.09f);
        // Trees stay outside the central apparatus/trajectory and the turbine flow volume.
        for(int i=0;i<12;i++) {
            float x=-32+i*5.6f,z=(i%3==0?24:-23)-i%4*2;
            if(id=="ProjectileMotion") {x=-20+i*13;z=-45-i%3*6;}
            if(id=="Betz") {x=-48+i*9;z=38+i%3*6;}
            LivingLandscape.Tree(g,new(x,Height(x,z),z),id=="ProjectileMotion"?6:3.5f+i%3*.6f,71+i);
        }
    }
    public static void Field(DioramaGeometry g,float x0,float x1,float z0,float z1,int cells,Func<float,float,float> height)
    {
        g.Material=SurfaceMaterial.Landscape;Vec3 grass=new(.29f,.40f,.18f);
        for(int j=0;j<cells;j++)for(int i=0;i<cells;i++) {
            float x=x0+(x1-x0)*i/cells,xx=x0+(x1-x0)*(i+1)/cells,z=z0+(z1-z0)*j/cells,zz=z0+(z1-z0)*(j+1)/cells;
            g.Quad(new(x,height(x,z),z),new(x,height(x,zz),zz),new(xx,height(xx,zz),zz),new(xx,height(xx,z),z),grass);
        }
    }
    private static void River(DioramaGeometry g)
    {
        // A continuous river under the suspended span, with visible bed and closed ends.
        float Bank(float x,float z)=>-.12f+Math.Clamp((Math.Abs(x)-3)/8,0,1)*.30f;
        Field(g,-100,-3,-120,120,60,Bank);Field(g,3,100,-120,120,60,Bank);
        g.Material=SurfaceMaterial.Stone;
        g.Box(new(0,-1.05f,0),new(3,.16f,120),new(.29f,.31f,.25f));
        foreach(int side in new[]{-1,1}) {
            g.Box(new(side*2.95f,-.54f,0),new(.15f,.48f,120),new(.38f,.40f,.30f));
            g.Material=SurfaceMaterial.Concrete;
            g.Box(new(side*2.5f,-.16f,0),new(.35f,.32f,1),new(.55f,.56f,.52f));
            g.Material=SurfaceMaterial.Stone;
        }
        g.Material=SurfaceMaterial.MarineWater;
        g.Quad(new(-2.8f,-.36f,-120),new(-2.8f,-.36f,120),new(2.8f,-.36f,120),new(2.8f,-.36f,-120),new(.07f,.27f,.29f));
        WaterBodyGeometry.Wall(g,new(-2.8f,-.36f,120),new(2.8f,-.36f,120),-.9f);
        WaterBodyGeometry.Wall(g,new(2.8f,-.36f,-120),new(-2.8f,-.36f,-120),-.9f);
        LivingLandscape.Grass(g,-22,22,-24,24,6500,91,Bank,(x,z)=>Math.Abs(x)>3.4f,.10f);
        for(int i=0;i<10;i++) {
            float x=(i%2==0?-1:1)*(9+i%3*2),z=-9-i*1.7f;
            LivingLandscape.Tree(g,new(x,Bank(x,z),z),3+i%3*.4f,31+i);
        }
    }
}
