#version 330 core
in vec2 vUv;out vec4 fragColor;uniform sampler2D uColor;
void main(){fragColor=texture(uColor,vUv);}
