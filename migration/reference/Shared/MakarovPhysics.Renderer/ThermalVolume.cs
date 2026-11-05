namespace MakarovPhysics.Renderer;
/// <summary>Ray-marched scalar density with Beer-Lambert extinction and self-shadowed lighting.
/// The input is a simulation field, not a collection of moving cloud sprites.</summary>
public sealed unsafe class ThermalVolume : IDisposable
{
 private Shader? _shader;private uint _vao,_texture;private float[] _atlas=[];private long _revision=-1;private int _width,_height;
 private float _densityPeak=.8f;
 private Vec3 _hotCentre=new(0,2.5f,0);
 private const string Vertex=@"#version 330 core
out vec2 ndc;
void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);ndc=p*2.-1.;gl_Position=vec4(ndc,0.,1.);}";
 private const string Fragment=@"#version 330 core
in vec2 ndc;out vec4 color;
uniform sampler2D field;uniform vec3 grid;uniform vec3 low;uniform vec3 high;uniform vec3 eye;uniform mat4 inverseVP;uniform mat4 vp;uniform float age;uniform vec3 hotCentre;uniform float densityPeak;
vec2 sampleField(vec3 p){vec3 q=(p-low)/(high-low);if(any(lessThan(q,vec3(0)))||any(greaterThan(q,vec3(1))))return vec2(0);
 q*=grid-1.;float z=floor(q.z),w=q.z-z;
 vec2 a=vec2((q.x+.5+z*grid.x)/(grid.x*grid.z),(q.y+.5)/grid.y),b=a+vec2(grid.x/(grid.x*grid.z),0);
 return mix(texture(field,a).rg,texture(field,b).rg,w);}
