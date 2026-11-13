#version 330 core
in vec4 color;
in vec3 shape;
in vec3 world;
out vec4 pixel;
uniform vec3 uEye;
uniform float uTime;
#include "environment.glsl"
float cellHash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
// A puddle of the managed game: a nearly still mirror of the city and the sky, with
// rings where the rain falls into it.
vec3 puddle(vec3 bed)
{
    vec2 q = world.xz;
    float atten = 1.0 / (1.0 + length(uEye - world) * .012);
    vec2 slope = vec2(cos(dot(q, vec2(.17, .11)) + uTime * .55), sin(dot(q, vec2(-.13, .21)) - uTime * .42)) * .04;
    slope += vec2(sin(dot(q, vec2(.31, -.16)) + uTime * .73), cos(dot(q, vec2(.22, .29)) + uTime * .32)) * .023;
    slope *= atten * .2;
    vec2 cell = floor(q * 1.7), local = fract(q * 1.7) - .5;
    float phase = fract(uTime * 1.9 + cellHash(vec3(cell, 1.0))), radius = length(local);
    float ring = sin((radius - phase * .8) * 65.0) * exp(-abs(radius - phase * .8) * 30.0) * (1.0 - phase);
    float filtered = exp(-dot(fwidth(q * 20.0), fwidth(q * 20.0)));
    slope += local / max(.04, radius) * ring * .018 * uRain * filtered;
    vec3 n = normalize(vec3(slope.x, 1.0, slope.y)), v = normalize(uEye - world);
    float fresnel = .12 + .75 * pow(1.0 - max(0.0, dot(n, v)), 4.0);
    return mix(bed, toDisplay(cityReflection(world, reflect(-v, n), .12, false)), fresnel);
}
void main()
{
    float coverage;
    if (shape.z < 1.5)
        coverage = 1.0 - smoothstep(0.12, 1.0, dot(shape.xy, shape.xy));
    else if (shape.z < 2.5)
    {
        float edge = 0.86 + 0.07*sin(5.0*shape.x + 6.0*shape.y) + 0.06*cos(8.0*shape.y);
        coverage = 1.0 - smoothstep(edge - 0.14, edge, length(shape.xy));
    }
    else if (shape.z < 3.5)
        coverage = 1.0 - smoothstep(0.70, 1.0, abs(shape.x));
    else if (shape.z > 4.5)
    {
        float r = length(shape.xy);
        float width = max(fwidth(r), .022);
        float outer = 1.0 - smoothstep(.018, .018 + width, abs(r - .87));
        float inner = 1.0 - smoothstep(.012, .012 + width, abs(r - .64));
        coverage = max(outer, inner * .46);
    }
    else
    {
        float edge = 0.92 + 0.04 * sin(shape.x * 8.0 + shape.y * 5.0)
                          + 0.03 * sin(shape.y * 11.0 - shape.x * 4.0);
        coverage = 1.0 - smoothstep(edge - 0.045, edge, length(shape.xy));
    }
    vec3 shown = shape.z > 3.5 && shape.z < 4.5 ? puddle(color.rgb) : color.rgb;
    // The haze of the scene takes distant smoke, spray and water as well.
    shown = mix(uFog, shown, airTransmission(uEye, world));
    // Premultiplied: fire and sparks are brighter than white in the managed game, whose
    // frame is floating point, and keep their light while they fade. Here each channel is
    // clipped after the fade instead of before it.
    float alpha = color.a * coverage;
    pixel = vec4(min(vec3(1.0), shown * alpha), alpha);
}
