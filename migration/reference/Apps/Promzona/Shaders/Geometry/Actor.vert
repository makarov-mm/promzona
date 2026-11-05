#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uModel, uView, uProj;
out vec3 vWorldPos, vNormal, vLocal;
out vec2 vUv;

#include "ActorGait.glsl"

#include "Skinning.glsl"

void main()
{
    mat4 skin = citySkin();
    vec3 posed = (skin * vec4(aPos, 1.)).xyz;
    vec4 world = uModel * vec4(animalPose(posed), 1.);
    vWorldPos = world.xyz;
    vLocal = posed;
    vec3 norm = mat3(skin) * aNormal;
    if (uGait.z < 0. && posed.y > 1.05 && posed.z > .10)
    {
        float a = uGait.z + 10.;
        norm.zy = mat2(cos(a), sin(a), -sin(a), cos(a)) * norm.zy;
    }
    vNormal = mat3(transpose(inverse(uModel))) * norm;
    vUv = aUv;
    gl_Position = uProj * uView * world;
}
