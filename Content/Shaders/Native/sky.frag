#version 330 core
// The sky of the managed game (Sky/Weather.frag): a gradient from the haze at the horizon
// to the zenith, the sun with its low glow, the moon, a slowly drifting cloud deck and
// stars. The same draw fills the faces of the reflection probe.
in vec2 uv;
out vec4 fragColor;
#include "environment.glsl"
uniform vec3 uRight, uUp, uForward; // axes of the view; the first two reach the edges of the frame
uniform float uTime;
uniform int uProbePass;

float skyHash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float skyNoise(vec2 p) {
    vec2 cell = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(skyHash(cell), skyHash(cell + vec2(1, 0)), f.x),
               mix(skyHash(cell + vec2(0, 1)), skyHash(cell + 1.0), f.x), f.y);
}
float clouds(vec2 p) {
    float value = 0.0, amplitude = 0.53;
    for (int octave = 0; octave < 4; ++octave) {
        value += amplitude * skyNoise(p);
        p = p * 2.03 + vec2(13.2, 7.1);
        amplitude *= 0.48;
    }
    return value;
}
void main() {
    vec3 ray = normalize(uForward + uRight * (uv.x * 2.0 - 1.0) + uUp * (uv.y * 2.0 - 1.0));
    vec3 sun = normalize(uSun);
    float day = uDay, rain = uRain, golden = uAir.z;
    float altitude = max(0.0, ray.y);
    vec3 sky = mix(uFog, uZenith, pow(altitude, 0.55));

    float sunDot = max(0.0, dot(ray, sun));
    float nearHorizon = smoothstep(0.0, 0.07, altitude) * (1.0 - smoothstep(0.12, 0.58, altitude));
    sky += vec3(0.56, 0.22, 0.045) * golden * nearHorizon * pow(sunDot, 3.0);
    vec3 sunlight = mix(vec3(1.0, 0.84, 0.61), vec3(1.0, 0.68, 0.36), golden);
    sky += sunlight * (pow(sunDot, 1600.0) * 0.8 + pow(sunDot, 20.0) * 0.12)
         * day * (1.0 - rain) * (1.0 - rain) * smoothstep(-0.04, 0.025, sun.y);
    vec3 moon = normalize(vec3(-sun.x, max(0.22, -sun.y), -sun.z));
    float moonDot = max(0.0, dot(ray, moon));
    sky += vec3(0.40, 0.47, 0.57) * (smoothstep(0.99982, 0.99996, moonDot) + 0.025 * pow(moonDot, 80.0))
         * (1.0 - day) * (1.0 - rain);

    if (ray.y > 0.015) {
        // The clouds drift by seconds on the screen, whatever the speed of the game clock.
        vec2 skyPlane = ray.xz / (ray.y + 0.18);
        vec2 p = skyPlane * 2.15 + uTime * vec2(0.0030, 0.0012);
        float density = clouds(p);
        float upper = smoothstep(mix(0.44, 0.29, rain), mix(0.69, 0.56, rain), density);
        vec2 lowPlane = skyPlane * 3.8 + uTime * vec2(-0.006, 0.0035);
        float lowNoise = 0.68 * skyNoise(lowPlane) + 0.32 * skyNoise(lowPlane * 2.31);
        float lower = smoothstep(0.47, 0.77, lowNoise) * mix(0.19, 0.78, rain);
        float horizonFade = smoothstep(0.015, 0.22, altitude);

        // Sample toward the light: the sunward rim sees less cloud in front of it.
        vec2 lightStep = sun.xz * 0.24;
        float towardSun = 0.67 * skyNoise(p + lightStep)
                        + 0.33 * skyNoise((p + lightStep) * 2.03 + vec2(13.2, 7.1));
        float edgeLight = smoothstep(-0.06, 0.24, density - towardSun)
                        * (1.0 - 0.78 * upper);
        vec3 shade = mix(vec3(0.53, 0.60, 0.69), vec3(0.25, 0.30, 0.37), rain);
        vec3 lit = mix(vec3(0.91, 0.93, 0.95), vec3(0.61, 0.66, 0.70), rain);
        lit = mix(lit, vec3(1.0, 0.73, 0.49), golden * (0.35 + 0.65 * sunDot));
        vec3 cloud = mix(shade, lit, clamp(0.40 + 1.5 * edgeLight - 0.25 * density, 0.0, 1.0));
        cloud = mix(vec3(0.021, 0.028, 0.044), cloud, day);
        sky = mix(sky, cloud, upper * horizonFade);

        vec3 lowColor = mix(vec3(0.014, 0.019, 0.030), shade * (0.85 + 0.16 * lowNoise), day);
        sky = mix(sky, lowColor, lower * horizonFade);
        float coverage = upper + lower * (1.0 - upper);

        float stars = step(0.998, skyHash(floor(ray.xz / (altitude + 0.1) * 480.0)))
                    * pow(max(0.0, skyNoise(p * 110.0)), 12.0);
        sky += vec3(0.4, 0.47, 0.6) * stars * (1.0 - day) * (1.0 - rain)
             * (1.0 - coverage) * smoothstep(0.12, 0.35, altitude);
    }
    fragColor = vec4(uProbePass != 0 ? fromDisplay(sky) : sky, 1.0);
}
