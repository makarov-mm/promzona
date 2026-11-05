namespace MakarovPhysics.Renderer;

/// <summary>
/// Cinematic capture: renders the scene pass into an oversized buffer and
/// resolves it down to the window. Two samples per axis mean four colour
/// samples per final pixel - it antialiases everything MSAA cannot reach
/// (point sprites, additive line work, shader-drawn field textures) and
/// sharpens fine geometry, at four times the fill cost.
///
/// This is the honest way to make a recording look like an offline render:
/// the simulation is still solved live at full rate, only the image is
/// oversampled. Nothing is baked, nothing is pre-rendered.
///
/// It wraps ONLY the scene pass. The interface is drawn afterwards at native
/// resolution, so text and instrument overlays stay pixel-exact.
/// </summary>
public sealed class Supersampler : IDisposable
{
    private uint _framebuffer;
    private uint _colour;
    private uint _depth; // a depth TEXTURE, not a renderbuffer: the GL wrapper has no renderbuffer entry points, and ShadowMap already proves the texture path
    private uint _vao, _vbo;
    private Shader? _resolve;

    private int _width, _height, _factor = 1;

    /// <summary>Samples per axis actually in use (1 when idle).</summary>
    public int Factor => _factor;

    /// <summary>
    /// Point the scene pass at the oversized buffer. Returns false when the
    /// factor is 1 or the buffer could not be built - the caller then renders
    /// straight to the window, which is always a valid outcome.
    /// </summary>
    public bool Begin(int width, int height, int factor)
    {
        if (factor <= 1 || width <= 0 || height <= 0) return false;

        try
        {
            Ensure(width, height, factor);
        }
        catch
        {
            // an oversized buffer is a luxury: on a driver that refuses it we
            // simply render at native size rather than failing the frame
            Dispose();
            return false;
        }

        GL.BindFramebuffer(GL.FRAMEBUFFER, _framebuffer);
        GL.RenderScale = factor;
        GL.Viewport(0, 0, width, height); // scaled by RenderScale
        return true;
    }

    /// <summary>
    /// Resolve the oversized image onto the window. The shader takes a 2x2 tap
    /// per output pixel: hardware bilinear alone would only average two of the
    /// four samples on the diagonal.
    /// </summary>
    public void End(int width, int height)
    {
        GL.RenderScale = 1;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        GL.Viewport(0, 0, width, height);

        if (_resolve is null || _colour == 0) return;

        GL.Disable(GL.DEPTH_TEST);
        GL.Disable(GL.BLEND);
        GL.Disable(GL.CULL_FACE);

        _resolve.Use();
        _resolve.SetInt("uSource", 0);
        _resolve.SetVec2("uTexel", 1f / _width, 1f / _height);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _colour);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, 3);
        GL.BindVertexArray(0);

        GL.Enable(GL.DEPTH_TEST);
    }

    private void Ensure(int width, int height, int factor)
    {
        int w = width * factor, h = height * factor;
        if (_framebuffer != 0 && w == _width && h == _height && factor == _factor) return;

        _width = w;
        _height = h;
        _factor = factor;

        if (_colour == 0) _colour = GL.GenTexture();
        GL.BindTexture(GL.TEXTURE_2D, _colour);
        GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, w, h, 0, GL.RGBA, GL.UNSIGNED_BYTE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);

        if (_depth == 0) _depth = GL.GenTexture();
        GL.BindTexture(GL.TEXTURE_2D, _depth);
        GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.DEPTH_COMPONENT24, w, h, 0, GL.DEPTH_COMPONENT, GL.FLOAT);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.NEAREST);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.NEAREST);

        if (_framebuffer == 0) _framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(GL.FRAMEBUFFER, _framebuffer);
        GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, _colour, 0);
        GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.TEXTURE_2D, _depth, 0);

        bool ok = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        GL.BindTexture(GL.TEXTURE_2D, 0);
        if (!ok) throw new InvalidOperationException("Supersampler: framebuffer is incomplete.");

        _resolve ??= new Shader(ResolveVertex, ResolveFragment);

        if (_vao == 0)
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            float[] tri = [-1f, -1f, 3f, -1f, -1f, 3f];
            GL.BufferData(GL.ARRAY_BUFFER, tri, GL.STATIC_DRAW);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }
    }

    public void Dispose()
    {
        // best effort: dying mid-pass must not leave the scaled FBO current
        GL.RenderScale = 1;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);

        if (_framebuffer != 0)
        {
            GL.DeleteFramebuffer(_framebuffer); 
            _framebuffer = 0;
        }

        if (_colour != 0)
        {
            GL.DeleteTexture(_colour);
            _colour = 0;
        }

        if (_depth != 0)
        {
            GL.DeleteTexture(_depth); 
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

        _resolve?.Dispose();
        _resolve = null;
        _factor = 1;
    }

    private const string ResolveVertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

    private const string ResolveFragment = @"#version 330 core
in vec2 vUv;
uniform sampler2D uSource;
uniform vec2 uTexel;      // one texel of the OVERSIZED buffer
out vec4 FragColor;

void main()
{
    // a 2x2 box centred on the output pixel: bilinear alone would blend only
    // two of the four samples, leaving the diagonal aliasing untouched
    vec3 c = texture(uSource, vUv + vec2(-0.5, -0.5) * uTexel).rgb
           + texture(uSource, vUv + vec2( 0.5, -0.5) * uTexel).rgb
           + texture(uSource, vUv + vec2(-0.5,  0.5) * uTexel).rgb
           + texture(uSource, vUv + vec2( 0.5,  0.5) * uTexel).rgb;
    FragColor = vec4(c * 0.25, 1.0);
}";
}
