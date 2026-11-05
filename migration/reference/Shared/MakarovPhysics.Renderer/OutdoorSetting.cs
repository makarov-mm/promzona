namespace MakarovPhysics.Renderer;

/// <summary>Opt-in cached outdoor scenery. Apparatus may join its depth pass,
/// or an exhibit can draw it into its own shadow map. No simulation state.</summary>
public sealed class OutdoorSetting(Action<DioramaGeometry> build) : IDisposable
{
    public static readonly Vec3 Sun = new Vec3(-.5f,.85f,.65f).Normalized();
    public readonly EnvironmentStage Sky = new() { Kind=EnvironmentKind.DaySky, DetailedWeather=true, SunDir=Sun };
    private StudioSurface? _surface;
    private ShadowMap? _shadow;
    public bool Terrestrial = true;
    public float Time;
    public StudioSurface Surface
    {
        get
        {
            if (_surface is not null) return _surface;
            var g=new DioramaGeometry {CaptureSurface=true,BevelBoxes=true};
            build(g);
            var surface=new StudioSurface(Math.Max(3,g.SurfacePoints.Count)) {DetailedFinish=true,Outdoor=Terrestrial};
            try { surface.Update(g.SurfacePoints); } catch { surface.Dispose(); throw; }
            return _surface=surface;
        }
    }
    public void DrawSky(Mat4 view,Mat4 proj,Vec3 eye,float far) => Sky.Draw(view,proj,eye,Time,far);
    public void Draw(Mat4 view,Mat4 proj,Vec3 eye,float radius,Vec3 centre,float far,bool sky=true)
    {
        _shadow??=new ShadowMap(2048);
        var ls=ShadowMap.LightSpace(centre,Sun,radius);
        uint draw=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
        var vp=GL.PhysicalViewport();bool sc=GL.IsEnabled(GL.SCISSOR_TEST);
        GL.Disable(GL.SCISSOR_TEST);GL.Enable(GL.DEPTH_TEST);GL.DepthMask(true);GL.Disable(GL.BLEND);
        try { _shadow.BeginDepthPass();Surface.DrawDepth(ls,Time); }
        finally {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,draw);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);
            GL.RestorePhysicalViewport(vp.X,vp.Y,vp.Width,vp.Height);if(sc)GL.Enable(GL.SCISSOR_TEST);
        }
        if(sky)DrawSky(view,proj,eye,far);
        GL.Enable(GL.DEPTH_TEST);GL.DepthMask(true);GL.Disable(GL.BLEND);
        Surface.Draw(view,proj,eye,Sun,new(1.05f,1.02f,.97f),ls,_shadow.DepthTexture,Time,environmentFill:1.25f);
    }
    public void Dispose(){_surface?.Dispose();_surface=null;_shadow?.Dispose();_shadow=null;Sky.Dispose();}
}
