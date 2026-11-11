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
    else if (shape.z < 3.5)
        coverage = 1.0 - smoothstep(0.70, 1.0, abs(shape.x));
    else if (shape.z > 4.5)
    {
        float r = length(shape.xy);
        float width = max(fwidth(r), .022);
        float outer = 1.0 - smoothstep(.018, .018 + width, abs(r - .87));
        float inner = 1.0 - smoothstep(.012, .012 + width, abs(r - .64));
        coverage = max(outer, inner * .46);
    }
    else
    {
        float edge = 0.92 + 0.04 * sin(shape.x * 8.0 + shape.y * 5.0)
                          + 0.03 * sin(shape.y * 11.0 - shape.x * 4.0);
        coverage = 1.0 - smoothstep(edge - 0.045, edge, length(shape.xy));
    }
    pixel = vec4(color.rgb, color.a * coverage);
}
