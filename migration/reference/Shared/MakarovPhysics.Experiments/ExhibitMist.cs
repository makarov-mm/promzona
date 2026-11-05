using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Sorted, depth-tested soft billboards for illustrative cold vapour.
/// This is a presentation effect; it does not supply physical fluid quantities.</summary>
public sealed class ExhibitMist : IDisposable
{
    private readonly List<(Vec3 Position, float Radius, Vec3 Color, float Opacity, float Phase)> _puffs = [];
    private readonly Shader _shader;
    private uint _vao, _vbo;
    private float[] _data = [];
    public int Count => _puffs.Count;

    public ExhibitMist()
    {
        _shader = new Shader(Vertex, Fragment);
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 40, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, GL.FLOAT, false, 40, 12);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 2, GL.FLOAT, false, 40, 24);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 2, GL.FLOAT, false, 40, 32);
        GL.EnableVertexAttribArray(3);
        GL.BindVertexArray(0);
    }

    public void Clear() => _puffs.Clear();

    public void Add(Vec3 position, float radius, Vec3 color, float opacity, float phase)
    {
        if (radius > 0 && opacity > 0)
        {
            _puffs.Add((position, radius, color, Math.Clamp(opacity, 0, 1), phase));
        }
    }

    public void Draw(Mat4 view, Mat4 projection, Vec3 eye)
    {
        if (_puffs.Count == 0)
            return;

        int count = _puffs.Count * 60;
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);

        if (_data.Length < count)
        {
            _data = new float[Math.Max(count, _data.Length * 2)];
            GL.BufferData(GL.ARRAY_BUFFER, (nint)(_data.Length * sizeof(float)), GL.DYNAMIC_DRAW);
        }

        int k = 0;

        foreach ((Vec3 Position, float Radius, Vec3 Color, float Opacity, float Phase) puff in _puffs.OrderByDescending(p => (p.Position - eye).Length()))
        {
            Vec3 forward = (eye - puff.Position).Normalized();
            Vec3 right = Vec3.Cross(Math.Abs(forward.Y) > .95f ? Vec3.UnitZ : Vec3.UnitY, forward).Normalized() * puff.Radius;
            Vec3 up = Vec3.Cross(forward, right).Normalized() * puff.Radius;

            void Point(float x, float y)
            {
                Vec3 p = puff.Position + right * x + up * y;
                _data[k++] = p.X;
                _data[k++] = p.Y;
                _data[k++] = p.Z;
                _data[k++] = puff.Color.X;
                _data[k++] = puff.Color.Y;
                _data[k++] = puff.Color.Z;
                _data[k++] = x;
                _data[k++] = y;
                _data[k++] = puff.Opacity;
                _data[k++] = puff.Phase;
            }

            Point(-1, -1);
            Point(1, -1);
            Point(1, 1);
            Point(-1, -1);
            Point(1, 1);
            Point(-1, 1);
        }

        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, k);
        bool blend = GL.IsEnabled(GL.BLEND), cull = GL.IsEnabled(GL.CULL_FACE), depth = GL.GetInteger(0x0B72) != 0;
        uint src = (uint)GL.GetInteger(0x80C9), dst = (uint)GL.GetInteger(0x80C8);

        try
        {
            GL.DepthMask(false);
            GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            _shader.Use();
            _shader.SetMatrix4("uVP", projection * view);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(GL.TRIANGLES, 0, _puffs.Count * 6);
            GL.BindVertexArray(0);
        }
        finally
        {
            GL.DepthMask(depth);
            GL.BlendFunc(src, dst);

            if (blend)
                GL.Enable(GL.BLEND);
            else
                GL.Disable(GL.BLEND);

            if (cull)
                GL.Enable(GL.CULL_FACE);
            else
                GL.Disable(GL.CULL_FACE);
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
layout(location=0) in vec3 aPosition;layout(location=1) in vec3 aColor;
layout(location=2) in vec2 aUv;layout(location=3) in vec2 aState;
uniform mat4 uVP;out vec3 vColor;out vec2 vUv;out vec2 vState;
void main(){vColor=aColor;vUv=aUv;vState=aState;gl_Position=uVP*vec4(aPosition,1);}";
    private const string Fragment = @"#version 330 core
in vec3 vColor;in vec2 vUv;in vec2 vState;out vec4 FragColor;
void main(){float r2=dot(vUv,vUv);if(r2>=1.)discard;
float cloud=.70+.12*sin(9.*vUv.x+vState.y)*sin(7.*vUv.y-vState.y)+.10*cos(13.*vUv.y+4.*vUv.x+vState.y);
float a=pow(1.-r2,2.)*cloud*vState.x;
FragColor=vec4(vColor*(.88+.12*vUv.y),a);}";
}
