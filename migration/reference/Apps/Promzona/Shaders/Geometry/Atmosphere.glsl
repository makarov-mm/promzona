uniform float uFogDensity, uFogHeight, uWeatherRain, uWeatherDay;
uniform vec3 uFogHorizon, uFogZenith;

float cityAirTransmission()
{
    float a = max(0., uViewPos.y) / max(1., uFogHeight), b = max(0., vWorldPos.y) / max(1., uFogHeight);
    float layer = abs(b - a) < .001 ? exp(-.5 * (a + b)) : (exp(-a) - exp(-b)) / (b - a);
    return exp(-min(80., length(vWorldPos - uViewPos) * uFogDensity * layer));
}
vec3 cityFog(vec3 color)
{
    return mix(uFogHorizon, color, cityAirTransmission());
}
