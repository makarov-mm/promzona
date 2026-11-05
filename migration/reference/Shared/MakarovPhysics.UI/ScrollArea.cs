namespace MakarovPhysics.UI;

/// <summary>
/// Vertical scroll container: stacks children like UiPanel, clips them with a
/// scissor rectangle and scrolls with the mouse wheel. A slim scrollbar shows
/// the position. Content shorter than the viewport behaves like a plain panel.
/// </summary>
public class ScrollArea : UiPanel
{
    private const float WheelStep = 48f;
    private const float BarWidth = 5f;
    private float _scroll; // content offset, ≥ 0
    private float _contentHeight; // full stacked height
    public bool CanScroll => _contentHeight > Rect.H + 1;

    public override void Layout()
    {
        float pad = Theme.Padding;

        // Two-pass width resolution: the old code sized the content by
        // LAST layout's CanScroll - the first layout after a content change
        // either drew the scrollbar over the controls or kept a stale gutter.
        // Measure with the current guess, and if scrollability flips, measure
        // once more at the corrected width before placing anything.
        float w = MeasureContent(pad, Rect.W - 2 * pad - (CanScroll ? BarWidth + 2 : 0));

        // second pass: place with the scroll offset applied
        float y = Rect.Y + pad - _scroll;

        foreach (Widget c in Children.Where(c => c.Shown))
        {
            float h = c.PreferredHeight(w);
            c.Rect = new UiRect(Rect.X + pad, y, w, h);
            (c as UiPanel)?.Layout();
            y += h + Theme.Spacing;
        }
    }

    /// <summary>Measures _contentHeight at the given width; re-measures once if
    /// the resulting scrollability changes the available width. Returns the
    /// width the children were finally measured at.</summary>
    private float MeasureContent(float pad, float width)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            _contentHeight = pad;

            foreach (Widget c in Children.Where(widget => widget.Shown))
            {
                _contentHeight += c.PreferredHeight(width) + Theme.Spacing;
            }

            _contentHeight += pad - Theme.Spacing;

            float corrected = Rect.W - 2 * pad - (CanScroll ? BarWidth + 2 : 0);
            if (Math.Abs(corrected - width) < 0.5f) break;
            width = corrected; // scrollbar appeared/disappeared - measure again
        }

        ClampScroll();
        return width;
    }

    private void ClampScroll() => _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _contentHeight - Rect.H));

    public override void Draw(Draw2D d)
    {
        if (DrawBackground)
        {
            d.Rect(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBackground);
            d.RectOutline(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBorder);
        }

        d.SetClip(Rect);

        // Draw runs every frame for every panel: no iterator, no delegate.
        foreach (Widget c in Children)
        {
            if (!c.Shown || c.Rect.Bottom <= Rect.Y || c.Rect.Y >= Rect.Bottom) continue;
            c.Draw(d);
        }

        d.SetClip(null);

        if (CanScroll)
        {
            float track = Rect.H - 4;
            float thumb = MathF.Max(24, track * Rect.H / _contentHeight);
            float t = _scroll / (_contentHeight - Rect.H);
            float y = Rect.Y + 2 + t * (track - thumb);
            d.Rect(Rect.Right - BarWidth - 2, Rect.Y + 2, BarWidth, track, Theme.SliderTrack);
            d.Rect(Rect.Right - BarWidth - 2, y, BarWidth, thumb, Theme.ControlActive);
        }
    }

    public override Widget? HitTest(float x, float y)
    {
        // a disabled container disables its whole subtree (the base contract)
        if (!Shown || !Enabled || !Rect.Contains(x, y)) return null;

        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Widget? hit = Children[i].HitTest(x, y);

            // children scrolled out of view must not react
            if (hit is not null && hit.Rect.Y < Rect.Bottom && hit.Rect.Bottom > Rect.Y)
            {
                return hit;
            }
        }

        return this;
    }

    public override bool OnMouseWheel(int delta)
    {
        if (!CanScroll) return false;
        _scroll -= delta * WheelStep;
        ClampScroll();
        Layout();
        return true;
    }
}
