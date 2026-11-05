namespace MakarovPhysics.Renderer;

/// <summary>
/// A streaming point cloud drawn as ROUND additive sprites: position + colour
/// per vertex (the ColorLineMesh layout), but the fragment shader shapes the
/// point with gl_PointCoord into a soft radial falloff and premultiplies the
/// colour, so the caller blends with ONE, ONE and overlapping filaments stack
/// into bloom instead of overwriting each other.
/// </summary>
public sealed class GlowPoints : IDisposable
{
    private const int FloatsPerVertex = 6; // x y z r g b

    private const string Vs = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
uniform mat4 uView;
uniform mat4 uProj;
uniform float uRadius;      // world radius; <= 0 keeps the old glPointSize state
uniform float uViewportH;   // pixels, only read when uRadius > 0
out vec3 vColor;
void main()
{
    vec4 eye = uView * vec4(aPos, 1.0);
    gl_Position = uProj * eye;
    // A sprite sized in WORLD units shrinks with distance like the thing it
    // stands for (22.08). The old path - a size in pixels from the GL state -
    // stays for every existing caller: writing gl_PointSize only under the
    // flag keeps both well defined.
    if (uRadius > 0.0)
        gl_PointSize = uViewportH * uProj[1][1] * uRadius / max(0.001, -eye.z);
    vColor = aColor;
}";

    private const string Fs = @"#version 330 core
in vec3 vColor;
out vec4 FragColor;
void main()
{
    vec2 d = gl_PointCoord * 2.0 - 1.0;
    float r2 = dot(d, d);
    if (r2 > 1.0) discard;
    float a = exp(-3.0 * r2) * (1.0 - r2);   // soft core, zero at the rim
    FragColor = vec4(vColor * a, 1.0);       // premultiplied for ONE,ONE
}";

    private Shader? _shader;
    private uint _vao, _vbo;
    private readonly float[] _data;
    private int _count;
    private bool _overflowReported;

    /// <summary>Points the buffer can hold; Update clips to it.</summary>
    public int Capacity => _data.Length / FloatsPerVertex;

    /// <summary>Points the last Update kept.</summary>
    public int Count => _count;

    /// <summary>
    /// Points the last Update was handed and could NOT keep. Clipping is the
    /// right defensive behaviour for a GL buffer, but a producer that never
    /// looks at this believes everything it drew is on screen: PointVortices
    /// let the dye grow to 73 200 points over a 43 200-point buffer and the
    /// user's paint simply stopped appearing (27.08 review). Non-zero here is
    /// a sizing bug in the caller, and the first occurrence is written to the
    /// debug output so it is not silent in a development build.
    /// </summary>
    public int Dropped { get; private set; }

    public GlowPoints(int maxPoints)
    {
        if (maxPoints < 1) throw new ArgumentOutOfRangeException(nameof(maxPoints));
        _data = new float[maxPoints * FloatsPerVertex];

        // Transactional construction (see Mesh)
        try
        {
            _shader = new Shader(Vs, Fs);
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, (nint)(_data.Length * sizeof(float)), GL.DYNAMIC_DRAW);
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

    public void Update(IReadOnlyList<TrailPoint> points)
    {
        int capacity = Capacity;
        _count = Math.Min(points.Count, capacity);
        Dropped = points.Count - _count;

        if (Dropped > 0 && !_overflowReported)
        {
            _overflowReported = true;
            System.Diagnostics.Debug.WriteLine($"GlowPoints: {points.Count} points offered to a {capacity}-point buffer; {Dropped} not drawn");
        }

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

    /// <param name="radiusWorld">Sprite radius in world units. Zero (the
    /// default) keeps the historic behaviour - the size comes from the GL
    /// point-size state and does not track distance.</param>
    /// <param name="viewportHeightPx">Only read when a world radius is given.</param>
    public void Draw(Mat4 view, Mat4 proj, float radiusWorld = 0f, float viewportHeightPx = 0f)
    {
        if (_count == 0 || _shader is null) return;
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetFloat("uRadius", radiusWorld);
        _shader.SetFloat("uViewportH", viewportHeightPx);

        // the vertex stage only writes gl_PointSize on the world-radius path,
        // so the flag goes on exactly there - an unwritten gl_PointSize under
        // an enabled flag is undefined, and that is a real bug, not pedantry
        bool sized = radiusWorld > 0f;
        bool hadPointSize = sized && GL.IsEnabled(GL.PROGRAM_POINT_SIZE);
        if (sized && !hadPointSize) GL.Enable(GL.PROGRAM_POINT_SIZE);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.POINTS, 0, _count);
        GL.BindVertexArray(0);
        if (sized && !hadPointSize) GL.Disable(GL.PROGRAM_POINT_SIZE);
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
