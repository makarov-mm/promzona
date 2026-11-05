using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;

/// <summary>Material-lit instrument with a separately composited transparent enclosure.</summary>
public abstract class InstrumentExhibit : EngineeringSiteExhibit
{
    protected readonly DioramaGeometry GlassGeometry=new(){CaptureSurface=true};
    private ExhibitGlass? _glass;
    protected readonly DioramaGeometry EffectsGeometry=new(){CaptureSurface=true};
    private StudioSurface? _effects;
    protected override bool WorkshopFinish=>true;
    protected override float SurfaceFill=>1.65f;
    protected override float StudioFloorY=>EnvironmentStyle==EnvironmentKind.DaySky?-.04f:-1.3f;
    protected override Vec3 LightColor=>EnvironmentStyle==EnvironmentKind.DaySky?new(2.7f,2.55f,2.3f):new(3.2f,3.3f,3.4f);
    protected virtual Vec3 GlassScattering=>Vec3.Zero;
    protected virtual bool LiquidGlass=>false;
    protected virtual bool WaterSectionGlass=>false;
    protected virtual float GlassDepth=>.8f;
    protected virtual Vec3 GlassTint=>new(.006f,.012f,.018f);
    public override void Initialize(IModuleContext c){base.Initialize(c);_glass=new(){Absorption=GlassTint,Liquid=LiquidGlass,WaterSection=WaterSectionGlass,OpticalDepth=GlassDepth,Scattering=GlassScattering};}
    protected sealed override void DrawExperiment(DioramaGeometry g){GlassGeometry.Clear();EffectsGeometry.Clear();DrawInstrument(g,GlassGeometry);}
    protected abstract void DrawInstrument(DioramaGeometry opaque,DioramaGeometry glass);
    protected override void DrawTransparent(Mat4 view,Mat4 projection,Vec3 eye)
    {
        _glass?.SetGeometry(GlassGeometry);_glass?.Draw(view,projection,eye,.45f);
        // Explanatory wave fronts and measured rays are not opaque objects in the shadow map.
        if(EffectsGeometry.SurfacePoints.Count>0){_effects??=new StudioSurface();_effects.Update(EffectsGeometry.SurfacePoints);_effects.Draw(view,projection,eye,LightDirection,LightColor,Mat4.Identity,0,(float)SurfaceTime);}
    }
    public override void Dispose(){_glass?.Dispose();_glass=null;_effects?.Dispose();_effects=null;base.Dispose();}
}

