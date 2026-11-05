#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 5) in mat4 iModel;
layout(location = 9) in vec4 iAnimation;
#define CITY_SKIN_POSES iAnimation.xyz
uniform mat4 uLightSpace;

#include "Skinning.glsl"

void main()
{
    gl_Position = uLightSpace * iModel * citySkin() * vec4(aPos, 1.);
}
