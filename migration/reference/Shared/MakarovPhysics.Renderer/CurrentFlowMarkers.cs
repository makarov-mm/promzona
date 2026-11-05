namespace MakarovPhysics.Renderer;

/// <summary>
/// Emits the moving charge markers along a path as short dashes, ready for a
/// GL.LINES batch. Dashes rather than dots because the renderer draws lines
/// anyway: no point-size support is needed, and a dash oriented along the wire
/// reads as motion even when it is only a few pixels long.
/// </summary>
public static class CurrentFlowMarkers
{
    /// <summary>Safety bound on one emit call, so a huge path cannot flood the batch.</summary>
    public const int MaxMarkers = 2048;

    /// <summary>
    /// Appends the markers of one path. <paramref name="phase"/> is the
    /// fractional offset of the first marker (see CurrentFlow.Advance), so the
    /// whole train slides forward without the spacing ever changing.
    /// </summary>
    /// <returns>How many markers were emitted.</returns>
    public static int Emit(
        FlowPath path, double phase, float spacing, float dashFraction,
        Vec3 color, List<TrailPoint> into)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(into);

        if (!float.IsFinite(spacing) || spacing <= 0f) return 0;
        if (!double.IsFinite(phase)) phase = 0;
        if (path.TotalLength <= 0f) return 0;

        float dash = spacing * Math.Clamp(dashFraction, 0.05f, 0.9f);
        float half = dash * 0.5f;

        // The train is anchored at the phase offset, so a marker never appears
        // or vanishes at the ends: it slides in from one and out of the other.
        double offset = phase - Math.Floor(phase);
        int emitted = 0;

        for (int i = 0; i < MaxMarkers; i++)
        {
            float center = (float)((i + offset) * spacing);
            if (center > path.TotalLength) break;

            float from = Math.Clamp(center - half, 0f, path.TotalLength);
            float to = Math.Clamp(center + half, 0f, path.TotalLength);
            if (!(to - from > 1e-5f)) continue;

            if (!path.TryPointAt(from, out Vec3 a, out _)) continue;
            if (!path.TryPointAt(to, out Vec3 b, out _)) continue;

            into.Add(new TrailPoint(a, color));
            into.Add(new TrailPoint(b, color));
            emitted++;
        }

        return emitted;
    }
}
