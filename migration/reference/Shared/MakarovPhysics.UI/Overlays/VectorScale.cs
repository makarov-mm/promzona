namespace MakarovPhysics.UI.Overlays;

/// <summary>
/// The scale a group of vector arrows is drawn at.
///
/// This is the opposite decision from the one the field arrow lattice makes,
/// and the difference is not stylistic. A lattice shows one quantity sampled
/// over a region across many orders of magnitude, so its length map saturates.
/// A force diagram shows several quantities at ONE point, and the reason to
/// draw it at all is to see them relate: that buoyancy is a little less than
/// weight, that the two nearly cancel, that the leftover is the acceleration.
/// None of that survives a nonlinear map. So the scale here is strictly
/// linear and strictly SHARED: every arrow in the group is drawn at the same
/// pixels per unit, or the picture stops being a diagram and becomes an
/// illustration.
///
/// The scale is quantized to 1-2-5 and only refitted when it has drifted out
/// of a band, for two reasons: the arrows stop breathing on every frame while
/// the physics wobbles, and the legend can print a round number.
/// </summary>
public static class VectorScale
{
    /// <summary>Round a scale down to the nearest 1-2-5 step of its decade.</summary>
    public static float Quantize(float pixelsPerUnit)
    {
        if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f) return 0f;
        double decade = Math.Pow(10, Math.Floor(Math.Log10(pixelsPerUnit)));
        if (!double.IsFinite(decade) || decade <= 0) return 0f;
        double normalized = pixelsPerUnit / decade;
        double step = normalized >= 5 ? 5 : normalized >= 2 ? 2 : 1;
        return (float)(step * decade);
    }

    /// <summary>
    /// Scale at which the largest vector of the group reaches (at most) the
    /// target length. Rounding DOWN is what guarantees the "at most": the
    /// longest arrow must not overshoot the room reserved for it.
    /// </summary>
    public static float Fit(float largestMagnitude, float targetPixels)
    {
        if (!float.IsFinite(largestMagnitude) || largestMagnitude <= 0f) return 0f;
        if (!float.IsFinite(targetPixels) || targetPixels <= 0f) return 0f;
        return Quantize(targetPixels / largestMagnitude);
    }

    /// <summary>
    /// Whether the current scale should be refitted. True when the longest
    /// arrow would run past the target, or when it has shrunk so far that the
    /// diagram has become unreadable.
    /// </summary>
    public static bool NeedsRefit(float pixelsPerUnit, float largestMagnitude, float targetPixels, float lowerFraction = 0.3f)
    {
        if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f) return true;
        if (!float.IsFinite(largestMagnitude) || largestMagnitude <= 0f) return false;
        float longest = largestMagnitude * pixelsPerUnit;
        return longest > targetPixels || longest < targetPixels * Math.Clamp(lowerFraction, 0.05f, 0.9f);
    }

    /// <summary>
    /// A round number of units to draw the legend bar with, no longer than
    /// <paramref name="targetPixels"/> at the current scale.
    /// </summary>
    public static float LegendStep(float pixelsPerUnit, float targetPixels)
    {
        if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f) return 0f;
        if (!float.IsFinite(targetPixels) || targetPixels <= 0f) return 0f;
        return Quantize(targetPixels / pixelsPerUnit);
    }
}
