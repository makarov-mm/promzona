using MakarovPhysics.Renderer;
namespace Promzona.Game;
internal static class CityImportedSiteVisual
{
    public static void Block(DioramaGeometry g,int i,int j,int lod)
    {
        double x=CityTraffic.Coordinate(i),z=CityTraffic.Coordinate(j);
        foreach(var site in CityImportedSites.All()){
            if(site.Model=="metro_2033_train"||!CityImportedModels.Available.Contains(site.Model)||!CityTraffic.InBlock(site.Position,x,z))continue;
            int start=g.SurfacePoints.Count;float px=(float)site.Position.X,pz=(float)site.Position.Z,y=(float)site.Ground;
            // A short foundation joins the rigid imported object to sloping ground.
            if(site.Model!="tires_decor"){
                g.Material=SurfaceMaterial.Concrete;float low=(float)Math.Min(CityTerrain.Ground(px-site.HalfX,pz-site.HalfZ),CityTerrain.Ground(px+site.HalfX,pz+site.HalfZ))-.12f;
                g.Box(new(px,(low+y)*.5f,pz),new(site.HalfX+.04f,Math.Max(.05f,(y-low)*.5f),site.HalfZ+.04f),new(.35f,.36f,.34f));
            }
            if(site.Model=="free_soviet_tram_x-series"){
                g.Material=SurfaceMaterial.CastMetal;for(int side=-1;side<=1;side+=2)g.Box(new(px+side*.76f,y+.035f,pz),new(.045f,.035f,site.Length*.57f),new(.32f,.34f,.34f));
                if(lod<2){g.Material=SurfaceMaterial.Wood;for(float a=-site.Length*.55f;a<site.Length*.56f;a+=.65f)g.Box(new(px,y-.01f,pz+a),new(1.1f,.04f,.10f),new(.19f,.14f,.10f));}
            }
            CityTerrainGeometry.Absolute(g,start);
        }
    }
}
