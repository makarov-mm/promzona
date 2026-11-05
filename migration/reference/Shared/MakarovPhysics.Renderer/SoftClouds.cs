namespace MakarovPhysics.Renderer;

public readonly record struct CloudSprite(Vec3 Position,float Radius,float Opacity,Vec3 Color,float Seed);
/// <summary>Alpha-blended, camera-facing puffs with procedural density. The
/// caller supplies physical/visual positions; this class never advances time.</summary>
public sealed class SoftClouds : IDisposable
{
    private Shader? _shader;
    private uint _vao,_vbo;
    private readonly float[] _data;
    private readonly List<CloudSprite> _sorted=[];
    public SoftClouds(int capacity=1024)
    {
        _data=new float[capacity*6*11];
        try
        {
            _shader=new Shader(Vertex,Fragment);_vao=GL.GenVertexArray();_vbo=GL.GenBuffer();
            GL.BindVertexArray(_vao);GL.BindBuffer(GL.ARRAY_BUFFER,_vbo);
            GL.BufferData(GL.ARRAY_BUFFER,(nint)(_data.Length*sizeof(float)),GL.DYNAMIC_DRAW);
            int[] sizes=[3,2,3,3];int offset=0;
            for(uint i=0;i<4;i++){GL.VertexAttribPointer(i,sizes[i],GL.FLOAT,false,44,offset*sizeof(float));GL.EnableVertexAttribArray(i);offset+=sizes[i];}
            GL.BindVertexArray(0);
        }
        catch{Dispose();throw;}
    }
    public void Draw(IReadOnlyList<CloudSprite> clouds,Mat4 view,Mat4 projection,Vec3 eye,Vec3 target)
    {
        if(_shader is not { } shader||clouds.Count==0)return;
        var forward=(target-eye).Normalized();_sorted.Clear();_sorted.AddRange(clouds);
        _sorted.Sort((a,b)=>Vec3.Dot(b.Position-eye,forward).CompareTo(Vec3.Dot(a.Position-eye,forward)));
        int count=Math.Min(_sorted.Count,_data.Length/66),p=0;
        ReadOnlySpan<float> uv=[-1,-1,1,-1,1,1,-1,-1,1,1,-1,1];
        for(int i=0;i<count;i++)for(int k=0;k<6;k++)
        {
            var c=_sorted[i];_data[p++]=c.Position.X;_data[p++]=c.Position.Y;_data[p++]=c.Position.Z;
            _data[p++]=uv[k*2];_data[p++]=uv[k*2+1];
            _data[p++]=c.Color.X;_data[p++]=c.Color.Y;_data[p++]=c.Color.Z;
            _data[p++]=c.Radius;_data[p++]=c.Opacity;_data[p++]=c.Seed;
        }
        bool depth=GL.IsEnabled(GL.DEPTH_TEST),blend=GL.IsEnabled(GL.BLEND),cull=GL.IsEnabled(GL.CULL_FACE);
        bool write=GL.GetInteger(0x0B72)!=0;
        uint src=(uint)GL.GetInteger(0x80C9),dst=(uint)GL.GetInteger(0x80C8);
        try
        {
            GL.Enable(GL.DEPTH_TEST);GL.DepthMask(false);GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);GL.BlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);
            shader.Use();shader.SetMatrix4("uView",view);shader.SetMatrix4("uProj",projection);
            GL.BindBuffer(GL.ARRAY_BUFFER,_vbo);GL.BufferSubData(GL.ARRAY_BUFFER,0,_data,count*66);
            GL.BindVertexArray(_vao);GL.DrawArrays(GL.TRIANGLES,0,count*6);GL.BindVertexArray(0);
        }
        finally
        {
            GL.DepthMask(write);GL.BlendFunc(src,dst);
            if(!depth)GL.Disable(GL.DEPTH_TEST);if(!blend)GL.Disable(GL.BLEND);if(cull)GL.Enable(GL.CULL_FACE);
        }
    }
    public void Dispose(){_shader?.Dispose();_shader=null;if(_vao!=0){GL.DeleteVertexArray(_vao);_vao=0;}if(_vbo!=0){GL.DeleteBuffer(_vbo);_vbo=0;}}
    private const string Vertex="""
        #version 330 core
        layout(location=0) in vec3 aPos;
        layout(location=1) in vec2 aCorner;
        layout(location=2) in vec3 aColor;
        layout(location=3) in vec3 aMeta;
        uniform mat4 uView,uProj;
        out vec2 vUV;out vec3 vColor;out vec2 vMeta;
        void main(){vec4 p=uView*vec4(aPos,1);p.xy+=aCorner*aMeta.x;gl_Position=uProj*p;vUV=aCorner;vColor=aColor;vMeta=aMeta.yz;}
        """;
    private const string Fragment="""
        #version 330 core
        in vec2 vUV;in vec3 vColor;in vec2 vMeta;out vec4 FragColor;
        float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
        float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);}
        void main(){
            float r=length(vUV);if(r>1.)discard;
            float a=vMeta.y*6.28;mat2 rot=mat2(cos(a),sin(a),-sin(a),cos(a));
            vec2 p=rot*vUV*2.7+vMeta.y*17.;
            float n=.55*noise(p)+.28*noise(p*2.1)+.17*noise(p*4.3);
            float edge=1.-smoothstep(.32,.98,r+(n-.5)*.22);
            float density=edge*(.48+.70*n)*vMeta.x;
            float light=.76+.21*vUV.y+.13*n;
            FragColor=vec4(vColor*light,clamp(density,0.,.85));
        }
        """;
}
