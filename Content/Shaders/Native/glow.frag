#version 330 core
in vec2 uv;
out vec4 color;
uniform sampler2D source;
uniform int pass;
uniform float threshold;
vec3 highlights(vec3 rgb) {
    float peak = max(rgb.r, max(rgb.g, rgb.b));
    return rgb * smoothstep(threshold, threshold + 0.16, peak);
}
void main() {
    vec2 pixel = 1.0 / vec2(textureSize(source, 0));
    if (pass == 0) {
        // Four bilinear taps cover a 4x4 footprint before blurring.
        vec3 c = highlights(texture(source, uv + pixel * vec2(-1,-1)).rgb)
               + highlights(texture(source, uv + pixel * vec2( 1,-1)).rgb)
               + highlights(texture(source, uv + pixel * vec2(-1, 1)).rgb)
               + highlights(texture(source, uv + pixel * vec2( 1, 1)).rgb);
        color = vec4(0.25 * c, 1);
    } else {
        vec2 d = pass == 1 ? vec2(pixel.x,0) : vec2(0,pixel.y);
        vec3 c = texture(source, uv).rgb * 0.227027;
        c += (texture(source, uv + d * 1.384615).rgb +
              texture(source, uv - d * 1.384615).rgb) * 0.316216;
        c += (texture(source, uv + d * 3.230769).rgb +
              texture(source, uv - d * 3.230769).rgb) * 0.070270;
        color = vec4(c, 1);
    }
}
