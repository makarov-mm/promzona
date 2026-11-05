#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aColor;
layout(location = 2) in vec3 aUv;
uniform mat4 uView, uProj;
out vec4 color;
out vec3 uv;
out vec3 vWorldPos;
void main()
{
    gl_Position = uProj * uView * vec4(aPos, 1);
    color = aColor;
    uv = aUv;
    vWorldPos = aPos;
}
