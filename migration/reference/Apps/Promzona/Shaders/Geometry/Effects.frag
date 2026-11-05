#version 330 core
#extension GL_GOOGLE_include_directive : require
in vec4 color;
in vec3 uv;
in vec3 vWorldPos;
uniform vec3 uViewPos;
out vec4 FragColor;
#include "Atmosphere.glsl"
void main()
{
    float a = color.a;
    if (uv.z > .5)
    {
        float r = length(uv.xy);
        a *= 1. - smoothstep(.12, 1., r);
    }
    if (uv.z > 2.5)
    {
        a *= .45 + .55 * step(.15, sin(uv.y * 24. + abs(uv.x) * 5.));
    }
    else if (uv.z > 1.5)
        a *= .65 + .35 * sin(uv.x * 18. + sin(uv.y * 14.));
    if (a < .005)
        discard;
    FragColor = vec4(cityFog(color.rgb), a);
}
