namespace MakarovPhysics.Renderer;

/// <summary>
/// A streaming polyline with a color per vertex - the scalable way to draw
/// trails: one draw call instead of hundreds of little spheres (a 700-point
/// trail as UV-spheres cost ~700 draw calls and ~10⁶ triangles per frame).
/// The buffer is allocated once and refilled via BufferSubData.
/// </summary>
public sealed class ColorLineMesh : IDisposable
{
    private const int FloatsPerVertex = 6; // x y z r g b

    private const string Vs = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vColor;
void main()
{
    gl_Position = uProj * uView * vec4(aPos, 1.0);
    vColor = aColor;
}";

    private const string Fs = @"#version 330 core
in vec3 vColor;
out vec4 FragColor;
void main() { FragColor = vec4(vColor, 1.0); }";

    private Shader? _shader;
    private uint _vao, _vbo;
    private readonly float[] _data;
    private int _count;

    public uint Mode = GL.LINE_STRIP;

    public ColorLineMesh(int maxPoints)
    {
        if (maxPoints < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints));
        }

        _data = new float[maxPoints * FloatsPerVertex];

        // Transactional construction (see Mesh)
        try
        {
            _shader = new Shader(Vs, Fs);
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _data.Length * sizeof(float), GL.DYNAMIC_DRAW);
            const int stride = FloatsPerVertex * sizeof(float);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Refill from trail points (position + per-point color).</summary>
    public void Update(IReadOnlyList<TrailPoint> points)
    {
        _count = Math.Min(points.Count, _data.Length / FloatsPerVertex);

        for (int i = 0; i < _count; i++)
        {
            int b = i * FloatsPerVertex;
            TrailPoint p = points[i];
            _data[b + 0] = p.Pos.X; _data[b + 1] = p.Pos.Y; _data[b + 2] = p.Pos.Z;
            _data[b + 3] = p.Color.X; _data[b + 4] = p.Color.Y; _data[b + 5] = p.Color.Z;
        }

        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * FloatsPerVertex);
    }

    /// <summary>Draw with the built-in per-vertex-color shader.</summary>
    public void Draw(Mat4 view, Mat4 proj)
    {
        // GL.POINTS legitimately draws a single vertex; only line modes need two
        int minimumVertices = Mode == GL.POINTS ? 1 : 2;
        if (_count < minimumVertices || _shader is null) return;
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(Mode, 0, _count);
        GL.BindVertexArray(0);
    }

    /// <summary>Draw with WHATEVER shader is currently bound: just the VAO
    /// and the vertices. For clients like GlassPanes that own their shader
    /// and use this mesh purely as geometry (12.08 - the parameterless call
    /// the glass was already making, now with a name that says what it
    /// skips).</summary>
    public void DrawRaw()
    {
        int minimumVertices = Mode == GL.POINTS ? 1 : 2;
        if (_count < minimumVertices) return;
        GL.BindVertexArray(_vao);
        GL.DrawArrays(Mode, 0, _count);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _shader = null;

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
