using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>SphereSprites streaming layout, with spherical fragment depth and a
/// matching shadow pass. One vertex per 3D grain, not thousands of CPU meshes.</summary>
public sealed class ExhibitSpheres : IDisposable
{
    private Shader? _shader, _depth;
    private uint _vao, _vbo;
    private readonly float[] _data;
    private readonly float _radius;
    public int Count => _count;
    private int _count;

    public ExhibitSpheres(int capacity, float radius)
    {
        if (capacity < 1 || !float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _data = new float[capacity * 6];
        _radius = radius;

        try
        {
            _shader = new Shader(Vertex, Fragment);
            _depth = new Shader(DepthVertex, DepthFragment);
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _data.Length * sizeof(float), GL.DYNAMIC_DRAW);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, 24, 12);
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Update(IReadOnlyList<Vec3> positions, IReadOnlyList<Vec3> colors)
    {
        if (positions.Count != colors.Count || positions.Count * 6 > _data.Length)
            throw new ArgumentException("Particle stream capacity mismatch");

        _count = positions.Count;

        for (int i = 0; i < _count; i++)
        {
            Vec3 p = positions[i];
            Vec3 c = colors[i];
            int k = i * 6;
            _data[k] = p.X;
            _data[k + 1] = p.Y;
            _data[k + 2] = p.Z;
            _data[k + 3] = c.X;
            _data[k + 4] = c.Y;
            _data[k + 5] = c.Z;
        }

        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * 6);
    }

    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, Mat4 lightSpace, uint shadow, Vec3 light)
    {
        if (_shader is not { } s)
            return;

        s.Use();
        s.SetMatrix4("uView", view);
        s.SetMatrix4("uProj", proj);
        s.SetFloat("uRadius", _radius);
        s.SetFloat("uViewportH", GL.PhysicalViewport().Height);
        Materials.ApplyLight(s, light, new(1.65f, 1.55f, 1.35f), .63f, .82f, .15f);
        s.SetVec3("uViewPos", eye);
        s.SetMatrix4("uLightSpace", lightSpace);
        s.SetInt("uShadowMap", 6);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .58f);
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
        DrawPoints();
        GL.ActiveTexture(GL.TEXTURE0);
    }

    public void DrawDepth(Mat4 lightSpace)
    {
        if (_depth is not { } s)
            return;

        s.Use();
        s.SetMatrix4("uLightSpace", lightSpace);
        s.SetFloat("uRadius", _radius);
        s.SetFloat("uViewportW", GL.PhysicalViewport().Width);
        DrawPoints();
    }

    private void DrawPoints()
    {
        bool enabled = GL.IsEnabled(GL.PROGRAM_POINT_SIZE);

        if (!enabled)
            GL.Enable(GL.PROGRAM_POINT_SIZE);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.POINTS, 0, _count);
        GL.BindVertexArray(0);

        if (!enabled)
            GL.Disable(GL.PROGRAM_POINT_SIZE);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _shader = null;
        _depth?.Dispose();
        _depth = null;

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

    private const string Vertex = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aColor;
uniform mat4 uView,uProj;
uniform float uRadius,uViewportH;
out vec3 vColor,vEye,vCentre;
void main(){
 vEye=(uView*vec4(aPos,1)).xyz;vCentre=aPos;vColor=aColor;
 gl_Position=uProj*vec4(vEye,1);
 gl_PointSize=max(1.,uViewportH*uProj[1][1]*uRadius/max(.001,-vEye.z));
}";
    private const string Fragment = @"#version 330 core
in vec3 vColor,vEye,vCentre;
uniform mat4 uView,uProj;
uniform float uRadius;
" + DefaultShaders.PbrLightUniforms + DefaultShaders.PbrFunctions + @"
void main(){
 vec2 p=gl_PointCoord*2.-1.;p.y=-p.y;float r2=dot(p,p);if(r2>1.)discard;
 vec3 ne=vec3(p,sqrt(1.-r2));vec3 nw=transpose(mat3(uView))*ne;
 vec4 clip=uProj*vec4(vEye+uRadius*ne,1);
 gl_FragDepth=.5+.5*clip.z/clip.w;
 FragColor=shadePbr(vCentre+uRadius*nw,nw,vColor,.30,.05,.5);
}";
    private const string DepthVertex = @"#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4 uLightSpace;
uniform float uRadius,uViewportW;
void main(){
 gl_Position=uLightSpace*vec4(aPos,1);
 float scale=length(vec3(uLightSpace[0][0],uLightSpace[1][0],uLightSpace[2][0]));
 gl_PointSize=max(1.,uViewportW*uRadius*scale);
}";
    private const string DepthFragment = @"#version 330 core
uniform mat4 uLightSpace;
uniform float uRadius;
void main(){
 vec2 p=gl_PointCoord*2.-1.;float r2=dot(p,p);if(r2>1.)discard;
 float scale=length(vec3(uLightSpace[0][2],uLightSpace[1][2],uLightSpace[2][2]));
 gl_FragDepth=gl_FragCoord.z-.5*uRadius*sqrt(1.-r2)*scale;
}";
}
