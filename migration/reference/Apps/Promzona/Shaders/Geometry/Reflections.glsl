uniform samplerCube uProbe;
uniform int uProbeReady;
uniform vec3 uProbeCentre;
vec3 cityReflection(vec3 direction, float roughness, vec3 normal)
{
    vec3 sky = cityEnvironment(direction, roughness);
    if (uProbeReady == 0)
        return sky;
    vec3 relative = vWorldPos - uProbeCentre;
    // Local spherical parallax approximation; fade smoothly to the sky probe outside it.
    float b = dot(relative, direction),
          t = -b + sqrt(max(0., b * b + 70. * 70. - dot(relative, relative)));
    vec3 sampleDirection = relative + direction * max(1., t);
    float weight = 1. - smoothstep(28., 100., length(relative));
    return mix(sky, textureLod(uProbe, sampleDirection, roughness * 6.).rgb, weight);
}
