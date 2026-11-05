#version 330 core
in vec3 vWorld, vNormal;
in vec2 vUv;
flat in vec4 vDetail;
out vec4 fragColor;
uniform sampler2D uColor, uNormal, uOrm, uRug;
uniform int uHasNormal, uHasOrm, uSkin, uSurface, uDetail;
uniform vec3 uEye, uSun, uFog, uTint;
uniform float uDay, uRain, uEmissive, uAlpha, uTime, uHour;
uniform float uRoughness, uMetallic, uDetailStart, uDetailEnd;
uniform sampler2D uShadow;
uniform mat4 uShadowMatrix;
uniform int uDepthPass, uTreeLod;
uniform float uShadowStrength;
const float PI = 3.14159265;
float hash(float n) { return fract(sin(n * 12.9898 + 1.312) * 43758.5453); }
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
    float fresnel = .09+.55*pow(1.0-max(dot(normal,view),0),5.0);
    vec3 sky = pow(uFog,vec3(2.2))*(.3+.7*uDay);
    roomColor = mix(roomColor,sky,fresnel);
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
    vec4 tex = texture(uColor,vUv);
    if (tex.a < .05) discard;
    if (uSurface == 9) {
        if (foliage(vUv,vDetail.w) < .45) discard;
    }
    // Alpha-tested foliage casts its silhouette; skip the complete material shader.
    if (uDepthPass != 0) { fragColor = vec4(0.); return; }
    vec3 n = normalize(vNormal);
    if (!gl_FrontFacing) n = -n;
    float phaseWidth = max(fwidth(vWorld.x*51.7),fwidth(vWorld.z*43.1));
    float fade = 1.0-smoothstep(1.0,PI,phaseWidth);
    float grain = sin(vWorld.x*51.7)*sin(vWorld.z*43.1);
    if (uSurface == 8) {
        float stripes = sin(vWorld.y*28.+sin(vWorld.x*43.+vWorld.z*37.)*2.);
        if (vDetail.w < .5) {
            float mark = smoothstep(.68,.9,stripes)*smoothstep(.2,.65,sin(vWorld.x*13.+vWorld.z*16.));
            tex.rgb = mix(vec3(.69,.70,.62),vec3(.16,.16,.14),mark);
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
    if (uSurface == 1) tex.rgb *= .90+.10*sin(vWorld.x*.12)*sin(vWorld.z*.09)+.04*grain*fade;
    if (uSurface == 2) tex.rgb *= .90+.08*grain*fade;
    if (uSurface == 3) {
        vec2 grid = abs(fract(vWorld.xz*.5)-.5), width = max(fwidth(vWorld.xz*.5),vec2(.008));
        float seam = max(smoothstep(.48-width.x,.5,grid.x),smoothstep(.48-width.y,.5,grid.y));
        tex.rgb *= 1.0-.2*seam;
    }
    if (uSurface == 5) tex.rgb *= .93+.07*grain*fade;
    if (uSurface == 7) {
        float phase = (abs(n.x)>.5 ? vWorld.z : vWorld.x)*25.0;
        tex.rgb *= .9+.1*sin(phase)*(1.0-smoothstep(1.0,PI,fwidth(phase)));
    }
    if (uSurface == 4) n = normalize(vec3(.035*cos(vWorld.x*1.7+vWorld.z*.7+uTime*.8),1,.045*sin(vWorld.z*1.1-vWorld.x*.3+uTime*.65)));
    if (uHasNormal != 0) {
        // Cotangent frame handles mirrored UVs and both orientations of a facade.
        vec3 q1 = dFdx(vWorld), q2 = dFdy(vWorld);
        vec2 st1 = dFdx(vUv), st2 = dFdy(vUv);
        vec3 p2 = cross(q2,n), p1 = cross(n,q1);
        vec3 t = p2*st1.x+p1*st2.x, b = p2*st1.y+p1*st2.y;
        float scale = inversesqrt(max(max(dot(t,t),dot(b,b)),1e-12));
        n = normalize(mat3(t*scale,b*scale,n)*(texture(uNormal,vUv).xyz*2.0-1.0));
    }
    vec3 albedo = pow(max(tex.rgb*uTint,vec3(.001)),vec3(2.2));
    vec3 orm = uHasOrm != 0 ? texture(uOrm,vUv).rgb : vec3(1,uRoughness,uMetallic);
    float ao = clamp(orm.r,.15,1), metal = clamp(orm.b,0,1);
    float rough = mix(clamp(orm.g,.08,1),.24,uRain*max(n.y,0));
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
    vec3 color = albedo*(.12+.28*uDay)*ao+(diffuse+specular)*light*nl*(.15+2.8*uDay)*sunVisibility;
    if (uSurface == 9) color += albedo*light*.42*uDay*max(dot(-n,l),0.)*sunVisibility;
    color += sky*f0*(.18+.45*(1.0-rough))*ao + albedo*uEmissive;
    if (uSurface == 6) color = apartment(n,v);
    if (uSurface == 4) color = mix(color,sky,.04+.70*pow(1.0-max(dot(n,v),0),5.0));
    float fog = 1.0-exp(-eyeDistance*(.00052+.0028*uRain));
    color = mix(color,sky,fog);
    color = color/(color+vec3(1));
    fragColor = vec4(pow(color,vec3(1.0/2.2)),tex.a*uAlpha);
}
