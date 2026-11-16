#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec4 aColor;
layout(location=2) in vec3 aShape;
uniform mat4 uViewProjection;
out vec4 color;
out vec3 shape;
out vec3 world;
uniform float uTime;
uniform vec3 uHeadPosition[8], uHeadColor[8], uHeadAxis[8];
#include "headlight.glsl"
#include "water.glsl"

void main()
{
    world = aPosition;
    if (aShape.z == 5.0)
        world.y += shoreWave(world.xz, uTime).x;
    gl_Position = uViewProjection * vec4(world, 1.0);
    color = aColor;
    if (aShape.z >= 7.0)
    {
        for (int i = 0; i < 8; ++i)
        {
            vec3 offset = world - uHeadPosition[i];
            float d2 = dot(offset, offset);
            if (d2 < 34.0 * 34.0 && dot(uHeadColor[i], uHeadColor[i]) > 0.0001)
            {
                float beam = lowBeam(offset * inversesqrt(max(d2, 0.001)), uHeadAxis[i]);
                float scatter = beam * (1.0 - smoothstep(400.0, 1156.0, d2)) / (1.0 + d2 * 0.022);
                color.rgb += uHeadColor[i] * scatter * 0.38;
            }
        }
    }
    shape = aShape;
}
