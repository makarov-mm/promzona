#version 330 core
#extension GL_GOOGLE_include_directive : require
in vec3 vWorldPos, vNormal, vLocal;
in vec2 vUv;
uniform sampler2D uTexture;
uniform vec3 uTint, uGait;
uniform int uHasMap, uCabin;
#include "Renderer/Shader.PbrLightUniforms.glsl"
#include "Renderer/Shader.PbrFunctions.glsl"
#include "Atmosphere.glsl"
#include "LocalLighting.glsl"
void cityMaterial()
{
    vec3 c = (uHasMap == 1 ? texture(uTexture, vUv).rgb : vec3(.6)) * uTint;
    if (uGait.z <= 0.)
    {
        int slot = int(vUv.x * 16.);
        float choice = uGait.x;
        if (slot == 0)
        {
            vec3 cloth = mix(vec3(.20, .30, .32), vec3(.55, .24, .15), fract(choice * .37));
            if (choice > 5.)
                cloth = mix(vec3(.42, .39, .30), vec3(.16, .19, .25), fract(choice * .61));
            c = choice > 8.5 ? vec3(.74, .81, .75) : cloth;
        }
        if (slot == 1)
            c = mix(vec3(.39, .24, .15), vec3(.85, .65, .49), uGait.y / 5.);
        if (slot == 2)
            c = mix(vec3(.10, .13, .18), vec3(.26, .23, .20), fract(choice * .43));
        if (slot == 5 || slot == 6)
            c = mix(c, vec3(.55, .51, .43), step(6.5, choice) * .8);
        if (choice == 10. && (slot == 0 || slot == 2))
            c = slot == 0 ? vec3(.19, .27, .34) : vec3(.10, .14, .20);
        if (choice == 11. && (slot == 0 || slot == 2))
            c = vec3(.16, .21, .17) * (1. + .20 * sin(vWorldPos.y * 37.) * sin(vWorldPos.x * 25.));
        if (choice == 12. && slot == 0)
            c = vec3(.56, .10, .29);
        if (choice == 13. && slot == 0)
            c = vec3(.30, .26, .37);
        if (choice >= 14. && slot == 0)
            c = vec3(.27, .26, .19);
        if (choice == 17. && (slot == 0 || slot == 2))
            c = vec3(.055, .06, .07);
        if (choice == 18. && (slot == 0 || slot == 2))
            c = vec3(.20, .25, .14) * (1. + .15 * sin(vLocal.y * 31.) * sin(vLocal.x * 27.));
        if (choice == 16. && (slot == 0 || slot == 2))
            c = slot == 0 ? vec3(.70, .61, .45) : vec3(.13, .41, .50);
        // The source civilian mesh wears shorts. Uniforms cover the lower legs too;
        // retain exposed face/hands instead of giving the officers civilian bare calves.
        if (choice == 10. || choice == 11. || choice == 17. || choice == 18.)
        {
            vec3 uniformColour = choice == 17.   ? vec3(.055, .06, .07)
                                 : choice == 10. ? vec3(.10, .14, .20)
                                                 : vec3(.16, .21, .17);
            if (slot == 1 && vLocal.y < .94)
                c = uniformColour;
            if (vLocal.y < .16)
                c = vec3(.055, .06, .055);
        }
        if (choice == 9. && (slot == 0 || slot == 2 || slot == 1 && vLocal.y < .96))
        {
            c = vLocal.y > .66 ? vec3(.85, .88, .83) : vec3(.12, .18, .20);
            if (vLocal.y < .16)
                c = vec3(.07, .08, .075);
        }
    }
    vec3 n = normalize(vNormal);
    if (uGait.z <= 0. && (int(vUv.x * 16.) == 0 || int(vUv.x * 16.) == 2))
    {
        vec2 weave = vLocal.xy * 260.;
        float filtered = exp(-dot(fwidth(weave), fwidth(weave)) * 2.);
        float thread = sin(weave.x) * sin(weave.y), height = thread * .00035 * filtered;
        vec3 dx = dFdx(vWorldPos), dy = dFdy(vWorldPos), r1 = cross(dy, n), r2 = cross(n, dx);
        float det = dot(dx, r1);
        if (abs(det) > 1e-9)
            n = normalize(abs(det) * n - sign(det) * (dFdx(height) * r1 + dFdy(height) * r2));
        c *= .97 + .03 * thread * filtered;
    }
    vec3 lit = shadePbr(vWorldPos, n, c, .8, 0., .3).rgb;
    vec3 result = outdoorLightResult(lit, c, n);
    if (uCabin == 1)
        result += c * vec3(.26, .23, .17) * (1. - uDaylight);
    FragColor = vec4(result, 1);
}
void main()
{
    cityMaterial();
    FragColor.rgb = cityFog(FragColor.rgb);
}
