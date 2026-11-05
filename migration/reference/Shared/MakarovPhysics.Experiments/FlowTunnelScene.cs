using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Opt-in optical flow bench. The clear test section matches the
/// numerical domain. Ducts, frame, rake and supports are illustrative apparatus,
/// not additional solver boundaries. Static geometry is uploaded only once.</summary>
public sealed class FlowTunnelScene : IDisposable
{
    private readonly StudioSurface _surface = new(180000);
    private readonly ExhibitGlass _glass = new();
    private readonly ShadowMap _shadow = new(1536);
    private readonly StudioCyclorama _room = new();
    private readonly Vec3 _half;
    private readonly float _size;

    public FlowTunnelScene(Vec3 half, bool water)
    {
        _half = half;
        _size = half.X / 1.8f;
        _room.FloorY = -half.Y - .85f * _size;
        _room.BackdropColor = water ? new Vec3(.095f, .14f, .17f) : new Vec3(.13f, .15f, .18f);
        _room.FloorColor = new(.19f, .22f, .24f);
        _room.Radius = 10 * _size;
        _room.CoveRadius = 3 * _size;
        var g = new DioramaGeometry { CaptureSurface = true };
        Vec3 steel = new(.60f, .66f, .69f), dark = new(.035f, .048f, .06f);
        Vec3 paint = water ? new Vec3(.075f, .40f, .48f) : new Vec3(.23f, .32f, .43f);
        float x = half.X, y = half.Y, z = half.Z, s = _size;
        g.Material = SurfaceMaterial.Enamel;
        g.Box(new(0, -y - .20f * s, 0), new(x + .48f * s, .12f * s, z + .24f * s), paint);
        g.Material = SurfaceMaterial.Steel;
        g.Box(new(0, -y - .34f * s, 0), new(x + .44f * s, .035f * s, z + .20f * s), steel);

        foreach (int side in new[] { -1, 1 })
        {
            float fx = side * x;
            g.Material = SurfaceMaterial.Enamel;

            foreach (int edge in new[] { -1, 1 })
            {
                g.Box(new Vec3(fx, edge * (y + .025f * s), 0), new Vec3(.07f * s, .06f * s, z + .07f * s), paint);
                g.Box(new Vec3(fx, 0, edge * (z + .025f * s)), new Vec3(.07f * s, y, .055f * s), paint);
            }

            g.Material = SurfaceMaterial.Steel;

            foreach (int sy in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 })
                {
                    var bolt = new Vec3(fx + side * .073f * s, sy * (y + .025f * s), sz * (z + .025f * s));
                    g.Tube(bolt, bolt + new Vec3(side * .025f * s, 0, 0), .031f * s, steel);
                }
            }

            // Short tapered ducts outside the simulated section.
            float outer = side * (x + .38f * s);
            g.Material = SurfaceMaterial.Enamel;

            foreach (int sy in new[] { -1, 1 })
                g.Quad(new Vec3(fx, sy * y, -z), new Vec3(fx, sy * y, z), new Vec3(outer, sy * (y + .10f * s), z + .1f * s), new Vec3(outer, sy * (y + .10f * s), -z - .1f * s), paint);

            foreach (int sz in new[] { -1, 1 })
                g.Quad(new Vec3(fx, -y, sz * z), new Vec3(outer, -y - .1f * s, sz * (z + .1f * s)), new Vec3(outer, y + .1f * s, sz * (z + .1f * s)), new Vec3(fx, y, sz * z), paint);

            foreach (int sz in new[] { -1, 1 })
            {
                g.Material = SurfaceMaterial.Steel;
                var foot = new Vec3(side * (x - .24f * s), -y - .74f * s, sz * (z + .06f * s));
                g.Tube(foot, foot + new Vec3(0, .43f * s, 0), .045f * s, steel);
                g.Material = SurfaceMaterial.Rubber;
                g.Box(foot, new(.11f * s, .04f * s, .10f * s), dark);
            }
        }

        // Brushed lower rails keep the top and front viewing area clear.
        g.Material = SurfaceMaterial.Steel;

        foreach (int sz in new[] { -1, 1 })
            g.Box(new Vec3(0, -y - .015f * s, sz * (z + .025f * s)), new Vec3(x, .025f * s, .025f * s), steel);

        // Slender top glazing retainers and a rear inspection light.
        foreach (int sz in new[] { -1, 1 })
            g.Box(new Vec3(0, y + .008f * s, sz * (z + .012f * s)), new Vec3(x, .009f * s, .009f * s), steel);

        g.Material = SurfaceMaterial.Emissive;
        g.Box(new Vec3(0, y - .006f * s, -z - .006f * s), new Vec3(x - .08f * s, .006f * s, .006f * s), new Vec3(.18f, .54f, .66f));
        g.Material = SurfaceMaterial.Steel;

        // Injection rake stays outside the numerical inlet.
        g.Tube(new Vec3(-x - .16f * s, -y * .8f, -z * .82f), new Vec3(-x - .16f * s, y * .8f, -z * .82f), .022f * s, steel);

        for (int i = 0; i < 7; i++)
        {
            float ry = (-.72f + i * .24f) * y;
            g.Tube(new Vec3(-x - .16f * s, ry, -z * .82f), new Vec3(-x - .09f * s, ry, -z * .82f), .012f * s, steel);
        }

        // Engraved ruler and an inlaid flow arrow, readable without captions.
        g.Material = SurfaceMaterial.Ceramic;

        for (int i = 0; i <= 40; i++)
            g.Box(new Vec3(-x + i * (2 * x / 40), -y - .069f * s, z + .135f * s), new Vec3(.004f * s, .0015f * s, (i % 5 == 0 ? .045f : .023f) * s), new Vec3(.72f, .81f, .83f));

        g.Material = SurfaceMaterial.Brass;
        ExhibitGeometry.Arrow(g, new Vec3(-.36f * s, -y - .072f * s, z + .20f * s), new Vec3(.36f * s, -y - .072f * s, z + .20f * s), new Vec3(.83f, .58f, .23f), .012f * s);
        _surface.Update(g.SurfacePoints);
        var glass = new DioramaGeometry { CaptureSurface = true };
        // Thin shells, not a coloured volume. Keep absorption subtle.
        glass.Box(Vec3.Zero, new(x, y, z), new(1, 1, 1));
        _glass.SetGeometry(glass);
    }

    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, float time)
    {
        _room.Draw(view, proj, eye, 60 * _size);
        Vec3 light = KeyLight.FromView(view);
        Mat4 lightSpace = ShadowMap.LightSpace(new(0, -.3f * _size, 0), light, (_half.X + .8f * _size) * 1.4f);
        uint draw = (uint)GL.GetInteger(0x8CA6), read = (uint)GL.GetInteger(0x8CAA);
        (int X, int Y, int Width, int Height) viewport = GL.PhysicalViewport();
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
        _surface.Draw(view, proj, eye, light, new Vec3(1.65f, 1.72f, 1.8f), lightSpace, _shadow.DepthTexture, time, .57f, environmentFill: 1);
        GL.Enable(GL.CULL_FACE);
    }

    public void DrawDepth(Mat4 view, Mat4 proj) => _surface.DrawDepth(proj*view);
    public void DrawGlass(Mat4 view, Mat4 proj, Vec3 eye, float strength) => _glass.Draw(view,proj,eye,strength);

    public void Dispose()
    {
        _surface.Dispose();
        _glass.Dispose();
        _shadow.Dispose();
        _room.Dispose();
    }
}
