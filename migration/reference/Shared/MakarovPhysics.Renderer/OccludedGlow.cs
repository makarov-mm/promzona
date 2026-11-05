namespace MakarovPhysics.Renderer;

/// <summary>Fresh, depth-tested emission. No screen-space history survives a
/// camera move. The single-sample fallback draws against the caller's depth.
/// Only opt-in laboratories use this pass; older post effects are unchanged.</summary>
public sealed class OccludedGlow : IDisposable
{
    private readonly PostFxStack _fx = new()
    {
        BloomStrength = .28f, BloomThreshold = .72f, BloomCap = .8f,
        Exposure = .8f, Vignette = .08f
    };

    public void Draw(int width, int height, Action drawDepth, Action drawLight)
    {
        uint draw = (uint)GL.GetInteger(0x8CA6), read = (uint)GL.GetInteger(0x8CAA);
        var viewport = GL.PhysicalViewport();
        bool scissor = GL.IsEnabled(GL.SCISSOR_TEST);
        int scale = GL.RenderScale;
        try
        {
            // Use physical pixels once, including at high DPI and in captures.
            GL.RenderScale = 1;
            GL.Disable(GL.SCISSOR_TEST);
            GL.DepthMask(true);
            _fx.BeginScene(viewport.Width, viewport.Height, 0);
            if (_fx.SceneMsaaSamples > 0)
            {
                GL.Enable(GL.DEPTH_TEST);
                GL.ColorMask(false, false, false, false);
                drawDepth();
                GL.ColorMask(true, true, true, true);
                GL.DepthMask(false);
                GL.Enable(GL.BLEND);
                GL.BlendFunc(GL.ONE, GL.ONE);
                drawLight();
                GL.Disable(GL.BLEND);
                _fx.EndScene();
                _fx.CompositeTo(draw);
            }
            else
            {
                // PostFxStack's non-MSAA buffer has no depth attachment.
                // Keep occlusion correct on that hardware, without bloom.
                GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
                GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);
                GL.RestorePhysicalViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
                GL.Enable(GL.DEPTH_TEST);
                GL.DepthMask(false);
                GL.Enable(GL.BLEND);
                GL.BlendFunc(GL.ONE, GL.ONE);
                drawLight();
            }
        }
        finally
        {
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.Disable(GL.BLEND);
            GL.Enable(GL.DEPTH_TEST);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);
            GL.RenderScale = scale;
            GL.RestorePhysicalViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            if (scissor) GL.Enable(GL.SCISSOR_TEST);
        }
    }

    public void Dispose() => _fx.Dispose();
}
