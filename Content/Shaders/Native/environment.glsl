// Sky, haze and reflections, shared by the scene, the effects and the sky itself.
// A port of the managed game's Atmosphere.glsl, Reflections.glsl and the environment of
// LocalLighting.glsl. "Display" colours are those of the finished picture, in which the
// managed game shaded; the scene shader works in linear light and compresses at its end.
#include "water.glsl"
#include "cloud_layer.glsl"
uniform float uTime;

uniform vec3 uSun;          // towards the sun, not normalised
uniform vec3 uFog, uZenith; // display colours of the sky at the horizon (the haze) and overhead
uniform vec4 uAir;          // haze per metre, its height scale, golden light, wetness of the street
uniform float uDay, uRain;
uniform samplerCube uProbe;
uniform int uProbeReady;
uniform int uProbeRoofed;
uniform vec3 uProbeCentre;

vec3 toDisplay(vec3 light) {
    light = max(light, vec3(0.));
    return pow(light/(light + vec3(1.)), vec3(1./2.2));
}
vec3 fromDisplay(vec3 shown) {
    vec3 light = pow(max(shown, vec3(0.)), vec3(2.2));
    return light/max(vec3(1.) - light, vec3(.06));
}
// Share of the light that crosses the haze between the eye and a point. The haze is an
// exponential layer: it thins with height.
float airTransmission(vec3 eye, vec3 point) {
    float scale = max(1., uAir.y), a = max(0., eye.y)/scale, b = max(0., point.y)/scale;
    float layer = abs(b - a) < .001 ? exp(-.5*(a + b)) : (exp(-a) - exp(-b))/(b - a);
    return exp(-min(80., length(point - eye)*uAir.x*layer));
}
// What a surface mirrors where the probe does not reach: ground, a jagged skyline, the
// sky above it and the glint of the sun. Linear light.
float openSeaView(vec3 position, vec3 ray)
{
    vec2 coast = abs(position.x) > abs(position.z) ? vec2(sign(position.x), 0.0)
                                                   : vec2(0.0, sign(position.z));
    return smoothstep(-65.0, -7.0, shoreDistance(position.xz))
         * smoothstep(-0.16, 0.20, dot(ray.xz, coast));
}

vec3 cityEnvironment(vec3 position, vec3 ray, float roughness, bool roofed, float seaward) {
    if (roofed) return fromDisplay(vec3(.13, .14, .13));
    vec3 ground = mix(vec3(.012, .016, .022), vec3(.12, .14, .12), uDay);
    vec3 sky = mix(uFog, uZenith, smoothstep(0., .8, ray.y));
    float around = atan(ray.z, ray.x);
    float skyline = .10 + .035*sin(around*19.) + .025*sin(around*37.);
    // Out at sea the horizon reflects the sky. Keep the skyline on rays facing the island.
    float edge = smoothstep(skyline - .03 - roughness*.09, skyline + .03 + roughness*.09, ray.y);
    edge = mix(edge, 1.0, seaward);
    float sun = pow(max(0., dot(ray, normalize(uSun))), mix(500., 22., roughness));
    if (sun > 0.0001 && uDay > 0.01)
        sun *= transmittedSun(position, normalize(uSun), uTime, uRain);
    return fromDisplay(mix(ground, sky, edge) + vec3(1., .84, .60)*sun*.55*uDay*(1. - uRain));
}
// The city around `position` seen along `direction`: the local probe with a spherical
// parallax correction, fading into the analytic environment away from its centre.
vec3 cityReflection(vec3 position, vec3 direction, float roughness, bool roofed) {
    float seaward = openSeaView(position, direction);
    vec3 beyond = cityEnvironment(position, direction, roughness, roofed, seaward);
    if (uProbeReady == 0 || roofed != (uProbeRoofed != 0)) return beyond;
    // The sky is at infinity: clouds keep their direction outside the local parallax volume.
    // Never borrow a city-centre probe for a distant stretch of coast.
    if (seaward > 0.0 && !roofed)
    {
        float skyWeight = seaward * openSeaView(uProbeCentre, direction)
                        * smoothstep(0.015, 0.14, direction.y);
        beyond = mix(beyond, textureLod(uProbe, direction, roughness * 6.0).rgb, skyWeight);
    }
    vec3 relative = position - uProbeCentre;
    float b = dot(relative, direction);
    float t = -b + sqrt(max(0., b*b + 70.*70. - dot(relative, relative)));
    float weight = 1. - smoothstep(28., 100., length(relative));
    return mix(beyond, textureLod(uProbe, relative + direction*max(1., t), roughness*6.).rgb, weight);
}
