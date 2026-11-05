using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI;

/// <summary>
/// 2D batcher: accumulates quads (pixel position, uv, color) and draws them in
/// batches per font page; solid fills use the first page's white pixel.
/// Pixel-space orthographic projection, origin in the top-left corner.
/// </summary>
public sealed class Draw2D : IDisposable
{
    private const int MaxQuads = 8192;
    private const int FloatsPerVertex = 8; // x y u v r g b a
    private readonly float[] _data = new float[MaxQuads * 6 * FloatsPerVertex];
    private int _quadCount;
    private Shader? _shader;
    private uint _vao, _vbo;
    private float _screenW, _screenH; // logical (DPI-independent) size
    private int _physH; // physical pixels (for scissor)
    private float _scale = 1f; // physical / logical
    private FontAtlas? _atlas;
    private int _batchPage;

    private const string Vs = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aColor;
uniform vec3 uScreen;   // z unused (the binding only exposes Uniform3f)
out vec2 vUv;
out vec4 vColor;
void main()
{
    // pixels → NDC, Y axis pointing down
    vec2 ndc = vec2(aPos.x / uScreen.x * 2.0 - 1.0, 1.0 - aPos.y / uScreen.y * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vUv = aUv;
    vColor = aColor;
}";

    private const string Fs = @"#version 330 core
in vec2 vUv;
in vec4 vColor;
uniform sampler2D uTex;
out vec4 FragColor;
void main()
{
    FragColor = vColor * texture(uTex, vUv);
}";

    /// <summary>Call with the GL context current.</summary>
    public void InitGL(FontAtlas atlas)
    {
        try
        {
            InitGLCore(atlas);
        }
        catch
        {
            Dispose(); // partial VAO/VBO/shader must not leak (idempotent)
            throw;
        }
    }

    private void InitGLCore(FontAtlas atlas)
    {
        _atlas = atlas;
        _shader = new Shader(Vs, Fs);
        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        const int stride = FloatsPerVertex * sizeof(float);
        GL.VertexAttribPointer(0, 2, GL.FLOAT, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, GL.FLOAT, false, stride, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 4, GL.FLOAT, false, stride, 4 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        // allocate the streaming buffer once; frames upload via BufferSubData
        GL.BufferData(GL.ARRAY_BUFFER, _data.Length * sizeof(float), GL.DYNAMIC_DRAW);
        GL.BindVertexArray(0);
    }

    /// <summary>Swap the glyph atlas (used when the UI scale changes).</summary>
    public void SetAtlas(FontAtlas atlas)
    {
        Flush();
        _atlas = atlas;
        _batchPage = 0;
    }

    public FontAtlas Atlas => _atlas ?? throw new InvalidOperationException("Draw2D is not initialized.");

    /// <param name="logicalW">screen width in logical units (physical / scale)</param>
    public void BeginFrame(float logicalW, float logicalH, int physW, int physH, float scale)
    {
        _screenW = logicalW;
        _screenH = logicalH;
        _physH = physH;
        _scale = scale;
        _quadCount = 0;
        _batchPage = 0;
        Atlas.BeginFrame();
    }

    /// <summary>
    /// Clip subsequent drawing to a logical-space rectangle (null - disable).
    /// Flushes the current batch, so use sparingly (scroll areas).
    /// </summary>
    public void SetClip(UiRect? rect)
    {
        Flush();

        if (rect is { } r)
        {
            int x = (int)MathF.Floor(r.X * _scale);
            int w = (int)MathF.Ceiling(r.W * _scale);
            int h = (int)MathF.Ceiling(r.H * _scale);
            int y = _physH - (int)MathF.Floor(r.Y * _scale) - h; // GL scissor is bottom-left based
            GL.Enable(GL.SCISSOR_TEST);
            GL.Scissor(x, y, w, h);
        }
        else
        {
            GL.Disable(GL.SCISSOR_TEST);
        }
    }

    public void Rect(float x, float y, float w, float h, UiColor c)
    {
        (float u, float v) = WhitePixel();
        PushQuad(x, y, w, h, u, v, u, v, c);
    }

    public void RectOutline(float x, float y, float w, float h, UiColor c, float t = 1f)
    {
        Rect(x, y, w, t, c);
        Rect(x, y + h - t, w, t, c);
        Rect(x, y + t, t, h - 2 * t, c);
        Rect(x + w - t, y + t, t, h - 2 * t, c);
    }

    /// <summary>A filled disc as a fan of degenerate quads. 16 segments is
    /// visually round at control sizes (radio buttons, ~14 px).</summary>
    public void Circle(float cx, float cy, float r, UiColor c, int segments = 16)
    {
        (float u, float v) = WhitePixel();

        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments;
            float a1 = MathF.Tau * (i + 1) / segments;

            PushQuadRaw(
                cx, cy,
                cx + r * MathF.Cos(a0), cy + r * MathF.Sin(a0),
                cx + r * MathF.Cos(a1), cy + r * MathF.Sin(a1),
                cx, cy,
                u, v, u, v, c);
        }
    }

    /// <summary>The disc's outline, as a ring of line quads.</summary>
    public void CircleOutline(float cx, float cy, float r, UiColor c, float t = 1f, int segments = 16)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments;
            float a1 = MathF.Tau * (i + 1) / segments;

            Line(cx + r * MathF.Cos(a0), cy + r * MathF.Sin(a0),
                 cx + r * MathF.Cos(a1), cy + r * MathF.Sin(a1), c, t);
        }
    }

    public void Line(float x0, float y0, float x1, float y1, UiColor c, float t = 1f)    {
        // a line as a rotated quad
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        float nx = -dy / len * t * 0.5f, ny = dx / len * t * 0.5f;
        (float u, float v) = WhitePixel();

        PushQuadRaw(
            x0 + nx, y0 + ny, x1 + nx, y1 + ny,
            x1 - nx, y1 - ny, x0 - nx, y0 - ny,
            u, v, u, v, c);
    }

    /// <summary>Draw text; returns its width in pixels.</summary>
    public float Text(float x, float y, string s, UiColor c)
    {
        float pen = x;

        foreach (string element in FontAtlas.Elements(s))
        {
            GlyphInfo g = Atlas.Glyph(element);
            UsePage(g.Page);

            if (g.W > 0)
            {
                // Snapped to whole pixels. The pen advances in floats, so
                // without this every second glyph lands on a half pixel and
                // LINEAR magnification spreads it across two - which reads as
                // soft text everywhere and as nothing in particular anywhere.
                // The pen itself stays fractional, so the advances do not drift
                // and a long line ends where it should.
                PushQuad(MathF.Round(pen + g.OffsetX), MathF.Round(y + g.OffsetY), g.W, g.H, g.U0, g.V0, g.U1, g.V1, c);
            }

            pen += g.Advance;
        }

        return pen - x;
    }

    /// <summary>Scale scene annotation glyphs without changing the surrounding UI atlas or input coordinates.</summary>
    public float TextScaled(float x, float y, string text, UiColor color, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0) return 0;
        if (Math.Abs(scale - 1) < .001f) return Text(x, y, text, color);
        float pen = x;

        foreach (string element in FontAtlas.Elements(text))
        {
            GlyphInfo g = Atlas.Glyph(element);
            UsePage(g.Page);

            if (g.W > 0)
            {
                PushQuad(MathF.Round(pen + g.OffsetX * scale), MathF.Round(y + g.OffsetY * scale), g.W * scale, g.H * scale, g.U0, g.V0, g.U1, g.V1, color);
            }

            pen += g.Advance * scale;
        }

        return pen - x;
    }

    public float MeasureText(string s) => Atlas.MeasureText(s);

    private void UsePage(int page)
    {
        if (_batchPage == page) return;
        Flush();
        _batchPage = page;
    }

    private (float u, float v) WhitePixel()
    {
        UsePage(0);
        return Atlas.WhiteUv;
    }
    private void PushQuad(float x, float y, float w, float h, float u0, float v0, float u1, float v1, UiColor c) => PushQuadRaw(x, y, x + w, y, x + w, y + h, x, y + h, u0, v0, u1, v1, c);

    private void PushQuadRaw(
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float u0, float v0,
        float u1, float v1,
        UiColor c)
    {
        if (_quadCount >= MaxQuads) Flush();

        int b = _quadCount * 6 * FloatsPerVertex;

        V(ref b, x0, y0, u0, v0);
        V(ref b, x1, y1, u1, v0);
        V(ref b, x2, y2, u1, v1);
        V(ref b, x0, y0, u0, v0);
        V(ref b, x2, y2, u1, v1);
        V(ref b, x3, y3, u0, v1);

        _quadCount++;
        return;

        void V(ref int i, float x, float y, float u, float v)
        {
            _data[i++] = x; 
            _data[i++] = y; 
            _data[i++] = u; 
            _data[i++] = v;

            _data[i++] = c.R; 
            _data[i++] = c.G; 
            _data[i++] = c.B;
            _data[i++] = c.A;
        }
    }

    /// <summary>Flush the accumulated geometry. Called by UiContext at the end of a frame.</summary>
    public void Flush() => FlushBatch(null);

    /// <summary>
    /// Draws a full-colour icon as a single quad with its own texture.
    ///
    /// Everything else in the UI is batched against the font atlas, so an icon
    /// cannot join that batch: it has its own texture and has to be its own
    /// draw call, which is why the pending batch is flushed first. A tool
    /// palette has a few dozen icons and that is a few dozen calls, which is
    /// nothing next to the scene, and it is the same arrangement the original
    /// sandbox uses.
    /// </summary>
    public void DrawIcon(float x, float y, float w, float h, Texture? icon, UiColor tint = default)
    {
        if (icon is null || _shader is null) return;
        Flush();
        UiColor color = tint.A > 0 ? tint : new UiColor(1f, 1f, 1f, 1f);

        // Texture upload flips the bitmap for the 3D convention (origin at the
        // bottom), while the UI has y growing downwards: so the TOP edge of the
        // quad samples v = 1, or every icon comes out upside down.
        PushQuad(x, y, w, h, 0f, 1f, 1f, 0f, color);
        FlushBatch(icon);
    }

    private void FlushBatch(Texture? texture)
    {
        if (_quadCount == 0 || _shader is null) return;
        if (texture is null && _atlas is null) return;

        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.Disable(GL.DEPTH_TEST);
        // UI quads are wound clockwise in NDC (Y axis points down) - with
        // back-face culling on they would all be dropped
        GL.Disable(GL.CULL_FACE);

        _shader.Use();
        _shader.SetInt("uTex", 0);
        _shader.SetVec3("uScreen", _screenW, _screenH, 0);   // z unused
        GL.ActiveTexture(GL.TEXTURE0);
        (texture ?? _atlas!.TextureForPage(_batchPage)).Bind();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        // zero-allocation streaming: upload only the used part of the batch
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _quadCount * 6 * FloatsPerVertex);
        GL.DrawArrays(GL.TRIANGLES, 0, _quadCount * 6);
        GL.BindVertexArray(0);

        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.DEPTH_TEST);
        GL.Disable(GL.BLEND);
        _quadCount = 0;
    }

    public void Dispose()
    {
        _shader?.Dispose();

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
