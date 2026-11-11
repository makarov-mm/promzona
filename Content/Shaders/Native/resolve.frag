#version 330 core
in vec2 uv;
out vec4 color;
uniform sampler2D sceneImage;
uniform sampler2D glowImage;
uniform bool enableBloom;
uniform vec3 conditions; // daylight, rain, low sun
float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

vec3 smoothEdge(vec2 stepUV) {
    vec3 center = texture(sceneImage, uv).rgb;
    float nw = luma(texture(sceneImage, uv + stepUV * vec2(-1, 1)).rgb);
    float ne = luma(texture(sceneImage, uv + stepUV * vec2( 1, 1)).rgb);
    float sw = luma(texture(sceneImage, uv + stepUV * vec2(-1,-1)).rgb);
    float se = luma(texture(sceneImage, uv + stepUV * vec2( 1,-1)).rgb);
    float m = luma(center);
    float lo = min(m, min(min(nw,ne), min(sw,se)));
    float hi = max(m, max(max(nw,ne), max(sw,se)));
    if (hi-lo < max(0.04, hi * 0.125)) return center;
    vec2 dir = vec2(sw+se-nw-ne, ne+se-nw-sw);
    float reduce = max((nw+ne+sw+se) * 0.03125, 0.0078125);
    dir = clamp(dir / (min(abs(dir.x),abs(dir.y)) + reduce), -6.0, 6.0) * stepUV;
    vec3 a = 0.5 * (texture(sceneImage,uv-dir/6.).rgb + texture(sceneImage,uv+dir/6.).rgb);
    vec3 b = 0.5 * a + 0.25 * (texture(sceneImage,uv-dir*.5).rgb + texture(sceneImage,uv+dir*.5).rgb);
    return (luma(b) < lo || luma(b) > hi) ? a : b;
}
void main() {
    vec3 c = smoothEdge(1.0 / vec2(textureSize(sceneImage, 0)));
    float night = 1.0 - conditions.x;
    float warm = conditions.z * (1.0 - conditions.y);
    // Scene is already tone-mapped. Preserve blacks and highlights.
    c += c * (1.0 - c) * (vec3(.07, .005, -.075) * warm + .09 * night + .05 * conditions.y);
    vec3 halo = enableBloom ? texture(glowImage, uv).rgb : vec3(0.0);
    c += (1.0 - c) * halo * (.065 + .20 * night + .07 * conditions.y);
    color = vec4(clamp(c,0.,1.), 1);
}
