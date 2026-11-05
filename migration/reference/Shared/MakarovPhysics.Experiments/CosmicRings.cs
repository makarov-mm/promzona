using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>
/// A ring system as a lit, translucent sheet (wave W5, 20.10): a radial density
/// profile (Saturn's measured edges and gaps scaled to the ring's span, a
/// smooth generic sheet, or a few narrow Uranian ringlets), the planet's shadow
/// on the sheet by a ray against the planet's ellipsoid, back-scatter on the
/// sunlit face and a dimmer transmitted light on the other, and an optical
/// thickness that grows at grazing angles. The planet's own shader casts the
/// sheet's shadow back onto the planet with the same profile
/// (CosmosPainter.Fragment, `ringShade`). Unresolved ring material, not the
/// particles of SaturnRings, which keeps its own kilometre-scale shader.
/// </summary>
public sealed class CosmicRings : IDisposable
{
    private readonly Mesh _quad = new(
        [-1, 0, -1, 0, 1, 0, 1, 0, -1, 0, 1, 0, 1, 0, 1, 0, 1, 0, -1, 0, 1, 0, 1, 0],
        [0, 2, 1, 0, 3, 2]);
    private readonly Shader _shader = new(Vertex, Fragment);

    /// <summary>Saturn's ring span in kilometres, the scale of profile 0.</summary>
    public const double SaturnInnerKm = 66900, SaturnOuterKm = 140300;

    public void Draw(CosmosScene scene, Mat4 view, Mat4 projection, Vec3 eye, float exposure,
                     (Vec3 Pos, float Radius, bool Any) light)
    {
        if (scene.Rings.Count == 0) return;
        bool culled = GL.IsEnabled(GL.CULL_FACE);
        GL.Disable(GL.CULL_FACE);   // the sheet is seen from both hemispheres
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(false);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", projection);
        _shader.SetVec3("uEye", eye);
        _shader.SetFloat("uExposure", exposure);
        foreach (var ring in scene.Rings)
        {
            var axis = ring.Axis.Normalized();
            var seed = Math.Abs(axis.Y) < .9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            var u = Vec3.Cross(seed, axis).Normalized();
            var v = Vec3.Cross(axis, u);
            _shader.SetMatrix4("uModel", Mat4.FromColumns(u * ring.Outer, axis, v * ring.Outer, ring.Center));
            _shader.SetVec3("uCenter", ring.Center);
            _shader.SetVec3("uAxis", axis);
            _shader.SetFloat("uInner", ring.Inner);
            _shader.SetFloat("uOuter", ring.Outer);
            _shader.SetVec3("uTint", ring.Tint);
            _shader.SetInt("uProfile", ring.Profile);
            // the planet the sheet belongs to: the body at its centre
            int planet = -1;
            for (int i = 0; i < scene.Bodies.Count; i++)
                if ((scene.Bodies[i].Center - ring.Center).Length() < 1e-4f * Math.Max(1, ring.Outer)) { planet = i; break; }
            var body = planet >= 0 ? scene.Bodies[planet] : default;
            _shader.SetFloat("uHasPlanet", planet >= 0 ? 1 : 0);
            _shader.SetVec3("uPlanetSize", planet >= 0 ? body.Size : new Vec3(1, 1, 1));
            bool point = light.Any && (planet < 0 || !body.Emissive);
            var sun = planet >= 0 ? body.Sun.Normalized() : new Vec3(-.7f, .35f, .8f).Normalized();
            _shader.SetFloat("uPointLight", point ? 1 : 0);
            _shader.SetVec3("uLightPos", point ? light.Pos : ring.Center + sun * 1e4f);
            _shader.SetVec3("uSun", sun);
            _quad.Draw();
        }
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        if (culled) GL.Enable(GL.CULL_FACE);
    }

    public void Dispose()
    {
        _quad.Dispose();
        _shader.Dispose();
    }

