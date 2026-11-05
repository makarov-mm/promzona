namespace Promzona.Game;

/// <summary>Opt-in production-exhibit finishes. Procedural texture coordinates are in metres.
/// Reflections are a filtered analytic studio, not ray-traced reflections of scene geometry.
/// Fine patterns fade below a pixel instead of sparkling as the camera moves.</summary>
internal static class WorkshopSurface
{
    internal const string WorkshopLightingGlsl = @"
float workshopNoise(vec3 p) {
    float footprint=length(fwidth(p));
    return mix(noiseSurface(p),.5,smoothstep(.35,1.5,footprint));
}
vec2 workshopPlane(vec3 p,vec3 n) {
    vec3 a=abs(n);
    return a.y>a.x && a.y>a.z?p.xz:(a.x>a.z?p.zy:p.xy);
}
float workshopPanel(vec3 r,vec3 direction,vec3 across,vec2 size,float rough) {
    float facing=dot(r,direction);
    vec2 uv=vec2(dot(r,across),dot(r,cross(direction,across)))/max(.08,facing);
    float blur=.025+rough*rough*1.8;
    vec2 edge=1.-smoothstep(size-vec2(blur),size+vec2(blur),abs(uv));
    return edge.x*edge.y*smoothstep(0.,.2,facing);
}
vec3 workshopEnvironment(vec3 r,float rough) {
    vec3 base=mix(vec3(.32,.30,.28),vec3(.72,.79,.86),smoothstep(-.45,.7,r.y));
    float ceiling=workshopPanel(r,normalize(vec3(.1,.94,.32)),vec3(1,0,0),vec2(.72,.14),rough);
    float window=workshopPanel(r,normalize(vec3(-.8,.32,.50)),normalize(vec3(.53,0,.848)),vec2(.42,.64),rough);
    float rim=workshopPanel(r,normalize(vec3(.85,.3,-.43)),normalize(vec3(.451,0,.893)),vec2(.16,.65),rough);
    return base+vec3(1.65,1.57,1.40)*ceiling+vec3(1.1,1.32,1.55)*window+vec3(.8,.95,1.2)*rim;
}
vec4 shadeWorkshop(vec3 colour,vec3 normal,vec3 p,int material) {
    vec3 c=max(colour,vec3(.001)),n=normalize(normal);
    vec3 v=normalize(uViewPos-vWorldPos);
    if(dot(n,v)<0.)n=-n;
    if(material==36) {
        // Dense straw/grain is rough organic matter, not varnished wood. A cheap
        // two-sided diffuse/transmission lobe keeps a whole moving field affordable.
        vec3 l=normalize(uLightDir);
        float nl=max(0.,dot(n,l));
        float shadow=uShadowStrength>0.?mix(1.,shadowFactor(vWorldPos,nl),uShadowStrength):1.;
        float back=pow(max(0.,dot(-v,l)),3.);
        vec3 result=c*(vec3(.28,.29,.24)+uLightColor*(.30*nl+.08*back)*shadow);
        if(uOutdoor==1)result*=cloudShade(vWorldPos,l,uTime);
        result/=1.+max(max(result.r,result.g),result.b)*.24;
        return vec4(result,1);
    }
    float rough=.34,metal=0.,coat=0.,h=0.,strength=1.;
    vec2 uv=workshopPlane(p,n);
    float fine=workshopNoise(p*240.),broad=workshopNoise(p*3.7);
    if(material==1) {
        // Baked powder-coat: a quiet orange peel, not exposed bare metal.
        h=(fine-.5)*.000075;rough=.30+.065*fine;coat=.22;
        c*=.97+.045*broad;
    } else if(material==2||material==3||material==10||material==15||material==34) {
        float brush=fineStripe(p.y*6800.+p.z*35.);
        float rings=fineStripe(length(p.xz)*12500.);
        float marks=material==15?mix(brush,rings,.55):brush;
        h=marks*.000007;
        rough=(material==15?.21:material==34?.26:.18)+.045*fine;
        metal=1.;c*=.96+.025*marks;
    } else if(material==16) {
        h=(workshopNoise(p*850.)-.5)*.00032;
        rough=.64;metal=.65;c*=.92+.10*fine;
    } else if(material==4||material==33) {
        float grain=workshopNoise(vec3(p.x*1.8,p.y*18.,p.z*35.));
        float fibres=fineStripe(p.z*2200.+grain*8.);
        float annual=fineStripe(p.z*95.+grain*9.+sin(p.x*2.)*2.);
        c*=.78+.25*grain+.08*annual+.018*fibres;
        h=fibres*.000018+annual*.000012;
        rough=material==33?.27:.49;coat=material==33?.6:.12;
    } else if(material==5||material==26) {
        h=(fine-.5)*.000045;rough=material==5?.82:.38;
        c*=.96+.045*fine;coat=material==26?.12:0.;
    } else if(material==6||material==35) {
        rough=.18;coat=.55;h=(fine-.5)*.000025;
        // Tile grout is authored as geometry; do not draw a second grid over it.
    } else if(material==11||material==17||material==30) {
        float pores=workshopNoise(p*600.);
        c*=.92+.07*broad+.025*pores;
        h=(pores-.5)*(material==30?.00012:.0003);
        rough=material==30?.88:.76;
    } else if(material==27) {
        // Plain weave: warp and weft, softened at a distance.
        vec2 cycles=uv*3500.;
        float warp=fineStripe(cycles.x),weft=fineStripe(cycles.y);
        h=(warp+weft)*.000016;rough=.9;
        c*=.93+.035*warp+.035*weft;
    } else if(material==28) {
        // Wet slip with concentric throwing marks; cut faces retain their supplied colour.
        float rings=fineStripe(p.y*2400.+workshopNoise(p*30.)*2.);
        h=rings*.000035+(fine-.5)*.000018;rough=.24;coat=.45;
        c*=.94+.05*broad+.025*rings;
    } else if(material==29) {
        vec2 cell=uv/vec2(.24,.08);
        cell.x+=mod(floor(cell.y),2.)*.5;
        vec2 edge=min(fract(cell),1.-fract(cell))*vec2(.24,.08);
        float mortar=smoothstep(.003,.0055,min(edge.x,edge.y));
        float brick=hashSurface(vec3(floor(cell),7.));
        c=mix(vec3(.31,.28,.24),c*(.83+.25*brick),mortar);
        c*=.95+.055*fine;h=(mortar-1.)*.0018+(fine-.5)*.0004;rough=.87;
    } else if(material==31) {
        h=(fine-.5)*.000009;rough=.84;c*=.97+.025*fine;
    } else if(material==32) {
        rough=.10;metal=.75;
        float angle=1.-abs(dot(n,v));
        c*=mix(vec3(.72,.84,1.),vec3(1.,.66,.45),angle);
        // Die boundaries and exposure are drawn by the actual wafer model.
    } else if(material==13||material==14||material==22) {
        rough=material==22?.09:.055;metal=material==22?0.:1.;coat=.35;
    } else if(material==37) {
        rough=.40;coat=.12;
    } else if(material==12) {
        rough=.24;metal=1.;h=(workshopNoise(p*40.)-.5)*.0007;
    }
    n=surfaceBump(n,h,strength);
    // Specular anti-aliasing on curved surfaces and fine normal detail.
    float variance=max(dot(dFdx(n),dFdx(n)),dot(dFdy(n),dFdy(n)));
    rough=clamp(sqrt(rough*rough+min(.2,variance*.6)),.055,.95);
    vec3 l=normalize(uLightDir),halfway=normalize(l+v),r=reflect(-v,n);
    float nv=max(.001,dot(n,v)),nl=max(0.,dot(n,l));
    vec3 f0=mix(vec3(.04),c,metal),f=fresnelSchlick(max(0.,dot(v,halfway)),f0);
    float shadow=uShadowStrength>0.?mix(1.,shadowFactor(vWorldPos,nl),uShadowStrength):1.;
    vec3 specular=f*distributionGGX(max(0.,dot(n,halfway)),rough)*visibilitySmith(nv,nl,rough);
    vec3 direct=((1.-f)*c*(1.-metal)/PI+specular)*uLightColor*nl*shadow;
    vec3 irradiance=mix(vec3(.17,.15,.13),vec3(.46,.53,.62),n.y*.5+.5);
    vec3 env=workshopEnvironment(r,rough);
    if(uOutdoor==1) {
        // Broad reflections on painted/rough surfaces need no high-frequency cloud lookup.
        env=metal>.7 && rough<.26 ? outdoorSky(r,vWorldPos,l,uTime)
            : mix(vec3(.31,.32,.23),vec3(.58,.72,.90),smoothstep(-.18,.65,r.y));
        irradiance=mix(vec3(.22,.22,.17),vec3(.52,.63,.76),n.y*.5+.5);
        direct*=cloudShade(vWorldPos,l,uTime);
    }
    vec2 brdf=envBRDFApprox(nv,rough);
    vec3 reflected=env*(f0*brdf.x+vec3(brdf.y));
    vec3 result=direct+c*(1.-metal)*irradiance*.68+reflected*.85;
    // Thin dielectric clear coat over paint, varnish, glaze and wet clay.
    if(coat>0.) {
        vec2 ab=envBRDFApprox(nv,.16);
        float fres=.04+.96*pow(1.-nv,5.);
        float coatRough=max(.20,rough*.7);
        float coatSpec=distributionGGX(max(0.,dot(n,halfway)),coatRough)*visibilitySmith(nv,nl,coatRough);
        float coatF=.04+.96*pow(1.-max(0.,dot(v,halfway)),5.);
        result=result*(1.-coat*fres*.45)+workshopEnvironment(r,.16)*(.04*ab.x+ab.y)*coat*.45
             +uLightColor*(coat*coatF*coatSpec*nl*shadow*.28);
    }
    if(material==37) {
        // Warm transmission from the lamp beneath the bottle; an analytic presentation light.
        float lamp=1./(1.+.5*(p.y-.75)*(p.y-.75));
        float wrapped=.4+.6*max(0.,dot(-n,normalize(vec3(-p.x,.75-p.y,-p.z))));
        result+=c*vec3(1.0,.55,.24)*lamp*wrapped*.85;
    }
    result=max(result,vec3(0));
    result=result/(1.+max(max(result.r,result.g),result.b)*.24);
    return vec4(result,1);
}
";
}
