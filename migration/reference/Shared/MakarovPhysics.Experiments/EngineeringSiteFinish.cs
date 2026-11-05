using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Architectural fittings for the row-eight exhibits. Built into the cached site mesh,
/// never into a physical model. No moving part or measurement is replaced by decoration.</summary>
public static class EngineeringSiteFinish
{
    private static readonly Vec3 Steel = new(.52f,.58f,.64f), Dark = new(.10f,.15f,.18f),
        Brass = new(.72f,.53f,.25f), Warm = new(.93f,.86f,.67f), Pale = new(.70f,.76f,.77f);

    public static void Add(DioramaGeometry g, string id)
    {
        switch (id)
        {
            case "EngWaterClock":
                MuseumWall(g, 0, -2.44f, 3, 2.9f);
                CabinetFront(g, new(0,.4f,.356f), .53f,.31f);
                break;
            case "EngTidePredictor":
                MuseumWall(g,.1f,-2.94f,3,3.1f);
                // Apron and brass corner caps under the original working bench.
                Box(g,new(.1f,.65f,.39f),new(1.24f,.065f,.025f),new(.22f,.12f,.065f),SurfaceMaterial.Wood);
                foreach(float x in new[]{-1.13f,1.33f})
                    Box(g,new(x,.75f,.4f),new(.035f,.025f,.035f),Brass,SurfaceMaterial.Brass);
                break;
            case "EngFlywheelStorage":
                WallPanels(g,0,-2.59f,4.1f,3.2f,.8f);
                LightBar(g,new(0,2.9f,-2.52f),2.7f);
                CabinetFront(g,new(1.55f,.85f,-.616f),.36f,.61f);
                break;
            case "EngBaseIsolation":
                WallPanels(g,1,-7.87f,10,8.7f,2);
                foreach(float x in new[]{-7f,1f,9f}) LightBar(g,new(x,8.4f,-7.8f),2.4f);
                // Fixed pit perimeter, clear of the moving table and its moat.
                Rail(g,new(15,0,-5),new(15,0,5),1.1f,.045f);
                break;
            case "EngEscalator":
                WallPanels(g,-10.5f,-3.98f,4.5f,8.5f,1.5f);
                LightBar(g,new(-10.5f,6.8f,-3.89f),3.1f);
                for(int i=0;i<8;i++)
                    Box(g,new(-10.5f,.103f,-3.5f+i),new(4.4f,.002f,.007f),new(.35f,.37f,.38f),SurfaceMaterial.Flat);
                Rail(g,new(10,6.1f,-3.8f),new(14.5f,6.1f,-3.8f),1.05f,.03f);
                break;
            case "EngPneumaticPost":
                foreach(float y in new[]{0f,3.8f,7.6f,11.6f})
                {
                    Box(g,new(7.5f,y+.16f,-1.67f),new(9.9f,.16f,.025f),Dark,SurfaceMaterial.Enamel);
                    foreach(float x in new[]{-1f,3.5f,12f,16f})
                    {
                        WindowTrim(g,new(x,y+2.2f,-1.66f),.72f,.72f,.035f);
                    }
                    LightBar(g,new(7.5f,y+3.3f,-1.61f),1.7f);
                }
                break;
            case "EngFilmProjector":
                // Acoustic panels on the far wall. They do not obstruct the projector or the light path.
                for(int k=0;k<10;k++)
                    Box(g,new(1.5f+k*1.1f,2.7f,-4.48f),new(.40f,1.8f,.025f),new(.12f,.16f,.20f),SurfaceMaterial.Rubber);
                foreach(float x in new[]{2f,5f,8f,11f})
                    LightBar(g,new(x,.27f,-4.41f),.21f);
                Box(g,new(-.05f,.07f,.12f),new(.32f,.012f,.008f),Brass,SurfaceMaterial.Brass);
                break;
            case "EngCoolingTower":
                // Plant facade seams, insulated pipe supports and a service ladder beside the cutaway.
                WallPanels(g,-6.2f,1.22f,1.45f,2.8f,.7f);
                Ladder(g,new(1.66f,.55f,-1.25f),3.7f,.36f);
                Rail(g,new(-1.5f,4.25f,-1.5f),new(1.5f,4.25f,-1.5f),.65f,.022f);
                break;
            case "EngCompressedAirStorage":
                Rail(g,new(-4.5f,0,-2.4f),new(4.5f,0,-2.4f),.75f,.026f);
                foreach(float x in new[]{-4.3f,4.3f}) Bollard(g,new(x,0,2.15f),.65f);
                break;
            case "EngDistrictHeating":
                // Access covers and paving joints stay clear of the exposed heating mains.
                for(int k=0;k<10;k++)
                    Box(g,new(-5.5f+k,.007f,.83f),new(.008f,.004f,.17f),new(.35f,.38f,.40f),SurfaceMaterial.Flat);
                foreach(float x in new[]{-2.5f,.5f,3.5f})
                    Grille(g,new(x,.018f,.83f),.18f,.13f);
                break;
            case "EngWindFarm":
                // Painted transformer door, access-road edges, and aerial conductors on the existing poles.
                CabinetFront(g,new(.55f,.18f,-3.495f),.29f,.15f);
                for(int k=0;k<3;k++)
                {
                    Vec3 a=new(2.2f+4.5f*k,1.1f,-6.1f-1.5f*k);
                    Vec3 b=new(2.2f+4.5f*(k+1),1.1f,-6.1f-1.5f*(k+1));
                    for(int wire=-1;wire<=1;wire++)
                        SagWire(g,a+new Vec3(wire*.19f,0,0),b+new Vec3(wire*.19f,0,0),.10f,.004f);
                }
                break;
            case "EngPumpedStorage":
                // Turbine-house edging and a safe service route above the lower reservoir.
                Rail(g,new(1.85f,.04f,-.58f),new(3.2f,.04f,-.58f),.30f,.012f);
                foreach(float x in new[]{1.8f,3.3f}) Bollard(g,new(x,0,.75f),.25f);
                break;
            case "EngTidalLagoon":
                Ladder(g,new(.64f,-.55f,1.32f),1.25f,.24f);
                foreach(float z in new[]{-2f,-5f,-8f,-11f,-14f}) Bollard(g,new(.48f,.4f,z),.26f);
                break;
            case "EngSolarFurnace":
                // Perimeter posts leave the entire mirror field and ray path open.
                Rail(g,new(-8,0,-26),new(8,0,-26),.8f,.025f);
                foreach(float x in new[]{-8f,8f}) Bollard(g,new(x,0,-9),.7f);
                break;
            case "EngFerrisWheel":
                // Paved plaza and warm lamps give the moving cabins a clear setting.
                for(int x=-8;x<=8;x+=2)
                    Box(g,new(x,.103f,0),new(.012f,.002f,5.8f),new(.38f,.37f,.34f),SurfaceMaterial.Flat);
                foreach(float x in new[]{-7.5f,7.5f})
                {
                    Box(g,new(x,.16f,-4.5f),new(.3f,.16f,.3f),Dark,SurfaceMaterial.CastMetal);
                    g.Material=SurfaceMaterial.MachinedMetal;
                    g.Tube(new(x,.3f,-4.5f),new(x,3.6f,-4.5f),.045f,Steel);
                    LightBar(g,new(x,3.65f,-4.5f),.4f);
                }
                break;
            case "EngTelegraph":
                // Stone thresholds at the two stations; no fake signals added to the circuit.
                foreach(float x in new[]{-9.4f,9.4f})
                {
                    Box(g,new(x,-.018f,0),new(1.3f,.018f,1),new(.53f,.51f,.45f),SurfaceMaterial.Concrete);
                    for(int k=-2;k<=2;k++) Box(g,new(x+k*.5f,.002f,0),new(.004f,.002f,1),Dark,SurfaceMaterial.Flat);
                }
                break;
        }
    }

