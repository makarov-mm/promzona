#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uModel, uLightSpace;

#include "WheelPose.glsl"

void main()
{
    vec3 p = aPos, n = aNormal;
    wheelPose(p, n, aUv);
    gl_Position = uLightSpace * uModel * vec4(p, 1.);
}
