namespace MakarovPhysics.Renderer;
/// <summary>Opt-in material geometry for the small teaching exhibits. Dimensions are half extents.
/// One geometry stream supplies both opaque colour and shadow depth; transparent overlays come last.</summary>
public sealed class ExhibitWorkshop : IDisposable
{
 public readonly DioramaGeometry G=new(){CaptureSurface=true};
 private readonly StudioSurface _surface=new(600000);
 private readonly ShadowMap _shadow=new(1024);
 public static readonly Vec3 Ink=new(.075f,.11f,.14f), Silver=new(.63f,.68f,.72f), Teal=new(.08f,.42f,.46f), Gold=new(.78f,.55f,.23f), Ivory=new(.78f,.77f,.67f);
 public void Begin()=>G.Clear();
 public void Box(Vec3 c,Vec3 h,Vec3 col,SurfaceMaterial m=SurfaceMaterial.Enamel){G.Material=m;G.Box(c,h,col);}
 public void Tube(Vec3 a,Vec3 b,float r,Vec3 col,SurfaceMaterial m=SurfaceMaterial.Steel){G.Material=m;G.Tube(a,b,r,col);}
 public void Ball(Vec3 c,Vec3 h,Vec3 col,SurfaceMaterial m=SurfaceMaterial.Steel){G.Material=m;G.Ellipsoid(c,h,col);}
 public void Ring(Vec3 c,float r,float wire,Vec3 col,Vec3? normal=null,SurfaceMaterial m=SurfaceMaterial.Brass)
 {
  Vec3 n=normal??Vec3.UnitY,u=Vec3.Cross(n,MathF.Abs(n.Y)>.9f?Vec3.UnitZ:Vec3.UnitY).Normalized(),v=Vec3.Cross(n,u);
  int count=Math.Clamp((int)(r/Math.Max(wire,.0001f)*2),20,64);
  for(int i=0;i<count;i++){float a=i*MathF.Tau/count,b=(i+1)*MathF.Tau/count;Tube(c+(u*MathF.Cos(a)+v*MathF.Sin(a))*r,c+(u*MathF.Cos(b)+v*MathF.Sin(b))*r,wire,col,m);}
 }
 public void Arrow(Vec3 a,Vec3 b,float width,Vec3 col)
 {
  var d=b-a;float len=d.Length();if(len<.0001f)return;var n=d/len;var side=Vec3.Cross(n,Math.Abs(n.Y)>.9f?Vec3.UnitZ:Vec3.UnitY).Normalized();
  Tube(a,b,width,col,SurfaceMaterial.Emissive);float head=Math.Min(len*.3f,width*7);
  Tube(b,b-n*head+side*head*.5f,width,col,SurfaceMaterial.Emissive);Tube(b,b-n*head-side*head*.5f,width,col,SurfaceMaterial.Emissive);
 }
 public void Bench(Vec3 c,Vec3 half,bool holes=true)
 {
  Box(c,half,Silver,SurfaceMaterial.Steel);Box(c-new Vec3(0,half.Y+.06f,0),new(half.X*.98f,.06f,half.Z*.98f),Ink);
  foreach(float x in new[]{-half.X*.82f,half.X*.82f})foreach(float z in new[]{-half.Z*.75f,half.Z*.75f})
  {Tube(c+new Vec3(x,-half.Y-.1f,z),c+new Vec3(x,-half.Y-.48f,z),.07f,Ink);Tube(c+new Vec3(x,-half.Y-.48f,z),c+new Vec3(x,-half.Y-.5f,z),.11f,Ink,SurfaceMaterial.Rubber);}
  if(holes)for(float x=-half.X+.16f;x<half.X-.1f;x+=.24f)for(float z=-half.Z+.14f;z<half.Z-.1f;z+=.24f)
   Tube(c+new Vec3(x,half.Y+.001f,z),c+new Vec3(x,half.Y+.003f,z),.012f,Ink,SurfaceMaterial.Rubber);
 }
 public void Post(Vec3 p,float bottom,float height=.15f)
 {Box(new(p.X,bottom+.025f,p.Z),new(height,.025f,height),Ink);Tube(new(p.X,bottom+.05f,p.Z),p,.026f,Silver);Ring(p-new Vec3(0,.12f,0),.04f,.009f,Gold);}
 public void Draw(Mat4 view,Mat4 projection,Vec3 eye,float radius=5,Vec3? centre=null,float time=0)
     => Draw(view,projection,eye,radius,centre,time,null,1000);
 public void Draw(Mat4 view,Mat4 projection,Vec3 eye,float radius,Vec3? centre,float time,OutdoorSetting? outdoor,float farPlane)
 {
  _surface.DetailedFinish=outdoor is not null;_surface.Outdoor=outdoor is not null;
  _surface.Update(G.SurfacePoints);var light=new Vec3(-.5f,.85f,.65f).Normalized();var ls=ShadowMap.LightSpace(centre??Vec3.Zero,light,radius);
  uint draw=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);var vp=GL.PhysicalViewport();bool sc=GL.IsEnabled(GL.SCISSOR_TEST);
  GL.Disable(GL.SCISSOR_TEST);GL.Enable(GL.DEPTH_TEST);GL.DepthMask(true);GL.Disable(GL.BLEND);
  _shadow.BeginDepthPass();_surface.DrawDepth(ls,time);outdoor?.Surface.DrawDepth(ls,time);GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,draw);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);GL.RestorePhysicalViewport(vp.X,vp.Y,vp.Width,vp.Height);if(sc)GL.Enable(GL.SCISSOR_TEST);
  if(outdoor is not null) {
   outdoor.Time=time;outdoor.DrawSky(view,projection,eye,farPlane);
   GL.Enable(GL.DEPTH_TEST);GL.DepthMask(true);GL.Disable(GL.BLEND);
   outdoor.Surface.Draw(view,projection,eye,light,new(1.05f,1.02f,.97f),ls,_shadow.DepthTexture,time,environmentFill:1.25f);
  }
  _surface.Draw(view,projection,eye,light,new(1.05f,1.02f,.97f),ls,_shadow.DepthTexture,time,.72f,environmentFill:1.65f);
 }
 public void Dispose(){_surface.Dispose();_shadow.Dispose();}
}
