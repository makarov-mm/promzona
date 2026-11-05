#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
uniform mat4 uModel, uLightSpace;

#include "ActorGait.glsl"

#include "Skinning.glsl"

void main()
{
    gl_Position = uLightSpace * uModel * vec4(animalPose((citySkin() * vec4(aPos, 1.)).xyz), 1.);
}
