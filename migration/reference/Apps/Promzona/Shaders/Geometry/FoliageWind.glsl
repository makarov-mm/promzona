vec3 foliageWind(vec3 p, vec3 local, int material, float time)
{
    if (material == 24)
    {
        float bend = max(0., local.y);
        float gust = sin(p.x * .7 + p.z * .45 - time * 1.25) + .35 * sin(p.z * 1.9 - time * 2.1);
        p.x += bend * gust * .22;
        p.z += bend * gust * .08;
    }
    else if (material == 20)
    {
        float flutter = sin(p.x * 3.1 + p.z * 2.2 - time * 1.4) * .003;
        p.x += flutter;
        p.y += flutter * .3;
    }
    return p;
}
