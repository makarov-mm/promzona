namespace MakarovPhysics.Numerics;

/// <summary>
/// How much of a spherical light a spherical occluder hides from a point:
/// the eclipse geometry shared by the celestial-body shaders (umbra and
/// penumbra on a planet, a moon in its planet's shadow) and by the tests
/// that pin them. The GLSL in CosmosPainter is a line-for-line copy of
/// <see cref="Lit"/>; a change here is a change there.
///
/// The two discs are compared by their ANGULAR radii as seen from the
/// point, and the overlap is the planar overlap of two circles with those
/// radii - exact in the limit of small angles, which every eclipse is (the
/// Sun from Earth is 0.27°; from Io, Jupiter is 10° and the planar
/// approximation is still within a percent of the spherical cap).
/// Twin: Tools/twins/shadowing_twin.py.
/// </summary>
public static class Shadowing
{
    /// <summary>Apparent angular radius of the Sun from 1 AU, radians (696 000 km / 1 AU).</summary>
    public const double SolarAngularRadius = 0.004652;

    /// <summary>
    /// Fraction of the area of a disc of radius <paramref name="light"/>
    /// covered by a disc of radius <paramref name="occluder"/> whose centre
    /// lies <paramref name="separation"/> away. 1 when fully covered; for a
    /// smaller occluder inside the light disc, the ratio of the areas
    /// (an annular eclipse).
    /// </summary>
    public static double CoveredFraction(double light, double occluder, double separation)
    {
        if (light <= 0) return separation < occluder ? 1 : 0;
        if (occluder <= 0) return 0;
        if (separation >= light + occluder) return 0;
        if (separation <= Math.Abs(occluder - light))
            return occluder >= light ? 1 : occluder * occluder / (light * light);
        double a = (separation * separation + light * light - occluder * occluder) / (2 * separation);
        double b = (separation * separation + occluder * occluder - light * light) / (2 * separation);
        double area = light * light * Math.Acos(Math.Clamp(a / light, -1, 1)) - a * Math.Sqrt(Math.Max(0, light * light - a * a))
                    + occluder * occluder * Math.Acos(Math.Clamp(b / occluder, -1, 1)) - b * Math.Sqrt(Math.Max(0, occluder * occluder - b * b));
        return area / (Math.PI * light * light);
    }

    /// <summary>
    /// Light reaching the point (px, py, pz) from a spherical source centred
    /// at (lx, ly, lz) of radius <paramref name="lightRadius"/>, past a sphere
    /// centred at (cx, cy, cz) of radius <paramref name="occluderRadius"/>:
    /// 1 in full light, 0 in the umbra, between the two in the penumbra.
    /// <paramref name="lightAngular"/> &gt; 0 replaces the source's angular
    /// radius computed from its size and distance - for a scene whose Sun is
    /// drawn nearer and larger than it is, so the umbra keeps its true length.
    /// </summary>
    public static double Lit(double px, double py, double pz,
                             double lx, double ly, double lz, double lightRadius,
                             double cx, double cy, double cz, double occluderRadius,
                             double lightAngular = 0)
    {
        double tx = lx - px, ty = ly - py, tz = lz - pz;
        double ox = cx - px, oy = cy - py, oz = cz - pz;
        double dL = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        double dO = Math.Sqrt(ox * ox + oy * oy + oz * oz);
        if (dO <= occluderRadius) return 0;          // the point is inside the occluder
        if (dL <= 0 || dO >= dL) return 1;           // the occluder is not nearer than the light
        double cosSep = (tx * ox + ty * oy + tz * oz) / (dL * dO);
        if (cosSep <= 0) return 1;                   // the occluder is behind the point
        double alphaLight = lightAngular > 0 ? lightAngular : Math.Asin(Math.Min(1, lightRadius / dL));
        double alphaOccluder = Math.Asin(Math.Min(1, occluderRadius / dO));
        double separation = Math.Acos(Math.Min(1, cosSep));
        return 1 - CoveredFraction(alphaLight, alphaOccluder, separation);
    }
}
