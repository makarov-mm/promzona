#version 330 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uColor;

void main()
{
    vec3 color = texture(uColor, vUv).rgb;
    // GDI leaves the alpha channel undefined. Zero RGB is reserved for the unpainted canvas.
    fragColor = vec4(color, any(greaterThan(color, vec3(0.0))) ? 1.0 : 0.0);
}
