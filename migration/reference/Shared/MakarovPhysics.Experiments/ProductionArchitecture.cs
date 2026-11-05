using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Cached architectural finish for the production exhibits. Fittings are visual only;
/// no geometry or dimension here participates in an experiment's numerical model.</summary>
internal static class ProductionArchitecture
{
    private static readonly Vec3 Trim=new(.22f,.28f,.32f), Metal=new(.64f,.69f,.72f);
    internal static void Add(DioramaGeometry g,string id)
    {
        var previous=g.Material;
        switch(id)
        {
            case "EngCncMill":
                Bay(g,0,-2.14f,2.55f,2.9f,true);
                ServicePanel(g,new(0,.45f,.588f),.98f,.35f);break;
            case "EngLathe": Bay(g,0,-1.39f,3.1f,3.1f); break;
            case "EngFdmPrinter": Bay(g,0,-1.00f,2.5f,2.9f); break;
            case "EngLaserCutter": Bay(g,0,-1.75f,2.1f,2.9f); break;
            case "EngInjectionMolding":
                Bay(g,-.5f,-1.42f,2.5f,2.8f,true);
                ServicePanel(g,new(-1.15f,.5f,.432f),.28f,.23f);
                ServicePanel(g,new(-.35f,.5f,.432f),.28f,.23f);break;
            case "EngRollingMill": Bay(g,0,-9.94f,13.5f,7.7f); break;
            case "EngGlassBlowing": Craft(g,.2f,-2.66f,3.0f,2.8f); break;
            case "EngPotteryWheel": Craft(g,0,-1.54f,1.9f,2.7f); break;
            case "EngSewingMachine": Craft(g,0,-1.15f,2.3f,2.7f); break;
            case "EngOffsetPress": Bay(g,0,-4.90f,10.2f,5.9f); break;
            case "EngWaferStage": Bay(g,-.2f,-1.55f,2.0f,2.7f,true); break;
            case "EngLensGrinder": Bay(g,.3f,-1.05f,1.9f,2.9f); break;
            case "EngMusicBox": Craft(g,0,-.86f,2.3f,2.9f); break;
            case "EngCoffeeMachine": Strip(g,new(.0f,1.603f,-.285f),.52f,.012f,true); break;
            case "EngCentrifuge": Bay(g,0,-.99f,2.5f,2.9f); break;
            case "EngWindTunnelBalance": Bay(g,.4f,-2.66f,5.25f,4.3f); break;
            case "EngIndustrialCt": Bay(g,.2f,-2.05f,2.3f,3.1f); break;
            // Outdoor and night scenes retain their authored landscape and observatory.
        }
        g.Material=previous;
    }
    private static void ServicePanel(DioramaGeometry g,Vec3 centre,float w,float h)
    {
        // A recessed perimeter gasket and captive screws on the already closed cabinet.
        g.Material=SurfaceMaterial.Rubber;
        foreach(float t in new[]{-1f,1f}) {
            g.Box(centre+new Vec3(t*w,0,0),new(.0015f,h,.001f),Trim*.5f);
            g.Box(centre+new Vec3(0,t*h,0),new(w,.0015f,.001f),Trim*.5f);
        }
        g.Material=SurfaceMaterial.BrushedAluminium;
        foreach(float x in new[]{-.94f,.94f})foreach(float y in new[]{-.90f,.90f}) {
            Vec3 p=centre+new Vec3(x*w,y*h,.001f);
            g.Tube(p,p+Vec3.UnitZ*.002f,.005f,Metal);
        }
    }
    private static void Bay(DioramaGeometry g,float x,float z,float width,float height,bool newWall=false)
    {
        if(newWall) {
            g.Material=SurfaceMaterial.Plaster;
            g.Box(new(x,height*.5f,z-.035f),new(width,height*.5f,.025f),new(.68f,.73f,.76f));
        }
        g.Material=SurfaceMaterial.Enamel;
        g.Box(new(x,.085f,z),new(width,.085f,.012f),Trim);
        // Shallow expansion joints, a protected service duct, and a suspended linear luminaire.
        for(int i=-2;i<=2;i++)
            g.Box(new(x+i*width*.4f,height*.5f,z+.003f),new(.004f,height*.5f,.004f),new(.44f,.49f,.50f));
        g.Box(new(x,height*.70f,z+.025f),new(width,.025f,.025f),new(.38f,.42f,.43f));
        Strip(g,new(x,height*.88f,z+.12f),width*.78f,.025f,false);
        foreach(float s in new[]{-1f,1f}) {
            g.Material=SurfaceMaterial.BrushedAluminium;
            g.Box(new(x+s*width*.65f,height*.92f,z+.12f),new(.008f,height*.04f,.008f),Metal);
        }
    }
    private static void Craft(DioramaGeometry g,float x,float z,float width,float height)
    {
        g.Material=SurfaceMaterial.LacqueredWood;
        g.Box(new(x,.06f,z),new(width,.06f,.014f),new(.26f,.17f,.105f));
        Strip(g,new(x,height*.87f,z+.07f),width*.58f,.022f,true);
    }
    private static void Strip(DioramaGeometry g,Vec3 at,float halfLength,float halfHeight,bool warm)
    {
        g.Material=SurfaceMaterial.Enamel;
        g.Box(at,new(halfLength+.025f,halfHeight+.012f,.035f),Trim);
        g.Material=SurfaceMaterial.Emissive;
        g.Box(at+new Vec3(0,-halfHeight,.018f),new(halfLength,.005f,.020f),warm?new(1f,.79f,.48f):new(.81f,.91f,1f));
        g.Material=SurfaceMaterial.BrushedAluminium;
        foreach(float s in new[]{-1f,1f})
            g.Box(at+new Vec3(s*(halfLength+.015f),0,.025f),new(.010f,halfHeight+.01f,.014f),Metal);
    }
}
