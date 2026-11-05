using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Magnified photospheric morphology, evaluated per fragment so cell
/// borders and surface texture remain continuous at close viewing distances.</summary>
public sealed class CosmicPhotosphere : IDisposable
{
    private readonly Mesh _patch = new([-2.6f, .09f, -1.95f, 0, 1, 0, 2.6f, .09f, -1.95f, 0, 1, 0, 2.6f, .09f, 1.95f, 0, 1, 0, -2.6f, .09f, 1.95f, 0, 1, 0], [0, 2, 1, 0, 3, 2]);
    private readonly Shader _shader = new(Vertex, Fragment), _depth = new(Vertex, DepthFragment);
    /// <param name="hdr">Write linear radiance for the opaque HDR pass (the
    /// composite applies exposure and gamma); otherwise tonemap in place.</param>
    /// <param name="center">Centre of the patch (W9: it used to sit at the origin, y = 0.09).</param>
    /// <param name="extent">Size factor of the 5.2 x 3.9 patch; 1 is the original.</param>
    /// <param name="contrast">Granule-to-lane brightness contrast about the original look (1); the module's temperature contrast over 350 K (ring 3, 20.10).</param>
    public void Draw(Mat4 view, Mat4 projection, Vec3 eye, float scale, float phase, float exposure, bool hdr = false, Vec3 center = default, float extent = 1, float contrast = 1)
    {
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Disable(GL.CULL_FACE);
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", projection);
        _shader.SetFloat("uScale", scale);
        _shader.SetFloat("uPhase", phase);
        _shader.SetFloat("uExposure", exposure);
        _shader.SetFloat("uHdr", hdr ? 1 : 0);
        _shader.SetVec3("uCenter", center);
        _shader.SetFloat("uExtent", extent <= 0 ? 1 : extent);
        _shader.SetFloat("uContrast", contrast);
        _patch.Draw();
        GL.Enable(GL.CULL_FACE);
    }

    /// <summary>The patch into the depth buffer only (colour mask set by the caller).</summary>
    public void DrawDepth(Mat4 view, Mat4 projection, Vec3 center = default, float extent = 1)
    {
        _depth.Use();
        _depth.SetMatrix4("uView", view);
        _depth.SetMatrix4("uProj", projection);
        _depth.SetVec3("uCenter", center);
        _depth.SetFloat("uExtent", extent <= 0 ? 1 : extent);
        _patch.Draw();
    }

    public void Dispose()
    {
        _patch.Dispose();
        _shader.Dispose();
        _depth.Dispose();
    }

    private const string DepthFragment = """
        #version 330 core
        in vec2 uv;
        void main(){}
        """;

    private const string Vertex = """
        #version 330 core
        layout(location=0) in vec3 aPos;
        uniform mat4 uView,uProj;
        uniform vec3 uCenter; uniform float uExtent;
        out vec2 uv;
        void main(){uv=aPos.xz;gl_Position=uProj*uView*vec4(uCenter+aPos*uExtent,1.);}
        """;
    private const string Fragment = """
        #version 330 core
        in vec2 uv;
        uniform float uScale,uPhase,uExposure,uHdr,uContrast;
        out vec4 color;
        vec2 hash(vec2 p){return fract(sin(vec2(dot(p,vec2(127.1,311.7)),dot(p,vec2(269.5,183.3))))*43758.5453);}
        float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);return mix(mix(hash(i).x,hash(i+vec2(1,0)).x,f.x),mix(hash(i+vec2(0,1)).x,hash(i+1.).x,f.x),f.y);}
        void main(){
            vec2 p=uv*uScale*3.8;
            p+=vec2(noise(p*2.6),noise(p*2.6+9.))*.46;
            vec2 cell=floor(p),fraction=fract(p);float first=9.,second=9.,seed=0.;
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++){
                vec2 offset=vec2(x,y),h=hash(cell+offset);
                vec2 centre=offset+.5+.46*sin(uPhase*.6+6.28318*h);
                float d=length(centre-fraction);
                if(d<first){second=first;first=d;seed=h.x;}else second=min(second,d);
            }
            float edge=second-first;
            float bright=pow(smoothstep(.006,.30,edge),.55)*(.63+.28*seed);
            float grain=.7*noise(p*19.)+.3*noise(p*43.);
            bright*=.82+.30*grain;
            bright*=.48+.52*(1.-smoothstep(.15,.85,first));
            // the lane-to-granule contrast follows the module's temperature contrast
            bright=clamp(.5+(bright-.5)*uContrast,0.,1.);
            vec3 radiance=mix(vec3(.13,.035,.009),vec3(2.0,1.02,.40),bright);
            // Small bright points concentrate in some intergranular lanes.
            float points=pow(noise(p*12.),20.)*(1.-smoothstep(.02,.07,edge));
            radiance+=vec3(1.9,1.4,.7)*points;
            if(uHdr>.5){color=vec4(radiance,1.);return;}
            color=vec4(pow(1.-exp(-radiance*uExposure),vec3(1./2.2)),1.);
        }
        """;
}
