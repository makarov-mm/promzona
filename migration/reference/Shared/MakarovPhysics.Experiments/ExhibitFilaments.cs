using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Depth-tested luminous 3D streamers with an analytic white core and soft
/// coloured halo. Camera-facing ribbons avoid opaque outer tubes hiding their cores.</summary>
public sealed class ExhibitFilaments : IDisposable
{
    private readonly List<(Vec3 A, Vec3 B, Vec3 Color, float Width)> _segments = [];
    private readonly Shader _shader;
    private uint _vao, _vbo;
    private float[] _data = [];
    public int Count => _segments.Count;

    public ExhibitFilaments()
    {
        _shader = new(Vertex, Fragment);
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 32, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, GL.FLOAT, false, 32, 12);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 2, GL.FLOAT, false, 32, 24);
        GL.EnableVertexAttribArray(2);
        GL.BindVertexArray(0);
    }

    public void Clear() => _segments.Clear();
    public void Add(Vec3 a, Vec3 b, Vec3 color, float width = .035f)
    {
        if ((b - a).Length() > 1e-6f)
            _segments.Add((a, b, color, width));
    }

    public void Draw(Mat4 view, Mat4 proj, Vec3 eye)
    {
        if (_segments.Count == 0)
            return;
        int count = _segments.Count * 6 * 8;
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        if (_data.Length < count)
        {
            _data = new float[Math.Max(count, _data.Length * 2)];
            GL.BufferData(GL.ARRAY_BUFFER, (nint)(_data.Length * sizeof(float)), GL.DYNAMIC_DRAW);
        }

        int k = 0;
        void Point(Vec3 p, Vec3 c, float u, float v)
        {
            _data[k++] = p.X;
            _data[k++] = p.Y;
            _data[k++] = p.Z;
            _data[k++] = c.X;
            _data[k++] = c.Y;
            _data[k++] = c.Z;
            _data[k++] = u;
            _data[k++] = v;
        }

        foreach (var(a, b, c, width)in _segments)
        {
            Vec3 side = Vec3.Cross(b - a, eye - (a + b) * .5f).Normalized() * width;
            Point(a - side, c, -1, 0);
            Point(b - side, c, -1, 1);
            Point(b + side, c, 1, 1);
            Point(a - side, c, -1, 0);
            Point(b + side, c, 1, 1);
            Point(a + side, c, 1, 0);
        }

        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, k);
        bool blend = GL.IsEnabled(GL.BLEND), cull = GL.IsEnabled(GL.CULL_FACE), depth = GL.GetInteger(0x0B72) != 0;
        uint src = (uint)GL.GetInteger(0x80C9), dst = (uint)GL.GetInteger(0x80C8);
        try
        {
            GL.DepthMask(false);
            GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.ONE, GL.ONE);
            _shader.Use();
            _shader.SetMatrix4("uVP", proj * view);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(GL.TRIANGLES, 0, _segments.Count * 6);
            GL.BindVertexArray(0);
        }
        finally
        {
            GL.DepthMask(depth);
            GL.BlendFunc(src, dst);
            if (cull)
                GL.Enable(GL.CULL_FACE);
            else
                GL.Disable(GL.CULL_FACE);
            if (blend)
                GL.Enable(GL.BLEND);
            else
                GL.Disable(GL.BLEND);
        }
    }

    public void Dispose()
    {
        _shader.Dispose();
        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao);
            _vao = 0;
        }

        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo);
            _vbo = 0;
        }
    }

    private const string Vertex = @"#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec3 aColor;
layout(location=2) in vec2 aUv;
uniform mat4 uVP;
out vec3 vColor;out vec2 vUv;
void main(){vColor=aColor;vUv=aUv;gl_Position=uVP*vec4(aPosition,1);}";
    private const string Fragment = @"#version 330 core
in vec3 vColor;in vec2 vUv;
out vec4 FragColor;
void main(){float x=vUv.x;float halo=exp(-5.*x*x)*.42;float core=exp(-110.*x*x);
float strength=max(vColor.r,max(vColor.g,vColor.b));
FragColor=vec4(vColor*halo+mix(vColor,vec3(.82,.87,1)*strength,.45)*core,0);}";
}
