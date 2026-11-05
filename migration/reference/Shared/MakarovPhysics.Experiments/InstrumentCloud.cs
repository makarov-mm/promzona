using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;
/// <summary>Depth-tested, back-to-front noisy billboards for explanatory steam and hot clouds.</summary>
public sealed class InstrumentCloud : IDisposable
{
 public readonly record struct Puff(Vec3 Position,float Radius,Vec3 Tint,float Opacity,float Seed);
 private Shader? _shader;private uint _vao,_vbo;private readonly float[] _data=new float[600*6*11];
 private const string Vertex=@"#version 330 core
layout(location=0) in vec4 centreSize;
layout(location=1) in vec2 corner;
layout(location=2) in vec3 tint;
layout(location=3) in vec2 alphaSeed;
uniform mat4 view;uniform mat4 projection;
out vec2 uv;out vec3 ink;out float alpha;out float seed;
void main(){vec4 p=view*vec4(centreSize.xyz,1);p.xy+=corner*centreSize.w;gl_Position=projection*p;uv=corner;ink=tint;alpha=alphaSeed.x;seed=alphaSeed.y;}";
 private const string Fragment=@"#version 330 core
in vec2 uv;in vec3 ink;in float alpha;in float seed;out vec4 color;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);}
void main(){float r=dot(uv,uv);if(r>=1.)discard;
float n=.56*noise(uv*3.+seed)+.29*noise(uv*7.-seed)+.15*noise(uv*15.+seed);
float shape=1.-r-.25*(1.-n);if(shape<=0.)discard;
float z=sqrt(max(0.,shape));float edge=smoothstep(0.,.22,shape);
vec3 normal=normalize(vec3(uv+vec2(.12*(n-.5),.1*(n-.5)),z));
float diffuse=max(0.,dot(normal,normalize(vec3(-.5,.7,1.))));
float density=(.55+.65*n)*z;float opacity=1.-exp(-alpha*edge*density*1.6);
float shade=.36+.64*diffuse;vec3 lit=ink*shade*(.78+.34*n);
color=vec4(lit,opacity);}";
 public InstrumentCloud()
 {
  try{_shader=new(Vertex,Fragment);_vao=GL.GenVertexArray();_vbo=GL.GenBuffer();GL.BindVertexArray(_vao);GL.BindBuffer(GL.ARRAY_BUFFER,_vbo);GL.BufferData(GL.ARRAY_BUFFER,_data.Length*sizeof(float),GL.DYNAMIC_DRAW);
   GL.VertexAttribPointer(0,4,GL.FLOAT,false,44,0);GL.EnableVertexAttribArray(0);GL.VertexAttribPointer(1,2,GL.FLOAT,false,44,16);GL.EnableVertexAttribArray(1);GL.VertexAttribPointer(2,3,GL.FLOAT,false,44,24);GL.EnableVertexAttribArray(2);GL.VertexAttribPointer(3,2,GL.FLOAT,false,44,36);GL.EnableVertexAttribArray(3);GL.BindVertexArray(0);
  }catch{Dispose();throw;}
 }
 public void Draw(Mat4 view,Mat4 projection,Vec3 eye,IEnumerable<Puff> source)
 {
  if(_shader is null)return;Puff[] puffs=source.Take(600).OrderByDescending(p=>(p.Position-eye).Length()).ToArray();
  float[] corners=[-1,-1,1,-1,1,1,-1,-1,1,1,-1,1];int index=0;
  foreach(var p in puffs)for(int i=0;i<6;i++){_data[index++]=p.Position.X;_data[index++]=p.Position.Y;_data[index++]=p.Position.Z;_data[index++]=p.Radius;_data[index++]=corners[i*2];_data[index++]=corners[i*2+1];_data[index++]=p.Tint.X;_data[index++]=p.Tint.Y;_data[index++]=p.Tint.Z;_data[index++]=p.Opacity;_data[index++]=p.Seed;}
  GL.BindBuffer(GL.ARRAY_BUFFER,_vbo);GL.BufferSubData(GL.ARRAY_BUFFER,0,_data,index);_shader.Use();_shader.SetMatrix4("view",view);_shader.SetMatrix4("projection",projection);
  GL.Enable(GL.DEPTH_TEST);GL.DepthMask(false);GL.Enable(GL.BLEND);GL.BlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);GL.Disable(GL.CULL_FACE);GL.BindVertexArray(_vao);GL.DrawArrays(GL.TRIANGLES,0,puffs.Length*6);GL.BindVertexArray(0);GL.DepthMask(true);GL.Disable(GL.BLEND);
 }
 public void Dispose(){_shader?.Dispose();_shader=null;if(_vbo!=0){GL.DeleteBuffer(_vbo);_vbo=0;}if(_vao!=0){GL.DeleteVertexArray(_vao);_vao=0;}}
}
