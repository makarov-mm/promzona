using System.Numerics;
using MakarovPhysics.Renderer;
using MakarovPhysics.Numerics;
using static MakarovPhysics.Experiments.CosmosScene;
namespace MakarovPhysics.Experiments;
/// <summary>Procedural space hardware: material-shaded surfaces, cell grids,
/// ribbed wheels, multilayer blankets and mechanical joints. Shapes receive the
/// model pose; no time or model state is modified during rendering.</summary>
public sealed class SpaceHardware(CosmosScene scene,Vec3 origin,Quaternion orientation,float hardwareScale=1)
{
    public static readonly Vec3 White=new(.80f,.84f,.85f),Metal=new(.42f,.50f,.58f),Gold=new(.82f,.54f,.17f),Dark=new(.035f,.055f,.075f),Blue=new(.025f,.10f,.30f),Cyan=new(.10f,.68f,.87f),Orange=new(.95f,.30f,.055f);
    public readonly CosmosScene Scene=scene;
    public DioramaGeometry G=>scene.Geometry;
    public Vec3 At(Vec3 p){var q=Vector3.Transform(new Vector3(p.X,p.Y,p.Z),orientation);return origin+new Vec3(q.X,q.Y,q.Z)*hardwareScale;}
    public Vec3 Axis(Vec3 p){var q=Vector3.Transform(new Vector3(p.X,p.Y,p.Z),orientation);return new(q.X,q.Y,q.Z);}
    public void Box(Vec3 p,Vec3 half,Vec3 color,SurfaceMaterial material=SurfaceMaterial.Enamel){G.Material=material;G.Box(At(p),half*hardwareScale,color,Axis(Vec3.UnitX),Axis(Vec3.UnitY),Axis(Vec3.UnitZ));}
    public void Tube(Vec3 a,Vec3 b,float radius,Vec3 color,SurfaceMaterial material=SurfaceMaterial.Steel){G.Material=material;G.Tube(At(a),At(b),radius*hardwareScale,color);}
    public void Ball(Vec3 p,float radius,Vec3 color,SurfaceMaterial material=SurfaceMaterial.Steel){G.Material=material;G.Ellipsoid(At(p),new(radius*hardwareScale,radius*hardwareScale,radius*hardwareScale),color);}
    public void Ring(Vec3 p,Vec3 axis,float inner,float outer,float depth,Vec3 color){G.Material=SurfaceMaterial.Steel;MachineGeometry.Annulus(G,At(p),Axis(axis),inner*hardwareScale,outer*hardwareScale,depth*hardwareScale,0,MathF.Tau,color,40);}
    public void Panel(Vec3 p,float halfX,float halfZ)
    {
        Box(p,new(halfX,.028f,halfZ),Metal,SurfaceMaterial.Steel);
        int nx=6,nz=5;for(int i=0;i<nx;i++)for(int j=0;j<nz;j++)
        {float x=-halfX+(i+.5f)*2*halfX/nx,z=-halfZ+(j+.5f)*2*halfZ/nz;float shade=(float)(.85+.25*Noise(i*9+j,44));Box(p+new Vec3(x,.036f,z),new(halfX/nx*.91f,.008f,halfZ/nz*.93f),Blue*shade,SurfaceMaterial.Enamel);}
        for(int i=0;i<=nx;i++)Tube(p+new Vec3(-halfX+i*2*halfX/nx,.05f,-halfZ),p+new Vec3(-halfX+i*2*halfX/nx,.05f,halfZ),.003f,new(.40f,.50f,.71f));
    }
    public void FoilBox(Vec3 p,Vec3 half)
    {
        Box(p,half,Gold,SurfaceMaterial.Foil);
        for(int i=0;i<9;i++){float x=(i/8f*2-1)*half.X;Tube(p+new Vec3(x,half.Y+.003f,-half.Z),p+new Vec3(x+.02f,half.Y+.012f,half.Z),.009f,new(.98f,.70f,.27f),SurfaceMaterial.Brass);}
        for(int i=-1;i<=1;i+=2)for(int j=-1;j<=1;j+=2)Tube(p+new Vec3(i*half.X,-half.Y,j*half.Z),p+new Vec3(i*half.X,half.Y,j*half.Z),.015f,White);
    }
    public void Dish(Vec3 p,float radius)
    {
        G.Material=SurfaceMaterial.Steel;
        for(int j=0;j<7;j++)for(int i=0;i<40;i++)
        {Vec3 Point(int ring,int k){double r=radius*ring/7,a=k*Math.Tau/40;return At(p+P(r*Math.Cos(a),r*r/radius*.48,r*Math.Sin(a)));}G.Quad(Point(j,i),Point(j+1,i),Point(j+1,i+1),Point(j,i+1),White);}
        for(int i=0;i<3;i++){double a=i*Math.Tau/3;Tube(p+P(radius*.85*Math.Cos(a),radius*.35,radius*.85*Math.Sin(a)),p+new Vec3(0,radius*.9f,0),.008f,Metal);}Ball(p+new Vec3(0,radius*.9f,0),radius*.07f,Dark);
    }
    public void Nozzle(Vec3 p,float radius,double throttle,double time,Vec3? tint=null)
    {
        Ring(p,Vec3.UnitY,radius*.38f,radius*.50f,.04f,Metal);
        G.Material=SurfaceMaterial.Steel;
        for(int i=0;i<32;i++){double a=i*Math.Tau/32,b=(i+1)*Math.Tau/32;G.Quad(At(p+P(Math.Cos(a)*radius*.45,0,Math.Sin(a)*radius*.45)),At(p+P(Math.Cos(b)*radius*.45,0,Math.Sin(b)*radius*.45)),At(p+P(Math.Cos(b)*radius,-radius*1.4,Math.Sin(b)*radius)),At(p+P(Math.Cos(a)*radius,-radius*1.4,Math.Sin(a)*radius)),Dark*2);}
        if(throttle<=0)return;
        scene.GasRadius=.055f*hardwareScale;scene.StarRadius=.012f*hardwareScale;
        for(int i=0;i<240;i++){double f=(Noise(i,11)+time*1.8)%1,a=Noise(i,22)*Math.Tau,r=radius*(.30+f*.9)*Math.Sqrt(Noise(i,5)),y=-radius*(1.5+f*8*Math.Sqrt(throttle));scene.Dust(At(p+P(r*Math.Cos(a),y,r*Math.Sin(a))),tint??new Vec3(.22f,.45f,1), (float)((1-f)*.5));if(i%3==0)scene.Star(At(p+P(r*.6*Math.Cos(a),y,r*.6*Math.Sin(a))),new(.6f,.82f,1),(float)(1-f));}
    }
    public void Satellite(double deployment=1,bool cutaway=false)
    {
        FoilBox(Vec3.Zero,new(.43f,.34f,.47f));
        if(cutaway){Box(new(0,.35f,0),new(.39f,.015f,.43f),Dark);for(int i=-1;i<=1;i++)Box(new(i*.21f,.41f,0),new(.07f,.045f,.33f),i==0?Cyan:Metal);}
        Dish(new(0,.36f,.03f),.29f);
        for(int s=-1;s<=1;s+=2){Tube(new(s*.42f,0,0),new(s*(.48f+(float)deployment*1.3f),0,0),.025f,Metal);Panel(new(s*(.65f+(float)deployment*.85f),0,0),.18f+(float)deployment*.65f,.55f);}
        for(int s=-1;s<=1;s+=2)for(int z=-1;z<=1;z+=2){Box(new(s*.4f,-.25f,z*.42f),new(.06f,.06f,.06f),Dark);Tube(new(s*.4f,-.25f,z*.42f),new(s*.53f,-.25f,z*.42f),.025f,Metal);}
        Tube(new(-.16f,.28f,-.40f),new(-.16f,.92f,-.40f),.012f,White);Ball(new(-.16f,.93f,-.40f),.025f,Orange,SurfaceMaterial.Emissive);
    }
    public void Lander(double thrust,double time)
    {
        FoilBox(new(0,-.05f,0),new(.48f,.27f,.48f));
        Box(new(0,.45f,0),new(.37f,.22f,.35f),White);Box(new(0,.46f,.36f),new(.20f,.095f,.01f),Blue);
        Ring(new(0,.72f,0),Vec3.UnitY,.15f,.23f,.09f,Metal);Dish(new(-.29f,.78f,-.12f),.18f);
        for(int i=0;i<4;i++){double a=Math.PI*.25+i*Math.PI*.5;var inner=P(.38*Math.Cos(a),-.1,.38*Math.Sin(a));var knee=P(.82*Math.Cos(a),-.42,.82*Math.Sin(a));var foot=P(1.05*Math.Cos(a),-1.05,1.05*Math.Sin(a));Tube(inner,knee,.045f,Gold);Tube(knee,foot,.035f,Metal);Tube(P(.36*Math.Cos(a),-.32,.36*Math.Sin(a)),foot,.018f,White);Ring(foot,Vec3.UnitY,.015f,.16f,.035f,Metal);}
        Nozzle(new(0,-.30f,0),.23f,thrust,time);
    }
    public void DockingCraft(double plume,double time)
    {
        Tube(new(0,0,-.50f),new(0,0,.35f),.43f,White);FoilBox(new(0,0,-.50f),new(.39f,.31f,.17f));
        Ring(new(0,0,.40f),Vec3.UnitZ,.19f,.29f,.12f,Metal);Ring(new(0,0,.48f),Vec3.UnitZ,.18f,.22f,.02f,Orange);
        for(int i=0;i<8;i++){double a=i*Math.Tau/8;Ball(P(.245*Math.Cos(a),.245*Math.Sin(a),.53),.018f,Gold);}
        for(int s=-1;s<=1;s+=2){Tube(new(s*.4f,0,-.3f),new(s*1.1f,0,-.3f),.024f,Metal);Panel(new(s*1.25f,0,-.3f),.55f,.50f);Box(new(s*.29f,.29f,.12f),new(.055f,.055f,.055f),Dark);}
        if(plume>0)for(int s=-1;s<=1;s+=2){var nozzle=new SpaceHardware(scene,At(new(s*.33f,-.2f,-.4f)),orientation,hardwareScale*.25f);nozzle.Nozzle(Vec3.Zero,.2f,plume,time);}
    }
    public static void Earth(CosmosScene s,double time=0){s.Body(new(0,-10,-13),9,CosmicSurface.Ocean,new(.40f,.65f,.96f),time*.015,1.5);}
    public static void Terrain(CosmosScene s,Func<double,double,double> height,bool mars,Vector3 center,float scale=.32f)
    {
        var g=s.Geometry;g.Material=SurfaceMaterial.Stone;Vec3 At(double x,double z)=>P((x-center.X)*scale,(height(x,z)-center.Y)*scale,(z-center.Z)*scale);
        Vec3 Normal(double x,double z)=>P(-(height(x+.02,z)-height(x-.02,z))/.04,1,-(height(x,z+.02)-height(x,z-.02))/.04).Normalized();
        for(int i=0;i<80;i++)for(int j=0;j<80;j++){double x=-12+i*.3,z=-12+j*.3;float v=(float)(.94+.055*Math.Sin(x*2.1+z*.8)*Math.Cos(z*1.7));var c=mars?new Vec3(.43f,.205f,.10f)*v:new Vec3(.34f,.34f,.32f)*v;var a=At(x,z);var b=At(x,z+.3);var cc=At(x+.3,z+.3);var d=At(x+.3,z);var na=Normal(x,z);var nb=Normal(x,z+.3);var nc=Normal(x+.3,z+.3);var nd=Normal(x+.3,z);g.SurfaceTriangle(a,b,cc,na,nb,nc,c);g.SurfaceTriangle(a,cc,d,na,nc,nd,c);}
        for(int i=0;i<60;i++){double x=Noise(i,17)*22-11,z=Noise(i,18)*22-11;float size=(float)(.10+.30*Noise(i,19))*scale;var at=At(x,z);g.Ellipsoid(at,new(size,size*.7f,size*.8f),mars?new(.29f,.13f,.07f):new(.24f,.25f,.25f));}
    }
    public static void Rover(CosmosScene s,RoverDynamics m,bool mars)
        => Rover(s,m,mars,true);
    public static void Rover(CosmosScene s,RoverDynamics m,bool mars,bool surroundings)
    {
        const float scale=.72f;
        Vector3 center=new(m.Position.X,m.Position.Y-.7f,m.Position.Z);
        if(surroundings) Terrain(s,m.Terrain,mars,center,scale);
        var q=Quaternion.CreateFromYawPitchRoll((float)-m.Heading,(float)m.Roll,(float)m.Pitch);
        var h=new SpaceHardware(s,new(0,.7f*scale,0),q,scale);
        h.FoilBox(new(0,.07f,0),new(.64f,.22f,.44f));h.Box(new(0,.31f,0),new(.70f,.035f,.48f),White);
        if(!mars)h.Panel(new(-.12f,.38f,0),.5f,.45f);else{for(int j=-2;j<=2;j++)h.Box(new(-.62f,.18f,j*.12f),new(.20f,.23f,.014f),Dark,SurfaceMaterial.Steel);}
        h.Tube(new(.4f,.30f,0),new(.4f,1.04f,0),.035f,White);h.Box(new(.42f,1.06f,0),new(.12f,.07f,.21f),White);
        for(int j=-1;j<=1;j+=2){h.Ball(new(.55f,1.06f,j*.12f),.055f,Blue);h.Box(new(.69f,.1f,j*.36f),new(.015f,.07f,.10f),White);}
        h.Dish(new(-.26f,.45f,.2f),.17f);
        for(int i=0;i<6;i++)
        {
            var wp=m.Wheels[i];var wheel=P((wp.X-center.X)*scale,(wp.Y-center.Y)*scale,(wp.Z-center.Z)*scale);var axis=P(-Math.Sin(m.Heading),0,Math.Cos(m.Heading));
            var g=s.Geometry;g.Material=SurfaceMaterial.Steel;MachineGeometry.Annulus(g,wheel,axis,.10f*scale,.24f*scale,.13f*scale,0,MathF.Tau,mars?Metal:new(.38f,.40f,.42f),40);
            g.Tube(wheel-axis*.075f*scale,wheel+axis*.075f*scale,.075f*scale,Gold);
            for(int n=0;n<20;n++){double a=n*Math.Tau/20+m.WheelAngle;var radial=P(Math.Cos(m.Heading)*Math.Cos(a),Math.Sin(a),Math.Sin(m.Heading)*Math.Cos(a));g.Tube(wheel+radial*.239f*scale-axis*.074f*scale,wheel+radial*.239f*scale+axis*.074f*scale,.013f*scale,White);}
            var mount=h.At(new((i%3-1)*.45f,-.12f,i<3?-.44f:.44f));var mid=(mount+wheel)*.5f+new Vec3(0,.12f,0);g.Tube(mount,mid,.032f,White);g.Tube(mid,wheel,.027f,Metal);g.Ellipsoid(mid,new(.05f,.05f,.05f),Orange);
        }
        if(m.Sampling){h.Tube(new(.6f,0,.35f),new(.94f,-.33f,.35f),.025f,White);h.Tube(new(.94f,-.33f,.35f),new(.94f,-.5f,.35f),.027f,Metal);}
        if(!surroundings)return;
        foreach(var target in m.Targets){var p=P((target.X-center.X)*scale,(m.Terrain(target.X,target.Z)-center.Y)*scale,(target.Z-center.Z)*scale);s.Geometry.Material=SurfaceMaterial.Emissive;MachineGeometry.Annulus(s.Geometry,p,Vec3.UnitY,.28f,.31f,.012f,0,MathF.Tau,Cyan,32);}
        for(int i=1;i<m.Trail.Count;i++){var a=m.Trail[i-1]-center;var b=m.Trail[i]-center;s.Line(P(a.X*scale,(m.Terrain(m.Trail[i-1].X,m.Trail[i-1].Z)-center.Y+.03)*scale,a.Z*scale),P(b.X*scale,(m.Terrain(m.Trail[i].X,m.Trail[i].Z)-center.Y+.03)*scale,b.Z*scale),Gold);}
    }
}
