namespace MakarovPhysics.Renderer;

/// <summary>
/// How a field magnitude becomes an arrow length.
///
/// The naive answer, length proportional to magnitude, does not survive
/// contact with a real field: next to a point source the value diverges, and
/// two cells away it is already three orders of magnitude smaller. Scaled to
/// fit the strong end, the whole far field collapses into invisible dots;
/// scaled to fit the far field, the near arrows cover the scene. Both are the
/// same failure, a linear map applied to a quantity that is not linear.
///
/// So the length saturates instead: it grows monotonically with the magnitude,
/// approaches a bound and never passes it. Direction survives everywhere,
/// because an arrow never shrinks below a floor: knowing which way the field
/// points at a place where it is weak is still knowing something, and a
/// vanished arrow says nothing at all. Magnitude is then read from length AND
/// color together, which is what makes both ends of the range legible at once.
/// </summary>
public static class FieldArrowScale
{
    /// <summary>Shortest an arrow may get, as a fraction of the full length.</summary>
    public const float DefaultMinimumFraction = 0.18f;

    /// <summary>
    /// Saturating map of a magnitude onto 0..1, where the reference magnitude
    /// lands exactly at 0.5. Non-finite and non-positive input reads as no
    /// field, an infinite one as the top of the scale (a sample that landed
    /// on a singularity is as strong as the picture can show).
    /// </summary>
    public static float Normalize(float magnitude, float reference)
    {
        if (float.IsNaN(magnitude) || magnitude <= 0f) return 0f;
        if (float.IsPositiveInfinity(magnitude)) return 1f;
        if (!float.IsFinite(reference) || reference <= 0f) return 1f;

        return magnitude / (magnitude + reference);
    }

    /// <summary>
    /// Arrow length for a magnitude. Zero (no arrow at all) when there is no
    /// field to point along, otherwise between minimumFraction and the whole
    /// of maxLength.
    /// </summary>
    public static float Length(
        float magnitude, float reference, float maxLength,
        float minimumFraction = DefaultMinimumFraction)
    {
        if (!float.IsFinite(maxLength) || maxLength <= 0f) return 0f;
        if (float.IsNaN(magnitude) || magnitude <= 0f) return 0f;

        minimumFraction = Math.Clamp(minimumFraction, 0f, 1f);
        float t = Normalize(magnitude, reference);
        return maxLength * (minimumFraction + (1f - minimumFraction) * t);
    }

    /// <summary>
    /// Median of the sampled magnitudes, used as the automatic reference.
    /// The median rather than the mean or the maximum: one lattice point
    /// landing next to a point charge samples a value orders of magnitude
    /// above the rest, and either of those would let that single sample set
    /// the scale for the whole picture, leaving one bright arrow in a field
    /// of stubs.
    ///
    /// Sorts the span in place; callers pass a scratch buffer. Zeros and
    /// non-finite samples are dropped first, so a mostly empty region still
    /// scales by the part of it that has a field.
    /// </summary>
    public static float Median(Span<float> values)
    {
        int count = 0;
        for (int i = 0; i < values.Length; i++)
        {
            float value = values[i];
            if (float.IsFinite(value) && value > 0f) values[count++] = value;
        }

        if (count == 0) return 0f;

        Span<float> usable = values[..count];
        usable.Sort();

        return count % 2 == 1
            ? usable[count / 2]
            : 0.5f * (usable[count / 2 - 1] + usable[count / 2]);
    }
}
