namespace MakarovPhysics.Renderer;
/// <summary>Opt-in day/night exposure and local lights. A finite regular street
/// grid is evaluated from neighbouring cells, so a large town does not need
/// hundreds of per-fragment light uniforms. Local lights have no shadow maps.</summary>
public sealed class OutdoorLighting
{
    public float Daylight=1,StreetSpacing,GridLow,GridHigh;
    public readonly Vec3[] Positions=new Vec3[12],Colors=new Vec3[12],Directions=new Vec3[12];
    internal void Apply(Shader s)
    {
        // Bind defensively: uniform locations belong to this shader, not the previous draw.
        s.Use();
        s.SetFloat("uDaylight",Math.Clamp(Daylight,0,1));
        s.SetFloat("uStreetSpacing",StreetSpacing);
        s.SetFloat("uGridLow",GridLow);s.SetFloat("uGridHigh",GridHigh);
        for(int i=0;i<12;i++){
            s.SetVec3($"uLampPosition[{i}]",Positions[i]);s.SetVec3($"uLampColor[{i}]",Colors[i]);
            s.SetVec3($"uLampDirection[{i}]",Directions[i]);
        }
    }
    internal const string LocalLightingGlsl=@"
uniform int uLocalLighting;
uniform float uDaylight,uStreetSpacing,uGridLow,uGridHigh;
uniform vec3 uLampPosition[12],uLampColor[12],uLampDirection[12];
vec3 localLamp(vec3 pos,vec3 tint,vec3 axis,vec3 p,vec3 n,vec3 albedo,float range) {
    vec3 delta=pos-p;float d2=dot(delta,delta);if(d2>range*range)return vec3(0);
    vec3 l=delta*inversesqrt(max(d2,.001));
    float cone=dot(axis,axis)>.1?smoothstep(.78,.94,dot(-l,normalize(axis))):1.;
    float fall=pow(max(0.,1.-d2/(range*range)),2.)/(1.+d2*.08);
    return (albedo*.9+vec3(.05))*tint*(max(0.,dot(n,l))+.04)*fall*cone;
}
vec3 outdoorLightResult(vec3 shaded,vec3 albedo,vec3 n) {
    if(uLocalLighting==0)return shaded;
    vec3 result=shaded*mix(vec3(.12,.16,.24),vec3(1),uDaylight);
    float dark=1.-uDaylight;
    if(uStreetSpacing>0. && dark>.001) {
        vec2 cell=floor((vWorldPos.xz-uGridLow)/uStreetSpacing);
        for(int j=0;j<2;j++)for(int i=0;i<2;i++){
            vec2 node=vec2(uGridLow)+(cell+vec2(i,j))*uStreetSpacing;
            if(any(lessThan(node,vec2(uGridLow-.01)))||any(greaterThan(node,vec2(uGridHigh+.01))))continue;
            for(int side=-1;side<=1;side+=2) {
                result+=dark*localLamp(vec3(node.x+float(side)*5.15+.9,4.08,node.y+12.),
                    vec3(3.8,3.15,2.15),vec3(0),vWorldPos,n,albedo,15.);
                result+=dark*localLamp(vec3(node.x+12.9,4.08,node.y+float(side)*5.15),
                    vec3(3.8,3.15,2.15),vec3(0),vWorldPos,n,albedo,15.);
            }
        }
    }
    for(int i=0;i<12;i++)if(dot(uLampColor[i],uLampColor[i])>.001)
        result+=localLamp(uLampPosition[i],uLampColor[i],uLampDirection[i],vWorldPos,n,albedo,22.);
    return result;
}
";
}
