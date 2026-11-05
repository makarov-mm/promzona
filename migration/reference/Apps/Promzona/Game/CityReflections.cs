using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Double-buffered local environment probe. One 128px face per frame only
/// during refresh; static city and sky, no recursion or reflected actors.</summary>
internal sealed class CityReflections : IDisposable
{
    private const uint Cube=0x8513,Face=0x8515;
    private uint _front,_back,_framebuffer,_depth;
    private int _face=-1;
    private Vec3 _centre,_pending;
    private double _hour=-100,_rain=-100,_pendingHour,_pendingRain,_nextTime;
    private int _generation;
    public bool Ready {get;private set;}
    private bool _capturing;
    public int CapturedFaces {get;private set;}
    private static readonly Vec3[] Directions=[new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)];
    private static readonly Vec3[] Ups=[new(0,-1,0),new(0,-1,0),new(0,0,1),new(0,0,-1),new(0,-1,0),new(0,-1,0)];
    public CityReflections()
    {
        uint draw=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
        try {
            _front=CreateCube();_back=CreateCube();_depth=GL.GenRenderbuffer();
            GL.BindRenderbuffer(GL.RENDERBUFFER,_depth);GL.RenderbufferStorage(GL.RENDERBUFFER,GL.DEPTH_COMPONENT24,128,128);
            _framebuffer=GL.GenFramebuffer();GL.BindFramebuffer(GL.FRAMEBUFFER,_framebuffer);
            GL.FramebufferRenderbuffer(GL.FRAMEBUFFER,GL.DEPTH_ATTACHMENT,GL.RENDERBUFFER,_depth);
            GL.FramebufferTexture2D(GL.FRAMEBUFFER,GL.COLOR_ATTACHMENT0,Face,_back,0);GL.DrawBuffer(GL.COLOR_ATTACHMENT0);
            if(GL.CheckFramebufferStatus(GL.FRAMEBUFFER)!=GL.FRAMEBUFFER_COMPLETE)throw new InvalidOperationException("City reflection target incomplete");
        }catch{Dispose();throw;}finally{GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,draw);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);}
    }
    private static uint CreateCube()
    {
        uint result=GL.GenTexture();GL.ActiveTexture(GL.TEXTURE0);GL.BindTexture(Cube,result);
        for(uint i=0;i<6;i++)GL.TexImage2D(Face+i,0,(int)GL.RGBA16F,128,128,0,GL.RGBA,GL.FLOAT);
        GL.TexParameteri(Cube,GL.TEXTURE_MIN_FILTER,0x2703);GL.TexParameteri(Cube,GL.TEXTURE_MAG_FILTER,(int)GL.LINEAR);
        GL.TexParameteri(Cube,GL.TEXTURE_WRAP_S,(int)GL.CLAMP_TO_EDGE);GL.TexParameteri(Cube,GL.TEXTURE_WRAP_T,(int)GL.CLAMP_TO_EDGE);GL.TexParameteri(Cube,0x8072,(int)GL.CLAMP_TO_EDGE);
        GL.GenerateMipmap(Cube);return result;
    }
    public void Update(Vec3 centre,double hour,double rain,double time,int generation,Action<Mat4,Mat4,Vec3> render)
    {
        if(_face<0){
            if(Ready&&(centre-_centre).Length()<22&&Math.Abs(hour-_hour)<.25&&Math.Abs(rain-_rain)<.12&&generation==_generation&&time<_nextTime)return;
            _pending=centre;_pendingHour=hour;_pendingRain=rain;_generation=generation;_face=0;
        }
        var viewport=GL.PhysicalViewport();uint draw=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
        int scale=GL.RenderScale;bool scissor=GL.IsEnabled(GL.SCISSOR_TEST);
        try {
            GL.RenderScale=1;GL.Disable(GL.SCISSOR_TEST);GL.BindFramebuffer(GL.FRAMEBUFFER,_framebuffer);
            GL.FramebufferTexture2D(GL.FRAMEBUFFER,GL.COLOR_ATTACHMENT0,Face+(uint)_face,_back,0);
            GL.Viewport(0,0,128,128);GL.DepthMask(true);GL.ClearColor(0,0,0,1);GL.Clear(GL.COLOR_BUFFER_BIT|GL.DEPTH_BUFFER_BIT);
            _capturing=true;
            render(Mat4.LookAt(_pending,_pending+Directions[_face],Ups[_face]),Mat4.Perspective(MathF.PI/2,1,.2f,180),_pending);
            CapturedFaces++;_face++;
            if(_face==6){
                GL.ActiveTexture(GL.TEXTURE0);GL.BindTexture(Cube,_back);GL.GenerateMipmap(Cube);
                (_front,_back)=(_back,_front);_centre=_pending;_hour=_pendingHour;_rain=_pendingRain;Ready=true;_face=-1;_nextTime=time+12;
            }
        }finally{
            _capturing=false;
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,draw);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);GL.RenderScale=scale;
            GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);GL.ActiveTexture(GL.TEXTURE0);GL.DepthMask(true);
            if(scissor)GL.Enable(GL.SCISSOR_TEST);else GL.Disable(GL.SCISSOR_TEST);
        }
    }
    public void Apply(Shader shader)
    {
        // Matching vehicle program is already bound by the draw pass.
        shader.SetInt("uProbe",12);shader.SetInt("uProbeReady",Ready&&!_capturing?1:0);shader.SetVec3("uProbeCentre",_centre);
        GL.ActiveTexture(GL.TEXTURE0+12);GL.BindTexture(Cube,_front);GL.ActiveTexture(GL.TEXTURE0);
    }
    public void Dispose(){if(_front!=0)GL.DeleteTexture(_front);if(_back!=0)GL.DeleteTexture(_back);if(_depth!=0)GL.DeleteRenderbuffer(_depth);if(_framebuffer!=0)GL.DeleteFramebuffer(_framebuffer);_front=_back=_depth=_framebuffer=0;Ready=false;}
}