float hash(vec3 p){return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453);}
float noise(vec3 p){vec3 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);}
void main(){vec4 farPoint=inverseVP*vec4(ndc,1,1);vec3 ray=normalize(farPoint.xyz/farPoint.w-eye);
 float radius=3.4*pow(age+.03,.4),extent=max(high.x,radius+2.);
 vec3 boundsLow=vec3(-extent,0.,-extent),boundsHigh=vec3(extent,max(high.y,radius+2.),extent);
 vec3 safe=mix(vec3(-1),vec3(1),greaterThanEqual(ray,vec3(0)))*max(abs(ray),vec3(.000001));
 vec3 ta=(boundsLow-eye)/safe,tb=(boundsHigh-eye)/safe,aa=min(ta,tb),bb=max(ta,tb);
 float begin=max(0.,max(aa.x,max(aa.y,aa.z))),end=min(bb.x,min(bb.y,bb.z));
 if(ray.y<-.0001)end=min(end,(.035-eye.y)/ray.y);if(end<=begin)discard;
 float stepSize=(end-begin)/220.,transmission=1.,first=-1.;vec3 sum=vec3(0),sun=normalize(vec3(-.6,1.,.35));
 float formed=smoothstep(.5,4.0,age),flash=exp(-age*.65),fireRadius=min(2.3,.45+pow(max(age,0.),.4));
 for(int i=0;i<220;i++){
  float t=begin+(float(i)+.5)*stepSize;if(transmission<.006)break;vec3 p=eye+ray*t;
  vec3 warp=.5*(vec3(noise(p*1.4),noise(p*1.4+31.),noise(p*1.4-17.))-.5);
  vec2 v=sampleField(p+warp);float billow=noise(p*1.7+vec3(0,-age*.30,0));
  float detail=.28+.50*smoothstep(.12,.9,billow)+.16*noise(p*4.3)+.06*noise(p*10.7);
  float opticalDensity=v.x/max(.015,densityPeak);
  float density=smoothstep(.035,.30,opticalDensity-.14*billow)*detail*8.*sqrt(max(.03,densityPeak)/.5)*formed;
  float optical=0.;if(density>.002)for(int j=1;j<=5;j++)optical+=sampleField(p+sun*float(j)*.65).x*.65;
  float light=exp(-optical*3.2),phase=.85+.15*pow(max(0.,dot(ray,sun)),4.);
  vec3 tint=mix(vec3(.55,.46,.34),vec3(.88,.89,.90),smoothstep(.6,3.4,p.y));
  vec3 smoke=tint*(.20+light*1.05)*phase+vec3(.045,.057,.075);
  float hot=smoothstep(.02,.35,v.y)*exp(-age*.27);
  vec3 fire=mix(vec3(1.8,.30,.04),vec3(3.3,1.7,.45),hot)*hot;
  vec3 radiance=smoke+fire;
  // Optical staging of the initial luminous thermal. This does not add explosive energy
  // to the low-Mach teaching solver, and is not a calibrated pressure/yield model.
  vec3 local=p-mix(vec3(0,1.1,0),hotCentre,smoothstep(0.,1.6,age));
  float hotEdge=length(local)/(fireRadius*(.88+.15*noise(local*3.-age*.8)));
  float fireDensity=(1.-smoothstep(.78,1.04,hotEdge))*14.*exp(-age*2.0);
  float core=clamp(1.-hotEdge,0.,1.);
  vec3 fireColor=mix(vec3(4.8,.65,.025),vec3(13.,11.5,8.),smoothstep(.05,.75,core))*flash;
  float total=density+fireDensity;
  if(total>.0001)radiance=(radiance*density+fireColor*fireDensity)/total;
  density=total;
  // A finite-thickness, turbulent hemisphere of transient condensation. Ground dust
  // trails the front. No opaque wire circle or schematic contour is drawn.
  float distance=length(p-vec3(0,1.1,0)),width=.12+.035*radius;
  float wave=exp(-pow((distance-radius)/width,2.))*smoothstep(.12,.35,age)*exp(-age*.75);
  wave*=smoothstep(.2,.65,noise(p*1.8-age*.2));
  float wake=exp(-pow((length(p.xz)-radius*.94)/(.35+age*.05),2.))*exp(-p.y/(.2+age*.04))*exp(-age*.4);
  float shell=wave*.65+wake*.65;
  if(shell>0.){vec3 shellColor=mix(vec3(.50,.42,.31),vec3(.92,.96,1.05),wave/max(.001,wave+wake));radiance=(radiance*density+shellColor*shell)/(density+shell);density+=shell;}
  if(density<.002)continue;float alpha=1.-exp(-density*stepSize);if(first<0.&&alpha>.001)first=t;
  sum+=transmission*alpha*radiance;transmission*=1.-alpha;
 }
 if(first<0.||transmission>.995)discard;vec4 depth=vp*vec4(eye+ray*first,1);gl_FragDepth=.5+.5*depth.z/depth.w;
 color=vec4(sum/max(.001,1.-transmission),1.-transmission);}";
 public ThermalVolume(){try{_shader=new(Vertex,Fragment);_vao=GL.GenVertexArray();_texture=GL.GenTexture();}catch{Dispose();throw;}}
 public unsafe void Draw(Mat4 view,Mat4 projection,Vec3 eye,int nx,int ny,int nz,float cell,float[] data,long revision,float age)
 {
  if(_shader is null)return;int width=nx*nz,height=ny;
  bool cull=GL.IsEnabled(GL.CULL_FACE),blend=GL.IsEnabled(GL.BLEND),depth=GL.IsEnabled(GL.DEPTH_TEST),depthWrite=GL.GetInteger(0x0B72)!=0;
  uint blendSrc=(uint)GL.GetInteger(0x80C9),blendDst=(uint)GL.GetInteger(0x80C8),alphaSrc=(uint)GL.GetInteger(0x80CB),alphaDst=(uint)GL.GetInteger(0x80CA);
  try {
  GL.ActiveTexture(GL.TEXTURE0);GL.BindTexture(GL.TEXTURE_2D,_texture);
  if(_revision!=revision||width!=_width||height!=_height){
   if(_atlas.Length!=width*height*4)_atlas=new float[width*height*4];
   double heat=0,hx=0,hy=0,hz=0;_densityPeak=0;
   for(int z=0;z<nz;z++)for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int src=(x+nx*(y+ny*z))*8,dst=(x+z*nx+y*width)*4;_atlas[dst]=data[src+4];_densityPeak=Math.Max(_densityPeak,data[src+4]);_atlas[dst+1]=data[src+3];double weight=Math.Max(0,data[src+3])*Math.Max(0,data[src+4]);heat+=weight;hx+=weight*(x-(nx-1)*.5)*cell;hy+=weight*y*cell;hz+=weight*(z-(nz-1)*.5)*cell;}
   if(heat>1e-8)_hotCentre=new((float)(hx/heat),(float)(hy/heat),(float)(hz/heat));
   fixed(float* p=_atlas){if(width!=_width||height!=_height)GL.TexImage2D(GL.TEXTURE_2D,0,0x881A,width,height,0,GL.RGBA,GL.FLOAT,p);else GL.TexSubImage2D(GL.TEXTURE_2D,0,0,0,width,height,GL.RGBA,GL.FLOAT,p);}
   GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MIN_FILTER,(int)GL.LINEAR);GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MAG_FILTER,(int)GL.LINEAR);GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_S,(int)GL.CLAMP_TO_EDGE);GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_T,(int)GL.CLAMP_TO_EDGE);_revision=revision;_width=width;_height=height;
  }
  Mat4 vp=projection*view;_shader.Use();_shader.SetMatrix4("vp",vp);_shader.SetMatrix4("inverseVP",vp.Inverted());_shader.SetVec3("eye",eye);_shader.SetVec3("grid",new(nx,ny,nz));_shader.SetVec3("low",new(-(nx-1)*cell*.5f,0,-(nz-1)*cell*.5f));_shader.SetVec3("high",new((nx-1)*cell*.5f,(ny-1)*cell,(nz-1)*cell*.5f));_shader.SetFloat("age",age);_shader.SetVec3("hotCentre",_hotCentre);_shader.SetFloat("densityPeak",_densityPeak);_shader.SetInt("field",0);
  GL.Enable(GL.DEPTH_TEST);GL.DepthMask(false);GL.Enable(GL.BLEND);GL.BlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);GL.Disable(GL.CULL_FACE);GL.BindVertexArray(_vao);GL.DrawArrays(GL.TRIANGLES,0,3);GL.BindVertexArray(0);GL.BindTexture(GL.TEXTURE_2D,0);
  } finally {GL.DepthMask(depthWrite);GL.BlendFuncSeparate(blendSrc,blendDst,alphaSrc,alphaDst);if(cull)GL.Enable(GL.CULL_FACE);else GL.Disable(GL.CULL_FACE);if(blend)GL.Enable(GL.BLEND);else GL.Disable(GL.BLEND);if(depth)GL.Enable(GL.DEPTH_TEST);else GL.Disable(GL.DEPTH_TEST);}
 }
 public void Dispose(){_shader?.Dispose();_shader=null;if(_vao!=0){GL.DeleteVertexArray(_vao);_vao=0;}if(_texture!=0){GL.DeleteTexture(_texture);_texture=0;}}
}
