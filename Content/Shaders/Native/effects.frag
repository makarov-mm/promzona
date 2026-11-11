#version 330 core
in vec4 color;
in vec3 shape;
out vec4 pixel;
void main()
{
    float coverage;
    if (shape.z < 1.5)
        coverage = 1.0 - smoothstep(0.12, 1.0, dot(shape.xy, shape.xy));
    else if (shape.z < 2.5)
    {
        float edge = 0.86 + 0.07*sin(5.0*shape.x + 6.0*shape.y) + 0.06*cos(8.0*shape.y);
        coverage = 1.0 - smoothstep(edge - 0.14, edge, length(shape.xy));
    }
    else
        coverage = 1.0 - smoothstep(0.70, 1.0, abs(shape.x));
    pixel = vec4(color.rgb, color.a * coverage);
}
