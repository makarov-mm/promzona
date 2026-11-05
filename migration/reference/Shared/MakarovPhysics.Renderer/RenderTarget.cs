namespace MakarovPhysics.Renderer;

/// <summary>
/// An offscreen color render target (FBO + RGBA8 texture) with a blit-to-
/// screen pass. Built for heavy per-pixel modules (the black-hole geodesic
/// tracer): render the scene at a reduced internal resolution, then stretch
/// it over the viewport - spatial resolution costs far less visual quality
/// than cutting the integration steps would.
/// With <c>depth: true</c> the target also carries a depth renderbuffer, for
/// an accumulation that must respect occluders: draw the solids depth-only
/// into it first, then the light with the depth test on (Tokamak's long
/// exposure - without it every orbit behind the solenoid column painted
/// straight through the steel).
/// </summary>
public sealed class RenderTarget : IDisposable
{
    private const string BlitVertexShader = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

    private const string BlitFragmentShader = @"#version 330 core
in vec2 vUv;
uniform sampler2D uFrame;
out vec4 FragColor;
void main() { FragColor = texture(uFrame, vUv); }";

    public int Width { get; private set; }
    public int Height { get; private set; }

    private uint _framebuffer;
    private uint _texture;
    private uint _depth;
    /// <summary>True when the target was built with a depth renderbuffer.</summary>
    public bool HasDepth { get; }
    private Shader? _blitShader;
    private uint _vao, _vbo;

    public RenderTarget(int width, int height, bool depth = false)
    {
        HasDepth = depth;
        // rollback on partial construction: the constructor throwing means no
        // caller ever owns this object, so any GL ids created so far would
        // leak for the rest of the session - Dispose() is idempotent and safe
        try
        {
            _blitShader = new Shader(BlitVertexShader, BlitFragmentShader);

            // fullscreen triangle in NDC for the blit pass
            float[] triangle = [-1f, -1f, 3f, -1f, -1f, 3f];
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, triangle, GL.STATIC_DRAW);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            _texture = GL.GenTexture();
            if (depth) _depth = GL.GenRenderbuffer();
            _framebuffer = GL.GenFramebuffer();
            Resize(width, height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>(Re)allocate the color texture (and the depth renderbuffer,
    /// when there is one) - called on scale changes.</summary>
    public void Resize(int width, int height)
    {
        Width = Math.Max(16, width);
        Height = Math.Max(16, height);

        GL.BindTexture(GL.TEXTURE_2D, _texture);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
        GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, Width, Height, 0, GL.RGBA, GL.UNSIGNED_BYTE);
        GL.BindFramebuffer(GL.FRAMEBUFFER, _framebuffer);
        GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, _texture, 0);
        if (_depth != 0)
        {
            GL.BindRenderbuffer(GL.RENDERBUFFER, _depth);
            GL.RenderbufferStorage(GL.RENDERBUFFER, GL.DEPTH_COMPONENT24, Width, Height);
            GL.BindRenderbuffer(GL.RENDERBUFFER, 0);
            GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.RENDERBUFFER, _depth);
        }
        bool complete = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);

        if (!complete)
        {
            throw new InvalidOperationException("RenderTarget: framebuffer is incomplete.");
        }
    }

    /// <summary>Redirect rendering into the target (sets the viewport too).</summary>
    public void Begin()
    {
        GL.BindFramebuffer(GL.FRAMEBUFFER, _framebuffer);
        GL.Viewport(0, 0, Width, Height);
    }

    /// <summary>Back to the default framebuffer with the given viewport.</summary>
    public void End(int viewportWidth, int viewportHeight)
    {
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        GL.Viewport(0, 0, viewportWidth, viewportHeight);
    }

    /// <summary>Stretch the target texture over the current viewport.</summary>
    public void Blit()
    {
        if (_blitShader is null) return;
        _blitShader.Use();
        _blitShader.SetInt("uFrame", 0);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _texture);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, 3);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _blitShader?.Dispose();
        _blitShader = null;

        if (_framebuffer != 0)
        {
            GL.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        if (_texture != 0)
        {
            GL.DeleteTexture(_texture); 
            _texture = 0;
        }

        if (_depth != 0)
        {
            GL.DeleteRenderbuffer(_depth);
            _depth = 0;
        }

        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo); 
            _vbo = 0;
        }

        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao); 
            _vao = 0;
        }
    }
}
