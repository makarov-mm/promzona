#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uModel, uView, uProj;
uniform vec3 uBodyMin, uBodySpan;
out vec3 vWorldPos, vNormal, vBodyPos;
out vec2 vUv;

#include "WheelPose.glsl"

void main()
{
    vec3 p = aPos, n = aNormal;
    wheelPose(p, n, aUv);
    vec4 world = uModel * vec4(p, 1.);
    vWorldPos = world.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * n;
    vBodyPos = (aPos - uBodyMin) / uBodySpan;
    vUv = uTextured == 1 ? vec2(aUv.x, aUv.y >= 2. ? mod(aUv.y - 2., 2.) : aUv.y) : vec2(aUv.x, .5);
    gl_Position = uProj * uView * world;
}
