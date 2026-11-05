namespace MakarovPhysics.Renderer;

/// <summary>One world-anchored cloud deck for sky, terrain, water and apparatus.
/// Time is presentation seconds, never accelerated simulation time. Opt-in at each renderer.</summary>
public static class OutdoorAtmosphere
{
    // Render-space metres: the same intersection is used when looking up from
    // the camera and when following the sunlight upward from a surface.
    public const string Glsl = @"
float weatherHash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float weatherNoise(vec2 p) {
    vec2 i=floor(p),f=fract(p); f=f*f*(3.-2.*f);
    return mix(mix(weatherHash(i),weatherHash(i+vec2(1,0)),f.x),
        mix(weatherHash(i+vec2(0,1)),weatherHash(i+vec2(1,1)),f.x),f.y);
}
float cloudDensity(vec2 world,float seconds) {
    vec2 p=world*.025+vec2(seconds*.0011,seconds*.00032)+vec2(4.2,8.7);
    float f=0.,a=.5;
    for(int i=0;i<5;i++){f+=a*weatherNoise(p);p=p*2.03+vec2(17.1,9.2);a*=.5;}
    return smoothstep(.43,.69,f);
}
float cloudShade(vec3 world,vec3 sun,float seconds) {
    if(sun.y<=.03)return 1.;
    vec2 hit=world.xz+sun.xz*(max(0.,60.-world.y)/max(.12,sun.y));
    return 1.-.23*cloudDensity(hit,seconds);
}
vec3 outdoorSky(vec3 direction,vec3 origin,vec3 sun,float seconds) {
    vec3 d=normalize(direction);float alt=max(d.y,0.);
    vec3 sky=mix(vec3(.74,.84,.90),vec3(.22,.43,.68),pow(alt,.48));
    float s=max(dot(d,sun),0.);
    sky+=vec3(1.,.87,.64)*(.15*pow(s,12.)+1.4*pow(s,1600.));
    if(d.y>.025) {
        vec2 hit=origin.xz+d.xz*(max(1.,60.-origin.y)/d.y);
        float c=cloudDensity(hit,seconds);
        float rim=max(0.,c-cloudDensity(hit+sun.xz*3.,seconds));
        vec3 lit=mix(vec3(.96,.96,.93),vec3(.67,.73,.79),c*.6)+rim*.7;
        sky=mix(sky,lit,c*smoothstep(.025,.18,d.y)*.88);
    }
    return sky;
}
// Filtered soil/grass at several scales, with agricultural bands and soil
// gaps. The mesh supplies silhouette blades separately near a site.
vec3 fieldAlbedo(vec2 p,vec3 grass) {
    float macro=weatherNoise(p*.085),small=weatherNoise(p*2.7);
    float stripe=sin((p.y+.18*p.x)*2.8);
    stripe*=exp(-pow(fwidth((p.y+.18*p.x)*2.8),2.));
    vec3 dry=grass*vec3(1.18,1.04,.67)+vec3(.055,.035,.006);
    vec3 c=mix(grass*.76,dry,smoothstep(.2,.82,macro));
    c*=.95+.035*stripe+.10*small;
    float fine=mix(weatherNoise(p*65.),.5,smoothstep(.2,1.5,length(fwidth(p*65.))));
    return c*(.89+.22*fine);
}
// Shared lighting makes a local sculpted hillside meet the distant meadow
// without a rectangular change in exposure or colour.
vec3 outdoorGroundWithHaze(vec3 p,vec3 n,vec3 grass,vec3 sun,vec3 eye,float seconds,float shade,float haze) {
    vec3 col=fieldAlbedo(p.xz,grass);
    vec3 ambient=mix(vec3(.28,.30,.26),vec3(.42,.50,.62),.5+.5*n.y);
    vec3 lit=col*(ambient+vec3(1.,.96,.88)*max(dot(n,sun),0.)*.95*shade);
    lit*=mix(1.,cloudShade(p,sun,seconds),clamp(haze,0.,1.));
    float fog=1.-exp(-length(p-eye)/110.);
    return mix(lit,vec3(.74,.85,.96)*.95,clamp(fog*fog*1.1,0.,.94)*clamp(haze,0.,1.));
}
vec3 outdoorGround(vec3 p,vec3 n,vec3 grass,vec3 sun,vec3 eye,float seconds,float shade) {
    return outdoorGroundWithHaze(p,n,grass,sun,eye,seconds,shade,1.);
}
";
}
