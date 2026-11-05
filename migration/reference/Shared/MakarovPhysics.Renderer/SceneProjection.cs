namespace MakarovPhysics.Renderer;

/// <summary>
/// One frame of camera state in the form an instrument can measure with: maps
/// world points to logical UI coordinates and back. A module rebuilds it every
/// frame from the matrices it is about to render with, so the instruments stay
/// glued to the scene while the camera orbits.
///
/// Coordinates are LOGICAL pixels, not physical ones: the UI layer works in
/// DPI-independent units and the mouse arrives already divided by the scale,
/// so an instrument that measured in physical pixels would silently mismeasure
/// on every 125 or 150 percent monitor.
/// </summary>
public readonly struct SceneProjection
{
    private readonly Mat4 _viewProjection;
    private readonly Mat4 _inverse;

    /// <summary>Render area in physical pixels (the GL viewport).</summary>
    public readonly int ViewportWidth;
    public readonly int ViewportHeight;

    /// <summary>Physical pixels per logical unit (UiContext.Scale).</summary>
    public readonly float UiScale;

    /// <summary>false when the camera is unusable: nothing measurable this frame.</summary>
    public readonly bool IsValid;

    public SceneProjection(Mat4 view, Mat4 projection, int viewportWidth, int viewportHeight, float uiScale)
    {
        _viewProjection = projection * view;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        UiScale = float.IsFinite(uiScale) && uiScale > 0f ? uiScale : 1f;
        bool invertible = _viewProjection.TryInvert(out Mat4 inverse);
        _inverse = invertible ? inverse : Mat4.Identity;
        IsValid = invertible && viewportWidth > 0 && viewportHeight > 0;
    }

    /// <summary>Logical width of the render area.</summary>
    public float LogicalWidth => ViewportWidth / UiScale;

    /// <summary>Logical height of the render area.</summary>
    public float LogicalHeight => ViewportHeight / UiScale;

    /// <summary>
    /// World point to logical UI coordinates. false when the point sits behind
    /// the camera, where the perspective divide would mirror it onto the screen
    /// as a plausible looking but wrong position.
    /// </summary>
    public bool TryWorldToScreen(Vec3 world, out float x, out float y)
    {
        x = 0f;
        y = 0f;

        if (!IsValid || !world.IsFinite) return false;

        Vec3 clip = _viewProjection.TransformPoint(world);

        float w = 
            _viewProjection[3, 0] * world.X + 
            _viewProjection[3, 1] * world.Y + 
            _viewProjection[3, 2] * world.Z +
            _viewProjection[3, 3];

        if (!float.IsFinite(w) || w <= 1e-6f) return false;
        x = (clip.X / w * 0.5f + 0.5f) * LogicalWidth;
        y = (0.5f - clip.Y / w * 0.5f) * LogicalHeight;
        return float.IsFinite(x) && float.IsFinite(y);
    }

    /// <summary>Ray through a logical UI point, pointing into the scene.</summary>
    public bool TryScreenRay(float x, float y, out Vec3 origin, out Vec3 direction)
    {
        origin = Vec3.Zero;
        direction = Vec3.Zero;
        if (!IsValid || !float.IsFinite(x) || !float.IsFinite(y)) return false;

        float ndcX = x / LogicalWidth * 2f - 1f;
        float ndcY = 1f - y / LogicalHeight * 2f;

        // Two points on the same pixel, one per depth plane. This works for an
        // orthographic camera as well, where the two unproject to a segment
        // parallel to the view direction rather than to a fan from an eye.
        if (!TryUnproject(ndcX, ndcY, -1f, out Vec3 near)) return false;
        if (!TryUnproject(ndcX, ndcY, 1f, out Vec3 far)) return false;
        if (!(far - near).TryNormalize(out direction)) return false;

        origin = near;
        return true;
    }

    /// <summary>Logical UI point to the world point it picks on a plane.</summary>
    public bool TryScreenToPlane(float x, float y, in ScenePlane plane, out Vec3 world)
    {
        world = Vec3.Zero;
        if (!plane.IsValid) return false;
        if (!TryScreenRay(x, y, out Vec3 origin, out Vec3 direction)) return false;
        return plane.TryIntersect(origin, direction, out world);
    }

    /// <summary>
    /// The same pick from a PHYSICAL pointer position - what InputState.MouseX
    /// and MouseY hold. The unit is in the name because the two spaces share a
    /// type: a module that handed raw MouseX/MouseY to TryScreenToPlane was
    /// exact at 100 % scaling and painted a scale-factor away from the cursor
    /// at 125–200 % (PointVortices, 27.08 review). unitscheck.py refuses the
    /// raw call; this is the call it accepts.
    /// </summary>
    public bool TryPhysicalToPlane(float px, float py, in ScenePlane plane, out Vec3 world) => TryScreenToPlane(px / UiScale, py / UiScale, plane, out world);

    /// <summary>Ray through a PHYSICAL pointer position (see TryPhysicalToPlane).</summary>
    public bool TryPhysicalRay(float px, float py, out Vec3 origin, out Vec3 direction) => TryScreenRay(px / UiScale, py / UiScale, out origin, out direction);

    /// <summary>
    /// Logical pixels spanned by one world unit at <paramref name="world"/>
    /// along <paramref name="direction"/>. Rulers size their tick spacing with
    /// it so the scale stays readable at any zoom, including under perspective
    /// where the far end of the same ruler is visibly finer than the near end.
    /// Returns 0 when the point cannot be measured.
    /// </summary>
    public float PixelsPerUnit(Vec3 world, Vec3 direction)
    {
        if (!direction.TryNormalize(out Vec3 unit)) return 0f;

        // A short baseline keeps the estimate local without falling into
        // float cancellation.
        const float baseline = 0.01f;
        if (!TryWorldToScreen(world, out float x0, out float y0)) return 0f;
        if (!TryWorldToScreen(world + unit * baseline, out float x1, out float y1)) return 0f;

        float dx = x1 - x0;
        float dy = y1 - y0;
        float pixels = MathF.Sqrt(dx * dx + dy * dy) / baseline;
        return float.IsFinite(pixels) ? pixels : 0f;
    }

    private bool TryUnproject(float ndcX, float ndcY, float ndcZ, out Vec3 world)
    {
        world = Vec3.Zero;

        Vec3 point = _inverse.TransformPoint(new Vec3(ndcX, ndcY, ndcZ));
        float w = _inverse[3, 0] * ndcX
                + _inverse[3, 1] * ndcY
                + _inverse[3, 2] * ndcZ
                + _inverse[3, 3];

        if (!float.IsFinite(w) || MathF.Abs(w) < 1e-9f) return false;
        world = point / w;
        return world.IsFinite;
    }
}
