using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;
using static MakarovPhysics.Experiments.CosmosScene;
namespace MakarovPhysics.Experiments;
/// <summary>Shared drawing primitives for instruments in the fifth Astronomy ring.</summary>
public static class AstronomyLab
{
    public sealed class CurveCache(int samples = 181)
    {
        private double[] _key=[],_values=[];
        public double[] Sample(double[] key,Func<double,double> sample)
        {
            if(!_key.SequenceEqual(key)){_key=key;_values=Enumerable.Range(0,samples).Select(i=>sample(i/(double)(samples-1))).ToArray();}
            return _values;
        }
    }
    public static readonly Vec3 Blue=new(.21f,.66f,1), Gold=new(1,.61f,.17f), Teal=new(.18f,.88f,.66f);
    public static Vec3 Spectrum(double nm)
    {
        double r=0,g=0,b=0;
        if(nm<440){r=(440-nm)/60;b=1;}else if(nm<490){g=(nm-440)/50;b=1;}else if(nm<510){g=1;b=(510-nm)/20;}else if(nm<580){r=(nm-510)/70;g=1;}else if(nm<645){r=1;g=(645-nm)/65;}else r=1;
        return P(Math.Clamp(r,0,1),Math.Clamp(g,0,1),Math.Clamp(b,0,1));
    }
    public static void Shell(CosmosScene s,Vec3 center,double radius,Vec3 color,int count=1400,float strength=.07f,int salt=42)
    {
        for(int i=0;i<count;i++)s.Dust(center+Direction(i,salt)*(float)(radius*(.985+.03*Noise(i,salt+3))),color,strength);
    }
    public static void Galaxy(CosmosScene s,int count,double phase=0,double radius=3, bool halo=false)
    {
        s.StarRadius=.016f;s.GasRadius=.065f;
        for(int i=0;i<count;i++){
            double r=radius*Math.Pow(Noise(i,812),.6),a=i%4*Math.PI/2+2.4*Math.Log(.15+r)+phase/Math.Sqrt(.4+r)+.34*Gaussian(i,819);
            var p=P(r*Math.Cos(a),.055*Gaussian(i,830)*(1+r*.15),r*Math.Sin(a));
            var color=Vec3.Lerp(new(1,.76f,.42f),new(.36f,.64f,1),(float)Math.Clamp(r/radius,0,1));
            s.Star(p,color,(float)(.4+.6*Noise(i,852)));
            if(i%3==0)s.Dust(p,color,.18f);
        }
        s.Volume(Vec3.Zero,new(.8f,.22f,.8f),1,new(.85f,.48f,.16f),.35f,(float)phase);
        if(halo)for(int i=0;i<2400;i++)s.Dust(Direction(i,841)*(float)(radius*Math.Pow(Noise(i,845),.45)),new(.16f,.30f,.72f),.035f);
    }
    private static double Gaussian(int i,int salt)=>Math.Sqrt(-2*Math.Log(Math.Max(1e-8,Noise(i,salt))))*Math.Cos(Math.Tau*Noise(i,salt+1));
    public static void Dish(CosmosScene s,Vec3 origin,float size=1)
    {
        var g=s.Geometry;g.Material=SurfaceMaterial.Steel;
        Vec3 Q(double x,double y,double z)=>origin+P(x,y,z)*size;
        g.Box(Q(0,-.63,0),new Vec3(.42f,.09f,.42f)*size,new(.18f,.24f,.29f));
        g.Tube(Q(0,-.55,0),Q(0,-.1,0),.12f*size,new(.63f,.7f,.8f));
        for(int ring=0;ring<18;ring++)for(int i=0;i<64;i++){
            double r0=ring/18d,r1=(ring+1)/18d,a=i*Math.Tau/64,b=(i+1)*Math.Tau/64;
            Vec3 V(double r,double angle)=>Q(r*Math.Cos(angle),r*r*.42,r*Math.Sin(angle));
            g.Quad(V(r0,a),V(r1,a),V(r1,b),V(r0,b),new(.65f,.73f,.83f));
        }
        for(int i=0;i<24;i++){double a=i*Math.Tau/24;s.Curve(t=>Q(t*Math.Cos(a),t*t*.42+.006,t*Math.Sin(a)),new(.2f,.32f,.43f),20,false);}
        for(int k=0;k<3;k++){double a=k*Math.Tau/3;g.Tube(Q(.92*Math.Cos(a),.35,.92*Math.Sin(a)),Q(0,.94,0),.017f*size,new(.41f,.50f,.59f));}
        g.Box(Q(0,.94,0),new Vec3(.065f,.11f,.065f)*size,new(.87f,.63f,.22f));
    }
    public static void CurvePanel(Draw2D d,UiRect a,string title,double[] values,double cursor,UiColor color,string units="")
    {
        if(a.W<640||a.H<340||values.Length<2)return;
        float w=Math.Min(300,a.W*.29f),h=132;
        var r=new UiRect(a.X,a.Y,w,h);SceneDashboard.Card(d,r,color);
        SceneDashboard.Paragraph(d,title,r.X+10,r.Y+10,w-20,SceneDashboard.Ink);
        double min=values.Min(),max=values.Max();if(max-min<1e-12)max=min+1;
        float x=r.X+12,y=r.Y+46,pw=w-24,ph=58;
        for(int j=0;j<3;j++)d.Line(x,y+j*ph/2,x+pw,y+j*ph/2,new(.18f,.23f,.3f));
        for(int i=1;i<values.Length;i++)d.Line(x+(i-1)*pw/(values.Length-1),y+ph-(float)((values[i-1]-min)/(max-min))*ph,x+i*pw/(values.Length-1),y+ph-(float)((values[i]-min)/(max-min))*ph,color,1.6f);
        if(cursor>=0){float xx=x+(float)Math.Clamp(cursor,0,1)*pw;d.Line(xx,y,xx,y+ph,SceneDashboard.Gold,1.2f);}
        d.Text(x,y+ph+6,$"{min:G3} … {max:G3} {units}",SceneDashboard.Muted);
    }
}
