#version 330 core
#extension GL_GOOGLE_include_directive : require
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec3 aColor;
layout(location = 3) in vec3 aLocal;
layout(location = 4) in float aMaterial;
uniform mat4 uView, uProj;
uniform vec3 uOffset;
out vec3 vWorldPos, vNormal, vColor, vLocal;
flat out int vMaterial;
uniform float uTime;

#include "FoliageWind.glsl"

void main()
{
    vWorldPos = foliageWind(aPos, aLocal, int(aMaterial + .5), uTime) + uOffset;
    vNormal = aNormal;
    vColor = aColor;
    vLocal = aLocal;
    vMaterial = int(aMaterial + .5);
    gl_Position = uProj * uView * vec4(vWorldPos, 1);
}
