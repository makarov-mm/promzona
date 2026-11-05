using MakarovPhysics.Renderer;
using static MakarovPhysics.Experiments.InstrumentDrawing;
namespace MakarovPhysics.Experiments;
/// <summary>Mechanical details and measured-data graphics shared by the instrument laboratories.</summary>
public static class InstrumentCraft
{
 public static void Bolt(DioramaGeometry g,Vec3 p,float radius=.045f)
 {
  g.Material=SurfaceMaterial.MachinedMetal;g.Tube(p,p+Vec3.UnitY*.035f,radius,Steel);
  g.Material=SurfaceMaterial.Rubber;g.Box(p+Vec3.UnitY*.036f,new(radius*.65f,.002f,.005f),Dark);
 }
 public static void Flange(DioramaGeometry g,Vec3 c,float r,Vec3 normal,Vec3 color)
 {
  Vec3 n=normal.Normalized(),u=Vec3.Cross(Math.Abs(n.Y)<.8f?Vec3.UnitY:Vec3.UnitX,n).Normalized(),v=Vec3.Cross(n,u);
  g.Material=SurfaceMaterial.MachinedMetal;g.Tube(c-n*.04f,c+n*.04f,r,color);Ring(g,c+n*.05f,r*.87f,.012f,Dark,n);
  for(int j=0;j<8;j++){float a=j*MathF.Tau/8;Vec3 p=c+(u*MathF.Cos(a)+v*MathF.Sin(a))*r*.77f;g.Tube(p+n*.04f,p+n*.075f,.026f,Steel);}
 }
 public static void Gauge(DioramaGeometry g,Vec3 c,float r,double fraction,Vec3 ink)
 {
  g.Material=SurfaceMaterial.BrushedAluminium;g.Tube(c-new Vec3(0,0,.04f),c+new Vec3(0,0,.04f),r,Steel);
  g.Material=SurfaceMaterial.Paper;g.Tube(c+new Vec3(0,0,.04f),c+new Vec3(0,0,.046f),r*.87f,White);
  g.Material=SurfaceMaterial.Flat;
  for(int j=0;j<=20;j++){float a=MathF.PI*1.2f-j*MathF.PI*1.4f/20;Vec3 u=new(MathF.Cos(a),MathF.Sin(a),0);g.Tube(c+u*r*.72f+new Vec3(0,0,.05f),c+u*r*(j%5==0?.56f:.64f)+new Vec3(0,0,.05f),.004f,Dark);}
  float angle=MathF.PI*1.2f-(float)Math.Clamp(fraction,0,1)*MathF.PI*1.4f;g.Tube(c+new Vec3(0,0,.06f),c+new Vec3(MathF.Cos(angle)*r*.68f,MathF.Sin(angle)*r*.68f,.06f),.009f,ink);g.Ellipsoid(c+new Vec3(0,0,.06f),new(.025f,.025f,.012f),Dark);
 }
 public static void Flow(DioramaGeometry g,Vec3[] path,double phase,int count,Vec3 color,float size=.04f)
 {
  float length=0;for(int j=1;j<path.Length;j++)length+=(path[j]-path[j-1]).Length();if(length<=0)return;
  g.Material=SurfaceMaterial.Emissive;
  for(int i=0;i<count;i++){float d=(float)((phase+i/(double)count)%1)*length;for(int j=1;j<path.Length;j++){Vec3 delta=path[j]-path[j-1];float len=delta.Length();if(d<=len){Vec3 p=path[j-1]+delta*(d/len);g.Ellipsoid(p,new(size,size,size),color);break;}d-=len;}}
 }
 public static void Housing(DioramaGeometry g,Vec3 p,Vec3 half,Vec3 color)
 {
  g.Material=SurfaceMaterial.Enamel;g.Box(p,half,color);g.Material=SurfaceMaterial.Rubber;
  for(int i=0;i<9;i++)g.Box(p+new Vec3(-half.X*.65f+i*half.X*.16f,half.Y*.3f,half.Z+.005f),new(half.X*.04f,half.Y*.38f,.006f),Dark);
  for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Bolt(g,p+new Vec3(x*half.X*.83f,half.Y,z*half.Z*.8f),.026f);
 }
 public static void MonitorStand(DioramaGeometry g,Vec3 centre,float width,float baseY)
 {
  g.Material=SurfaceMaterial.MachinedMetal;g.Box(new(centre.X,baseY,centre.Z-.25f),new(width*.22f,.045f,.36f),Dark);g.Box(new(centre.X,(centre.Y+baseY)*.5f,centre.Z-.2f),new(.08f,(centre.Y-baseY)*.5f,.09f),Steel);
 }
}
