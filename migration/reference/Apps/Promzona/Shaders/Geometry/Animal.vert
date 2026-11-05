#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
layout(location = 5) in mat4 iModel;
layout(location = 9) in vec4 iAnimation;
layout(location = 10) in vec4 iTint;
#define CITY_SKIN_POSES iAnimation.xyz
uniform mat4 uView, uProj;
out vec3 vWorldPos, vNormal, vTint;
out vec2 vUv;

#include "Skinning.glsl"

void main()
{
    mat4 skin = citySkin();
    vec4 p = iModel * skin * vec4(aPos, 1.);
    vWorldPos = p.xyz;
    vNormal = mat3(iModel) * mat3(skin) * aNormal;
    vUv = aUv;
    vTint = iTint.rgb;
    gl_Position = uProj * uView * p;
}
