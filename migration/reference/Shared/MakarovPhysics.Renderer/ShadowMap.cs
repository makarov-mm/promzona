namespace MakarovPhysics.Renderer;

/// <summary>
/// Depth-only framebuffer for a directional light's shadow map. This started
/// as the sandbox's private class; it is shared because every module with a
/// floor wants the same thing, and a hand-drawn blob under each object is not
/// a shadow - it does not know about the geometry casting it.
/// Owns the FBO and its DEPTH_COMPONENT24 texture; the caller binds the map
/// with <see cref="BeginDepthPass"/>, renders the occluders with the depth
/// shader, then restores the caller's framebuffer and viewport with <see cref="EndDepthPass"/>
/// and samples <see cref="DepthTexture"/> in the lit pass. Construction is
/// Transactional: a failing GL call releases everything
/// created so far.
/// </summary>
public sealed class ShadowMap : IDisposable
{
    public int Size { get; }
    public uint DepthTexture { get; private set; }
    private uint _framebuffer;
    private int _previousDraw, _previousRead;
    private (int X, int Y, int Width, int Height) _previousViewport;
    private bool _previousScissor;


    public ShadowMap(int size = 2048)
    {
        Size = size;

        try
        {
            DepthTexture = GL.GenTexture();
            GL.BindTexture(GL.TEXTURE_2D, DepthTexture);
            // the eight-argument overload passes the null pointer itself: a
            // pointer-typed argument, even a literal null, is what CS9363
            // refuses outside an unsafe block on the 11.0 preview compiler -
            // and it refused it INSIDE an `unsafe` constructor's try block
            // (02.09), so no call here relies on the modifier
            GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.DEPTH_COMPONENT24, size, size, 0, GL.DEPTH_COMPONENT, GL.FLOAT);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.NEAREST);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.NEAREST);
            // clamp-to-edge: the lit shader treats coordinates outside [0;1]
            // as fully lit, so no border color is needed
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);

            _framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(GL.FRAMEBUFFER, _framebuffer);
            GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.TEXTURE_2D, DepthTexture, 0);
            GL.DrawBuffer(GL.NONE); // depth only - no color attachment
            GL.ReadBuffer(GL.NONE);

            if (GL.CheckFramebufferStatus(GL.FRAMEBUFFER) != GL.FRAMEBUFFER_COMPLETE)
            {
                throw new InvalidOperationException("Shadow-map framebuffer is incomplete.");
            }
        }
        catch
        {
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
            Dispose();
            throw;
        }

        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        GL.BindTexture(GL.TEXTURE_2D, 0);
    }

    /// <summary>
    /// The light's view-projection for a directional source: an orthographic
    /// box aimed at <paramref name="centre"/> from <paramref name="lightDir"/>
    /// (the direction TOWARDS the light, the same convention the lit shader
    /// uses), sized to <paramref name="radius"/>. Everything inside that
    /// sphere casts and receives; everything outside is left lit.
    /// </summary>
    public static Mat4 LightSpace(Vec3 centre, Vec3 lightDir, float radius)
    {
        Vec3 dir = lightDir.Normalized();
        Vec3 eye = centre + dir * (radius * 2.2f);
        Vec3 up = MathF.Abs(dir.Y) > 0.95f ? new Vec3(0, 0, 1) : Vec3.UnitY;
        Mat4 view = Mat4.LookAt(eye, centre, up);
        Mat4 proj = Mat4.Ortho(-radius, radius, -radius, radius, 0.05f, radius * 4.6f);
        return proj * view;
    }

    /// <summary>Bind the map for rendering occluder depth; sets the viewport.</summary>
    public void BeginDepthPass()
    {
        _previousDraw = GL.GetInteger(0x8CA6); // GL_DRAW_FRAMEBUFFER_BINDING
        _previousRead = GL.GetInteger(0x8CAA); // GL_READ_FRAMEBUFFER_BINDING
        _previousViewport = GL.PhysicalViewport();
        _previousScissor = GL.IsEnabled(GL.SCISSOR_TEST);
        GL.BindFramebuffer(GL.FRAMEBUFFER, _framebuffer);
        GL.Disable(GL.SCISSOR_TEST);
        // The depth texture has fixed physical dimensions, independent of
        // the host's supersampling scale or the current split-screen view.
        GL.RestorePhysicalViewport(0, 0, Size, Size);
        GL.Clear(GL.DEPTH_BUFFER_BIT);
    }

    /// <summary>Restore the target, physical viewport and scissor state of the caller.</summary>
    public void EndDepthPass()
    {
        GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, (uint)_previousDraw);
        GL.BindFramebuffer(GL.READ_FRAMEBUFFER, (uint)_previousRead);
        GL.RestorePhysicalViewport(_previousViewport.X, _previousViewport.Y,
            _previousViewport.Width, _previousViewport.Height);
        if (_previousScissor) GL.Enable(GL.SCISSOR_TEST);
        else GL.Disable(GL.SCISSOR_TEST);
    }

    public void Dispose()
    {
        if (_framebuffer != 0)
        {
            GL.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        if (DepthTexture != 0)
        {
            GL.DeleteTexture(DepthTexture);
            DepthTexture = 0;
        }
    }
}
