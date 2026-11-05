#version 330 core
#extension GL_GOOGLE_include_directive : require
in vec3 vWorldPos, vNormal, vColor, vLocal;
flat in int vMaterial;
uniform float uTime, uEnvironmentFill;
uniform sampler2D uFacade, uFacadeBump, uAtlas, uAtlasHeight, uAtlasFinish;
uniform float uWetness;
uniform float uLandEdge;
uniform int uHasFacade;
uniform int uEmissionOnly, uDetailedFinish, uOutdoor, uWorkshopFinish;
uniform float uAtmosphereDensity;
#include "Renderer/Shader.PbrLightUniforms.glsl"
#include "Renderer/Shader.PbrFunctions.glsl"
#include "Renderer/OutdoorAtmosphere.Glsl.glsl"
float hashSurface(vec3 p)
{
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}
float noiseSurface(vec3 p)
{
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3. - 2. * f);
    return mix(mix(mix(hashSurface(i), hashSurface(i + vec3(1, 0, 0)), f.x),
                   mix(hashSurface(i + vec3(0, 1, 0)), hashSurface(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hashSurface(i + vec3(0, 0, 1)), hashSurface(i + vec3(1, 0, 1)), f.x),
                   mix(hashSurface(i + vec3(0, 1, 1)), hashSurface(i + vec3(1, 1, 1)), f.x), f.y),
               f.z);
}
float fineStripe(float phase)
{
    float fw = fwidth(phase);
    return sin(phase) * exp(-fw * fw * .3);
}
// Screen-space surface-gradient bump: follows arbitrary rotated primitives
// without a tangent attribute; broad patterns affect colour, not the normal.
vec3 surfaceBump(vec3 n, float height, float strength)
{
    vec3 sx = dFdx(vWorldPos), sy = dFdy(vWorldPos);
    vec3 r1 = cross(sy, n), r2 = cross(n, sx);
    float det = dot(sx, r1);
    if (abs(det) < 1e-10)
        return n;
    vec3 grad = sign(det) * (dFdx(height) * r1 + dFdy(height) * r2);
    return normalize(abs(det) * n - strength * grad);
}
#include "Atmosphere.glsl"
#include "LocalLighting.glsl"
#include "Reflections.glsl"
#include "Renderer/WorkshopSurface.WorkshopLightingGlsl.glsl"
vec2 atlasUV(int tile, vec2 uv)
{
    vec2 at = (vec2(tile % 8, tile / 8) * 512. + vec2(4.5) + clamp(uv, vec2(0), vec2(1)) * 503.) / 4096.;
    return vec2(at.x, 1. - at.y);
}
vec3 atlasColor(int tile, vec2 uv)
{
    return texture(uAtlas, atlasUV(tile, uv)).rgb;
}
vec4 facadeSample(sampler2D map, int tile, vec2 raw)
{
    vec2 dx = dFdx(raw) * vec2(503., -503.) / 4096., dy = dFdy(raw) * vec2(503., -503.) / 4096.;
    return textureGrad(map, atlasUV(tile, fract(raw)), dx, dy);
}
vec3 facadeNormal(vec3 n, vec2 raw, vec2 xy)
{
    vec3 dp1 = dFdx(vWorldPos), dp2 = dFdy(vWorldPos);
    vec2 du1 = dFdx(raw), du2 = dFdy(raw);
    vec3 t = cross(dp2, n) * du1.x + cross(n, dp1) * du2.x;
    vec3 b = cross(dp2, n) * du1.y + cross(n, dp1) * du2.y;
    float scale = inversesqrt(max(max(dot(t, t), dot(b, b)), 1e-12));
    // UVs are world projected upward; the image-coordinate V inversion is deliberate.
    xy *= .72;
    return normalize(t * scale * xy.x - b * scale * xy.y + n * sqrt(max(.01, 1. - dot(xy, xy))));
}
float boxRay(vec3 o, vec3 d, vec3 lo, vec3 hi)
{
    vec3 a = (lo - o) / d, b = (hi - o) / d;
    vec3 near = min(a, b), far = max(a, b);
    float t = max(near.x, max(near.y, near.z)), end = min(far.x, min(far.y, far.z));
    return end > max(0., t) ? max(0., t) : 1e6;
}
void cityMaterial()
{
    vec3 c = max(vColor, vec3(0)), n = normalize(vNormal), p = vLocal;
    if (vMaterial == 360 || vMaterial == 361)
    {
        if (uEmissionOnly == 2)
        {
            FragColor = vec4(0, 0, 0, 1);
            return;
        }
        vec2 q = vWorldPos.xz;
        float atten = 1. / (1. + length(uViewPos - vWorldPos) * .012);
        vec2 slope =
            vec2(cos(dot(q, vec2(.17, .11)) + uTime * .55), sin(dot(q, vec2(-.13, .21)) - uTime * .42)) *
            .04;
        slope +=
            vec2(sin(dot(q, vec2(.31, -.16)) + uTime * .73), cos(dot(q, vec2(.22, .29)) + uTime * .32)) *
            .023;
        slope *= atten * (vMaterial == 361 ? .2 : 1.);
        if (vMaterial == 361)
        {
            vec2 cell = floor(q * 1.7), local = fract(q * 1.7) - .5;
            float phase = fract(uTime * 1.9 + hashSurface(vec3(cell, 1.))), radius = length(local);
            float ring =
                sin((radius - phase * .8) * 65.) * exp(-abs(radius - phase * .8) * 30.) * (1. - phase);
            float filtered = exp(-dot(fwidth(q * 20.), fwidth(q * 20.)));
            slope += local / max(.04, radius) * ring * .018 * uWetness * filtered;
        }
        n = normalize(vec3(slope.x, 1., slope.y));
        vec3 v = normalize(uViewPos - vWorldPos);
        vec3 reflected = reflect(-v, n);
        vec3 sky = mix(vec3(.39, .51, .57), vec3(.16, .32, .49), clamp(reflected.y * .8 + .2, 0., 1.)) *
                   (.06 + .85 * uDaylight);
        float fres = .12 + .75 * pow(1. - max(0., dot(n, v)), 4.);
        vec3 base = vMaterial == 360 ? vec3(.025, .16, .19) : vec3(.065, .075, .07);
        vec3 lit = outdoorLightResult(shadePbr(vWorldPos, n, base, .13, .05, 1.).rgb, base, n);
        FragColor = vec4(mix(lit, cityReflection(reflected, .12, n), fres), 1);
        return;
    }
    if (vMaterial < 100 && vMaterial != 8 && vMaterial != 22 && vMaterial != 31)
    {
        vec2 uv = fract((abs(n.y) > .5   ? vWorldPos.xz
                         : abs(n.x) > .5 ? vWorldPos.zy
                                         : vWorldPos.xy) *
                        1.3);
        float grain = dot(atlasColor(30, uv), vec3(.333));
        c *= .85 + grain * .45;
        n = surfaceBump(n, grain * .006, .35);
    }
    if (vMaterial == 350 || vMaterial == 351)
    {
        float flicker = vMaterial == 351 ? step(.20, sin(uTime * 13.) * sin(uTime * 2.7)) : 1.;
        c *= mix(.65, 3.2 * flicker, 1. - uDaylight);
        FragColor = vec4(uEmissionOnly == 2 ? c * (1. - uDaylight) * flicker : c, 1);
        return;
    }
    if (vMaterial >= 100 && vMaterial < 300)
    {
        bool facade = vMaterial >= 200;
        int tile = vMaterial - (facade ? 200 : 100);
        vec2 raw =
            (abs(n.x) > .5 ? vWorldPos.zy : vWorldPos.xy) * (tile == 15 || tile == 21 ? .42 : .22);
        vec2 uv = facade ? fract(raw) : p.xy;
        vec3 textureColour = facade ? facadeSample(uAtlas, tile, raw).rgb : atlasColor(tile, uv);
        // Keep PBR detail, but let the district's authored material palette control the facade.
        if (facade)
        {
            float luma = dot(textureColour, vec3(.2126, .7152, .0722));
            textureColour = mix(vec3(luma), textureColour, .62);
        }
        c = textureColour * (facade ? clamp(c * 1.05, vec3(.70), vec3(1.05)) : vec3(1));
        if (tile >= 25 && tile <= 29)
        {
            float edge = length((uv - .5) * 2.);
            float irregular = tile <= 27 ? noiseSurface(vec3(uv * 12., float(tile))) * .10 : 0.;
            if (edge > .94 - irregular)
                discard;
            float roadGrain = noiseSurface(vWorldPos * 70.), roadPatch = noiseSurface(vWorldPos * .7);
            vec3 road = vec3(.19, .19, .175) * (.78 + roadGrain * .17 + roadPatch * .20);
            c = mix(c, road, smoothstep(.52, .91 - irregular, edge));
        }
        float h = facade ? facadeSample(uAtlasHeight, tile, raw).r
                         : texture(uAtlasHeight, atlasUV(tile, uv)).r;
        vec4 finish = facadeSample(uAtlasFinish, tile, raw);
        if (facade && uDetailedFinish == 1)
            n = facadeNormal(n, raw, finish.rg * 2. - 1.);
        else
            n = surfaceBump(n, h * .023, .5);
        if (facade)
        {
            float stain = noiseSurface(vec3(vWorldPos.x * .16, vWorldPos.y * .025, vWorldPos.z * .16));
            c *= .89 + .11 * stain;
            if (tile == 13 || tile == 20)
            {
                vec2 panel = abs(fract(raw / vec2(.66, .55)) - .5);
                vec2 width = max(fwidth(raw / vec2(.66, .55)), vec2(.002));
                float joint = 1. - smoothstep(.006, .006 + max(width.x, width.y), min(panel.x, panel.y));
                c *= 1. - joint * .15;
            }
        }
        if (uEmissionOnly == 2)
        {
            FragColor = vec4(0, 0, 0, 1);
            return;
        }
        float rough = facade ? clamp(finish.b, .28, .98) : .83;
        float metal = facade && (tile == 18 || tile == 19) ? (.45 * (1. - finish.b)) : .02;
        if (facade)
            c *= mix(.68, 1., finish.a);
        FragColor = shadePbr(vWorldPos, n, c, rough, metal, .4);
        FragColor.rgb = outdoorLightResult(FragColor.rgb, c, n);
        return;
    }
    if (vMaterial == 301)
    {
        float grain = noiseSurface(vWorldPos * 110.);
        float dune = sin(vWorldPos.x * 2.3 + sin(vWorldPos.z * .8) * 1.7);
        float patch = noiseSurface(vWorldPos * .16);
        c = mix(vec3(.47, .38, .23), vec3(.76, .66, .44), patch * .5 + grain * .24 + .25);
        float wet = clamp((max(abs(vWorldPos.x), abs(vWorldPos.z)) - (uLandEdge - 8.)) / 8., 0., 1.);
        c *= 1. - wet * .28;
        n = surfaceBump(n, dune * .013 + grain * .003, .7);
        if (uEmissionOnly == 2)
        {
            FragColor = vec4(0, 0, 0, 1);
            return;
        }
        FragColor =
            vec4(outdoorLightResult(shadePbr(vWorldPos, n, c, .94 - wet * .25, 0., 1.).rgb, c, n), 1);
        return;
    }
    if (vMaterial == 300)
    {
        float grain = noiseSurface(vWorldPos * 70.);
        float patch = noiseSurface(vWorldPos * .7);
        float broad = noiseSurface(vWorldPos * .06);
        c = vec3(.19, .19, .175) * (.78 + grain * .17 + patch * .20) * (.90 + .16 * broad);
        n = surfaceBump(n, grain * .004 * (1. - uWetness * .85), .3);
        vec3 v = normalize(uViewPos - vWorldPos);
        vec3 sky = cityReflection(reflect(-v, n), .12, n);
        float wet = uWetness * smoothstep(.2, .7, patch);
        if (uEmissionOnly == 2)
        {
            FragColor = vec4(0, 0, 0, 1);
            return;
        }
        vec3 shaded = shadePbr(vWorldPos, n, c * (1. - wet * .3), mix(.88, .14, wet), .05, 1.).rgb;
        shaded = outdoorLightResult(shaded, c, n);
        shaded = mix(shaded, sky, wet * (.12 + .40 * pow(1. - max(0., dot(n, v)), 4.)));
        FragColor = vec4(shaded, 1);
        return;
    }
    if (vMaterial == 302)
    {
        // Explicit paint material works on hills and bridges as well as sea level.
        float microFade = exp(-dot(fwidth(vWorldPos * 18.), fwidth(vWorldPos * 18.)));
        float wear = mix(.5, noiseSurface(vWorldPos * 18.), microFade) * .55 +
                     noiseSurface(vWorldPos * 1.7) * .45;
        float coverage = smoothstep(.15, .34, wear);
        vec3 asphalt = vec3(.19, .19, .175) *
                       (.78 + noiseSurface(vWorldPos * 70.) * .17 + noiseSurface(vWorldPos * .7) * .20);
        c = mix(asphalt, c * .78, coverage);
        FragColor = vec4(
            outdoorLightResult(shadePbr(vWorldPos, n, c, .87 - uWetness * .32, 0., 1.).rgb, c, n), 1);
        return;
    }
    if (uHasFacade == 1 && (vMaterial == 17 || vMaterial == 29 || vMaterial == 30) && abs(n.y) < .4)
    {
        vec2 uv = abs(n.x) > .5 ? p.zy : p.xy;
        vec3 tex = texture(uFacade, uv * .42).rgb;
        float h = texture(uFacadeBump, uv * .42).r;
        c *= .72 + tex * .48;
        n = surfaceBump(n, h * .009, .35);
        float seam =
            1. - smoothstep(.018, .045,
                            min(abs(fract(uv.x / 3.0) - .5) * 3., abs(fract(uv.y / 2.0) - .5) * 2.));
        c *= 1. - .20 * seam;
        c *= .85 + .15 * smoothstep(0., 1.8, p.y);
    }
    // A view-dependent room box behind each window. Local XY holds window metres,
    // local Z is a stable apartment seed; the facade itself remains a single quad.
    if (vMaterial == 31)
    {
        vec3 view = normalize(uViewPos - vWorldPos);
        vec3 tangent = abs(n.x) > .5 ? vec3(0, 0, 1) : vec3(1, 0, 0);
        vec2 uv = p.xy / vec2(1.28, 1.08) + .5;
        vec3 ray = vec3(-dot(view, tangent), -view.y, -max(.08, abs(dot(view, n))));
        vec3 pos = vec3(uv, 0.);
        vec3 target = vec3(ray.x > 0. ? 1. : 0., ray.y > 0. ? 1. : 0., -.9);
        vec3 tt = (target - pos) / ray;
        float depth = min(tt.x, min(tt.y, tt.z));
        vec3 hit = pos + ray * depth;
        float room = hashSurface(vec3(floor(p.z + .5), 2, 5));
        vec3 inside = mix(vec3(.27, .22, .16), vec3(.47, .37, .24), room);
        if (tt.y < tt.x && tt.y < tt.z)
            inside *= ray.y < 0. ? .55 : 1.25;
        float shift = (room - .5) * .15;
        if (tt.z <= tt.x && tt.z <= tt.y && hit.x > .10 && hit.x < .88 && hit.y > .30 && hit.y < .88)
            inside = atlasColor(22 + int(room * 2.99), (hit.xy - vec2(.10, .30)) / vec2(.78, .58)) * .75;
        if (tt.y <= tt.x && tt.y <= tt.z && ray.y < 0.)
            inside = atlasColor(22 + int(fract(room * 7.) * 2.99), vec2(hit.x, -hit.z)) * .5;
        float closest = depth;
        float sofa = boxRay(pos, ray, vec3(.12 + shift, .02, -.80), vec3(.65 + shift, .23, -.50));
        if (sofa < closest)
        {
            closest = sofa;
            inside = mix(vec3(.27, .12, .07), vec3(.15, .21, .10), room);
        }
        int roomType = int(fract(room * 17.) * 4.);
        float table = boxRay(pos, ray, vec3(.32, .02, -.47), vec3(.68, .27, -.18));
        if (table < closest)
        {
            closest = table;
            inside = vec3(.30, .17, .085);
        }
        if (roomType < 2)
        {
            // TV stands on a floor-supported cabinet; its bottom equals cabinet top.
            float cabinet = boxRay(pos, ray, vec3(.70, .0, -.75), vec3(.96, .25, -.48));
            if (cabinet < closest)
            {
                closest = cabinet;
                inside = vec3(.25, .13, .065);
            }
            float tv = boxRay(pos, ray, vec3(.73, .25, -.70), vec3(.93, .48, -.52));
            if (tv < closest)
            {
                closest = tv;
                vec3 t = pos + ray * tv;
                inside = vec3(.09, .065, .04);
                if (t.x > .75 && t.x < .91 && t.y > .28 && t.y < .45)
                    inside = vec3(.12, .26, .31);
            }
        }
        else
        {
            float storage =
                boxRay(pos, ray, vec3(.70, .0, -.84), vec3(.96, roomType == 2 ? .70 : .87, -.51));
            if (storage < closest)
            {
                closest = storage;
                vec3 t = pos + ray * storage;
                inside = roomType == 2 ? vec3(.63, .64, .54) : vec3(.27, .15, .075);
                if (roomType == 2 && abs(t.y - .50) < .009)
                    inside *= .35;
                if (roomType == 3 && abs(t.x - .83) < .009)
                    inside *= .45;
            }
        }
        float chandelier = boxRay(pos, ray, vec3(.43, .78, -.45), vec3(.57, .86, -.30));
        if (chandelier < closest)
            inside = vec3(.80, .63, .36);
        int curtainType = int(fract(room * 31.) * 10.);
        float detail = 1. - smoothstep(.012, .055, max(fwidth(uv.x), fwidth(uv.y)));
        float edge = min(uv.x, 1. - uv.x),
              fold = .91 + .09 * detail * sin(uv.x * 95. + sin(uv.y * 7.) * .3);
        float curtain = 0.;
        vec3 fabric = mix(vec3(.48, .29, .17), vec3(.57, .56, .39), fract(room * 9.));
        if (curtainType == 1)
            curtain = 1. - smoothstep(.12, .14, edge); // open pair
        if (curtainType == 2)
            curtain = 1. - smoothstep(.26, .28, edge); // half closed pair
        if (curtainType == 3)
            curtain = 1. - smoothstep(.485, .495, edge); // drawn drapes
        if (curtainType == 4)
            curtain = 1. - smoothstep(.57, .59, uv.x); // asymmetric left
        if (curtainType == 5)
            curtain = smoothstep(.39, .41, uv.x); // asymmetric right
        if (curtainType == 6 || curtainType == 7)
        { // lace/tulle: room remains visible
            float lace = mix(.1, pow(abs(sin(uv.x * 74.) * sin(uv.y * 90.)), 6.), detail);
            curtain = (curtainType == 6 ? .28 : .42) + lace * .28;
            fabric = vec3(.81, .78, .66);
            if (curtainType == 7 && uv.y < .40)
                curtain = 0.;
        }
        if (curtainType == 8)
        {
            curtain = 1. - smoothstep(.10 + .14 * abs(uv.y - .42), .13 + .14 * abs(uv.y - .42), edge);
            fabric = vec3(.46, .21, .14);
        }
        if (curtainType == 9)
        {
            curtain = smoothstep(.48, .50, uv.y);
            fabric = vec3(.64, .58, .42);
            fold = .9 + .1 * sin(uv.y * 85.);
        }
        // Floral repeat and fine weave are derivative filtered to avoid shimmer.
        float floral = sin(uv.x * 34.) * sin(uv.y * 29.);
        fabric *= fold * (.97 + .03 * floral * detail);
        inside = mix(inside, fabric, clamp(curtain, 0., 1.));
        float night = 1. - uDaylight;
        float period = mix(300., 2100., room);
        float interval = floor((uTime + room * period) / period);
        float lit = step(.68, hashSurface(vec3(floor(p.z + .5), interval, 19)));
        // Wall picture, floorboards, sofa and curtains establish room depth.

        if (tt.y < tt.x && tt.y < tt.z && ray.y < 0.)
            inside *= .8 + .2 * step(.04, fract(hit.x * 7.));
        inside *= .12 + uDaylight * .45 + lit * night * 1.6;
        vec3 reflection = cityReflection(reflect(-view, n), .16, n);
        float fresnel = .15 + .70 * pow(1. - abs(dot(n, view)), 4.);
        FragColor =
            vec4(uEmissionOnly == 2 ? inside * lit * night * .35 : mix(inside, reflection, fresnel), 1);
        return;
    }
    // Black opaque occluders share the emission pass depth buffer. Light
    // behind an electrode or a filter must not bloom through it.
    if (uEmissionOnly == 2)
    {
        FragColor = vec4(vMaterial == 8 ? c * 1.5 : vec3(0), 1);
        return;
    }
    if (vMaterial == 9)
    {
        FragColor = vec4(outdoorLightResult(c, c, n), 1);
        return;
    }
    if (vMaterial == 25)
    {
        FragColor = vec4(c, 1);
        return;
    }
    if (vMaterial == 8)
    {
        FragColor = vec4(c, 1);
        return;
    }
    if (vMaterial == 19 && uOutdoor == 1)
    {
        vec3 sun = normalize(uLightDir);
        float shade = uShadowStrength > 0.
                          ? mix(1., shadowFactor(vWorldPos, max(0., dot(n, sun))), uShadowStrength)
                          : 1.;
        // Shared demonstration fields fog out at 110 m. A city uses metre-scale
        // streets: retain clouds, but extend that atmospheric path to 1.375 km.
        // Otherwise every distant lawn becomes an opaque blue-white rectangle.
        // Terrain receives the same final aerial perspective as buildings and water.
        FragColor = vec4(outdoorLightResult(outdoorGroundWithHaze(vWorldPos, n, c, sun, vWorldPos, uTime,
                                                                  shade, uAtmosphereDensity),
                                            c, n),
                         1);
        return;
    }
    // Keep atmospheric water/vegetation and literal graph colours on their existing paths.
    if (uWorkshopFinish == 1 && vMaterial != 370 && vMaterial != 7 && vMaterial != 18 &&
        vMaterial != 19 && vMaterial != 20 && vMaterial != 21 && vMaterial != 23 && vMaterial != 24)
    {
        FragColor = shadeWorkshop(c, n, p, vMaterial);
        FragColor.rgb = outdoorLightResult(FragColor.rgb, c, n);
        return;
    }
    float rough = .36, metal = 0., spec = 1., height = 0., bump = .12;
    if (vMaterial == 19)
    {
        c = fieldAlbedo(vWorldPos.xz, c);
        rough = .95;
        spec = .12;
        float soil = noiseSurface(vWorldPos * 26.);
        height = soil * .008;
        bump = .26;
    }
    else if (vMaterial == 20 || vMaterial == 24)
    {
        rough = .85;
        spec = .18;
        float veins = fineStripe(p.x * 230. + p.z * 17.);
        c *= .87 + .13 * noiseSurface(p * 52.);
        height = veins * .001;
        bump = .12;
    }
    else if (vMaterial == 370)
    {
        // Birch lenticels run across the trunk, not along it like wood grain.
        vec2 bark = vec2((p.x + p.z) * 24., p.y * 38.);
        float pores = smoothstep(.62, .78, noiseSurface(vec3(bark.x * .7, bark.y, 1.)));
        c *= .91 - .60 * pores;
        rough = .91;
        spec = .15;
        height = pores * .0015;
        bump = .18;
    }
    else if (vMaterial == 21)
    {
        float groove = noiseSurface(vec3(p.x * 85., p.y * 6., p.z * 85.));
        c *= .65 + .55 * groove;
        rough = .94;
        spec = .12;
        height = groove * .016;
        bump = .4;
    }
    else if (vMaterial == 22)
    {
        rough = .12;
        metal = .58;
        spec = .6;
    }
    else if (vMaterial == 23)
    {
        vec2 tile = abs(fract(p.xz / vec2(.8, .5)) - .5);
        float seam = 1. - smoothstep(.475, .493, max(tile.x, tile.y));
        c *= .72 + .25 * seam;
        rough = .78;
        spec = .25;
        height = seam * .0008 + noiseSurface(p * 100.) * .0001;
        bump = .12;
    }
    else if (vMaterial == 17)
    {
        // Quiet concrete at room scale. Filter fine aggregate before it becomes pixel noise.
        float macro = noiseSurface(p * 3.2);
        float footprint = length(fwidth(p * 105.));
        float aggregate = mix(noiseSurface(p * 105.), .5, smoothstep(.35, 1.7, footprint));
        c *= .87 + .13 * macro + .065 * aggregate;
        if (uDetailedFinish == 1)
        {
            // Formwork pores and softly weathered lower wall, independent of camera.
            float pores = smoothstep(.69, .82, noiseSurface(p * 24.));
            c *= 1. - pores * .045;
        }
        rough = .79;
        spec = .32;
        height = (aggregate - .5) * .0008;
        bump = .14;
    }
    else if (vMaterial == 18)
    {
        // Three nonparallel wave trains avoid the old rectangular ripple pattern.
        float a = dot(p.xz, vec2(2.1, 1.3)) - uTime * .42;
        float b = dot(p.xz, vec2(-1.2, 3.7)) - uTime * .63;
        float q = dot(p.xz, vec2(5.3, 2.2)) - uTime * .91;
        float rip = sin(a) * .55 + sin(b + .4 * sin(a)) * .28 + sin(q) * .12;
        height = rip * .018;
        rough = .21;
        bump = .28;
        vec3 v = normalize(uViewPos - vWorldPos);
        float fresnel = pow(1. - abs(dot(n, v)), 4.);
        c = mix(c * .78, vec3(.40, .62, .73), fresnel * .55);
        c += vec3(.008, .018, .022) * (rip + 1.);
    }
    else if (vMaterial == 15)
    {
        // Filtered tool marks in the part's own frame: circular facing on gear
        // webs and a finer axial finish. The pattern rotates with the part.
        float rings = fineStripe(length(p.xy) * 720.);
        float brush = fineStripe(p.z * 520.);
        height = (rings + brush * .35) * .00045;
        bump = .16;
        rough = .25 + .025 * rings;
        metal = .94;
        c *= .96 + .025 * rings;
    }
    else if (vMaterial == 16)
    {
        float castGrain = noiseSurface(p * 125.);
        height = castGrain * .002;
        bump = .22;
        rough = .57;
        metal = .38;
        c *= .91 + .10 * castGrain;
    }
    else if (vMaterial == 2 || vMaterial == 3 || vMaterial == 10)
    {
        float brush = fineStripe(p.y * 460. + sin(p.x * 11.) * .6);
        rough = vMaterial == 2 ? .27 : .23;
        metal = .90;
        height = brush * .0012;
        c *= .93 + .055 * brush;
    }
    else if (vMaterial == 4)
    {
        float grain = noiseSurface(vec3(p.x * 1.3, p.y * 8., p.z * 10.));
        float pores = fineStripe(p.z * 170. + grain * 5.);
        float seam = smoothstep(.01, .025, abs(fract(p.z / .42) - .5));
        c *= .78 + .24 * grain + .035 * pores;
        c *= .91 + .09 * seam;
        rough = .68;
        spec = .38;
        height = pores * .0004;
        bump = .10;
    }
    else if (vMaterial == 5)
    {
        rough = .91;
        spec = .25;
        c *= .94 + .06 * noiseSurface(p * 85.);
    }
    else if (vMaterial == 6)
    {
        rough = .20;
        height = noiseSurface(p * 90.) * .0008;
        bump = .1;
    }
    else if (vMaterial == 7)
    {
        float ripple = sin(p.x * 7. + p.z * 3. - uTime * 1.1) * sin(p.z * 11. - uTime * .8);
        height = ripple * .012;
        rough = .16;
        bump = .25;
        vec3 v = normalize(uViewPos - vWorldPos);
        c = mix(c * .70, vec3(.43, .68, .79), pow(1. - abs(dot(n, v)), 4.) * .48);
        c += vec3(.015, .032, .045) * (ripple + 1.);
    }
    else if (vMaterial == 12)
    {
        float wrinkle = noiseSurface(p * 16.) + .25 * noiseSurface(p * 35.);
        height = wrinkle * .008;
        rough = .29;
        metal = .92;
        bump = .35;
        c *= .90 + .16 * wrinkle;
    }
    else if (vMaterial == 13 || vMaterial == 14)
    {
        rough = .08;
        metal = .98;
        spec = 1.2;
    }
    else if (vMaterial == 11)
    {
        float fleck = noiseSurface(p * 42.);
        c *= .80 + .24 * fleck;
        rough = .83;
        spec = .3;
        height = fleck * .003;
    }
    else
    {
        rough = .35;
        height = noiseSurface(p * 80.) * .0006;
    }
    if (uEnvironmentFill > 0. && !gl_FrontFacing)
        n = -n;
    if (uDetailedFinish == 1 && vMaterial == 1)
    {
        float peel = noiseSurface(p * 210.);
        height = peel * .0012;
        bump = .2;
        rough = .30;
    }
    n = surfaceBump(n, height, bump);
    FragColor = shadePbr(vWorldPos, n, c, rough, metal, spec);
    if (vMaterial == 18 && uDetailedFinish == 1)
    {
        // Fresnel reflection of the SAME sky as above the scene, with analytic
        // capillary slopes. Macro elevation remains the module's computed mesh.
        vec2 x = p.xz;
        float a = dot(x, vec2(3.1, 1.7)) - uTime * .75;
        float b = dot(x, vec2(-2.3, 5.1)) - uTime * 1.1;
        float q = dot(x, vec2(9.7, 4.2)) - uTime * 1.6;
        vec2 slope = cos(a) * vec2(.075, .041) + cos(b) * vec2(-.031, .069) + cos(q) * vec2(.025, .011);
        n = normalize(vNormal + vec3(-slope.x, 0, -slope.y) * .42);
        vec3 v = normalize(uViewPos - vWorldPos), r = reflect(-v, n);
        float fres = .035 + .965 * pow(1. - max(0., dot(n, v)), 5.);
        // Rough-water reflection is angularly filtered. Sharp cloud detail on
        // capillary normals creates false bright rings and horizon shimmer.
        vec3 sky = outdoorSky(r, vWorldPos, normalize(uLightDir), uTime) * .34;
        sky +=
            outdoorSky(normalize(r + vec3(.11, .08, 0)), vWorldPos, normalize(uLightDir), uTime) * .33;
        sky +=
            outdoorSky(normalize(r + vec3(-.11, .08, 0)), vWorldPos, normalize(uLightDir), uTime) * .33;
        float glint = pow(max(0., dot(n, normalize(v + normalize(uLightDir)))), 220.);
        vec3 deep = mix(vec3(.025, .16, .20), c * .70, .46);
        FragColor.rgb = mix(deep, sky, clamp(fres * .70, .08, .70)) + vec3(1., .93, .76) * glint * .65;
        FragColor.rgb *= mix(1., cloudShade(vWorldPos, normalize(uLightDir), uTime), uAtmosphereDensity);
        return;
    }
    if (vMaterial == 22)
    {
        vec3 r = reflect(-normalize(uViewPos - vWorldPos), n);
        vec3 sky = uOutdoor == 1
                       ? outdoorSky(r, vWorldPos, normalize(uLightDir), uTime) * uAtmosphereDensity
                       : vec3(.38, .52, .60);
        FragColor.rgb = outdoorLightResult(mix(c * .4, sky, .62), c, n);
        return;
    }
    if (vMaterial == 20 || vMaterial == 24)
    {
        float transmission =
            pow(max(0., dot(-normalize(uViewPos - vWorldPos), normalize(uLightDir))), 3.);
        FragColor.rgb += c * (.20 + .27 * transmission);
    }
    if (uOutdoor == 1)
        FragColor.rgb *= mix(1., cloudShade(vWorldPos, normalize(uLightDir), uTime), uAtmosphereDensity);
    // Optional broad cool/warm area-light reflection for isolated instruments.
    // Default zero preserves the existing lab scenes and their lighting.
    vec3 reflected = reflect(-normalize(uViewPos - vWorldPos), n);
    float blue = pow(max(0., dot(reflected, normalize(vec3(.8, .5, .4)))), 8.);
    float warm = pow(max(0., dot(reflected, normalize(vec3(-.6, .7, .2)))), 13.);
    if (vMaterial == 12 || vMaterial == 13)
    {
        float horizon = smoothstep(-.2, .6, reflected.y);
        float strip = pow(max(0., dot(reflected, normalize(vec3(-.2, .6, .9)))), 7.);
        FragColor.rgb += c * (.13 + .23 * horizon) + c * strip * .70;
    }
    if (vMaterial == 14)
    {
        // Broad sunshield / cold-sky reflections give the curved optical
        // surface a readable shape without painting grime onto a mirror.
        float horizon = smoothstep(-.13, .32, reflected.y + .18 * sin(reflected.x * 8.));
        float band = exp(-pow((reflected.y - .15 * sin(reflected.x * 5.)) / .13, 2.));
        vec3 sky = mix(vec3(.06, .10, .19), vec3(.83, .91, 1.1), horizon);
        FragColor.rgb = c * (sky * .83 + vec3(.56, .44, .25) * band) + FragColor.rgb * .45;
    }
    if (vMaterial == 15 || (uDetailedFinish == 1 && metal > .35))
    {
        // Broad neutral studio panels make curved metal readable between the
        // small direct highlights. Tint the reflection by the metal itself.
        float ceiling = smoothstep(-.45, .75, reflected.y);
        float softbox = exp(-pow((reflected.x + .30) / .52, 2.)) * smoothstep(-.65, .25, reflected.z);
        vec3 studio = mix(vec3(.14, .17, .22), vec3(.52, .59, .67), ceiling);
        FragColor.rgb += c * (studio * .68 + softbox * .34);
    }
    FragColor.rgb +=
        uEnvironmentFill * (c * (.05 + .10 * max(n.y, 0.)) +
                            metal * (vec3(.18, .30, .48) * blue + vec3(.46, .35, .22) * warm));
    FragColor.rgb = outdoorLightResult(FragColor.rgb, c, n);
}
void main()
{
    cityMaterial();
    FragColor.rgb = uEmissionOnly == 2 ? FragColor.rgb * cityAirTransmission() : cityFog(FragColor.rgb);
}
