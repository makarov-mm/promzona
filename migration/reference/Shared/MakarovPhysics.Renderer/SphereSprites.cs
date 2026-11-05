namespace MakarovPhysics.Renderer;

/// <summary>
/// A streaming point cloud drawn as SHADED SPHERE IMPOSTORS: position and
/// colour per vertex (the GlowPoints layout), a world-space radius shared by
/// all points. The vertex stage sizes the sprite from the projection so the
/// balls track camera zoom; the fragment stage reconstructs the sphere
/// normal from the point coordinate and lights it - diffuse, ambient and a
/// tight specular - which is the mathematically honest shading of a sphere,
/// not a gradient trick. Opaque, depth-tested: atoms occlude each other.
/// Draw() enables GL.PROGRAM_POINT_SIZE itself and puts it back (22.08).
/// It used to be the CALLER's job, and the job was silently skipped in two of
/// the seven modules: without the flag the driver ignores gl_PointSize and
/// every sprite comes out one pixel wide, so the bath of Brownian motion read
/// as dust when the arithmetic said 38 px. A class whose whole point is a
/// vertex-computed point size has no business leaving that flag to a comment.
/// </summary>
public sealed class SphereSprites : IDisposable
{
    private const int FloatsPerVertex = 6; // x y z r g b

    private const string Vs = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
uniform mat4 uView;
uniform mat4 uProj;
uniform float uRadius;      // world units
uniform float uViewportH;   // pixels
uniform float uMinDiameter; // optional visibility marker; never changes collision geometry
out vec3 vColor;
void main()
{
    vec4 eye = uView * vec4(aPos, 1.0);
    gl_Position = uProj * eye;
    // projected diameter in pixels: proj[1][1] = cot(fovy/2)
    gl_PointSize = max(uMinDiameter, uViewportH * uProj[1][1] * uRadius / max(0.001, -eye.z));
    vColor = aColor;
}";

    private const string Fs = @"#version 330 core
in vec3 vColor;
out vec4 FragColor;
void main()
{
    vec2 p = gl_PointCoord * 2.0 - 1.0;
    float r2 = dot(p, p);
    if (r2 > 1.0) discard;
    vec3 n = vec3(p.x, -p.y, sqrt(1.0 - r2));
    vec3 l = normalize(vec3(0.42, 0.62, 0.66));
    float diff = max(0.0, dot(n, l));
    float spec = pow(max(0.0, dot(n, normalize(l + vec3(0.0, 0.0, 1.0)))), 42.0);
    vec3 c = vColor * (0.34 + 0.72 * diff) + vec3(1.0) * spec * 0.55;
    FragColor = vec4(c, 1.0);
}";

    private Shader? _shader;
    private uint _vao, _vbo;
    private readonly float[] _data;
    private int _count;

    public SphereSprites(int maxPoints)
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

    public void Draw(Mat4 view, Mat4 proj, float radiusWorld, float viewportHeightPx, float minimumDiameterPx = 0f)
    {
        if (_count == 0 || _shader is null) return;
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetFloat("uRadius", radiusWorld);
        _shader.SetFloat("uViewportH", viewportHeightPx);
        _shader.SetFloat("uMinDiameter", float.IsFinite(minimumDiameterPx) ? Math.Clamp(minimumDiameterPx, 0f, 64f) : 0f);
        bool hadPointSize = GL.IsEnabled(GL.PROGRAM_POINT_SIZE);
        if (!hadPointSize) GL.Enable(GL.PROGRAM_POINT_SIZE);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.POINTS, 0, _count);
        GL.BindVertexArray(0);
        if (!hadPointSize) GL.Disable(GL.PROGRAM_POINT_SIZE);
    }

    public void Dispose()
    {
        _shader?.Dispose(); _shader = null;

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
