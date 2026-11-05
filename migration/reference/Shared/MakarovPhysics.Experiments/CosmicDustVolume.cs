using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>A prescribed, clumpy dust torus. Integrates opacity along each view
/// ray; illumination is an artistic proxy, not a radiative-equilibrium solver.</summary>
public sealed class CosmicDustVolume : IDisposable
{
    private readonly Mesh _bound = Primitives.Sphere(48, 24);
    private readonly Shader _shader = new(Vertex, Fragment);
    /// <param name="center">Centre of the torus (W9: it used to sit at the origin).</param>
    /// <param name="scale">Size factor; 1 is the original torus of major radius 1.48.</param>
    public void Draw(Mat4 view, Mat4 projection, Vec3 eye, float opticalDepth, float exposure, Vec3 center = default, float scale = 1)
    {
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE_MINUS_SRC_ALPHA);
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", projection);
        _shader.SetVec3("uEye", eye);
        _shader.SetVec3("uCenter", center);
        _shader.SetFloat("uScale", scale <= 0 ? 1 : scale);
        _shader.SetFloat("uDepth", opticalDepth);
        _shader.SetFloat("uExposure", exposure);
        _bound.Draw();
        GL.Disable(GL.BLEND);
    }

    public void Dispose()
    {
        _bound.Dispose();
        _shader.Dispose();
    }

    private const string Vertex = """
        #version 330 core
        layout(location=0) in vec3 aPos;
        uniform mat4 uView,uProj;
        uniform vec3 uCenter; uniform float uScale;
        out vec3 world;
        void main(){world=uCenter+aPos*2.08*uScale;gl_Position=uProj*uView*vec4(world,1);}
        """;
    private const string Fragment = """
        #version 330 core
        in vec3 world;
        uniform mat4 uView,uProj;
        uniform vec3 uEye,uCenter;
        uniform float uDepth,uExposure,uScale;
        out vec4 colour;
        float hash(vec3 p){p=fract(p*.3183099+vec3(.1,.2,.3));p*=17.;return fract(p.x*p.y*p.z*(p.x+p.y+p.z));}
        float noise(vec3 p){vec3 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);
            return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
                       mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);}
        void main(){
            // the march runs in the torus's own frame: centred, unit scale
            vec3 eye=(uEye-uCenter)/uScale;
            vec3 dir=normalize(world-uEye);
            float b=dot(eye,dir),h=b*b-dot(eye,eye)+2.08*2.08;
            if(h<0.)discard;
            h=sqrt(h);float begin=max(0.,-b-h),end=-b+h,ds=(end-begin)/88.;
            vec3 light=vec3(0);float trans=1.,depthAt=end;bool found=false;
            for(int i=0;i<88;i++){
                float t=begin+(float(i)+.5)*ds;vec3 p=eye+dir*t;
                float radial=length(p.xz),tube=length(vec2(radial-1.48,p.y));
                float turbulence=.50*noise(p*7.)+.30*noise(p*17.)+.20*noise(p*39.);
                float density=exp(-pow(tube/.43,4.))*smoothstep(.20,.62,turbulence);
                float a=1.-exp(-density*ds*uDepth*2.2);
                float inner=exp(-max(0.,radial-1.0)*3.5);
                float top=.36+.64*smoothstep(-.45,.45,p.y);
                vec3 lit=mix(vec3(.12,.095,.075)*top,vec3(1.1,.38,.075),inner);
                lit*=.72+.65*turbulence;
                light+=trans*a*lit*uExposure;trans*=1.-a;
                if(!found&&trans<.82){depthAt=t;found=true;}
                if(trans<.015)break;
            }
            if(trans>.985)discard;
            // Opaque core rays stop following additive particles from showing
            // through the dust; the feathered fringe remains translucent.
            vec4 clip=uProj*uView*vec4(uEye+dir*depthAt*uScale,1);
            gl_FragDepth=trans<.25 ? clip.z/clip.w*.5+.5 : .999999;
            colour=vec4(light,1.-trans);
        }
        """;
}
