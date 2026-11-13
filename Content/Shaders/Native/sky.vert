#version 330 core
out vec2 uv;
void main() {
    // One triangle at the far plane: the sky fills whatever the scene left open.
    uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(2.0 * uv - 1.0, 1.0, 1.0);
}
