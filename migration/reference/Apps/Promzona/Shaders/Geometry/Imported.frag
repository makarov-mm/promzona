#version 330 core
#extension GL_GOOGLE_include_directive : require
in vec3 vWorldPos, vNormal, vLocal;
in vec2 vUv;
uniform sampler2D uColorMap, uNormalMap, uFinishMap;
uniform int uHasColor, uHasNormal, uHasFinish, uGlass, uVehicle, uFar, uFarSurfaceAlpha;
uniform vec3 uColor, uPaint, uSize;
uniform float uRough, uMetal, uDamage;
uniform int uTransparent;
uniform float uLodBlend;
uniform int uLodLayer;
#include "Renderer/Shader.PbrLightUniforms.glsl"
#include "Renderer/Shader.PbrFunctions.glsl"
#include "Atmosphere.glsl"
#include "LocalLighting.glsl"
#include "Reflections.glsl"
void main()
{
    // Complementary screen-door masks avoid transparent sorting and retain solid
    // silhouettes while switching geometry. No animated noise or frame-rate clock.
    if (uLodBlend > 0. && uLodBlend < 1.)
    {
        float mask = fract(52.9829189 * fract(dot(floor(gl_FragCoord.xy), vec2(.06711056, .00583715))));
        if (uLodLayer == 0 ? mask < uLodBlend : mask >= uLodBlend)
            discard;
    }
    vec4 tex = uHasColor == 1 ? texture(uColorMap, vUv) : vec4(1.);
    vec3 c = tex.rgb * uColor, n = normalize(vNormal), v = normalize(uViewPos - vWorldPos);
    if (!gl_FrontFacing)
        n = -n;
    if (uHasNormal == 1)
    {
        vec3 dp1 = dFdx(vWorldPos), dp2 = dFdy(vWorldPos);
        vec2 du1 = dFdx(vUv), du2 = dFdy(vUv);
        float det = du1.x * du2.y - du1.y * du2.x;
        if (abs(det) > 1e-8)
        {
            vec3 t = normalize((dp1 * du2.y - dp2 * du1.y) / det);
            t = normalize(t - n * dot(n, t));
            vec3 b = normalize(cross(n, t)) * sign(det);
            vec3 map = texture(uNormalMap, vUv).xyz * 2. - 1.;
            n = normalize(mat3(t, b, n) * map);
        }
    }
    vec3 finish = uHasFinish == 1 ? texture(uFinishMap, vUv).rgb : vec3(1., 1., 1.);
    float rough = clamp(uRough * finish.g, .16, .95), metal = clamp(uMetal * finish.b, 0., 1.);
    bool glass = uGlass == 1;
    if (uFarSurfaceAlpha == 1)
    {
        glass = tex.a < .1;
        metal = glass ? 0. : clamp((1. - tex.a) * 2.55, 0., 1.);
        rough = glass ? .18 : mix(.7, .38, metal);
    }
    // Repaint only broad painted panels; preserve tyres, dark glass, lamps and bright chrome.
    float lum = dot(c, vec3(.3, .59, .11)), chroma = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
    float paint = float(uVehicle == 1 && !glass && metal < .65 && vLocal.y > uSize.y * .25 &&
                        vLocal.y < uSize.y * .84 && abs(vLocal.z) < uSize.z * .46);
    paint *= smoothstep(.18, .42, lum) * (1. - smoothstep(.76, .98, lum)) *
             (1. - smoothstep(.20, .50, chroma));
    c = mix(c, uPaint * clamp(lum / .65, .25, 1.2), paint * .82);
    if (uDamage > 1.5)
    {
        c = vec3(.055, .05, .045);
        rough = .94;
        metal = 0.;
    }
    else if (uVehicle == 1)
        c = mix(c, vec3(.16, .12, .08),
                min(.25, uDamage * .12) * (1. - smoothstep(.05, .5, vLocal.y / uSize.y)));
    rough = mix(rough, max(.16, rough * .65), uSurfaceWetness);
    vec3 shade = outdoorLightResult(shadePbr(vWorldPos, n, c, rough, metal, 1.).rgb, c, n);
    float fres = pow(1. - abs(dot(n, v)), 5.),
          coat = glass ? .45 + .45 * fres : metal * .4 + paint * (.04 + .2 * fres);
    if (uDamage < 1.5)
        shade = mix(shade, cityReflection(reflect(-v, n), rough, n), clamp(coat, 0., .9));
    FragColor = vec4(cityFog(shade), uTransparent == 1 ? (.10 + .32 * fres) : 1.);
}