    private static void Box(DioramaGeometry g,Vec3 c,Vec3 h,Vec3 colour,SurfaceMaterial material)
    { g.Material=material;g.Box(c,h,colour); }
    private static void LightBar(DioramaGeometry g,Vec3 c,float half)
    {
        Box(g,c,new(half,.045f,.055f),Dark,SurfaceMaterial.MachinedMetal);
        Box(g,c+new Vec3(0,-.008f,.057f),new(half*.94f,.023f,.003f),Warm,SurfaceMaterial.Emissive);
    }
    private static void WindowTrim(DioramaGeometry g,Vec3 c,float x,float y,float t)
    {
        foreach(float sign in new[]{-1f,1f})
        {
            Box(g,c+new Vec3(sign*x,0,0),new(t,y+t,t),Pale,SurfaceMaterial.MachinedMetal);
            Box(g,c+new Vec3(0,sign*y,0),new(x,t,t),Pale,SurfaceMaterial.MachinedMetal);
        }
        Box(g,c,new(t*.6f,y,t),Pale,SurfaceMaterial.MachinedMetal);
    }
    private static void CabinetFront(DioramaGeometry g,Vec3 c,float x,float y)
    {
        foreach(float sign in new[]{-1f,1f})
        {
            Box(g,c+new Vec3(sign*x,0,0),new(.004f,y,.002f),Dark,SurfaceMaterial.Flat);
            Box(g,c+new Vec3(0,sign*y,0),new(x,.004f,.002f),Dark,SurfaceMaterial.Flat);
        }
        Box(g,c+new Vec3(x*.75f,0,.006f),new(.012f,Math.Min(.07f,y*.25f),.009f),Brass,SurfaceMaterial.Brass);
    }
    private static void MuseumWall(DioramaGeometry g,float x,float z,float half,float height)
    {
        Box(g,new(x,.45f,z),new(half,.45f,.025f),new(.15f,.21f,.23f),SurfaceMaterial.Enamel);
        Box(g,new(x,.92f,z+.03f),new(half,.025f,.035f),Brass,SurfaceMaterial.Brass);
        for(float xx=x-half+.3f;xx<x+half;xx+=.7f)
            Box(g,new(xx,.44f,z+.031f),new(.012f,.38f,.01f),new(.24f,.29f,.28f),SurfaceMaterial.Wood);
        LightBar(g,new(x,height-.2f,z+.07f),half*.65f);
    }
    private static void WallPanels(DioramaGeometry g,float cx,float z,float half,float height,float spacing)
    {
        for(float x=cx-half;x<=cx+half+.01f;x+=spacing)
            Box(g,new(x,height/2,z),new(.009f,height/2,.005f),new(.35f,.40f,.43f),SurfaceMaterial.Flat);
        Box(g,new(cx,.18f,z+.01f),new(half,.18f,.015f),new(.18f,.26f,.30f),SurfaceMaterial.Enamel);
    }
    private static void Rail(DioramaGeometry g,Vec3 a,Vec3 b,float height,float radius)
    {
        g.Material=SurfaceMaterial.MachinedMetal;
        for(int i=0,n=Math.Max(1,(int)((b-a).Length()/Math.Max(.3f,height*1.5f)));i<=n;i++)
        {var p=Vec3.Lerp(a,b,(float)i/n);g.Tube(p,p+Vec3.UnitY*height,radius,Steel);}
        foreach(float h in new[]{height,height*.5f})g.Tube(a+Vec3.UnitY*h,b+Vec3.UnitY*h,radius,Steel);
    }
    private static void Ladder(DioramaGeometry g,Vec3 foot,float height,float width)
    {
        g.Material=SurfaceMaterial.MachinedMetal;
        foreach(float s in new[]{-1f,1f})g.Tube(foot+new Vec3(0,0,s*width/2),foot+new Vec3(0,height,s*width/2),.015f,Steel);
        for(float y=.1f;y<height;y+=.22f)g.Tube(foot+new Vec3(0,y,-width/2),foot+new Vec3(0,y,width/2),.012f,Steel);
    }
    private static void Bollard(DioramaGeometry g,Vec3 p,float height)
    {
        g.Material=SurfaceMaterial.Enamel;
        g.Tube(p,p+Vec3.UnitY*height,height*.10f,Dark);
        g.Tube(p+Vec3.UnitY*(height*.67f),p+Vec3.UnitY*(height*.83f),height*.103f,Warm);
    }
    private static void Grille(DioramaGeometry g,Vec3 c,float x,float z)
    {
        Box(g,c,new(x,.006f,z),Dark,SurfaceMaterial.CastMetal);
        for(float a=-x+.02f;a<x;a+=.035f)Box(g,c+new Vec3(a,.008f,0),new(.007f,.005f,z*.92f),Steel,SurfaceMaterial.MachinedMetal);
    }
    private static void SagWire(DioramaGeometry g,Vec3 a,Vec3 b,float sag,float radius)
    {
        g.Material=SurfaceMaterial.Rubber;
        Vec3 P(float t)=>Vec3.Lerp(a,b,t)-Vec3.UnitY*(4*sag*t*(1-t));
        for(int k=0;k<16;k++)g.Tube(P(k/16f),P((k+1)/16f),radius,Dark);
    }
}
