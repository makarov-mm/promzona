#version 330 core
#extension GL_GOOGLE_include_directive : require
in vec3 vWorldPos, vNormal, vTint;
in vec2 vUv;
uniform sampler2D uColorMap, uNormalMap, uFinishMap;
uniform int uHasColor, uHasNormal, uHasFinish;
uniform vec3 uBase;
uniform float uRoughness, uNormalStrength;
#include "Renderer/Shader.PbrLightUniforms.glsl"
#include "Renderer/Shader.PbrFunctions.glsl"
#include "Atmosphere.glsl"
#include "LocalLighting.glsl"
void main()
{
    vec4 tex = uHasColor == 1 ? texture(uColorMap, vUv) : vec4(1.);
    if (tex.a < .3)
        discard;
    vec3 c = tex.rgb * uBase * vTint, n = normalize(vNormal);
    if (!gl_FrontFacing)
        n = -n;
    if (uHasNormal == 1)
    {
        vec3 dx = dFdx(vWorldPos), dy = dFdy(vWorldPos);
        vec2 a = dFdx(vUv), b = dFdy(vUv);
        float det = a.x * b.y - a.y * b.x;
        if (abs(det) > 1e-8)
        {
            vec3 t = normalize((dx * b.y - dy * a.y) / det);
            t = normalize(t - n * dot(n, t));
            vec3 bit = normalize(cross(n, t)) * sign(det);
            vec3 bump = texture(uNormalMap, vUv).xyz * 2. - 1.;
            bump.xy *= uNormalStrength;
            n = normalize(mat3(t, bit, n) * bump);
        }
    }
    float rough = clamp(uRoughness * (uHasFinish == 1 ? texture(uFinishMap, vUv).g : 1.), .55, .98);
    vec3 lit = shadePbr(vWorldPos, n, c, rough, 0., .3).rgb;
    FragColor = vec4(cityFog(outdoorLightResult(lit, c, n)), 1.);
}
