namespace MakarovPhysics.UI;

/// <summary>
/// A library of pictograms drawn from primitives.
///
/// Wrecksmith puts a 64-pixel PNG on each tool tile and loads it from an
/// Icons folder. That is the right answer for a game which ships assets; it
/// is the wrong one here. This suite ships no image files at all, on purpose:
/// every one of its eighty-five module previews is rendered procedurally, and
/// every sound is synthesised rather than played back. An icon set as code
/// keeps that, and gets three things for free: it is sharp at any DPI because
/// it is drawn at the size asked for rather than resampled, it recolors with
/// the theme, and it cannot go missing at runtime.
///
/// The style is stroked line art, since the batcher fills rectangles and
/// draws lines and has no filled polygon. That constraint is visible in the
/// result and consistent across the set, which is better than fighting it.
/// </summary>
public static class UiIcons
{
    /// <summary>Ring approximated by segments; the batcher has no circle.</summary>
    private static void Ring(Draw2D d, float cx, float cy, float r, UiColor c, float t = 1.4f, int segments = 14)
    {
        float previousX = cx + r, previousY = cy;

        for (int i = 1; i <= segments; i++)
        {
            float a = MathF.Tau * i / segments;
            float px = cx + MathF.Cos(a) * r, py = cy + MathF.Sin(a) * r;
            d.Line(previousX, previousY, px, py, c, t);
            previousX = px;
            previousY = py;
        }
    }

    private static void Triangle(Draw2D d, float x0, float y0, float x1, float y1, float x2, float y2, UiColor c, float t = 1.4f)
    {
        d.Line(x0, y0, x1, y1, c, t);
        d.Line(x1, y1, x2, y2, c, t);
        d.Line(x2, y2, x0, y0, c, t);
    }

    public static void Sphere(Draw2D d, float x, float y, float s, UiColor c)
    {
        Ring(d, x + s * 0.5f, y + s * 0.5f, s * 0.34f, c);
        // a highlight arc, so a sphere does not read as a flat circle
        Ring(d, x + s * 0.42f, y + s * 0.42f, s * 0.12f, c.WithAlpha(0.5f), 1f, 8);
    }

    public static void Box(Draw2D d, float x, float y, float s, UiColor c)
    {
        float m = s * 0.20f, w = s * 0.44f;
        d.RectOutline(x + m, y + m + s * 0.14f, w, w, c);
        // the top and side of a cube, drawn as an offset lid
        d.Line(x + m, y + m + s * 0.14f, x + m + s * 0.16f, y + m, c);
        d.Line(x + m + w, y + m + s * 0.14f, x + m + w + s * 0.16f, y + m, c);
        d.Line(x + m + s * 0.16f, y + m, x + m + w + s * 0.16f, y + m, c);
        d.Line(x + m + w + s * 0.16f, y + m, x + m + w + s * 0.16f, y + m + w, c);
        d.Line(x + m + w, y + m + s * 0.14f + w, x + m + w + s * 0.16f, y + m + w, c);
    }

    public static void Capsule(Draw2D d, float x, float y, float s, UiColor c)
    {
        float cx = x + s * 0.5f;
        float top = y + s * 0.26f, bottom = y + s * 0.74f, r = s * 0.14f;
        d.Line(cx - r, top, cx - r, bottom, c, 1.4f);
        d.Line(cx + r, top, cx + r, bottom, c, 1.4f);
        Ring(d, cx, top, r, c, 1.4f, 8);
        Ring(d, cx, bottom, r, c, 1.4f, 8);
    }

    public static void Flame(Draw2D d, float x, float y, float s, UiColor c)
    {
        float cx = x + s * 0.5f;
        d.Line(cx, y + s * 0.20f, cx + s * 0.16f, y + s * 0.50f, c, 1.4f);
        d.Line(cx + s * 0.16f, y + s * 0.50f, cx + s * 0.08f, y + s * 0.76f, c, 1.4f);
        d.Line(cx + s * 0.08f, y + s * 0.76f, cx - s * 0.10f, y + s * 0.76f, c, 1.4f);
        d.Line(cx - s * 0.10f, y + s * 0.76f, cx - s * 0.17f, y + s * 0.48f, c, 1.4f);
        d.Line(cx - s * 0.17f, y + s * 0.48f, cx, y + s * 0.20f, c, 1.4f);
        d.Line(cx, y + s * 0.46f, cx + s * 0.06f, y + s * 0.66f, c.WithAlpha(0.55f), 1f);
    }

    public static void Burst(Draw2D d, float x, float y, float s, UiColor c)
    {
        float cx = x + s * 0.5f, cy = y + s * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            float a = MathF.Tau * i / 8;
            float inner = s * (i % 2 == 0 ? 0.16f : 0.12f);
            float outer = s * (i % 2 == 0 ? 0.38f : 0.28f);

            d.Line(
                cx + MathF.Cos(a) * inner,
                cy + MathF.Sin(a) * inner,
                cx + MathF.Cos(a) * outer,
                cy + MathF.Sin(a) * outer,
                c, 1.4f);
        }

