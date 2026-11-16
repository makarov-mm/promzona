uniform int uCoverCount;
uniform vec3 uCoverCentre[12], uCoverHalf[12];

// Signed horizontal distance to the nearest roof edge above this point.
float roofDistance(vec3 point)
{
    float nearest = 1000.0;
    for (int i = 0; i < uCoverCount; ++i)
    {
        if (point.y > uCoverCentre[i].y - 0.04) continue;
        vec2 q = abs(point.xz - uCoverCentre[i].xz) - uCoverHalf[i].xz;
        nearest = min(nearest, length(max(q, 0.0)) + min(max(q.x, q.y), 0.0));
    }
    return nearest;
}
