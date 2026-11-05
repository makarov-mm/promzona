using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Continuous, deterministic density fields for extended emission.
/// Shell, cloud and disk morphology are illustrative; scientific measurements
/// remain owned by the module model. Front-to-back integration replaces point fog.</summary>
public sealed class CosmicNebula : IDisposable
{
    private readonly Mesh _bound = Primitives.Sphere(64, 32);
    private readonly Shader _shader = new(Vertex, Fragment);
    public void Draw(CosmicVolume volume, IReadOnlyList<CosmicBody> bodies, Mat4 view, Mat4 projection, Vec3 eye, float exposure)
    {
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(false);
        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE_MINUS_SRC_ALPHA);
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", projection);
        _shader.SetVec3("uEye", eye);
        _shader.SetVec3("uCenter", volume.Center);
        _shader.SetVec3("uSize", volume.Size);
        _shader.SetVec3("uTint", volume.Color);
        _shader.SetFloat("uDensity", volume.Density);
        _shader.SetFloat("uKind", volume.Kind);
        _shader.SetFloat("uPhase", volume.Phase);
        _shader.SetFloat("uCut",volume.Cut);
        _shader.SetFloat("uExposure", exposure);
        // Stop at the first opaque ellipsoid on the ray, including a star embedded in the cloud.
        int count = Math.Min(8, bodies.Count);
        _shader.SetInt("uCount", count);
        for (int i = 0; i < count; i++)
        {
            _shader.SetVec3($"uBody[{i}]", bodies[i].Center);
            _shader.SetVec3($"uRadius[{i}]", bodies[i].Size);
        }
        // The stars of the scene light the gas (W9): up to two, nearest first,
        // in the volume's own frame; none leaves the old radial proxy.
        int lights = 0;
        for (int i = 0; i < bodies.Count && lights < 2; i++)
        {
            var b = bodies[i];
            if (b.Surface is not (CosmicSurface.Star or CosmicSurface.Solar)) continue;
            // Map the light into the same ellipsoid coordinates used by the shader.
            Vec3 offset = b.Center - volume.Center;
            Vec3 local = new(offset.X / volume.Size.X, offset.Y / volume.Size.Y, offset.Z / volume.Size.Z);
            _shader.SetVec3($"uLight[{lights}]", local);
            _shader.SetVec3($"uLightColor[{lights}]", b.Color);
            lights++;
        }
        _shader.SetInt("uLights", lights);

