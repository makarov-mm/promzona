using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;

/// <summary>A reusable, photograph-textured machine-cell stage. The machine keeps its own
/// moving geometry; floors, wall panels and light housings are uploaded once.</summary>
public static class EngineeringCellStage
{
    public static void Build(DioramaGeometry g,float width=4,float depth=2)
    {
        g.Material=SurfaceMaterial.Enamel;
        g.Box(new(0,.78f,0),new(width,.10f,depth),new(.12f,.23f,.27f));
        g.Material=SurfaceMaterial.Steel;
        foreach(float x in new[]{-width+.3f,width-.3f})foreach(float z in new[]{-depth+.3f,depth-.3f}){
            g.Box(new(x,.38f,z),new(.07f,.38f,.07f),new(.3f,.34f,.37f));
            g.Material=SurfaceMaterial.Rubber;g.Box(new(x,.03f,z),new(.12f,.03f,.12f),new(.045f,.05f,.05f));g.Material=SurfaceMaterial.Steel;
        }
        for(float x=-width;x<=width;x+=.65f){g.Box(new(x,2,-depth-.35f),new(.018f,1.9f,.035f),new(.28f,.34f,.37f));}
        g.Material=SurfaceMaterial.Enamel;g.Box(new(0,3.75f,-.6f),new(width*.8f,.045f,.12f),new(.14f,.18f,.2f));
        g.Material=SurfaceMaterial.Emissive;g.Box(new(0,3.7f,-.6f),new(width*.77f,.014f,.08f),new(2.4f,2.2f,1.8f));
    }
    public static void Scans(ScannedSite s,float width=4,float depth=2)
    {
        s.Add(ScanMap.Stone,.45f,SurfaceMaterial.Concrete,g=>g.Box(new(0,-.055f,0),new(width+1,.05f,depth+1),new(.6f,.61f,.62f)),bump:.45f);
        s.Add(ScanMap.Plaster,.5f,SurfaceMaterial.Plaster,g=>g.Box(new(0,1.9f,-depth-.4f),new(width+1,1.9f,.04f),new(.64f,.68f,.67f)),bump:.35f);
    }
}
