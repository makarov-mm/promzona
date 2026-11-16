#version 330 core

in vec4 color;
in vec3 shape;
in vec3 world;
out vec4 pixel;
uniform vec3 uEye;
uniform float uTime;

#include "environment.glsl"

float cellHash(vec3 p)
{
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

// A puddle of the managed game: a nearly still mirror of the city and the sky, with rings where the rain falls into it.
vec3 puddle(vec3 bed)
{
    vec2 q = world.xz;
    float atten = 1.0 / (1.0 + length(uEye - world) * .012);
    vec2 slope = vec2(cos(dot(q, vec2(.17, .11)) + uTime * .55), sin(dot(q, vec2(-.13, .21)) - uTime * .42)) * .04;
    slope += vec2(sin(dot(q, vec2(.31, -.16)) + uTime * .73), cos(dot(q, vec2(.22, .29)) + uTime * .32)) * .023;
    slope *= atten * .2;
    vec2 cell = floor(q * 1.7);
    vec2 jitter = vec2(cellHash(vec3(cell, 2.0)), cellHash(vec3(cell, 3.0))) - 0.5;
    vec2 local = fract(q * 1.7) - 0.5 - 0.27 * jitter;
    float age = fract(uTime * 1.65 + cellHash(vec3(cell, 1.0)));
    float radius = length(local);
    float crest = radius - age * 0.33;
    float ring = cos(crest * 95.0) * exp(-abs(crest) * 47.0) * (1.0 - age);
    float filterWidth = length(fwidth(q * 22.0));
    float fade = (1.0 - smoothstep(0.27, 0.35, radius)) * exp(-filterWidth * filterWidth);
    slope += local / max(0.025, radius) * ring * 0.025 * uRain * fade;
    vec3 n = normalize(vec3(slope.x, 1.0, slope.y)), v = normalize(uEye - world);
    float fresnel = .12 + .75 * pow(1.0 - max(0.0, dot(n, v)), 4.0);
    return mix(bed, toDisplay(cityReflection(world, reflect(-v, n), .12, false)), fresnel);
}

void main()
{
    float coverage;

    if (shape.z < 1.5)
    {
        coverage = 1.0 - smoothstep(0.12, 1.0, dot(shape.xy, shape.xy));
    }
    else if (shape.z < 2.5)
    {
        float edge = 0.86 + 0.07*sin(5.0*shape.x + 6.0*shape.y) + 0.06*cos(8.0*shape.y);
        coverage = 1.0 - smoothstep(edge - 0.14, edge, length(shape.xy));
    }
    else if (shape.z < 3.5)
    {
        coverage = 1.0 - smoothstep(0.70, 1.0, abs(shape.x));
    }
    else if (shape.z > 5.5)
    {
        // Use world position along the track so adjoining segments have no visible caps.
        float edge = abs(shape.x);
        float pixel = max(fwidth(shape.x), 0.035);
        float grain = sin(dot(world.xz, vec2(31.0, 24.0))) * sin(dot(world.xz, vec2(-21.0, 33.0)));
        grain *= 1.0 - smoothstep(0.04, 0.12, length(fwidth(world.xz)));
        coverage = (1.0 - smoothstep(0.51, 1.0 + pixel, edge)) * (0.82 + 0.15 * grain);
    }
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
        float edge = 0.92 + 0.04 * sin(shape.x * 8.0 + shape.y * 5.0) + 0.03 * sin(shape.y * 11.0 - shape.x * 4.0);
        float rim = length(shape.xy);
        float transition = 0.09 + fwidth(rim);
        coverage = 1.0 - smoothstep(edge - transition, edge, rim);
    }

    vec3 shown = shape.z > 3.5 && shape.z < 4.5 ? puddle(color.rgb) : color.rgb;

    // The haze of the scene takes distant smoke, spray and water as well.
    shown = mix(uFog, shown, airTransmission(uEye, world));

    // Premultiplied: fire and sparks are brighter than white in the managed game, whose frame is floating point,
    // and keep their light while they fade. Here each channel is clipped after the fade instead of before it.
    float alpha = color.a * coverage;
    pixel = vec4(min(vec3(1.0), shown * alpha), alpha);
}
