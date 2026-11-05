using MakarovPhysics.Renderer;
namespace Promzona.Game;
internal static class CityPeopleDetail
{
    public static void Draw(DioramaGeometry g,CityModel m,CityVisibility? visibility,CityActorModels? actors=null){
        foreach(var p in m.Traffic.Pedestrians){
            if(p.Dead||p.KnockedFor>0||(p.Position-m.Traffic.PlayerPosition).Length>65)continue;
            bool job=p.Hailing||p.Injured>0;
            if(p.Persona<4&&!job&&!p.Courier)continue;
            Vec3 o=new((float)p.Position.X,(float)CityRoadLevels.Ground(p)+(p.ScooterId!=0?.287f:.15f),(float)p.Position.Z);
            if(!(visibility?.Sphere(o+Vec3.UnitY,2)??true))continue;
            Vec3 f=new((float)Math.Sin(p.Heading),0,(float)Math.Cos(p.Heading)),r=new(f.Z,0,-f.X);
            float h=(float)p.Height;
            Vec3 crown=o+Vec3.UnitY*(h-.04f);
            if((p.Officer||p.Persona==4||p.Courier)&&actors is not null&&actors.HeadSocket(p,m.Traffic,g.Eye,out var animatedCrown,out float heading)){
                crown=animatedCrown-Vec3.UnitY*.012f;
                f=new((float)Math.Sin(heading),0,(float)Math.Cos(heading));r=new(f.Z,0,-f.X);
            }
            // Jobs: a hailer holds an arm up at the kerb, an injured survivor presses a hand to the side.
            if(p.Hailing){
                g.Material=SurfaceMaterial.Rubber;Vec3 shoulder=o+Vec3.UnitY*(h*.82f)+r*.20f,hand=shoulder+r*.22f+Vec3.UnitY*(h*.30f)+f*.05f;
                g.Tube(shoulder,hand,.035f,new(.62f,.50f,.40f));g.Ellipsoid(hand,new(.05f,.06f,.05f),new(.78f,.62f,.50f));
            }else if(p.Injured>0){g.Material=SurfaceMaterial.Rubber;g.Ellipsoid(o+Vec3.UnitY*(h*.52f)+r*.19f+f*.06f,new(.06f,.07f,.05f),new(.78f,.62f,.50f));}
            if(p.Courier){
                // Insulated delivery bag: restrained cloth, reflective piping and straps.
                // Feet/torso scale with the same metre dimensions as the skin.
                int bagStart=g.SurfacePoints.Count;
                Vec3 centre=o-f*.28f+Vec3.UnitY*(h*.68f);
                g.Material=SurfaceMaterial.Rubber;Vec3 cloth=(p.Id%3) switch{0=>new(.28f,.38f,.22f),1=>new(.53f,.35f,.12f),_=>new(.26f,.32f,.43f)};
                g.Box(centre,new(.25f,.30f,.17f),cloth,r,Vec3.UnitY,f);
                g.Box(centre+Vec3.UnitY*.29f,new(.26f,.035f,.18f),cloth*.75f,r,Vec3.UnitY,f);
                g.Material=SurfaceMaterial.Polymer;
                for(int side=-1;side<=1;side+=2){
                    g.Tube(centre+r*(side*.245f)-f*.17f-Vec3.UnitY*.27f,centre+r*(side*.245f)-f*.17f+Vec3.UnitY*.25f,.012f,new(.76f,.75f,.65f));
                    g.Tube(o+r*(side*.14f)+f*.12f+Vec3.UnitY*(h*.79f),o+r*(side*.17f)+f*.12f+Vec3.UnitY*(h*.54f),.022f,new(.13f,.14f,.12f));
                }
                g.Box(centre-f*.176f,new(.16f,.025f,.008f),new(.74f,.73f,.61f),r,Vec3.UnitY,f);
                CityTerrainGeometry.Absolute(g,bagStart);
            }
            if(p.Persona<4)continue;
            if(p.Officer){
                g.Material=SurfaceMaterial.Rubber;
                Vec3 hat=p.Persona==5?new(.10f,.17f,.23f):p.Persona==12?new(.055f,.055f,.06f):new(.12f,.18f,.13f);
                g.Ellipsoid(crown,new(.108f,.037f,.115f),hat);
                g.Box(crown-Vec3.UnitY*.02f+f*.105f,new(.10f,.008f,.055f),hat,r,Vec3.UnitY,f);
                g.Material=SurfaceMaterial.Brass;
                g.Box(o+Vec3.UnitY*(h*.75f)+f*.13f+r*.12f,new(.028f,.042f,.012f),new(.72f,.59f,.27f),r,Vec3.UnitY,f);
            }
            if(p.Persona==4){
                g.Material=SurfaceMaterial.Paper;
                g.Box(o+Vec3.UnitY*(h*.45f),new(.15f,.18f,.105f),new(.84f,.87f,.81f),r,Vec3.UnitY,f);
                g.Ellipsoid(crown,new(.10f,.03f,.11f),new(.88f,.90f,.86f));
            }
            if(p.Persona==8)for(int side=-1;side<=1;side+=2){
                Vec3 bag=o+r*(side*.32f)+Vec3.UnitY*.50f+f*(float)(Math.Sin(p.Distance*9+side)*.04);
                g.Material=SurfaceMaterial.Paper;g.Ellipsoid(bag,new(.12f,.19f,.12f),new(.48f,.43f,.29f));
                g.Material=SurfaceMaterial.Rubber;
                for(int n=0;n<5;n++){float t=(n-2)*.05f;g.Tube(bag+r*t-Vec3.UnitY*.15f,bag+r*t+Vec3.UnitY*.16f,.005f,new(.22f,.20f,.12f));}
                g.Tube(bag+Vec3.UnitY*.17f-r*.08f,bag+Vec3.UnitY*.30f,.009f,new(.32f,.27f,.15f));
                g.Tube(bag+Vec3.UnitY*.30f,bag+Vec3.UnitY*.17f+r*.08f,.009f,new(.32f,.27f,.15f));
            }
            if(p.Persona==10){g.Material=SurfaceMaterial.Rubber;g.Ellipsoid(o-f*.15f+Vec3.UnitY*1.1f,new(.22f,.30f,.15f),new(.20f,.18f,.12f));}
        }
    }
}