public static class InstrumentDrawing
{
    public static readonly Vec3 Steel=new(.48f,.57f,.62f),White=new(.85f,.88f,.86f),Dark=new(.035f,.06f,.085f),Blue=new(.06f,.42f,.65f),Gold=new(.87f,.55f,.15f),Red=new(.9f,.16f,.06f),Copper=new(.69f,.29f,.12f);
    public static void Bench(DioramaGeometry g,float halfX=3,float halfZ=1.8f)
    {
        g.Material=SurfaceMaterial.Enamel;g.Box(new(0,-.24f,0),new(halfX,.16f,halfZ),new(.38f,.45f,.48f));
        g.Material=SurfaceMaterial.Ceramic;g.Box(new(0,-.055f,0),new(halfX+.035f,.035f,halfZ+.035f),new(.63f,.67f,.66f));
        g.Material=SurfaceMaterial.BrushedAluminium;g.Box(new(0,-.24f,halfZ+.014f),new(halfX,.14f,.025f),Steel);
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2){g.Box(new(x*(halfX-.2f),-.8f,z*(halfZ-.2f)),new(.065f,.4f,.065f),Steel);g.Material=SurfaceMaterial.Rubber;g.Tube(new(x*(halfX-.2f),-1.25f,z*(halfZ-.2f)),new(x*(halfX-.2f),-1.18f,z*(halfZ-.2f)),.11f,Dark);}
        g.Material=SurfaceMaterial.GlazedTile;g.Box(new(0,-1.33f,0),new(halfX+3,.06f,halfZ+3),new(.48f,.51f,.52f));
        g.Material=SurfaceMaterial.Plaster;g.Box(new(0,1.7f,-halfZ-1.8f),new(halfX+3,3,.08f),new(.36f,.43f,.49f));
        g.Material=SurfaceMaterial.Enamel;g.Box(new(0,-.85f,-halfZ-1.68f),new(halfX+3,.18f,.05f),new(.11f,.19f,.25f));
        g.Material=SurfaceMaterial.MachinedMetal;g.Box(new(0,4.38f,-halfZ-1.6f),new(halfX*.85f,.08f,.18f),Dark);
        g.Material=SurfaceMaterial.Emissive;g.Box(new(0,4.30f,-halfZ-1.56f),new(halfX*.82f,.015f,.12f),new(2.3f,2.5f,2.6f));
        g.Material=SurfaceMaterial.Emissive;g.Box(new(0,4.34f,-halfZ-1.40f),new(halfX*.82f,.025f,.008f),new(1.5f,1.6f,1.6f));
        g.Material=SurfaceMaterial.BrushedAluminium;for(int j=-2;j<=2;j++)g.Box(new(j*(halfX+2)/2,1.7f,-halfZ-1.70f),new(.012f,2.5f,.015f),new(.35f,.42f,.46f));
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)InstrumentCraft.Bolt(g,new(x*(halfX-.12f),-.012f,z*(halfZ-.12f)));
    }
    public static void Ring(DioramaGeometry g,Vec3 center,float radius,float thickness,Vec3 color,Vec3 normal)
    {
        Vec3 n=normal.Normalized(),u=Vec3.Cross(Math.Abs(n.Y)<.8f?Vec3.UnitY:Vec3.UnitX,n).Normalized(),v=Vec3.Cross(n,u);
        const int sides=48,section=8;
        for(int i=0;i<sides;i++)for(int j=0;j<section;j++)
        {
            float a=i*MathF.Tau/sides,b=(i+1)*MathF.Tau/sides,c=j*MathF.Tau/section,d=(j+1)*MathF.Tau/section;
            Vec3 radialA=u*MathF.Cos(a)+v*MathF.Sin(a),radialB=u*MathF.Cos(b)+v*MathF.Sin(b);
            Vec3 na=radialA*MathF.Cos(c)+n*MathF.Sin(c),nb=radialB*MathF.Cos(c)+n*MathF.Sin(c),nc=radialB*MathF.Cos(d)+n*MathF.Sin(d),nd=radialA*MathF.Cos(d)+n*MathF.Sin(d);
            Vec3 pa=center+radialA*radius+na*thickness,pb=center+radialB*radius+nb*thickness,pc=center+radialB*radius+nc*thickness,pd=center+radialA*radius+nd*thickness;
            g.SurfaceTriangle(pa,pb,pc,na,nb,nc,color);g.SurfaceTriangle(pa,pc,pd,na,nc,nd,color);
        }
    }
    public static void Lathe(DioramaGeometry g,Vec3 origin,(float Radius,float Height)[] profile,Vec3 color,float arc=MathF.Tau)
    {
        const int count=56;
        for(int j=1;j<profile.Length;j++)
        {
            var low=profile[j-1];var high=profile[j];float dr=high.Radius-low.Radius,dy=high.Height-low.Height;
            for(int i=0;i<count;i++)
            {
                float a=-MathF.PI*.5f+arc*i/count,b=-MathF.PI*.5f+arc*(i+1)/count;
                Vec3 r=new(MathF.Cos(a),0,MathF.Sin(a)),s=new(MathF.Cos(b),0,MathF.Sin(b));
                Vec3 na=(r*dy-Vec3.UnitY*dr).Normalized(),nb=(s*dy-Vec3.UnitY*dr).Normalized();
                Vec3 p=origin+r*low.Radius+Vec3.UnitY*low.Height,q=origin+s*low.Radius+Vec3.UnitY*low.Height,t=origin+s*high.Radius+Vec3.UnitY*high.Height,u=origin+r*high.Radius+Vec3.UnitY*high.Height;
                g.SurfaceTriangle(p,t,q,na,nb,nb,color);g.SurfaceTriangle(p,u,t,na,na,nb,color);
            }
        }
    }
    public static void Coil(DioramaGeometry g,Vec3 from,Vec3 to,float radius,int turns,float wire,Vec3 color)
    {
        Vec3 axis=(to-from).Normalized(),u=Vec3.Cross(Math.Abs(axis.Y)<.9f?Vec3.UnitY:Vec3.UnitX,axis).Normalized(),v=Vec3.Cross(axis,u);
        int segments=turns*16;
        Vec3 P(int i){float t=(float)i/segments,a=t*turns*MathF.Tau;return Vec3.Lerp(from,to,t)+(u*MathF.Cos(a)+v*MathF.Sin(a))*radius;}
        for(int i=0;i<segments;i++)g.Tube(P(i),P(i+1),wire,color);
    }
    public static void Pipe(DioramaGeometry g,Vec3[] route,float radius,Vec3 color)
    {for(int i=1;i<route.Length;i++){g.Tube(route[i-1],route[i],radius,color);if(i<route.Length-1)g.Ellipsoid(route[i],new(radius,radius,radius),color);}}
    public static Vec3 Heat(double value)=>Vec3.Lerp(new(.03f,.16f,.58f),new(1,.16f,.03f),(float)Math.Clamp(value,0,1));
    public static void ImagePanel(DioramaGeometry g,Vec3 lower,float width,float height,int nx,int ny,Func<int,int,double> read,bool spectrum=false)
    {
        g.Material=SurfaceMaterial.Polymer;g.Box(lower+new Vec3(width*.5f,height*.5f,-.04f),new(width*.5f+.08f,height*.5f+.08f,.055f),Dark);
        g.Material=SurfaceMaterial.Flat;
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            double f=read(x,y);Vec3 color=double.IsFinite(f)?spectrum?Heat(f):new Vec3((float)f,(float)f,(float)f):new(.025f,.04f,.06f);
            float x0=lower.X+width*x/nx,y0=lower.Y+height*y/ny,dx=width/nx,dy=height/ny;
            g.Quad(new(x0,y0,lower.Z+.02f),new(x0+dx,y0,lower.Z+.02f),new(x0+dx,y0+dy,lower.Z+.02f),new(x0,y0+dy,lower.Z+.02f),color);
        }
    }
}