        _bound.Draw();
        GL.Disable(GL.BLEND);
        GL.DepthMask(true);
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
        uniform vec3 uCenter,uSize;
        out vec3 world;
        void main(){world=uCenter+aPos*uSize;gl_Position=uProj*uView*vec4(world,1);}
        """;
    private const string Fragment = """
        #version 330 core
        in vec3 world;
        uniform vec3 uEye,uCenter,uSize,uTint,uBody[8],uRadius[8],uLight[2],uLightColor[2];
        uniform float uKind,uDensity,uPhase,uExposure,uCut;
        uniform int uCount,uLights;
        out vec4 color;
        float hash(vec3 p){p=fract(p*.3183099+vec3(.1,.2,.3));p*=17.;return fract(p.x*p.y*p.z*(p.x+p.y+p.z));}
        float noise(vec3 p){vec3 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);
            return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
                       mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);}
        float fbm(vec3 p){return .54*noise(p)+.27*noise(p*2.13)+.13*noise(p*4.31)+.06*noise(p*8.7);}
        vec2 hit(vec3 o,vec3 d){float a=dot(d,d),b=dot(o,d),h=b*b-a*(dot(o,o)-1.);if(h<0.)return vec2(1e9,-1e9);return vec2(-b-sqrt(h),-b+sqrt(h))/a;}
        void main(){
            vec3 ray=normalize(world-uEye);
            vec2 range=hit((uEye-uCenter)/uSize,ray/uSize);
            float begin=max(0.,range.x),end=range.y;
            for(int j=0;j<8;j++){if(j>=uCount)break;vec2 h=hit((uEye-uBody[j])/uRadius[j],ray/uRadius[j]);if(h.x>0.&&h.y>h.x)end=min(end,h.x);}
            if(end<=begin)discard;
            float stepSize=(end-begin)/72.,trans=1.;vec3 light=vec3(0);
            for(int i=0;i<72;i++){
                vec3 p=(uEye+ray*(begin+(float(i)+.5)*stepSize)-uCenter)/uSize;
                float r=length(p),turb=fbm(p*12.+vec3(0.,uPhase*.13,0.));
                float filaments=pow(max(0.,1.-abs(2.*fbm(p*23.+3.*turb)-1.)),4.);
                float density=0.;vec3 tint=uTint;
                if(uKind<1.5){
                    vec3 shaped=p;shaped.xz/=.68+.44*abs(p.y);
                    float shellRadius=length(shaped);
                    float shell=exp(-pow((shellRadius-.70-.09*(turb-.5))/.033,2.));
                    float folds=.04+2.8*pow(filaments,3.);
                    density=shell*folds;
                    tint=mix(vec3(.12,.75,.88),uTint,smoothstep(.1,.8,abs(p.y)));
                    tint*=1.2+2.5*filaments;
                    density+=.035*exp(-pow((r-.9)/.03,2.))*(.3+.7*turb);
                }else if(uKind<2.5){
                    density=pow(smoothstep(.26,.67,turb),2.)*(1.-smoothstep(.30,1.,r));
                    tint=mix(vec3(.12,.23,.46),uTint,filaments);
                }else if(uKind<3.5){
                    float radial=length(p.xz),angle=atan(p.z,p.x);
                    float spiral=.6+.4*sin(angle*2.+radial*16.+turb*5.);
                    density=exp(-pow(p.y/(.05+.25*radial),2.))*smoothstep(.09,.2,radial)*(1.-smoothstep(.82,1.,radial))*(.4+.6*turb)*spiral;
                    tint=mix(vec3(1.,.58,.24),uTint,smoothstep(.2,.7,radial));
                }else if(uKind<4.5){
                    // Non-emissive storm cloud: soft density, darker interior
                    // and bright upper billows, without a solid shiny shell.
                    float edge=1.-smoothstep(.65,1.,r+.18*(turb-.5));
                    density=smoothstep(.25,.63,turb)*edge*2.2;
                    tint=uTint*(.25+.8*smoothstep(-.7,.8,p.y)+.2*turb);
                }
                if(uKind>4.5 && uKind<5.5){
                    // A spherical ionization front from the uniform-gas model.
                    // Small-scale structure illustrates gas, not a second solver.
                    float front=clamp(uPhase,0.,.94),neutral=smoothstep(front-.012,front+.012,r);
                    float macro=fbm(p*3.3);
                    float cloud=(.015+2.8*pow(smoothstep(.25,.7,macro),3.))*(1.-smoothstep(.82,1.,r));
                    float skin=exp(-pow((r-front)/.025,2.));
                    density=cloud*(.008+.9*neutral)+skin*.35*(.25+1.5*turb);
                    if(uCut>.5 && p.z>.06) density=0.;
                    tint=mix(vec3(.16,.44,.82),vec3(.48,.21,.095),neutral);
                    tint=mix(tint,vec3(.34,.75,1.4),skin*.85);
                }else if(uKind>5.5 && uKind<6.5){
                    // A translucent annotation membrane; not a material wall.
                    float shell=exp(-pow((r-.98)/.008,2.));
                    density=shell*(.4+.6*pow(1.-abs(dot(normalize(p),ray)),3.));
                    tint=uTint;
                }
                if(uKind>6.5 && uKind<7.5){
                    vec3 warped=p*3.5+vec3(fbm(p*5.),fbm(p*5.+11.),fbm(p*5.+29.))*.9;
                    float macro=fbm(warped), vein=pow(1.-abs(2.*fbm(warped*4.)-1.),9.);
                    float envelope=1.-smoothstep(.35,.92,r+.65*(macro-.5));
                    density=envelope*(.02+4.*pow(smoothstep(.29,.72,macro),3.))*(.45+1.7*vein);
                    float heated=exp(-r*r*25.)*uPhase;
                    tint=mix(vec3(.09,.13,.21),uTint,.2+.7*vein);
                    tint=mix(tint,vec3(1.7,.48,.12),heated);
                    if(uCut>.5 && p.z>0.)density=0.;
                }else if(uKind>7.5 && uKind<8.5){
                    vec3 d=normalize(p);float angular=fbm(d*9.);
                    float shock=.84+.022*(angular-.5);
                    float ridge=pow(1.-abs(2.*fbm(d*23.+angular*3.)-1.),14.);
                    float shell=exp(-pow((r-shock)/(.012+.022*turb),2.));
                    float inside=exp(-pow((r-.76)/.055,2.));
                    density=shell*(.035+3.*ridge)+inside*.45*pow(filaments,5.);
                    vec3 optical=vec3(1.5,.48,.12)*(1.+ridge*.6);
                    vec3 xray=vec3(.13,.52,1.1)*(1.+angular*.6);
                    tint=uPhase<.5 ? mix(xray,optical,.25+.55*smoothstep(.81,.88,r)) : uPhase<1.5 ? optical : xray;
                    if(uCut>.5 && p.z>.08)density=0.;
                }
                float a=1.-exp(-density*stepSize*uDensity*1.7);
                // illumination: the scene's stars when there are any (a glow
                // that falls off from each star, tinted by it), else the old
                // radial proxy about the volume's centre
                vec3 illumination=vec3(.55+.8*exp(-r*2.));
                if(uLights>0){
                    illumination=vec3(.40);
                    for(int k=0;k<2;k++){ if(k>=uLights)break; float dist=length(p-uLight[k]); illumination+=uLightColor[k]*1.1*exp(-dist*dist*1.6); }
                }
                light+=trans*a*tint*illumination*uExposure;trans*=1.-a;
                if(trans<.03)break;
            }
            if(trans>.998)discard;
            color=vec4(light,1.-trans);
        }
        """;
}
