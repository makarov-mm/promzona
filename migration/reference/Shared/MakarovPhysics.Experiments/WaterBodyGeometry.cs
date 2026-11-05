using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Closed outer edges for an educational water cutaway. The visible
/// front section is separately refractive; the remote walls prevent the sky
/// showing through below the surface. Levels always come from the module.</summary>
public static class WaterBodyGeometry
{
    public static void Wall(DioramaGeometry g,Vec3 a,Vec3 b,float bottom)
    {
        if(a.Y<=bottom && b.Y<=bottom)return;
        Vec3 lowA=new(a.X,bottom,a.Z),lowB=new(b.X,bottom,b.Z);
        int first=g.SurfacePoints.Count;
        var old=g.Material;g.Material=SurfaceMaterial.DeepWater;
        g.Quad(lowA,lowB,b,a,new(.055f,.25f,.30f));
        for(int i=first;i<g.SurfacePoints.Count;i++)
        {
            var v=g.SurfacePoints[i];float t=Math.Clamp((v.Position.Y-bottom)/Math.Max(.01f,Math.Max(a.Y,b.Y)-bottom),0,1);
            g.SurfacePoints[i]=v with { Color=Vec3.Lerp(new(.022f,.075f,.11f),new(.075f,.30f,.35f),t) };
        }
        g.Material=old;
    }

    /// <summary>Three remote walls; z1 faces the observer and is left to the
    /// module's shaped transparent section (turbine ducts/hulls remain open).</summary>
    public static void BackAndSides(DioramaGeometry g,float x0,float x1,float z0,float z1,float bottom,Func<float,float> level)
    {
        const int n=48;
        for(int i=0;i<n;i++)
        {
            float a=x0+(x1-x0)*i/n,b=x0+(x1-x0)*(i+1)/n;
            Wall(g,new(a,level(a),z0),new(b,level(b),z0),bottom);
        }
        Wall(g,new(x0,level(x0),z1),new(x0,level(x0),z0),bottom);
        Wall(g,new(x1,level(x1),z0),new(x1,level(x1),z1),bottom);
    }
}
