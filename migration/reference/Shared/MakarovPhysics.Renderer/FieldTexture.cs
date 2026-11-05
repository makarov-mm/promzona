namespace MakarovPhysics.Renderer;

/// <summary>
/// A 2D scalar/color field rendered as a textured quad - the standard canvas
/// for grid simulations (LBM flow, heat maps, wave fields). The RGBA byte
/// buffer lives on the CPU; call Upload() after refilling it, then Draw()
/// with the world-space rectangle the field should cover.
/// </summary>
public sealed unsafe class FieldTexture : IDisposable
{
    private const string QuadVertexShader = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;
uniform mat4 uView;
uniform mat4 uProj;
uniform mat4 uModel;  // orients the quad (e.g. lays it flat as a floor)
uniform vec4 uRect;   // x, y, width, height in the quad's local plane
out vec2 vUv;
void main()
{
    vec2 world = uRect.xy + aPos * uRect.zw;
    gl_Position = uProj * uView * uModel * vec4(world, 0.0, 1.0);
    vUv = aUv;
}";

    private const string QuadFragmentShader = @"#version 330 core
in vec2 vUv;
uniform sampler2D uField;
out vec4 FragColor;
void main() { FragColor = texture(uField, vUv); }";

    public int Width { get; }
    public int Height { get; }

    /// <summary>CPU-side RGBA pixels, row-major, 4 bytes per cell. Refill, then Upload().</summary>
    public byte[] Pixels { get; }

    private uint _textureId;
    private uint _vao, _vbo;
    private Shader? _shader;

    public FieldTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "FieldTexture needs positive dimensions.");

        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)]; // overflow → exception, not a tiny buffer

        try
        {
            _shader = new Shader(QuadVertexShader, QuadFragmentShader);
            _textureId = GL.GenTexture();

            GL.BindTexture(GL.TEXTURE_2D, _textureId);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);

            fixed (byte* p = Pixels)
            {
                GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, width, height, 0, GL.RGBA, GL.UNSIGNED_BYTE, p);
            }

            // unit quad: position (0..1) + uv, two triangles
            float[] quad =
            [
                0, 0, 0, 0,   1, 0, 1, 0,   1, 1, 1, 1,
                0, 0, 0, 0,   1, 1, 1, 1,   0, 1, 0, 1
            ];

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, quad, GL.STATIC_DRAW);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, GL.FLOAT, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }
        catch
        {
            // the constructor throws → nobody can Dispose this instance later
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Push the CPU pixel buffer to the GPU (call once per frame).
    /// TexSubImage2D updates the storage allocated by the constructor -
    /// re-specifying it with TexImage2D every frame would make the driver
    /// reallocate the texture (measurable on LBM-sized uploads).
    /// </summary>
    /// <summary>Bind the texture for a caller-supplied shader (unit 0).
    /// The built-in Draw stays available; this is for modules that want a
    /// custom fragment stage over the same uploaded field.</summary>
    public void Bind() => GL.BindTexture(GL.TEXTURE_2D, _textureId);

    public void Upload() => UploadRows(0, Height);

    /// <summary>
    /// Uploads a horizontal band. A texture filled progressively by workers
    /// gains a few rows at a time, and sending the whole image for each of
    /// them means megabytes across the bus for a few kilobytes of new pixels.
    /// </summary>
    public void UploadRows(int firstRow, int rowCount)
    {
        if (rowCount <= 0 || Height <= 0 || Width <= 0) return;

        firstRow = Math.Clamp(firstRow, 0, Height - 1);
        rowCount = Math.Min(rowCount, Height - firstRow);

        GL.BindTexture(GL.TEXTURE_2D, _textureId);
        GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);

        fixed (byte* p = &Pixels[firstRow * Width * 4])
        {
            GL.TexSubImage2D(GL.TEXTURE_2D, 0, 0, firstRow, Width, rowCount, GL.RGBA, GL.UNSIGNED_BYTE, p);
        }
    }

    /// <summary>Bind the field texture to a texture unit - for shaders that sample it directly.</summary>
    // ReSharper disable once MethodOverloadWithOptionalParameter
    public void Bind(uint unit = 0)
    {
        GL.ActiveTexture(GL.TEXTURE0 + unit);
        GL.BindTexture(GL.TEXTURE_2D, _textureId);
    }

    /// <summary>Draw the field covering the given rectangle in the world XY plane.</summary>
    public void Draw(Mat4 view, Mat4 projection, float worldX, float worldY, float worldW, float worldH) => Draw(view, projection, Mat4.Identity, worldX, worldY, worldW, worldH);

    /// <summary>
    /// Draw the field with an extra model transform - e.g. RotateX(−π/2) lays
    /// the quad flat in the XZ plane so the field becomes a floor/table top.
    /// </summary>
    public void Draw(Mat4 view, Mat4 projection, Mat4 model, float worldX, float worldY, float worldW, float worldH)
    {
        if (_shader is null) return;
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", projection);
        _shader.SetMatrix4("uModel", model);
        _shader.SetVec4("uRect", worldX, worldY, worldW, worldH);
        _shader.SetInt("uField", 0);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _textureId);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, 6);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _shader = null;

        if (_textureId != 0)
        {
            GL.DeleteTexture(_textureId); 
            _textureId = 0;
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