        Ring(d, cx, cy, s * 0.12f, c, 1.2f, 10);
    }

    public static void Shoot(Draw2D d, float x, float y, float s, UiColor c)
    {
        float cy = y + s * 0.5f;
        d.Line(x + s * 0.18f, cy, x + s * 0.72f, cy, c, 1.6f);
        d.Line(x + s * 0.72f, cy, x + s * 0.58f, cy - s * 0.10f, c, 1.6f);
        d.Line(x + s * 0.72f, cy, x + s * 0.58f, cy + s * 0.10f, c, 1.6f);
        Ring(d, x + s * 0.22f, cy, s * 0.07f, c, 1.2f, 8);
    }

    public static void Vehicle(Draw2D d, float x, float y, float s, UiColor c)
    {
        d.Line(x + s * 0.18f, y + s * 0.56f, x + s * 0.30f, y + s * 0.38f, c, 1.4f);
        d.Line(x + s * 0.30f, y + s * 0.38f, x + s * 0.62f, y + s * 0.38f, c, 1.4f);
        d.Line(x + s * 0.62f, y + s * 0.38f, x + s * 0.78f, y + s * 0.56f, c, 1.4f);
        d.Line(x + s * 0.18f, y + s * 0.56f, x + s * 0.78f, y + s * 0.56f, c, 1.4f);
        Ring(d, x + s * 0.32f, y + s * 0.64f, s * 0.09f, c, 1.3f, 10);
        Ring(d, x + s * 0.66f, y + s * 0.64f, s * 0.09f, c, 1.3f, 10);
    }

    public static void Barrel(Draw2D d, float x, float y, float s, UiColor c)
    {
        float left = x + s * 0.32f, right = x + s * 0.68f;
        float top = y + s * 0.24f, bottom = y + s * 0.76f;
        d.Line(left, top, left, bottom, c, 1.4f);
        d.Line(right, top, right, bottom, c, 1.4f);
        Ring(d, (left + right) * 0.5f, top, (right - left) * 0.5f, c, 1.2f, 10);
        d.Line(left, (top + bottom) * 0.5f, right, (top + bottom) * 0.5f, c.WithAlpha(0.6f), 1f);
        Ring(d, (left + right) * 0.5f, bottom, (right - left) * 0.5f, c, 1.2f, 10);
    }

    public static void Bridge(Draw2D d, float x, float y, float s, UiColor c)
    {
        float baseY = y + s * 0.66f;
        d.Line(x + s * 0.14f, baseY, x + s * 0.86f, baseY, c, 1.5f);

        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            float px = x + s * (0.18f + 0.64f * t);
            float arc = MathF.Sin(t * MathF.PI) * s * 0.26f;
            d.Line(px, baseY, px, baseY - arc, c.WithAlpha(0.75f), 1f);
        }

        Triangle(d, x + s * 0.18f, baseY, x + s * 0.5f, baseY - s * 0.28f, x + s * 0.82f, baseY, c);
    }

    public static void Gas(Draw2D d, float x, float y, float s, UiColor c)
    {
        for (int i = 0; i < 6; i++)
        {
            float a = MathF.Tau * i / 6 + 0.4f;
            Ring(d, x + s * (0.5f + 0.22f * MathF.Cos(a)), y + s * (0.5f + 0.22f * MathF.Sin(a)), s * 0.07f, c.WithAlpha(0.85f), 1f, 8);
        }

        Ring(d, x + s * 0.5f, y + s * 0.5f, s * 0.09f, c, 1.2f, 10);
    }

    public static void Water(Draw2D d, float x, float y, float s, UiColor c)
    {
        for (int row = 0; row < 3; row++)
        {
            float py = y + s * (0.36f + row * 0.16f);
            float previousX = x + s * 0.16f, previousY = py;

            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                float px = x + s * (0.16f + 0.68f * t);
                float wave = py + MathF.Sin(t * MathF.Tau + row) * s * 0.05f;
                d.Line(previousX, previousY, px, wave, c.WithAlpha(0.9f - row * 0.2f), 1.2f);
                previousX = px;
                previousY = wave;
            }
        }
    }

    public static void Gravity(Draw2D d, float x, float y, float s, UiColor c)
    {
        float cx = x + s * 0.5f;
        d.Line(cx, y + s * 0.20f, cx, y + s * 0.62f, c, 1.6f);
        d.Line(cx, y + s * 0.62f, cx - s * 0.10f, y + s * 0.48f, c, 1.6f);
        d.Line(cx, y + s * 0.62f, cx + s * 0.10f, y + s * 0.48f, c, 1.6f);
        d.Line(x + s * 0.22f, y + s * 0.76f, x + s * 0.78f, y + s * 0.76f, c.WithAlpha(0.7f), 1.4f);
    }

    public static void Wrecking(Draw2D d, float x, float y, float s, UiColor c)
    {
        d.Line(x + s * 0.22f, y + s * 0.18f, x + s * 0.62f, y + s * 0.18f, c, 1.4f);
        d.Line(x + s * 0.62f, y + s * 0.18f, x + s * 0.62f, y + s * 0.46f, c.WithAlpha(0.8f), 1.2f);
        Ring(d, x + s * 0.62f, y + s * 0.60f, s * 0.14f, c, 1.5f);
        d.Line(x + s * 0.22f, y + s * 0.18f, x + s * 0.22f, y + s * 0.80f, c.WithAlpha(0.7f), 1.2f);
    }

    public static void Target(Draw2D d, float x, float y, float s, UiColor c)
    {
        float cx = x + s * 0.5f, cy = y + s * 0.5f;
        Ring(d, cx, cy, s * 0.34f, c, 1.4f);
        Ring(d, cx, cy, s * 0.20f, c.WithAlpha(0.8f), 1.2f);
        Ring(d, cx, cy, s * 0.07f, c, 1.2f, 8);
    }

    public static void Spring(Draw2D d, float x, float y, float s, UiColor c)
    {
        float previousX = x + s * 0.5f, previousY = y + s * 0.16f;

        for (int i = 1; i <= 12; i++)
        {
            float t = i / 12f;
            float px = x + s * (0.5f + (i % 2 == 0 ? 0.18f : -0.18f));
            float py = y + s * (0.16f + 0.62f * t);
            d.Line(previousX, previousY, px, py, c, 1.4f);
            previousX = px;
            previousY = py;
        }

        d.Line(previousX, previousY, x + s * 0.5f, y + s * 0.86f, c, 1.4f);
    }
}
