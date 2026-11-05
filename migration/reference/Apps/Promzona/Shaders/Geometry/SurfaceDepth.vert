#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 3) in vec3 aLocal;
layout(location = 4) in float aMaterial;
uniform mat4 uModel, uLightSpace;
uniform float uTime;

#include "FoliageWind.glsl"

void main()
{
    gl_Position = uLightSpace * uModel * vec4(foliageWind(aPos, aLocal, int(aMaterial + .5), uTime), 1);
}
