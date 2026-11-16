// Elliptical low beam in the vehicle's pitched frame. The upper cutoff rises
// slightly on the kerb side. Both road shading and airborne droplets use it.
float lowBeam(vec3 ray, vec3 axis)
{
    float forward = dot(ray, axis);
    if (forward <= 0.0) return 0.0;
    vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), axis));
    vec3 up = cross(axis, right);
    vec2 slope = vec2(dot(ray, right), dot(ray, up)) / max(forward, 0.01);
    float cutoff = 0.08 + 0.045 * smoothstep(0.02, 0.38, slope.x);
    float upper = 1.0 - smoothstep(cutoff - 0.04, cutoff + 0.035, slope.y);
    float spread = 1.0 - smoothstep(0.22, 0.86, abs(slope.x));
    float core = exp(-5.0 * slope.x * slope.x - 7.0 * slope.y * slope.y);
    return upper * spread * (0.20 + 0.80 * core);
}
