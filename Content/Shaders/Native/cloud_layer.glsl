uniform sampler2D uCloudMap;

// Both sky rays and sunlight meet the same wind-driven horizontal deck.
// Bend just the near-horizon part of the projection to keep it finite.
vec2 cloudUv(vec3 origin, vec3 direction, float seconds)
{
    float rise = max(15.0, 148.0 - origin.y);
    vec2 deck = origin.xz + rise * direction.xz / (max(direction.y, 0.0) + 0.18);
    return (deck + seconds * vec2(1.9, 0.75)) / 740.0;
}
float cloudCoverage(float density, float rain)
{
    return smoothstep(mix(0.44, 0.29, rain), mix(0.69, 0.56, rain), density);
}
float transmittedSun(vec3 point, vec3 sun, float seconds, float rain)
{
    if (sun.y <= 0.0) return 1.0;
    float density = textureLod(uCloudMap, cloudUv(point, sun, seconds), 2.0).r;
    return 1.0 - 0.63 * smoothstep(0.02, 0.20, sun.y) * cloudCoverage(density, rain);
}
