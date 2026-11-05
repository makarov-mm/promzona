using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;
/// <summary>Reusable workshop details; dimensional outlines are presentation geometry, never solver inputs.</summary>
public static class WorkshopDrawing
{
 public static void Gear(DioramaGeometry g,Vec3 center,float radius,float phase,Vec3 color,int teeth=24)
 {
  g.Material=SurfaceMaterial.MachinedMetal;g.Tube(center-new Vec3(0,0,.07f),center+new Vec3(0,0,.07f),radius*.86f,color);
  for(int i=0;i<teeth;i++){float a=phase+i*MathF.Tau/teeth;Vec3 r=new(MathF.Cos(a),MathF.Sin(a),0),t=new(-r.Y,r.X,0);g.Box(center+r*radius,new(radius*.08f,radius*.075f,.075f),color,r,t,Vec3.UnitZ);}
  g.Tube(center-new Vec3(0,0,.13f),center+new Vec3(0,0,.13f),radius*.18f,InstrumentDrawing.Steel);
 }
 public static void Wheel(DioramaGeometry g,Vec3 center,float radius,float angle,float roll)
 {
  Vec3 f=new(MathF.Sin(angle),0,MathF.Cos(angle)),a=new(f.Z,0,-f.X);g.Material=SurfaceMaterial.Rubber;g.Tube(center-a*.13f,center+a*.13f,radius,InstrumentDrawing.Dark);
  g.Material=SurfaceMaterial.BrushedAluminium;g.Tube(center-a*.14f,center+a*.14f,radius*.62f,InstrumentDrawing.Steel);
  for(int side=-1;side<=1;side+=2)for(int i=0;i<6;i++){float q=roll+i*MathF.Tau/6;g.Tube(center+a*(side*.15f),center+a*(side*.15f)+(Vec3.UnitY*MathF.Cos(q)+f*MathF.Sin(q))*radius*.57f,.018f,InstrumentDrawing.Dark);}
 }
 public static void Boat(DioramaGeometry g,Vec3 p,float scale,Vec3 color, float yaw=0)
 {
  Vec3 r=new(MathF.Cos(yaw),0,-MathF.Sin(yaw)),f=new(MathF.Sin(yaw),0,MathF.Cos(yaw));
  g.Material=SurfaceMaterial.Enamel;g.Ellipsoid(p,new(.65f*scale,.3f*scale,1.8f*scale),color,r,Vec3.UnitY,f);
  g.Material=SurfaceMaterial.Wood;g.Box(p+new Vec3(0,.2f*scale,0),new(.54f*scale,.04f*scale,1.35f*scale),new(.55f,.33f,.13f),r,Vec3.UnitY,f);
  g.Material=SurfaceMaterial.Enamel;g.Box(p+new Vec3(0,.5f*scale,0),new(.4f*scale,.3f*scale,.6f*scale),InstrumentDrawing.White,r,Vec3.UnitY,f);
  g.Material=SurfaceMaterial.Polymer;g.Box(p+new Vec3(0,.55f*scale,0)+f*.61f*scale,new(.33f*scale,.15f*scale,.012f),InstrumentDrawing.Dark,r,Vec3.UnitY,f);
 }
 public static void Landscape(DioramaGeometry g,float extent,Func<float,float,float> height,Vec3 color,int cells=48)
 {
  g.Material=SurfaceMaterial.Stone;float d=2*extent/cells;
  Vec3 P(int x,int z)=>new(-extent+x*d,height(-extent+x*d,-extent+z*d),-extent+z*d);
  Vec3 N(Vec3 p)=>new Vec3(height(p.X-.05f,p.Z)-height(p.X+.05f,p.Z),.1f,height(p.X,p.Z-.05f)-height(p.X,p.Z+.05f)).Normalized();
  for(int z=0;z<cells;z++)for(int x=0;x<cells;x++){var a=P(x,z);var b=P(x+1,z);var c=P(x+1,z+1);var e=P(x,z+1);var tint=color*(.92f+.08f*MathF.Sin(a.X*1.3f)*MathF.Sin(a.Z*1.8f));g.SurfaceTriangle(a,c,b,N(a),N(c),N(b),tint);g.SurfaceTriangle(a,e,c,N(a),N(e),N(c),tint);}
 }
}
