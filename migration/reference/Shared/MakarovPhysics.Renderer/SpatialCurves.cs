namespace MakarovPhysics.Renderer;

/// <summary>Depth-tested, shaded ribbons for scientific curves. Two RGBA texels
/// per point; vertex pulling expands real segments to triangles on GL 3.3.
/// Width is in pixels and does not depend on driver support for wide lines.
/// The caller supplies depth testing and decides whether this layer writes depth.</summary>
public sealed unsafe class SpatialCurves : IDisposable
{
    private const int TextureWidth = 1024;
    private readonly float[] _data;
    private readonly int _capacity;
    private int _count;
    private Shader? _shader;
    private uint _texture, _vao;

    public SpatialCurves(int capacity)
    {
        if (capacity < 2 || capacity > 500000) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        int rows = (capacity * 2 + TextureWidth - 1) / TextureWidth;
        _data = new float[rows * TextureWidth * 4];
        try
        {
            _shader = new Shader(Vertex, Fragment);
            _vao = GL.GenVertexArray(); _texture = GL.GenTexture();
            GL.BindTexture(GL.TEXTURE_2D, _texture);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.NEAREST);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.NEAREST);
            GL.TexImage2D(GL.TEXTURE_2D, 0, 0x8814, TextureWidth, rows, 0, GL.RGBA, GL.FLOAT);
            GL.BindTexture(GL.TEXTURE_2D, 0);
        }
        catch { Dispose(); throw; }
    }

    public void Update(IReadOnlyList<TrailPoint> points)
    {
        if (points.Count > _capacity) throw new ArgumentOutOfRangeException(nameof(points));
        _count = points.Count;
        for (int i = 0; i < _count; i++)
        {
            var p = points[i]; int b = i * 8;
            _data[b] = p.Pos.X; _data[b + 1] = p.Pos.Y; _data[b + 2] = p.Pos.Z;
            _data[b + 3] = p.Pos.IsFinite && p.Color.IsFinite ? 1 : 0;
            _data[b + 4] = p.Color.X; _data[b + 5] = p.Color.Y; _data[b + 6] = p.Color.Z;
        }
        if (_count == 0) return;
        GL.ActiveTexture(GL.TEXTURE0); GL.BindTexture(GL.TEXTURE_2D, _texture);
        fixed (float* data = _data)
            GL.TexSubImage2D(GL.TEXTURE_2D, 0, 0, 0, TextureWidth,
                (_count * 2 + TextureWidth - 1) / TextureWidth, GL.RGBA, GL.FLOAT, data);
        GL.BindTexture(GL.TEXTURE_2D, 0);
    }

    public void Draw(Mat4 view, Mat4 proj, int width, int height, float stroke,
        float distance, float depthSpan, bool strip = true, float opacity = 1, bool writeDepth = true)
    {
        int segments = strip ? _count - 1 : _count / 2;
        if (segments < 1 || _shader is null || width < 1 || height < 1) return;
        bool cull = GL.IsEnabled(GL.CULL_FACE);
        GL.Disable(GL.CULL_FACE); GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); GL.DepthMask(writeDepth);
        try
        {
            GL.ActiveTexture(GL.TEXTURE0); GL.BindTexture(GL.TEXTURE_2D, _texture);
            _shader.Use(); _shader.SetMatrix4("uView", view); _shader.SetMatrix4("uProj", proj);
            _shader.SetInt("uPoints", 0); _shader.SetInt("uStrip", strip ? 1 : 0);
            _shader.SetVec2("uViewport", width, height);
            _shader.SetFloat("uStroke", Math.Clamp(stroke, .5f, 12));
            _shader.SetFloat("uDistance", distance); _shader.SetFloat("uDepthSpan", Math.Max(.01f, depthSpan));
            _shader.SetFloat("uOpacity", Math.Clamp(opacity, 0, 1));
            GL.BindVertexArray(_vao); GL.DrawArrays(GL.TRIANGLES, 0, segments * 6);
        }
        finally
        {
            GL.BindVertexArray(0); GL.BindTexture(GL.TEXTURE_2D, 0);
            GL.DepthMask(true); GL.Disable(GL.BLEND); if (cull) GL.Enable(GL.CULL_FACE);
        }
    }

    public void Dispose()
    {
        _shader?.Dispose(); _shader = null;
        if (_texture != 0) GL.DeleteTexture(_texture);
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        _texture = _vao = 0;
    }

    private const string Vertex = """
        #version 330 core
        uniform sampler2D uPoints;
        uniform mat4 uView,uProj;
        uniform vec2 uViewport;
        uniform float uStroke,uDistance,uDepthSpan;
        uniform int uStrip;
        out vec3 vColor;
        noperspective out float vAcross;
        out float vDepth;
        vec4 fetch(int k){return texelFetch(uPoints,ivec2(k%1024,k/1024),0);}
        void main(){
            int s=gl_VertexID/6,k=gl_VertexID%6;
            float end=(k==1||k==2||k==4)?1.:0.;
            float side=(k==2||k==4||k==5)?1.:-1.;
            int a=uStrip==1?s:s*2;
            vec4 pa=fetch(a*2),pb=fetch((a+1)*2);
            vec4 va=uView*vec4(pa.xyz,1),vb=uView*vec4(pb.xyz,1);
            vec4 ca=uProj*va,cb=uProj*vb;
            bool valid=pa.w>.5&&pb.w>.5&&ca.w>.05&&cb.w>.05;
            vec2 d=(cb.xy/max(.05,cb.w)-ca.xy/max(.05,ca.w))*uViewport;
            float len=length(d);
            vec2 normal=len>1e-6?vec2(-d.y,d.x)/len:vec2(0,1);
            vec4 p=mix(ca,cb,end);
            p.xy+=normal*side*(uStroke+1.2)/uViewport*p.w;
            gl_Position=valid?p:vec4(3,3,3,1);
            vAcross=side;
            vColor=mix(fetch(a*2+1).xyz,fetch((a+1)*2+1).xyz,end);
            vDepth=clamp((uDistance+uDepthSpan+mix(va.z,vb.z,end))/(2.*uDepthSpan),0.,1.);
        }
        """;
    private const string Fragment = """
        #version 330 core
        in vec3 vColor;
        noperspective in float vAcross;
        in float vDepth;
        uniform float uStroke,uOpacity;
        out vec4 color;
        void main(){
            float r=abs(vAcross),edge=max(fwidth(vAcross),.04);
            float alpha=1.-smoothstep(1.-edge,1.,r);
            if(alpha<.02)discard;
            float roundness=sqrt(max(0.,1.-r*r));
            vec3 c=vColor*(.55+.50*roundness)*mix(.42,1.1,vDepth);
            c+=vec3(.13)*pow(roundness,18.)*vDepth;
            color=vec4(c,alpha*uOpacity);
        }
        """;
}
