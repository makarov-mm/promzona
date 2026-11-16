uniform int uBlockedPair, uBlockerCount;
uniform vec3 uBoxCentre[12], uBoxHalf[12], uBoxForward[12];

float beamVisibility(vec3 origin, vec3 point)
{
    vec3 path = point - origin;
    float visibility = 1.0;
    for (int i = 0; i < uBlockerCount; ++i)
    {
        vec3 z = uBoxForward[i], x = vec3(z.z, 0.0, -z.x);
        vec3 offset = origin - uBoxCentre[i];
        vec3 p = vec3(dot(offset, x), offset.y, dot(offset, z));
        vec3 d = vec3(dot(path, x), path.y, dot(path, z));
        vec3 inv = mix(vec3(-1.0), vec3(1.0), greaterThanEqual(d, vec3(0.0))) / max(abs(d), vec3(0.00001));
        vec3 a = (-uBoxHalf[i] - p) * inv, b = (uBoxHalf[i] - p) * inv;
        vec3 lo = min(a, b), hi = max(a, b);
        float enter = max(max(lo.x, lo.y), max(lo.z, 0.0));
        float leave = min(min(hi.x, hi.y), min(hi.z, 1.0));
        // A short overlap fades the silhouette and avoids shadowing the receiving facade itself.
        visibility = min(visibility, 1.0 - smoothstep(0.035, 0.22, (leave - enter) * length(path)));
    }
    return visibility;
}
