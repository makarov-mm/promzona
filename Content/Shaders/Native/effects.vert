#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec4 aColor;
layout(location=2) in vec3 aShape;
uniform mat4 uViewProjection;
out vec4 color;
out vec3 shape;
out vec3 world;
uniform float uTime;
#include "water.glsl"

void main()
{
    world = aPosition;
    if (aShape.z == 5.0)
        world.y += shoreWave(world.xz, uTime).x;
    gl_Position = uViewProjection * vec4(world, 1.0);
    color = aColor;
    shape = aShape;
}
