using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;
namespace MakarovPhysics.Experiments;

/// <summary>Opt-in material exhibit for the Earth/Engineering pilots. Owns GL resources and a non-interactive readout overlay.</summary>
public abstract class DevelopmentExhibit : PhysicsModuleBase
{
 protected ExhibitWorkshop? Art;
 protected readonly OrbitCamera Camera=new(){Target=new(0,1,0),Yaw=.42f,Pitch=.36f,Distance=11,MaxDistance=28};
 private readonly List<(Checkbox box,Func<bool> get)> _toggles=[];
 private SceneDashboard? _dashboard;private UiLabel? _readout;
 protected virtual float Radius=>4.2f;
 protected virtual Vec3 Focus=>new(0,1,0);
 protected abstract void Geometry(ExhibitWorkshop art);
 protected abstract string[] Values {get;}
 protected abstract void Controls(UiPanel panel);
 public override SceneBackground Background=>SceneBackground.Diagram;
 public override ModuleCapabilities Capabilities => ModuleCapabilities.All | ModuleCapabilities.AutoStart;
 protected string T(string key)=>Tr("Module."+key);
 protected DevelopmentExhibit(){Dt=1.0/120;MaxSubSteps=32;}
 public override void Initialize(IModuleContext context)
 {
  base.Initialize(context);Art=new();
  if(context.GetService<UiContext>() is {} ui)
   _dashboard=ui.Root.Add(new SceneDashboard(context,(d,b)=>
   {
    string[] values=Values;float gap=8,w=(b.W-2*gap)/3,h=58;
    for(int i=0;i<3;i++){var r=new UiRect(b.X+i*(w+gap),b.Bottom-h,w,h);SceneDashboard.Card(d,r,i==1?SceneDashboard.Gold:SceneDashboard.Blue);
     d.Text(r.X+10,r.Y+9,TextLayout.Ellipsize(T("Measurements.M"+(i+1)),r.W-20,d.MeasureText),SceneDashboard.Muted);
     d.Text(r.X+10,r.Y+31,TextLayout.Ellipsize(values[i],r.W-20,d.MeasureText),SceneDashboard.Ink);}
   }));
 }
 public override void BuildUi(object root)
 {
  if(root is not UiPanel p)return;_toggles.Clear();
  p.Add(new UiLabel(T("Hint")){Wrap=true});
  CameraPresetBar.AddResponsive(p,Camera,[new("Camera.Overview",Focus,.42f,.36f,Radius),new("Camera.Close",Focus,.12f,.14f,Radius*.73f),new("Camera.Instrument",Focus,0,1.22f,Radius)],Tr,()=>Context is null?1.6f:(float)Context.ViewportWidth/Math.Max(1,Context.ViewportHeight));
  Controls(p);
  _readout=p.Add(new UiLabel(""){Wrap=true,Dim=true});
  p.Add(new UiLabel(T("Limits")){Wrap=true,Dim=true});
 }
 protected void Number(UiPanel p,string key,Func<double> get,Action<double> set,double min,double max,string units="")=>p.Add(new Slider(T("Params."+key),get,set,min,max,units));
 protected void Button(UiPanel p,string key,Action click){var b=p.Add(new UiButton(T("Buttons."+key)));b.Click+=click;}
 protected void Toggle(UiPanel p,string key,Func<bool> get,Action<bool> change){var b=p.Add(new Checkbox(T("Params."+key),get()));b.Changed+=change;_toggles.Add((b,get));}
 protected override void OnUiFrame(){foreach(var(box,get) in _toggles)box.SetValue(get());if(_readout is not null)_readout.Text=string.Join("\n",Values);}
 protected override void OnInput(InputState input)
 {
  if(input.ConsumedByUi)return;
  if((input.Buttons&MouseButton.Right)!=0)Camera.Drag(input.MouseDx,input.MouseDy,.006f);
  if(input.WheelDelta!=0)Camera.Zoom(input.WheelDelta>0?1/1.12f:1.12f);
 }
 public override void Render()
 {
  if(Art is null||Context is null)return;
  Art.Begin();Geometry(Art);float aspect=(float)Context.ViewportWidth/Math.Max(1,Context.ViewportHeight);
  Art.Draw(Camera.View(),Camera.Projection(aspect),Camera.Position(),Radius*1.5f,Focus,(float)Time);
 }
 public override void Dispose(){if(_dashboard is not null)_dashboard.Visible=false;Art?.Dispose();Art=null;base.Dispose();}
 protected static Vec3 V(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);
 protected static Vec3 Transform(Vec3 p,System.Numerics.Quaternion rotation)=>V(System.Numerics.Vector3.Transform(new(p.X,p.Y,p.Z),rotation));
 protected static void Bearing(ExhibitWorkshop a,Vec3 c,float deck=0)
 {
  a.Box(new(c.X,deck+.1f,c.Z),new(.3f,.10f,.35f),ExhibitWorkshop.Ink);
  a.Box(new(c.X,(c.Y+deck)/2,c.Z),new(.2f,(c.Y-deck)/2,.16f),ExhibitWorkshop.Teal);
  a.Ring(c,.23f,.07f,ExhibitWorkshop.Silver,Vec3.UnitZ);
  foreach(float x in new[]{-.22f,.22f})a.Tube(new(c.X+x,deck+.18f,c.Z),new(c.X+x,deck+.24f,c.Z),.045f,ExhibitWorkshop.Silver);
 }
 protected static void Gear(ExhibitWorkshop a,Vec3 c,float radius,int teeth,System.Numerics.Quaternion rotation,Vec3 color,float offset=0)
 {
  float m=2*radius/teeth,root=radius-1.25f*m,tip=radius+m,rb=radius*MathF.Cos(20*MathF.PI/180),pitch=MathF.Tau/teeth;
  float Inv(float r){float alpha=MathF.Acos(Math.Clamp(rb/r,-1,1));return MathF.Tan(alpha)-alpha;}
  float half=MathF.PI/(2*teeth)+Inv(radius);
  var pts=new List<(float r,float t)>();
  for(int k=0;k<teeth;k++)
  {
   float t=k*pitch+offset;
   pts.Add((root,t-pitch/2));pts.Add((root,t-half));
   for(int j=0;j<=6;j++){float r=Math.Max(root,rb)+(tip-Math.Max(root,rb))*j/6;pts.Add((r,t-half+Inv(r)));}
   for(int j=6;j>=0;j--){float r=Math.Max(root,rb)+(tip-Math.Max(root,rb))*j/6;pts.Add((r,t+half-Inv(r)));}
   pts.Add((root,t+half));pts.Add((root,t+pitch/2));
  }
  Vec3 P(float r,float t,float z)=>c+Transform(new(r*MathF.Cos(t),r*MathF.Sin(t),z),rotation);
  a.G.Material=color.X>.6f?SurfaceMaterial.Brass:SurfaceMaterial.Steel;
  for(int i=0;i<pts.Count;i++)
  {
   var p=pts[i];var q=pts[(i+1)%pts.Count];float inner=radius*.60f;
   a.G.Quad(P(p.r,p.t,-.14f),P(q.r,q.t,-.14f),P(q.r,q.t,.14f),P(p.r,p.t,.14f),color);
   a.G.Quad(P(inner,p.t,.14f),P(p.r,p.t,.14f),P(q.r,q.t,.14f),P(inner,q.t,.14f),color);
   a.G.Quad(P(inner,q.t,-.14f),P(q.r,q.t,-.14f),P(p.r,p.t,-.14f),P(inner,p.t,-.14f),color);
   a.G.Quad(P(inner,p.t,.14f),P(inner,q.t,.14f),P(inner,q.t,-.14f),P(inner,p.t,-.14f),color);
  }
  for(int i=0;i<5;i++){float t=i*MathF.Tau/5+offset;a.Tube(P(.16f,t,0),P(radius*.64f,t,0),radius*.065f,color);}
  a.Tube(c-new Vec3(0,0,.34f),c+new Vec3(0,0,.34f),.19f,ExhibitWorkshop.Silver);
  a.Tube(c+new Vec3(0,0,.34f),c+new Vec3(0,0,.38f),.09f,ExhibitWorkshop.Gold);
 }
}

