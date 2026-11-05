using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>Metre-based aerial perspective shared by sky, all opaque materials and bloom.
/// An exponential height layer approximates haze; this is not a volumetric weather solver.
/// Parameters follow existing smooth weather state, independently of draw distance/quality.</summary>
public readonly record struct CityAtmosphere(
    float Density,
    float HeightScale,
    Vec3 Horizon,
    Vec3 Zenith,
    float Rain,
    float Daylight)
{
    public float GoldenLight { get; private init; }

    private static float Smooth(float low, float high, float value)
    {
        float t = Math.Clamp((value - low) / (high - low), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static CityAtmosphere Create(double rain, double daylight, double sunHeight, double wetness, bool evening = false)
    {
        float r = (float)Math.Clamp(double.IsFinite(rain) ? rain : 0, 0, 1);
        float d = (float)Math.Clamp(double.IsFinite(daylight) ? daylight : 1, 0, 1);
        float w = (float)Math.Clamp(double.IsFinite(wetness) ? wetness : 0, 0, 1);
        float height = double.IsFinite(sunHeight) ? (float)sunHeight : 1;
        float clear = (1 - r) * (1 - r);
        float golden = Smooth(-.12f, .07f, height) * (1 - Smooth(.15f, .52f, height)) * clear;
        float twilight = (1 - Smooth(.02f, .18f, Math.Abs(height + .045f))) * clear;
        Vec3 horizon = Vec3.Lerp(new(.64f, .73f, .80f), new(.39f, .44f, .49f), r);
        horizon = Vec3.Lerp(Vec3.Lerp(new(.025f, .036f, .058f), new(.018f, .026f, .038f), r), horizon, d);
        horizon = Vec3.Lerp(horizon, evening ? new(.56f, .31f, .38f) : new(.49f, .42f, .55f), twilight * .62f);
        horizon = Vec3.Lerp(horizon, evening ? new(.90f, .55f, .38f) : new(.94f, .73f, .54f), golden * .68f);
        Vec3 zenith = Vec3.Lerp(new(.19f, .39f, .65f), new(.24f, .28f, .33f), r);
        zenith = Vec3.Lerp(new(.006f, .012f, .027f), zenith, d);
        zenith = Vec3.Lerp(zenith, new(.14f, .21f, .40f), .46f * twilight);
        return new CityAtmosphere(.00052f + .0028f * r + .00018f * w * (1 - d) + .00024f * golden,
            140 + 100 * r, horizon, zenith, r, d) { GoldenLight = golden };
    }

    public double Transmittance(double distance, double eyeHeight = 0, double targetHeight = 0)
    {
        double a = Math.Max(0, eyeHeight) / HeightScale, b = Math.Max(0, targetHeight) / HeightScale;
        double layer = Math.Abs(b - a) < .001 ? Math.Exp(-(a + b) * .5) : (Math.Exp(-a) - Math.Exp(-b)) / (b - a);
        return Math.Exp(-Math.Min(80, Math.Max(0, distance) * Density * layer));
    }

    internal void Apply(Shader s)
    {
        s.Use();
        s.SetFloat("uFogDensity", Density);
        s.SetFloat("uFogHeight", HeightScale);
        s.SetVec3("uFogHorizon", Horizon);
        s.SetVec3("uFogZenith", Zenith);
        s.SetFloat("uWeatherRain", Rain);
        s.SetFloat("uWeatherDay", Daylight);
    }
}
