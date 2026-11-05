using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityRoadLevelVisual
{
    public static void Block(DioramaGeometry g,int i,int j,int lod)
    {
        foreach(var s in CityRoadLevels.All){
            if(j!=s.J)continue;
            double lo=Math.Max(CityTraffic.Coordinate(i),s.X-CityRoadLevel.Length),hi=Math.Min(CityTraffic.Coordinate(i+1),s.X+CityRoadLevel.Length);
            if(hi<=lo)continue;
            int start=g.SurfacePoints.Count;
            Vec3 At(double x,double side,double up=0)=>new((float)x,(float)(s.Floor(x,s.CentreZ(x)+side)+up),(float)(s.CentreZ(x)+side));
            void Wall(double a,double b,double side,double h0,double h1){
                Vec3 p=At(a,side),q=At(b,side),pt=p+Vec3.UnitY*(float)h0,qt=q+Vec3.UnitY*(float)h1;
                Vec3 offset=Vec3.UnitZ*(float)(Math.Sign(side)*.14),color=new(.43f,.44f,.41f);
                // Two distinct faces and a cap: opposite normals on a coincident
                // double-sided quad fight in the depth buffer under local lighting.
                if(side<0){g.Quad(p,q,qt,pt,color);g.Quad(q+offset,p+offset,pt+offset,qt+offset,color);}
                else {g.Quad(q,p,pt,qt,color);g.Quad(p+offset,q+offset,qt+offset,pt+offset,color);}
                if(side<0)g.Quad(pt,qt,qt+offset,pt+offset,color);
                else g.Quad(qt,pt,pt+offset,qt+offset,color);
            }
            for(double a=lo;a<hi-.001;a+=1){double b=Math.Min(hi,a+1),mid=(a+b)*.5;
                g.Material=(SurfaceMaterial)300;
                for(double side=-5.8;side<5.799;){
                    double next=Math.Min(5.8,Math.Floor(side+.0001)+1);
                    g.Quad(At(a,side),At(a,next),At(b,next),At(b,side),new(.18f,.19f,.19f));side=next;
                }
                // The visible underside is a solid slab, with outward-facing faces.
                g.Material=SurfaceMaterial.Concrete;
                if(!s.Tunnel)g.Quad(At(b,-5.8,-.55),At(b,5.8,-.55),At(a,5.8,-.55),At(a,-5.8,-.55),new(.35f,.36f,.34f));
                for(int side=-1;side<=1;side+=2){
                    if(s.Tunnel){
                        double h0=s.Roof(mid)?CityRoadLevel.Clearance:Math.Max(.8,CityTerrain.Ground(a,s.CentreZ(a)+side*5.8)-s.Floor(a,s.CentreZ(a)+side*5.8)+.6);
                        double h1=s.Roof(mid)?CityRoadLevel.Clearance:Math.Max(.8,CityTerrain.Ground(b,s.CentreZ(b)+side*5.8)-s.Floor(b,s.CentreZ(b)+side*5.8)+.6);
                        Wall(a,b,side*5.8,h0,h1);
                    }else{
                        Wall(a,b,side*5.8,-.55, -.55);
                        Wall(a,b,side*5.65,.85,.85);
                    }
                    g.Material=SurfaceMaterial.Paving;
                    if(side>0)g.Quad(At(a,side*5.55,.13),At(b,side*5.55,.13),At(b,side*4.45,.13),At(a,side*4.45,.13),new(.42f,.41f,.37f));
                    else g.Quad(At(b,side*5.55,.13),At(a,side*5.55,.13),At(a,side*4.45,.13),At(b,side*4.45,.13),new(.42f,.41f,.37f));
                    g.Quad(At(a,side*4.45,.13),At(b,side*4.45,.13),At(b,side*4.45),At(a,side*4.45),new(.4f,.4f,.37f));
                    g.Material=SurfaceMaterial.Concrete;
                }
                if(s.Roof(mid)){
                    g.Material=SurfaceMaterial.Concrete;
                    g.Quad(At(a,-5.8,CityRoadLevel.Clearance),At(b,-5.8,CityRoadLevel.Clearance),At(b,5.8,CityRoadLevel.Clearance),At(a,5.8,CityRoadLevel.Clearance),new(.35f,.36f,.34f));
                }
                if(lod<2){
                    g.Material=SurfaceMaterial.Flat;
                    foreach(double side in new[]{-4.1,4.1})g.Quad(At(a,side-.055,.018),At(a,side+.055,.018),At(b,side+.055,.018),At(b,side-.055,.018),new(.73f,.70f,.57f));
                    if(((int)a%6+6)%6<3)g.Quad(At(a,-.055,.018),At(a,.055,.018),At(b,.055,.018),At(b,-.055,.018),new(.76f,.68f,.40f));
                    if(s.Roof(mid)&&(int)a%9==0){
                        g.Material=SurfaceMaterial.Emissive;
                        g.Box(At(a,0,4.70),new(.8f,.045f,.18f),new(.82f,.81f,.62f));
                        for(int side=-1;side<=1;side+=2)g.Box(At(a,side*5.6,1.3),new(.12f,.20f,.06f),new(.18f,.72f,.39f));
                    }
                }
            }
            if(!s.Tunnel)for(double x=s.X-54;x<=s.X+54;x+=36)if(x>=lo&&x<hi)for(int side=-1;side<=1;side+=2){
                double z=s.CentreZ(x)+side*7.1,floor=CityTerrain.Ground(x,z),top=s.Floor(x,z)-.55;
                g.Material=SurfaceMaterial.Concrete;
                g.Box(new((float)x,(float)((floor+top)*.5),(float)z),new(.7f,(float)((top-floor)*.5),.7f),new(.48f,.47f,.42f));
                g.Box(new((float)x,(float)top-.25f,(float)s.CentreZ(x)),new(1,.35f,7.8f),new(.39f,.40f,.38f));
            }
            if(s.Tunnel)foreach(double x in new[]{s.X-CityRoadLevel.Covered,s.X+CityRoadLevel.Covered})if(x>=lo&&x<hi){
                double y=s.CentreFloor(x),ground=CityTerrain.Ground(x,s.CentreZ(x));g.Material=SurfaceMaterial.Concrete;
                g.Box(new((float)x,(float)((y+4.9+ground)*.5),(float)s.CentreZ(x)),new(.65f,(float)Math.Max(.5,(ground-y-4.9)*.5),6.6f),new(.48f,.47f,.43f));
                for(int side=-1;side<=1;side+=2)g.Box(At(x,side*6.2,2.4),new(.65f,2.5f,.4f),new(.45f,.44f,.40f));
            }
            CityTerrainGeometry.Absolute(g,start);
        }
    }
}

