#version 330 core
// Contact shading from the depth of the finished frame, at half its resolution.
// A port of the managed game's PostProcess/Occlusion.frag.
in vec2 uv;
out float occlusion;
uniform sampler2D depthImage;
uniform vec4 lens; // projection [0][0] and [1][1], near plane, far plane
uniform int samples;

// Reconstruct at the centre of a depth texel. Half-resolution coordinates fall on the
// borders of full-resolution texels; rays through those borders, mixed with nearest
// depth samples, drew horizontal bands of self-shadow even on a flat plane.
ivec2 depthPixel(vec2 at) {
    ivec2 size = textureSize(depthImage, 0);
    return clamp(ivec2(at * vec2(size)), ivec2(0), size - 1);
}
vec3 position(ivec2 pixel) {
    ivec2 size = textureSize(depthImage, 0);
    pixel = clamp(pixel, ivec2(0), size - 1);
    float d = texelFetch(depthImage, pixel, 0).r;
    float z = lens.z * lens.w / (lens.w - d * (lens.w - lens.z));
    vec2 at = (vec2(pixel) + .5) / vec2(size);
    return vec3((at * 2. - 1.) * z / lens.xy, -z);
}
void main() {
    ivec2 pixel = depthPixel(uv);
    float d = texelFetch(depthImage, pixel, 0).r;
    vec3 p = position(pixel);
    if (d > .999999 || -p.z > 160.) {
        occlusion = 1.;
        return;
    }
    // The normal comes from the nearer neighbour on each axis, so that a silhouette
    // does not bend it.
    vec3 l = position(pixel - ivec2(1, 0)), r = position(pixel + ivec2(1, 0));
    vec3 b = position(pixel - ivec2(0, 1)), t = position(pixel + ivec2(0, 1));
    vec3 dx = abs(r.z - p.z) < abs(p.z - l.z) ? r - p : p - l;
    vec3 dy = abs(t.z - p.z) < abs(p.z - b.z) ? t - p : p - b;
    vec3 n = normalize(cross(dx, dy));
    if (dot(n, -p) < 0.) n = -n;
    vec3 tangent = normalize(cross(n, abs(n.z) < .9 ? vec3(0, 0, 1) : vec3(0, 1, 0)));
    vec3 bitangent = cross(n, tangent);
    float phase = fract(sin(dot(floor(gl_FragCoord.xy), vec2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
    const float radius = .95;
    float covered = 0.;
    for (int i = 0; i < 12; i++) {
        if (i >= samples) break;
        float f = (float(i) + .5) / float(samples), angle = phase + float(i) * 2.399963;
        float ring = sqrt(1. - f * f);
        vec3 direction = (tangent * cos(angle) + bitangent * sin(angle)) * ring + n * f;
        vec3 q = p + direction * radius * (.18 + .82 * f);
        vec2 at = q.xy * lens.xy / (-q.z) * .5 + .5;
        if (any(lessThan(at, vec2(.001))) || any(greaterThan(at, vec2(.999)))) continue;
        vec3 surface = position(depthPixel(at));
        float reach = 1. - smoothstep(radius * .5, radius * 2., length(surface - p));
        // A neighbouring texel of the same plane is not an occluder: at grazing angles
        // its centre can be in front of q through pixel quantisation alone.
        covered += step(-surface.z, -q.z - .035) * step(.025, dot(surface - p, n)) * reach;
    }
    occlusion = 1. - .56 * covered / float(samples) * (1. - smoothstep(70., 160., -p.z));
}
