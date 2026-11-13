// Sky, haze and reflections, shared by the scene, the effects and the sky itself.
// A port of the managed game's Atmosphere.glsl, Reflections.glsl and the environment of
// LocalLighting.glsl. "Display" colours are those of the finished picture, in which the
// managed game shaded; the scene shader works in linear light and compresses at its end.
uniform vec3 uSun;          // towards the sun, not normalised
uniform vec3 uFog, uZenith; // display colours of the sky at the horizon (the haze) and overhead
uniform vec4 uAir;          // haze per metre, its height scale, golden light, wetness of the street
uniform float uDay, uRain;
uniform samplerCube uProbe;
uniform int uProbeReady;
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
vec3 cityEnvironment(vec3 ray, float roughness, bool roofed) {
    if (roofed) return fromDisplay(vec3(.13, .14, .13));
    vec3 ground = mix(vec3(.012, .016, .022), vec3(.12, .14, .12), uDay);
    vec3 sky = mix(uFog, uZenith, smoothstep(0., .8, ray.y));
    float around = atan(ray.z, ray.x);
    float skyline = .10 + .035*sin(around*19.) + .025*sin(around*37.);
    float edge = smoothstep(skyline - .03 - roughness*.09, skyline + .03 + roughness*.09, ray.y);
    float sun = pow(max(0., dot(ray, normalize(uSun))), mix(500., 22., roughness));
    return fromDisplay(mix(ground, sky, edge) + vec3(1., .84, .60)*sun*.55*uDay*(1. - uRain));
}
// The city around `position` seen along `direction`: the local probe with a spherical
// parallax correction, fading into the analytic environment away from its centre.
vec3 cityReflection(vec3 position, vec3 direction, float roughness, bool roofed) {
    vec3 beyond = cityEnvironment(direction, roughness, roofed);
    if (uProbeReady == 0) return beyond;
    vec3 relative = position - uProbeCentre;
    float b = dot(relative, direction);
    float t = -b + sqrt(max(0., b*b + 70.*70. - dot(relative, relative)));
    float weight = 1. - smoothstep(28., 100., length(relative));
    return mix(beyond, textureLod(uProbe, relative + direction*max(1., t), roughness*6.).rgb, weight);
}
