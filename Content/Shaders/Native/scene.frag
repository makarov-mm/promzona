#version 330 core
in vec3 vWorld, vNormal, vLocal;
in vec2 vUv;
flat in vec4 vDetail;
out vec4 fragColor;
uniform sampler2D uColor, uNormal, uOrm, uRug;
uniform int uHasNormal, uHasOrm, uSkin, uSurface, uDetail;
uniform int uPaintIndex, uPaintMask;
// 1 on the body of a car, 2 on its glass: the clear coat that mirrors the city.
// -1 on a wreck, which mirrors nothing.
uniform int uCoat;
// 1 while a face of the reflection probe is rendered: linear light is kept.
uniform int uProbePass;
uniform vec3 uEye, uTint;
uniform float uEmissive, uAlpha, uTime, uHour;
#include "environment.glsl"
uniform float uRoughness, uMetallic, uDetailStart, uDetailEnd;
uniform sampler2D uShadow;
uniform mat4 uShadowMatrix;
uniform int uDepthPass, uTreeLod;
uniform float uShadowStrength;
// A dressed person: clothing choice plus one (zero: not a person), skin tone 0..5 and
// flags (1 trouser legs, 2 sleeves); hair colour, or a negative red for the palette's.
uniform vec3 uOutfit, uHair;
uniform int uLocalLights, uGridNodes;
uniform sampler2D uLampMap, uYardMask;
uniform float uGridLow, uLampSide;
uniform vec3 uLampPosition[12], uLampColor[12], uLampDirection[12];
const float PI = 3.14159265;
float hash(float n) { return fract(sin(n * 12.9898 + 1.312) * 43758.5453); }
// Value noise of the managed surface shader, for the patches of a wet street.
float latticeHash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
float patches(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f*f*(3.-2.*f);
    return mix(mix(mix(latticeHash(i), latticeHash(i+vec3(1,0,0)), f.x),
                   mix(latticeHash(i+vec3(0,1,0)), latticeHash(i+vec3(1,1,0)), f.x), f.y),
               mix(mix(latticeHash(i+vec3(0,0,1)), latticeHash(i+vec3(1,0,1)), f.x),
                   mix(latticeHash(i+vec3(0,1,1)), latticeHash(i+vec3(1,1,1)), f.x), f.y), f.z);
}
float foliage(vec2 uv, float species) {
    vec2 p = uv*2.-1.;
    float mask = 0.;
    // Three branching twigs per card. Broadleaf blades are roughly 5--10cm
    // at close LOD; spruce uses narrow paired needles on the same branches.
    for (int twig=-1; twig<=1; ++twig) {
        float a = float(twig)*.48;
        vec2 q = mat2(cos(a),-sin(a),sin(a),cos(a))*(p-vec2(0,-.8));
        float stem = (1.-smoothstep(.012,.025,abs(q.x)))*step(0.,q.y)*step(q.y,1.55);
        float row = floor(q.y*4.);
        float taper = 1.-clamp(q.y/1.8,0.,1.)*.55;
        float x = abs(q.x)/max(.2,taper);
        float y = fract(q.y*4.)-.5;
        vec2 leaf = vec2((x-.19)/.155,(y-x*.8)/.42);
        if (species < .5) leaf = vec2((x-.17)/.123,(y-x*.95)/.37);
        if (species > 1.5) leaf = vec2((x-.18)/.18,(y-x*.8)/.15);
        float blade = 1.-smoothstep(.85,1.,dot(leaf,leaf));
        mask = max(mask,max(stem,blade*step(0.,row)*step(row,5.)));
    }
    return mask*step(abs(p.x),.98)*step(abs(p.y),.98);
}
float sunlight(vec3 normal) {
    if (uShadowStrength <= 0.) return 1.;
    vec4 clip = uShadowMatrix * vec4(vWorld + normal*.025,1.);
    vec3 p = clip.xyz / clip.w * .5 + .5;
    if (p.z <= 0. || p.z >= 1.) return 1.;
    float edge = max(abs(p.x-.5),abs(p.y-.5))*2.;
    if (edge >= 1.) return 1.;
    float bias = .00010 + .00020*(1.-max(dot(normal,normalize(uSun)),0.));
    vec2 texel = 1./vec2(textureSize(uShadow,0));
    float occlusion = 0.;
    for (int y=-1; y<=1; ++y)
        for (int x=-1; x<=1; ++x)
            occlusion += step(texture(uShadow,p.xy+vec2(x,y)*texel).r+bias,p.z);
    return 1.-occlusion/9.*uShadowStrength*(1.-smoothstep(.8,1.,edge));
}
// Local lighting of the managed game: analytic street lamps on the road grid and a
// few moving lights (headlights, beacons). Short ranges, no shadows.
vec3 localLamp(vec3 pos, vec3 tint, vec3 axis, vec3 n, vec3 albedo, float range, float wet) {
    vec3 delta = pos - vWorld;
    float d2 = dot(delta, delta);
    if (d2 > range*range) return vec3(0.);
    vec3 l = delta * inversesqrt(max(d2, .001));
    float cone = dot(axis, axis) > .1 ? smoothstep(.78, .94, dot(-l, normalize(axis))) : 1.;
    float fall = pow(max(0., 1. - d2/(range*range)), 2.) / (1. + d2*.08);
    vec3 view = normalize(uEye - vWorld), halfway = normalize(l + view + vec3(.00001));
    float spec = pow(max(0., dot(n, halfway)), mix(32., 110., wet)) * (.08 + .30*wet);
    return ((albedo*.9 + vec3(.05)) * (max(0., dot(n, l)) + .04) + spec) * tint * fall * cone;
}
// Covered tunnels, as in the managed LocalLighting: x, z and half-length of the roofed
// part; floor height at the centre, floor slope along x and the phase of the lamp rows.
uniform vec3 uTunnels[8], uTunnelFloors[8];
uniform int uTunnelCount;
int tunnelAt(vec3 p) {
    for (int i = 0; i < 8; ++i) {
        if (i >= uTunnelCount) break;
        vec3 t = uTunnels[i];
        if (abs(p.x - t.x) <= t.z && abs(p.z - t.y) <= 5.95 &&
            p.y < uTunnelFloors[i].x + (p.x - t.x)*uTunnelFloors[i].y + 5.05) return i;
    }
    return -1;
}
vec3 localLights(vec3 n, vec3 albedo, bool underground) {
    vec3 sum = vec3(0.);
    float dark = underground ? 0. : 1. - uDay;
    float wet = underground ? 0. : uAir.w * max(n.y, 0.);
    if (dark > .001 && uGridNodes > 0) {
        // Each node owns the posts beside its northern and eastern streets; the lamp
        // map holds the height of every lamp head, or a large negative value for none.
        ivec2 cell = ivec2(floor((vWorld.xz - vec2(uGridLow)) / 36.));
        vec3 tint = vec3(3.8, 3.15, 2.15) * dark;
        for (int j = 0; j < 2; ++j)
            for (int i = 0; i < 2; ++i) {
                ivec2 node = cell + ivec2(i, j);
                if (node.x < 0 || node.y < 0 || node.x >= uGridNodes || node.y >= uGridNodes) continue;
                vec4 head = texelFetch(uLampMap, node, 0);
                vec2 o = vec2(uGridLow) + vec2(node)*36.;
                if (head.r > -500.) sum += localLamp(vec3(o.x - uLampSide + .5, head.r, o.y + 12.), tint, vec3(0.), n, albedo, 15., wet);
                if (head.g > -500.) sum += localLamp(vec3(o.x + uLampSide + .5, head.g, o.y + 12.), tint, vec3(0.), n, albedo, 15., wet);
                if (head.b > -500.) sum += localLamp(vec3(o.x + 12.5, head.b, o.y - uLampSide), tint, vec3(0.), n, albedo, 15., wet);
                if (head.a > -500.) sum += localLamp(vec3(o.x + 12.5, head.a, o.y + uLampSide), tint, vec3(0.), n, albedo, 15., wet);
            }
    }
    for (int i = 0; i < 12; ++i)
        if (dot(uLampColor[i], uLampColor[i]) > .001)
            sum += localLamp(uLampPosition[i], uLampColor[i], uLampDirection[i], n, albedo, 22., wet);
    return sum;
}
// Clothes of the managed Actor.frag on the 16-slot people palette: 0 top, 1 skin,
// 2 trousers, 5 and 6 hair. The loader keeps the height of every vertex in the standing
// pose in vUv.y (half metres) and adds one for the arms, so trouser legs and sleeves stay
// put in any pose; the managed shader cut them at a height of the moving body.
vec3 dress(vec3 c) {
    int slot = int(vUv.x*16.), choice = int(uOutfit.x-.5), flags = int(uOutfit.z+.5);
    float h = fract(vUv.y)*2., pick = float(choice);
    bool arm = vUv.y >= 1.;
    vec3 top = mix(vec3(.20,.30,.32), vec3(.55,.24,.15), fract(pick*.37));
    if (choice > 5) top = mix(vec3(.42,.39,.30), vec3(.16,.19,.25), fract(pick*.61));
    vec3 legs = mix(vec3(.10,.13,.18), vec3(.26,.23,.20), fract(pick*.43));
    if (choice == 9) { top = vec3(.85,.88,.83); legs = vec3(.12,.18,.20); }
    else if (choice == 10) { top = vec3(.19,.27,.34); legs = vec3(.10,.14,.20); }
    else if (choice == 11) top = legs = vec3(.16,.21,.17)*(1.+.20*sin(h*37.)*sin(vLocal.x*25.));
    else if (choice == 12) top = vec3(.56,.10,.29);
    else if (choice == 13) top = vec3(.30,.26,.37);
    else if (choice == 14 || choice == 15) top = vec3(.27,.26,.19);
    else if (choice == 16) { top = vec3(.70,.61,.45); legs = vec3(.13,.41,.50); }
    else if (choice == 17) top = legs = vec3(.055,.06,.07);
    else if (choice == 18) top = legs = vec3(.20,.25,.14)*(1.+.15*sin(h*31.)*sin(vLocal.x*27.));
    else if (choice == 19) { top = vec3(.085,.075,.07); legs = vec3(.15,.20,.31); }
    // A medic's coat reaches the knee and covers whatever is worn under it.
    bool coat = choice == 9 && h > .66;
    bool service = choice == 9 || choice == 10 || choice == 11 || choice == 17 || choice == 18;
    if (slot == 0) c = top;
    else if (slot == 2) c = coat ? top : legs;
    else if (slot == 1) {
        c = mix(vec3(.39,.24,.15), vec3(.85,.65,.49), uOutfit.y/5.);
        if (arm) { if ((flags & 2) != 0 && h > .78) c = top; }
        else if (h < .94 && ((flags & 1) != 0 || service)) c = coat ? top : legs;
    }
    else if ((slot == 5 || slot == 6) && uHair.x >= 0.) c = uHair;
    // Service boots instead of the summer shoes of the skin.
    if (service && h < .16) c = vec3(.055,.06,.055);
    return c;
}
float rectangle(vec2 p, vec2 centre, vec2 halfSize) {
    vec2 edge = abs(p - centre) - halfSize;
    vec2 aa = max(fwidth(p), vec2(.001));
    vec2 coverage = 1.0 - smoothstep(-aa, aa, edge);
    return coverage.x * coverage.y;
}
// One opaque quad per window. The ray meets the walls of a virtual room;
// curtains remain on the glass plane. No interior geometry or alpha sorting.
vec3 apartment(vec3 normal, vec3 view) {
    float seed = vDetail.x;
    int curtain = int(vDetail.y + .5), room = int(vDetail.z + .5);
    vec3 right = normalize(cross(vec3(0,1,0), normal));
    vec3 ray = -vec3(dot(view, right), view.y, max(.08, dot(view, normal)));
    vec3 origin = vec3((vUv.x - .5) * 1.28, (.5 - vUv.y) * 1.08, 0);
    vec3 bounds = mix(vec3(-1.5,-1.3,-2.6), vec3(1.5,1.35,0), step(vec3(0), ray));
    vec3 safeRay = mix(vec3(-1), vec3(1), step(vec3(0), ray)) * max(abs(ray),vec3(.0001));
    vec3 times = (bounds - origin) / safeRay;
    float distanceToWall = min(times.x, min(times.y, times.z));
    vec3 hit = origin + ray * max(0, distanceToWall);
    vec3 wallpaper = mix(vec3(.29,.25,.18), vec3(.34,.39,.32), hash(seed+17));
    float stripeWidth = fwidth(hit.x * 18.0);
    wallpaper *= 1.0 + .08 * sin(hit.x*18.0) * (1.0-smoothstep(1.0,PI,stripeWidth));
    vec3 interior = wallpaper;
    if (times.y < min(times.x,times.z))
        interior = ray.y < 0 ? vec3(.16,.105,.055) : vec3(.48,.44,.35);
    else if (times.x < times.z) interior *= .68;
    else {
        vec2 wall = vec2(hit.x, hit.y);
        if (room == 0) {
            float rug = rectangle(wall,vec2(-.35,.25),vec2(.88,.52));
            vec2 uv = (wall-vec2(-.35,.25))/vec2(1.76,-1.04)+.5;
            interior = mix(interior, pow(texture(uRug,clamp(uv,0.0,1.0)).rgb,vec3(2.2)),rug);
            float stand = rectangle(wall,vec2(.5,-.95),vec2(.62,.35));
            float tv = rectangle(wall,vec2(.5,-.39),vec2(.35,.21));
            interior = mix(interior,vec3(.11,.065,.027),stand);
            interior = mix(interior,vec3(.025,.065,.075),tv);
        } else if (room == 1) {
            float cupboard = rectangle(wall,vec2(.75,-.2),vec2(.52,1.0));
            interior = mix(interior,vec3(.13,.075,.04),cupboard);
            interior += vec3(.09,.08,.06)*rectangle(wall,vec2(.75,-.2),vec2(.015,.38));
        } else if (room == 2) {
            float fridge = rectangle(wall,vec2(-.65,-.36),vec2(.42,.88));
            interior = mix(interior,vec3(.47,.49,.42),fridge);
            interior *= 1.0-.5*rectangle(wall,vec2(-.65,-.08),vec2(.41,.018));
        }
    }
    // Independent, slow occupancy changes: 200--850 seconds, with a smooth transition.
    float period = mix(200.0,850.0,hash(seed+31));
    float clock = uTime / period + hash(seed+11);
    float slot = floor(clock);
    float occupied = mix(step(.36,hash(seed+slot)),step(.36,hash(seed+slot+1)),smoothstep(.97,1.0,fract(clock)));
    float evening = smoothstep(17.5,20.0,uHour)*(1.0-.5*smoothstep(23.0,24.0,uHour));
    float early = (1.0-smoothstep(0.0,2.0,uHour))*.5;
    float morning = smoothstep(5.0,6.5,uHour)*(1.0-smoothstep(7.0,8.0,uHour))*.3;
    float lamp = max(evening,max(early,morning))*occupied;
    vec3 roomColor = interior*(.12+.8*uDay+lamp*2.8)*mix(vec3(.75,.88,1),vec3(1,.78,.45),lamp);
    float cloth = 0.0;
    if (curtain >= 2 && curtain <= 4) {
        float opening = curtain == 2 ? .34 : curtain == 3 ? .24 : .13;
        cloth = smoothstep(opening-.01,opening+.01,abs(vUv.x-.5));
    } else if (curtain == 5) cloth = 1.0-smoothstep(.55,.57,vUv.x);
    else if (curtain == 6) cloth = 1.0;
    else if (curtain == 7 || curtain == 8) cloth = .38;
    else if (curtain == 9) cloth = .85;
    float phase = (curtain == 9 ? vUv.y*70.0 : vUv.x*48.0);
    float folds = .8+.2*sin(phase)*(1.0-smoothstep(1.0,PI,fwidth(phase)));
    vec3 fabric = mix(vec3(.34,.26,.18),vec3(.19,.26,.22),hash(seed+7));
    if (curtain == 7 || curtain == 8) fabric = vec3(.48,.47,.40);
    roomColor = mix(roomColor,fabric*folds*(.12+.7*uDay+1.4*lamp),cloth);
    // The glass mirrors the street as in the managed game.
    float fresnel = .15+.70*pow(1.0-abs(dot(normal,view)),4.0);
    roomColor = mix(roomColor,cityReflection(vWorld,reflect(-view,normal),.16,false),fresnel);
    float glass = rectangle(vUv,vec2(.5),vec2(.452,.452));
    glass *= 1.0-rectangle(vUv,vec2(.5),vec2(.018,.5));
    glass *= 1.0-rectangle(vUv,vec2(.75,.32),vec2(.25,.015));
    vec3 frame = vec3(.31,.32,.27)*(.2+.7*uDay);
    return mix(frame,roomColor,glass);
}
void main() {
    float eyeDistance = length(uEye-vWorld);
    if (uTreeLod >= 0) {
        float noise = fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(.06711056,.00583715))));
        float farWeight = smoothstep(85.,115.,eyeDistance);
        if ((uTreeLod == 0 && noise < farWeight) || (uTreeLod == 1 && noise >= farWeight)) discard;
        if (noise < smoothstep(560.,620.,eyeDistance)) discard;
    }
    if (uDetail != 0) {
        float coverage = 1.0-smoothstep(uDetailStart,uDetailEnd,eyeDistance);
        float noise = fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(.06711056,.00583715))));
        if (coverage <= noise) discard;
    }
    vec4 tex = texture(uColor, uSurface == 12 ? vec2(vWorld.x,-vWorld.z)*.5 : vUv);
    // In a baked vehicle atlas alpha describes glass and metallic surfaces.
    // Preserve the glass silhouette even when the atlas encodes zero there.
    float bakedFinish = tex.a;
    if (uSurface == 14) tex.a = 1.;
    int vehicleMaterial = -1;
    if (uSurface == 10) {
        // The single-row articulated palette stores a material ID in alpha, including 1 for body paint.
        int width = textureSize(uColor, 0).x;
        int slot = clamp(int(floor(vUv.x * float(width))), 0, width-1);
        tex = texelFetch(uColor, ivec2(slot, 0), 0);
        vehicleMaterial = int(round(tex.a * 255.));
        tex.a = 1.;
    }
    if (tex.a < .05) discard;
    if (uOutfit.x > .5) tex.rgb = dress(tex.rgb);
    if (uSurface == 9) {
        if (foliage(vUv,vDetail.w) < .45) discard;
    }
    if (uSurface == 11) {
        // Pothole photograph: the broken middle stays, the square border dissolves into the road.
        vec2 q = vUv*2.-1.;
        float rim = length(q)+.06*sin(atan(q.y,q.x)*5.+vWorld.x*2.3+vWorld.z*1.7);
        float noise = fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(.06711056,.00583715))));
        if (1.0-smoothstep(.80,.98,rim) <= noise) discard;
        tex.rgb = mix(tex.rgb*vec3(.72,.77,.80),vec3(.234,.252,.261),smoothstep(.50,.90,rim));
    }
    // Alpha-tested foliage casts its silhouette; skip the complete material shader.
    if (uDepthPass != 0) { fragColor = vec4(0.); return; }
    vec3 n = normalize(vNormal);
    if (!gl_FrontFacing) n = -n;
    float phaseWidth = max(fwidth(vWorld.x*51.7),fwidth(vWorld.z*43.1));
    float fade = 1.0-smoothstep(1.0,PI,phaseWidth);
    float grain = sin(vWorld.x*51.7)*sin(vWorld.z*43.1);
    if (uSurface == 8) {
        float bark = patches(vec3(vWorld.xz*8.,vWorld.y*2.7));
        if (vDetail.w < .5) {
            float flecks = patches(vec3(vWorld.xz*12.,vWorld.y*39.+bark));
            float mark = smoothstep(.64,.83,flecks)*(.35+.65*bark)*fade;
            tex.rgb = mix(vec3(.69,.70,.63)*(.88+.19*bark),vec3(.19,.18,.15),mark);
        } else {
            float cracks = sin((vWorld.x+vWorld.z)*54.+sin(vWorld.y*6.));
            tex.rgb = vec3(.24,.19,.13)*(.85+.15*cracks);
        }
    }
    if (uSurface == 9) {
        vec3 leaf = vDetail.w < .5 ? vec3(.30,.40,.13) : vDetail.w < 1.5 ? vec3(.25,.35,.12) : vec3(.16,.28,.20);
        tex.rgb = leaf*mix(.82,1.18,vDetail.z);
        float vein = exp(-abs(vUv.x-.5)*90.);
        tex.rgb *= 1.+.14*vein;
    }
    if (uSurface == 1) {
        float growth = patches(vec3(vWorld.xz*.16,.37));
        tex.rgb *= mix(vec3(.89,.93,.79),vec3(1.09,1.12,.97),growth);
    }
    if (uSurface == 1 && vDetail.x > .5) {
        vec2 block = (vWorld.xz-vec2(uGridLow))/36.;
        vec2 within = clamp((fract(block)*36.-6.)/24.,vec2(.011),vec2(.989));
        float pathMask = texture(uYardMask,(floor(block)+within)/float(uGridNodes-1)).r;
        float speck = patches(vec3(vWorld.xz*8.,.61));
        float clods = patches(vec3(vWorld.xz*1.8,.14));
        float wear = smoothstep(.08,.63,pathMask * (.70+.38*clods+.24*speck));
        float luma = dot(tex.rgb,vec3(.30,.53,.17));
        // Preserve the photographed ground's granular texture across the worn centre.
        float grit = dot(texture(uColor,vUv*1.8).rgb,vec3(.27,.56,.17));
        vec3 earth = mix(vec3(.37,.30,.215),vec3(.43,.355,.25),clods);
        tex.rgb = mix(tex.rgb,earth*(.58+.94*luma+.53*grit+.15*speck)/uTint,wear);
    }
    if (vDetail.w == -1. && abs(vNormal.y) < .5 && vLocal.y < 1.6) {
        float mottling = patches(vec3(vWorld.xz*1.6,vWorld.y*.7));
        float foot = 1.-smoothstep(.04,.62+.72*mottling,vLocal.y);
        tex.rgb *= mix(vec3(1.),vec3(.64,.67,.63),foot*(.72+.28*mottling));
    }
    if (uSurface == 12) {
        // Pick up the soil/leaf colour at the root. Sunlit tips are newer growth.
        vec3 leaf = mix(vec3(.42,.48,.20),vec3(.66,.66,.31),vDetail.z);
        tex.rgb = mix(tex.rgb,leaf,.57) * mix(.85,1.22,vUv.y);
        n = vec3(0,1,0);
    }
    if (uSurface == 2) tex.rgb *= .90+.08*grain*fade;
    if (uSurface == 3) {
        vec2 grid = abs(fract(vUv*1.25)-.5), width = max(fwidth(vUv*1.25),vec2(.008));
        float seam = max(smoothstep(.48-width.x,.5,grid.x),smoothstep(.48-width.y,.5,grid.y));
        tex.rgb *= 1.0-.2*seam;
    }
    if (uSurface == 5) {
        float dry = smoothstep(-2.77,-1.65,vWorld.y);
        float ripples = sin(vWorld.z*6.8+vWorld.x*.9+sin(vWorld.x*.7)*.9);
        tex.rgb *= mix(.65,1.,dry)*(.95+.05*grain*fade+.035*ripples*(1.-dry));
    }
    if (uSurface == 7) {
        float phase = (abs(n.x)>.5 ? vWorld.z : vWorld.x)*25.0;
        tex.rgb *= .9+.1*sin(phase)*(1.0-smoothstep(1.0,PI,fwidth(phase)));
    }
    if (uSurface == 4) {
        vec2 p = vWorld.xz;
        float swell = dot(p, vec2(.33, .19)) + .72*sin(p.y*.08) + uTime*.63;
        float chop = dot(p, vec2(-.57, .93)) + .8*sin(p.x*.14) - uTime*.91;
        float ripple = dot(p, vec2(1.45, .39)) + .54*sin(p.y*.32) + uTime*1.32;
        vec3 phases = vec3(swell, chop, ripple);
        vec3 waves = cos(phases) * (1.-smoothstep(vec3(.55), vec3(PI), fwidth(phases)));
        vec2 slope = waves.x*vec2(.024,.014) + waves.y*vec2(-.011,.019) + waves.z*vec2(.016,.004);
        // As in the managed game the swell flattens with distance: far water is a calm mirror.
        slope /= 1.+eyeDistance*.012;
        n = normalize(vec3(slope.x, 1., slope.y));
    }
    if (uHasNormal != 0) {
        // Cotangent frame handles mirrored UVs and both orientations of a facade.
        vec3 q1 = dFdx(vWorld), q2 = dFdy(vWorld);
        vec2 st1 = dFdx(vUv), st2 = dFdy(vUv);
        vec3 p2 = cross(q2,n), p1 = cross(n,q1);
        vec3 t = p2*st1.x+p1*st2.x, b = p2*st1.y+p1*st2.y;
        float scale = inversesqrt(max(max(dot(t,t),dot(b,b)),1e-12));
        vec3 map = texture(uNormal,vUv).xyz*2.-1.;
        if (uSurface == 1 || uSurface == 2) map.xy *= uSurface == 1 ? .55 : .42;
        n = normalize(mat3(t*scale,b*scale,n)*map);
    }
    vec3 baseColor = tex.rgb*uTint;
    // How much of the pixel is body paint: all of a dedicated paint material, the
    // coloured part of an atlas shared with the trim.
    float paint = 0.;
    if (vehicleMaterial == 1 || uPaintMask > 0) {
        const vec3 colors[12] = vec3[12](
            vec3(.19,.49,.55),vec3(.66,.22,.16),vec3(.40,.48,.25),vec3(.83,.72,.47),
            vec3(.28,.38,.66),vec3(.72,.71,.65),vec3(.48,.18,.29),vec3(.22,.40,.32),
            vec3(.78,.47,.22),vec3(.34,.32,.48),vec3(.48,.59,.66),vec3(.28,.26,.24));
        float lightness = dot(baseColor,vec3(.2126,.7152,.0722));
        float chroma = max(max(baseColor.r,baseColor.g),baseColor.b)-min(min(baseColor.r,baseColor.g),baseColor.b);
        paint = uPaintMask == 2 ? smoothstep(.025,.14,chroma)*smoothstep(.055,.19,lightness) : 1.;
        if (uSurface == 14) paint *= smoothstep(.57,.79,bakedFinish);
        if (uPaintIndex > 0) baseColor = mix(baseColor,colors[uPaintIndex-1]*(.50+.75*lightness),paint);
    }
    int tunnel = uTunnelCount > 0 ? tunnelAt(vWorld) : -1;
    // Wet asphalt of the managed game: the street does not wet evenly. Patches of it
    // darken, turn smooth and mirror the city for as long as the street stays wet.
    // No rain falls under the roof of a tunnel.
    float damp = 0.;
    if (uSurface == 2 && uAir.w > .002 && tunnel < 0) {
        damp = uAir.w*smoothstep(.2,.7,patches(vWorld*.7));
        baseColor *= 1.-damp*.3;
    }
    vec3 albedo = pow(max(baseColor,vec3(.001)),vec3(2.2));
    vec3 orm = uHasOrm != 0 ? texture(uOrm,vUv).rgb : vec3(1,uRoughness,uMetallic);
    float ao = clamp(orm.r,.15,1), metal = clamp(orm.b,0,1);
    float rainExposure = tunnel < 0 ? uAir.w : 0.;
    float rough = mix(clamp(orm.g,.08,1),.24,rainExposure*max(n.y,0));
    rough = mix(rough,.14,damp);
    if (uSurface == 1 || uSurface == 12) rough = mix(.96,.77,rainExposure);
    if (uSurface == 14) {
        metal = bakedFinish < .1 ? 0. : clamp((1.-bakedFinish)*2.55,0.,1.);
        rough = bakedFinish < .1 ? .18 : mix(.7,.38,metal);
    }
    if (vehicleMaterial == 22) rough = .12;
    else if (vehicleMaterial == 34) { rough = .25; metal = .82; }
    else if (vehicleMaterial == 1) rough = mix(.43, .25, rainExposure);
    vec3 v = normalize(uEye-vWorld), l = normalize(uSun), h = normalize(v+l);
    float nl = max(dot(n,l),0), nv = max(dot(n,v),.001), nh = max(dot(n,h),0);
    float a = rough*rough, a2 = a*a, denom = nh*nh*(a2-1.0)+1.0;
    float distribution = a2 / max(PI*denom*denom,.00001);
    float k = (rough+1.0)*(rough+1.0)/8.0;
    float geometry = nv/(nv*(1.0-k)+k)*nl/(nl*(1.0-k)+k);
    vec3 f0 = mix(vec3(.04),albedo,metal);
    vec3 fresnel = f0+(1.0-f0)*pow(1.0-max(dot(v,h),0),5.0);
    vec3 specular = distribution*geometry*fresnel/max(4.0*nv*nl,.001);
    vec3 diffuse = (1.0-fresnel)*(1.0-metal)*albedo/PI;
    float golden = exp(-pow((uSun.y-.15)*4.0,2.0))*uDay;
    vec3 light = mix(vec3(1,.97,.91),vec3(1,.64,.34),golden*.55);
    vec3 sky = pow(uFog,vec3(2.2));
    float sunVisibility = sunlight(n);
    // Cool skylight at night sets off the warm street lamps.
    vec3 skylight = mix(vec3(.60, .76, 1.10), vec3(1.), uDay);
    vec3 color = albedo*skylight*(.12+.28*uDay)*ao+(diffuse+specular)*light*nl*(.15+2.8*uDay)*sunVisibility;
    if (uSurface == 9) color += albedo*light*.42*uDay*max(dot(-n,l),0.)*sunVisibility;
    color += sky*f0*(.18+.45*(1.0-rough))*ao;
    if (tunnel >= 0) {
        // Under the roof: no sky and no sun, a dim fill and a ceiling lamp row every nine metres.
        vec3 t = uTunnels[tunnel], f = uTunnelFloors[tunnel];
        color = albedo*vec3(.13,.135,.12)*ao;
        float first = floor((vWorld.x - f.z)/9.)*9. + f.z;
        for (int k = 0; k < 2; ++k) {
            float x = first + float(k)*9.;
            color += localLamp(vec3(x, f.x + (x - t.x)*f.y + 4.7, t.y), vec3(3.2,2.9,2.1), vec3(0.), n, albedo, 13., 0.);
        }
    }
    if (uLocalLights != 0) color += localLights(n, albedo, tunnel >= 0);
    if (vehicleMaterial < 0) color += albedo*uEmissive;
    // Reflections of the managed game: glass, chrome and the clear coat of vehicles.
    float graze = pow(1.-abs(dot(n,v)),5.), coat = 0.;
    if (uSurface == 14 && bakedFinish < .1) coat = .45+.45*graze;
    else if (vehicleMaterial == 22) coat = .38+.50*graze;
    else if (vehicleMaterial == 34) coat = .55;
    else if (vehicleMaterial == 1) coat = .055+.28*graze;
    else if (uCoat == 2) coat = .45+.45*graze;
    else if (uCoat == 1) coat = metal*.4+paint*(.04+.2*graze);
    if (coat > 0. && uCoat >= 0) color = mix(color,cityReflection(vWorld,reflect(-v,n),rough,tunnel >= 0),min(coat,.9));
    if (damp > 0.) {
        // A thin water film reflects most strongly at grazing angles.
        float film = .021 + .46*graze;
        color = mix(color,cityReflection(vWorld,reflect(-v,n),.12,false),damp*film);
    }
    if (vehicleMaterial == 8) {
        bool blue = tex.b > tex.r * 1.4;
        if (blue && uEmissive > 0.) {
            float pulse = smoothstep(.52, .85, sin(uTime*12.));
            color += vec3(.015, .18, 1.) * pulse * 2.8;
        } else color += albedo * (1.-uDay) * .75;
    }
    if (uSurface == 6) color = apartment(n,v);
    if (uSurface == 4) color = mix(color,cityReflection(vWorld,reflect(-v,n),.12,tunnel >= 0),.12+.75*pow(1.0-max(dot(n,v),0),4.0));
    // The haze of the managed game takes a distant surface to the colour of the horizon.
    float air = airTransmission(uEye,vWorld);
    if (uProbePass != 0) {
        fragColor = vec4(mix(fromDisplay(uFog),color,air),1.);
        return;
    }
    fragColor = vec4(mix(uFog,toDisplay(color),air),tex.a*uAlpha);
}
