using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Streamed machined apparatus with the same material and shadow path
/// as the workshop exhibits. BeginLit queues geometry; Flush precedes overlays.</summary>
public sealed class MechanismStudio : IDisposable
{
    public readonly DioramaGeometry Geometry = new() { CaptureSurface = true };
    private readonly StudioSurface _surface = new();
    private readonly ShadowMap _shadow = new(1536);
    private Mat4 _view, _projection;
    private Vec3 _eye;
    public void MaterialBrushedMetal() => Geometry.Material=SurfaceMaterial.Steel;
    public void MaterialSteel() => Geometry.Material=SurfaceMaterial.Steel;
    public void MaterialRubber() => Geometry.Material=SurfaceMaterial.Rubber;
    public void MaterialPaintedMetal() => Geometry.Material=SurfaceMaterial.Enamel;
    public void MaterialPlastic() => Geometry.Material=SurfaceMaterial.Ceramic;
    public void MaterialChalk() => Geometry.Material=SurfaceMaterial.Flat;
    public void DrawBox(Vec3 p, Vec3 half, Vec3 color) => Geometry.Box(p,half,color);
    public void DrawBob(Vec3 p, float radius, Vec3 color) => Geometry.Ellipsoid(p,new(radius,radius,radius),color);
    public void DrawRod(Vec3 a, Vec3 b, Vec3 color, float radius) => Geometry.Tube(a,b,radius,color);

    public void BeginLit(Mat4 view, Mat4 projection, Vec3 eye)
    {
        _view = view;
        _projection = projection;
        _eye = eye;
        Geometry.Clear();
        Geometry.Eye = eye;
        MaterialSteel();
    }

    public void Bench(float halfWidth, float halfDepth, float top)
    {
        Geometry.Material = SurfaceMaterial.Stone;
        Geometry.Box(new Vec3(0, top - .075f, 0), new Vec3(halfWidth, .075f, halfDepth), new Vec3(.15f, .19f, .21f));
        Geometry.Material = SurfaceMaterial.Steel;
        Geometry.Box(new Vec3(0, top - .17f, 0), new Vec3(halfWidth - .06f, .035f, halfDepth - .06f), RoboticsGeometry.Metal);

        foreach (int x in new[] { -1, 1 })
        {
            foreach (int z in new[] { -1, 1 })
            {
                var p = new Vec3(x * (halfWidth - .17f), top - .9f, z * (halfDepth - .16f));
                Geometry.Material = SurfaceMaterial.Steel;
                Geometry.Tube(p, p + new Vec3(0, .73f, 0), .045f, RoboticsGeometry.Metal);
                Geometry.Material = SurfaceMaterial.Rubber;
                Geometry.Box(p, new Vec3(.08f, .035f, .08f), RoboticsGeometry.Dark);
            }
        }
    }

    public void Flush(float time, Vec3 center, float extent)
    {
        _surface.Update(Geometry.SurfacePoints);
        Vec3 light = new(-.45f, .85f, .65f);
        Mat4 lightSpace = ShadowMap.LightSpace(center, light, extent);
        uint draw = (uint)GL.GetInteger(0x8CA6), read = (uint)GL.GetInteger(0x8CAA);
        var viewport = GL.PhysicalViewport();
        int scale = GL.RenderScale;
        bool scissor = GL.IsEnabled(GL.SCISSOR_TEST);

        try
        {
            GL.RenderScale = 1;
            GL.Disable(GL.SCISSOR_TEST);
            _shadow.BeginDepthPass();
            _surface.DrawDepth(lightSpace);
        }
        finally
        {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);
            GL.RenderScale = scale;
            GL.RestorePhysicalViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            if (scissor) GL.Enable(GL.SCISSOR_TEST);
        }

        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Disable(GL.CULL_FACE);
        _surface.Draw(_view, _projection, _eye, light, new Vec3(1.7f, 1.62f, 1.48f), lightSpace, _shadow.DepthTexture, time, .65f, environmentFill: 1.2f);
        GL.Enable(GL.CULL_FACE);
    }

    public void Dispose()
    {
        _shadow.Dispose();
        _surface.Dispose();
    }
}
