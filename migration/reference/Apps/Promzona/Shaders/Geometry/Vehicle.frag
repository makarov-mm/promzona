#version 330 core
#extension GL_GOOGLE_include_directive : require
in vec3 vWorldPos, vNormal, vBodyPos;
in vec2 vUv;
uniform sampler2D uTexture, uFinish, uPaintMask;
uniform int uTextured, uGlass;
uniform vec3 uPaint;
uniform int uHasMap, uKind, uBraking, uBodySlot, uPaletteWidth;
uniform float uTime, uDamage;
#include "Renderer/Shader.PbrLightUniforms.glsl"
#include "Renderer/Shader.PbrFunctions.glsl"
#include "Atmosphere.glsl"
#include "LocalLighting.glsl"
#include "Reflections.glsl"
void cityMaterial()
{
    vec4 tex = uHasMap == 1 ? texture(uTexture, vUv) : vec4(.5, .5, .5, 1);
    vec3 c = tex.rgb;
    int material = int(tex.a * 255. + .5);
    int paletteSlot = int(floor(vUv.x * float(uPaletteWidth)));
    float paintMask = uTextured == 1 && uGlass == 0 ? texture(uPaintMask, vUv).r : 0.;
    bool paint = uTextured == 1 ? paintMask > .3 : paletteSlot == uBodySlot;
    if (uTextured == 1)
    {
        material = 1;
        c = mix(c, uPaint * clamp(dot(c, vec3(.3, .59, .11)) / .69, .3, 1.18), paintMask);
    }
    else if (paint)
    {
        c = uPaint;
        material = 1;
    }
    if (uDamage > 1.5)
    {
        c = vec3(.055, .050, .045);
        material = 1;
    }
    vec3 n = normalize(vNormal), v = normalize(uViewPos - vWorldPos);
    bool glass = uGlass == 1 || material == 22 || (material == 255 && c.b > c.r * 1.2);
    if (glass && !gl_FrontFacing)
        n = -n;
    if (glass && uWeatherRain > .05)
    {
        vec2 dropUv = (vBodyPos.xy + vBodyPos.zy * .37) * vec2(45., 65.),
             cell = fract(dropUv) - vec2(.5);
        float bead = exp(-dot(cell * vec2(1., .7), cell * vec2(1., .7)) * 55.) * .00022 * uWeatherRain *
                     exp(-dot(fwidth(dropUv), fwidth(dropUv)) * 3.);
        vec3 dx = dFdx(vWorldPos), dy = dFdy(vWorldPos), r1 = cross(dy, n), r2 = cross(n, dx);
        float determinant = dot(dx, r1);
        if (abs(determinant) > 1e-9)
            n = normalize(abs(determinant) * n -
                          sign(determinant) * (dFdx(bead) * r1 + dFdy(bead) * r2));
    }
    if (material == 8 && uDamage < 1.5)
    {
        if ((uKind == 3 || uKind == 4 || uKind == 6 || uKind == 7) && c.b > c.r * 1.4)
            c *= sin(uTime * 9. + vWorldPos.x * 2.) > 0. ? 2. : .13;
        else if (c.r > c.g * 2.)
            c *= uBraking == 1 ? 2.4 : .55;
        FragColor = vec4(c, 1);
        return;
    }
    float dirt = paint ? (1. - smoothstep(.08, .42, vBodyPos.y)) * .22 : 0.;
    float scratch = sin(vBodyPos.x * 310. + sin(vBodyPos.z * 45.) * 3.) * sin(vBodyPos.y * 145.);
    float filterFine = exp(-dot(fwidth(vBodyPos * 180.), fwidth(vBodyPos * 180.)));
    if (paint)
    {
        c = mix(c, vec3(.15, .12, .08), dirt);
        float scuff = smoothstep(.87, .99, scratch) * filterFine * min(.45, uDamage * .38);
        c = mix(c, vec3(.17, .18, .17), scuff);
        vec3 dx = dFdx(vWorldPos), dy = dFdy(vWorldPos), r1 = cross(dy, n), r2 = cross(n, dx);
        float det = dot(dx, r1),
              h = sin(vBodyPos.x * 480.) * sin(vBodyPos.z * 470.) * .00018 * filterFine;
        if (abs(det) > 1e-9)
            n = normalize(abs(det) * n - sign(det) * (dFdx(h) * r1 + dFdy(h) * r2));
    }
    bool rubber = !paint && !glass && max(c.r, max(c.g, c.b)) < .16;
    // Authored metal slots, not grey RGB: the truck's pale cargo box is painted sheet metal.
    bool chrome =
        uTextured == 0 && uDamage < 1.5 && !paint && !glass && !rubber &&
        (material == 34 || material == 16 || (uKind == 0 && (paletteSlot == 3 || paletteSlot == 5)) ||
         (uKind == 3 && paletteSlot == 2) || ((uKind == 6 || uKind == 7) && paletteSlot == 3));
    float rough = glass    ? .10
                  : paint  ? mix(.43, .24, uSurfaceWetness) + dirt * .6
                  : rubber ? .88
                  : chrome ? .24
                           : .65;
    float metal = chrome ? .85 : 0.;
    if (uTextured == 1)
    {
        vec4 finish = texture(uFinish, vUv);
        rough = clamp(finish.g, .15, .95);
        metal = finish.b * (1. - paintMask);
        if (glass)
        {
            rough = .15;
            metal = 0.;
            if (uDamage < 1.5)
                c = mix(c, vec3(.19, .25, .27), .6);
        }
        else
        {
            rough = mix(rough, max(.19, rough * .65), uSurfaceWetness);
            chrome = metal > .5;
            // Source has no normal map. This submillimetre relief is derived from its
            // worn albedo and filtered derivatives; it is not an authored normal map.
            float h = (1. - dot(tex.rgb, vec3(.3, .59, .11))) * .00065;
            vec3 dx = dFdx(vWorldPos), dy = dFdy(vWorldPos), r1 = cross(dy, n), r2 = cross(n, dx);
            float determinant = dot(dx, r1);
            if (abs(determinant) > 1e-9)
                n = normalize(abs(determinant) * n - sign(determinant) * (dFdx(h) * r1 + dFdy(h) * r2));
        }
        if (uDamage > 1.5)
        {
            rough = .94;
            metal = 0.;
            chrome = false;
            glass = false;
        }
    }
    vec3 shade = outdoorLightResult(shadePbr(vWorldPos, n, c, rough, metal, 1.).rgb, c, n);
    if (glass || paint || chrome)
    {
        vec3 sky = cityReflection(reflect(-v, n), rough, n);
        float fres = pow(1. - abs(dot(n, v)), 5.);
        float coat = glass    ? .38 + .50 * fres
                     : chrome ? .55
                              : (.055 + .28 * fres) * (1. - dirt) * (uDamage > 1.5 ? 0. : 1.);
        shade = mix(shade, sky, coat);
    }
    if (uTextured == 1 && uGlass == 0 && uDamage < 1.5)
    {
        bool lampBand = vBodyPos.y > .29 && vBodyPos.y < .60;
        bool headlamp =
            lampBand && vBodyPos.z > .965 && tex.b > tex.r * .90 && tex.b > .48 && tex.g > .42;
        bool tail = lampBand && vBodyPos.z < .035 && tex.r > tex.g * 1.8 && tex.r > .25;
        if (headlamp)
            shade += vec3(1., .85, .64) * (1. - uDaylight) * 1.8;
        if (tail)
            shade += vec3(.8, .035, .008) * (uBraking == 1 ? 1.5 : .35 * (1. - uDaylight));
    }
    FragColor = vec4(shade, uGlass == 1 ? (uDamage > 1.5 ? .78 : .60) : 1.);
}
void main()
{
    cityMaterial();
    FragColor.rgb = cityFog(FragColor.rgb);
}
