// The same wave phase lifts the shoreline mesh and the impact rings drawn over it.
uniform float uWaterEdge;

float shoreDistance(vec2 position)
{
    return max(abs(position.x), abs(position.y)) - uWaterEdge;
}

vec3 shoreWave(vec2 position, float time)
{
    float distanceToCoast = shoreDistance(position);
    float envelope = smoothstep(-54.0, -12.0, distanceToCoast)
                   * (1.0 - smoothstep(22.0, 48.0, distanceToCoast));
    float phase = distanceToCoast * 0.29 + time * 1.16
                + 0.75 * sin(dot(position, vec2(0.051, 0.037)));
    vec2 outward = abs(position.x) > abs(position.y) ? vec2(sign(position.x), 0.0)
                                                     : vec2(0.0, sign(position.y));
    vec2 phaseGradient = outward * 0.29 + 0.75 * cos(dot(position, vec2(0.051, 0.037))) * vec2(0.051, 0.037);
    float height = 0.12 * sin(phase) + 0.025 * sin(2.0 * phase + 0.4);
    vec2 gradient = phaseGradient * (0.12 * cos(phase) + 0.05 * cos(2.0 * phase + 0.4));
    return envelope * vec3(height, gradient);
}
