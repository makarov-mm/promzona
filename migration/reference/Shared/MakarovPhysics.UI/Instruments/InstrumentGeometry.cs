namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Pure helpers shared by the instruments: no state, no GL, no widgets, so the
/// parts that decide whether a grab lands and where a tick goes are testable on
/// their own.
/// </summary>
public static class InstrumentGeometry
{
    /// <summary>Distance from a point to a segment (both in logical pixels).</summary>
    public static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float lengthSquared = dx * dx + dy * dy;

        if (!(lengthSquared > 1e-12f))
        {
            float zx = px - ax;
            float zy = py - ay;
            return MathF.Sqrt(zx * zx + zy * zy);
        }

        float t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0f, 1f);
        float cx = px - (ax + dx * t);
        float cy = py - (ay + dy * t);
        return MathF.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>
    /// A round number to divide <paramref name="span"/> by: the 1-2-5 sequence
    /// scaled to the right decade. A ruler that ticked every 0.137 m would be
    /// technically correct and useless, which is the whole point of the rule.
    /// Returns 0 for a span that cannot be divided.
    /// </summary>
    public static double NiceStep(double span, int targetDivisions = 10)
    {
        if (!double.IsFinite(span) || span <= 0 || targetDivisions <= 0) return 0;

        double raw = span / targetDivisions;
        double decade = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        if (!double.IsFinite(decade) || decade <= 0) return 0;

        double normalized = raw / decade;
        double nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10,
        };

        double step = nice * decade;
        return double.IsFinite(step) && step > 0 ? step : 0;
    }

    /// <summary>
    /// Same as NiceStep, then doubled until one step is at least
    /// <paramref name="minimumPixels"/> wide on screen. Under perspective or at
    /// a low zoom the round step can collapse into a gray smear, and this is
    /// what keeps the scale legible instead.
    /// </summary>
    public static double NiceStepAtLeast(double span, double pixelsPerUnit, float minimumPixels, int targetDivisions = 10)
    {
        double step = NiceStep(span, targetDivisions);
        if (step <= 0 || !double.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0) return step;

        // Bounded: 40 doublings cover any span a scene can hold, and the guard
        // means a pathological projection cannot spin here forever.
        for (int i = 0; i < 40 && step * pixelsPerUnit < minimumPixels; i++)
        {
            step *= 2;
        }

        return step;
    }

    /// <summary>
    /// Angle at <paramref name="vertex"/> between two rays, in degrees (0..180).
    /// Both arms are given in plane-local coordinates, so the answer is the
    /// angle in the scene rather than the foreshortened one on screen.
    /// </summary>
    public static float AngleDegrees(
        float vertexU, float vertexV,
        float firstU, float firstV,
        float secondU, float secondV)
    {
        float au = firstU - vertexU, av = firstV - vertexV;
        float bu = secondU - vertexU, bv = secondV - vertexV;

        float lengthA = MathF.Sqrt(au * au + av * av);
        float lengthB = MathF.Sqrt(bu * bu + bv * bv);
        if (!(lengthA > 1e-9f) || !(lengthB > 1e-9f)) return 0f;

        float cosine = Math.Clamp((au * bu + av * bv) / (lengthA * lengthB), -1f, 1f);
        return MathF.Acos(cosine) * 180f / MathF.PI;
    }
}
