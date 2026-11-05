uniform vec3 uGait;
vec3 animalPose(vec3 p)
{
    if (uGait.z < 0.)
    {
        float a = uGait.z + 10.;
        if (p.y > 1.05 && p.z > .10)
        {
            vec2 v = p.zy - vec2(.04, 1.28);
            p.z = cos(a) * v.x - sin(a) * v.y + .04;
            p.y = sin(a) * v.x + cos(a) * v.y + 1.28;
        }
        return p;
    }
    if (uGait.z == 0. || uGait.y == 0.)
        return p;
    float lower = 1. - smoothstep(uGait.z * .5, uGait.z, p.y);
    float stride = sin(uGait.x + (p.x * p.z > 0. ? 0. : 3.14159265)) * uGait.y * lower;
    p.z += stride;
    p.y += max(0., stride) * .35;
    return p;
}