    private const string Vertex = """
        #version 330 core
        layout(location=0) in vec3 aPos;
        layout(location=1) in vec3 aNormal;
        uniform mat4 uModel, uView, uProj;
        out vec3 world;
        void main(){ world=(uModel*vec4(aPos,1.)).xyz; gl_Position=uProj*uView*vec4(world,1.); }
        """;

    /// <summary>The density profiles, shared verbatim with the planet shader's
    /// ring shadow (CosmosPainter.Fragment). x runs 0..1 from the inner to the
    /// outer edge. Profile 0 maps x onto Saturn's 66 900..140 300 km and reads
    /// the measured edges: C ring, B ring, Cassini division, A ring, Encke and
    /// Keeler gaps, the F ring. Profile 1 is a smooth generic sheet, 2 a few
    /// narrow ringlets.</summary>
    public const string ProfileGlsl = """
        float ringDensity(float x,int profile){
            if(x<0.||x>1.) return 0.;
            if(profile==0){
                float km=66900.+x*73400.;
                float d=km<74658.?.055:km<91975.?.28:km<117507.?.94:km<122340.?.035:km<136775.?.68:.015;
                if(abs(km-133589.)<162.5||abs(km-136505.)<21.) d=.008;
                if(abs(km-140180.)<75.) d=.40;
                float ribbons=.83+.09*sin(km*.0013)+.055*sin(km*.007)+.05*(.5+.5*sin(km*.026));
                return clamp(d*ribbons,0.,1.);
            }
            if(profile==1){
                float d=.62*(.72+.28*sin(x*41.))*(.85+.15*sin(x*7.3+1.))*smoothstep(0.,.04,x)*(1.-smoothstep(.92,1.,x));
                return clamp(d,0.,1.);
            }
            float d=0.;
            for(int i=0;i<5;i++){ float c=.12+.2*float(i)+.03*sin(float(i)*4.7); d+=.9*exp(-pow((x-c)/.012,2.)); }
            return clamp(d,0.,1.);
        }
        """;

    private const string Fragment = """
        #version 330 core
        in vec3 world;
        uniform vec3 uEye,uCenter,uAxis,uTint,uPlanetSize,uLightPos,uSun;
        uniform float uInner,uOuter,uExposure,uHasPlanet,uPointLight;
        uniform int uProfile;
        out vec4 color;
        """ + "\n" + ProfileGlsl + "\n" + """
        void main(){
            vec3 p=world-uCenter; vec3 inPlane=p-uAxis*dot(p,uAxis);
            float r=length(inPlane), x=(r-uInner)/(uOuter-uInner);
            float d=ringDensity(x,uProfile);
            if(d<.004) discard;
            vec3 l=uPointLight>.5?normalize(uLightPos-world):normalize(uSun);
            vec3 v=normalize(uEye-world);
            // the planet's shadow: the ray toward the light against the ellipsoid
            float shade=0.;
            if(uHasPlanet>.5){
                vec3 q=p/uPlanetSize, lq=l/uPlanetSize;
                float b=dot(q,lq), det=b*b-dot(lq,lq)*(dot(q,q)-1.);
                shade=b<0.?smoothstep(-.015,.015,det):0.;
            }
            vec3 ice=mix(uTint*.55,uTint,smoothstep(.1,.85,d));
            float fine=.93+.04*sin(x*210.)+.03*sin(x*1190.)*(1.-smoothstep(.002,.01,fwidth(x)));
            float cosL=abs(dot(uAxis,l));
            bool litFace=dot(uAxis,l)*dot(uAxis,v)>0.;
            // back-scatter on the sunlit face; on the other, light through the sheet
            float lit=litFace?.06+1.0*(.35+.65*cosL)*(1.-shade):.04+.38*(1.-shade)*(1.-.6*d);
            float mu=max(abs(dot(uAxis,v)),.08);
            float alpha=1.-exp(-d*.95/mu);
            color=vec4(ice*fine*lit*uExposure,alpha);
        }
        """;
}
