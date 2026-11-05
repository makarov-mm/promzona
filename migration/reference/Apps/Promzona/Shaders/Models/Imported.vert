#version 330 core

layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;

uniform mat4 uModel, uView, uProj;
out vec3 vWorldPos, vNormal, vLocal;
out vec2 vUv;

void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    vLocal = aPos;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vUv = aUv;
    gl_Position = uProj * uView * world;
}
