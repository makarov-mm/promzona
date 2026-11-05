uniform vec3 uCityTunnels[8], uCityTunnelFloors[8];
uniform int uCityTunnelCount;
int cityTunnelAt(vec3 p)
{
    for (int i = 0; i < 8; i++)
    {
        if (i >= uCityTunnelCount)
            break;
        vec3 t = uCityTunnels[i];
        if (abs(p.x - t.x) <= t.z && abs(p.z - t.y) <= 5.95 &&
            p.y < uCityTunnelFloors[i].x + (p.x - t.x) * uCityTunnelFloors[i].y + 5.05)
            return i;
    }
    return -1;
}
uniform int uLocalLighting, uHasTerrain;
uniform sampler2D uTerrainMap;
uniform float uTerrainOrigin, uTerrainSize;
float terrainY(vec2 p)
{
    if (uHasTerrain == 0)
        return 0.;
    vec2 uv = (p - vec2(uTerrainOrigin) + .5) / uTerrainSize;
    uv.y = 1. - uv.y;
    vec2 h = texture(uTerrainMap, uv).rg * 255.;
    return dot(h, vec2(256., 1.)) / 512.;
}
uniform float uSurfaceWetness, uDaylight, uStreetSpacing, uGridLow, uGridHigh, uLampSide;
uniform vec3 uLampPosition[12], uLampColor[12], uLampDirection[12];
// Analytic sky/ground/horizon reflection, not a second scene render.
vec3 cityEnvironment(vec3 ray, float roughness)
{
    vec3 ground = mix(vec3(.012, .016, .022), vec3(.12, .14, .12), uDaylight);
    vec3 sky = mix(uFogHorizon, uFogZenith, smoothstep(0., .8, ray.y));
    float skyline = .10 + .035 * sin(atan(ray.z, ray.x) * 19.) + .025 * sin(atan(ray.z, ray.x) * 37.);
    float edge = smoothstep(skyline - .03 - roughness * .09, skyline + .03 + roughness * .09, ray.y);
    vec3 result = mix(ground, sky, edge);
    float sun = pow(max(0., dot(ray, normalize(uLightDir))), mix(500., 22., roughness));
    float inside = (cityTunnelAt(vWorldPos) >= 0 ? 1. : 0.);
    return mix(result + vec3(1., .84, .60) * sun * .55 * uDaylight * (1. - uWeatherRain),
               vec3(.13, .14, .13), inside);
}
vec3 localLamp(vec3 pos, vec3 tint, vec3 axis, vec3 p, vec3 n, vec3 albedo, float range)
{
    vec3 delta = pos - p;
    float d2 = dot(delta, delta);
    if (d2 > range * range)
        return vec3(0);
    vec3 l = delta * inversesqrt(max(d2, .001));
    float cone = dot(axis, axis) > .1 ? smoothstep(.78, .94, dot(-l, normalize(axis))) : 1.;
    float fall = pow(max(0., 1. - d2 / (range * range)), 2.) / (1. + d2 * .08);
    vec3 view = normalize(uViewPos - p), halfway = normalize(l + view + vec3(.00001));
    float spec =
        pow(max(0., dot(n, halfway)), mix(32., 110., uSurfaceWetness)) * (.08 + .30 * uSurfaceWetness);
    return ((albedo * .9 + vec3(.05)) * (max(0., dot(n, l)) + .04) + spec) * tint * fall * cone;
}
vec3 outdoorLightResult(vec3 shaded, vec3 albedo, vec3 n)
{
    if (uLocalLighting == 0)
        return shaded;
    vec3 result = shaded * mix(vec3(.24, .29, .38), vec3(1), uDaylight) * (1. - .12 * uWeatherRain);
    // Soft hemisphere fill separates shaded facades from asphalt without washing out nights.
    result += albedo * vec3(.055, .064, .073) * uDaylight * (.65 + .35 * max(n.y, 0.));
    float dark = 1. - uDaylight;
    // Diffuse night skylight keeps unlit surfaces legible without raising lamp emission.
    result += albedo * vec3(.018, .023, .032) * dark * (.65 + .35 * max(n.y, 0.));
    float underground = (cityTunnelAt(vWorldPos) >= 0 ? 1. : 0.);
    if (underground > .5)
    {
        int tunnelIndex = cityTunnelAt(vWorldPos);
        vec3 tunnel = uCityTunnels[tunnelIndex], floorData = uCityTunnelFloors[tunnelIndex];
        result = shaded * .16 + albedo * vec3(.13, .135, .12);
        float lx = floor(vWorldPos.x / 9.) * 9.;
        for (int k = 0; k < 2; k++)
        {
            float x = lx + float(k) * 9.;
            result += localLamp(vec3(x, floorData.x + (x - tunnel.x) * floorData.y + 4.7, tunnel.y),
                                vec3(3.2, 2.9, 2.1), vec3(0), vWorldPos, n, albedo, 13.);
        }
        dark = 0.;
    }
    if (uStreetSpacing > 0. && dark > .001)
    {
        vec2 cell = floor((vWorldPos.xz - uGridLow) / uStreetSpacing);
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                vec2 node = vec2(uGridLow) + (cell + vec2(i, j)) * uStreetSpacing;
                if (any(lessThan(node, vec2(uGridLow - .01))) ||
                    any(greaterThan(node, vec2(uGridHigh + .01))))
                    continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    result +=
                        dark * localLamp(vec3(node.x + float(side) * uLampSide + .9,
                                              4.08 + terrainY(node + vec2(float(side) * uLampSide, 12.)),
                                              node.y + 12.),
                                         vec3(3.8, 3.15, 2.15), vec3(0), vWorldPos, n, albedo, 15.);
                    result +=
                        dark * localLamp(vec3(node.x + 12.9,
                                              4.08 + terrainY(node + vec2(12., float(side) * uLampSide)),
                                              node.y + float(side) * uLampSide),
                                         vec3(3.8, 3.15, 2.15), vec3(0), vWorldPos, n, albedo, 15.);
                }
            }
    }
    for (int i = 0; i < 12; i++)
        if (dot(uLampColor[i], uLampColor[i]) > .001)
            result +=
                localLamp(uLampPosition[i], uLampColor[i], uLampDirection[i], vWorldPos, n, albedo, 22.);
    return result;
}
