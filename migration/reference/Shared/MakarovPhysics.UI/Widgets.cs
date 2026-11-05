namespace MakarovPhysics.UI;

/// <summary>
/// Container. Layout: vertical stack (default), horizontal row (Horizontal = true)
/// or manual (AutoLayout = false, children keep their own Rects; this is how
/// top-level panels are positioned).
///
/// Layout, PreferredHeight and Draw run indexed loops rather than LINQ. An
/// iterator object and a delegate per pass, every frame, per panel, is exactly
/// the steady allocation the batcher was built to avoid.
/// </summary>
public class UiPanel : Widget
{
    public readonly List<Widget> Children = [];
    public bool DrawBackground = true;

    /// <summary>false - children keep their layout untouched (root, manual panels).</summary>
    public bool AutoLayout = true;

    /// <summary>true - children in a left-to-right row (toolbars, button rows).</summary>
    public bool Horizontal;

    /// <summary>Child width in a horizontal row; 0 - split the width evenly.</summary>
    public float ChildWidth;

    public T Add<T>(T child) where T : Widget
    {
        Children.Add(child);
        child.Parent = this;

        if (Ui is not null)
        {
            child.SetContext(Ui);
        }

        return child;
    }

    /// <summary>
    /// Add at a chosen position. The host needs this to decide whether a
    /// control belongs on the panel at all only AFTER the module has built
    /// its own - and still put it above them. Out-of-range indices clamp
    /// rather than throw: a caller that mis-counts gets a control in the
    /// wrong place, not a dead screen.
    /// </summary>
    public T Insert<T>(int index, T child) where T : Widget
    {
        Children.Insert(Math.Clamp(index, 0, Children.Count), child);
        child.Parent = this;

        if (Ui is not null)
        {
            child.SetContext(Ui);
        }

        return child;
    }

    /// <summary>
    /// Does this subtree contain a control the simple view would hide?
    ///
    /// The expert switch is only meaningful when there is something to
    /// switch: a panel with nothing marked Advanced shows the same controls
    /// either way, so the checkbox is a click that changes nothing. Recursive
    /// because modules group controls into rows and sub-panels.
    /// </summary>
    public static bool ContainsAdvanced(Widget widget)
    {
        if (widget.Advanced) return true;

        if (widget is UiPanel panel)
        {
            return panel.Children.Any(ContainsAdvanced);
        }

        return false;
    }

    public void Clear() => Children.Clear();

    internal override void SetContext(UiContext ui)
    {
        base.SetContext(ui);

        foreach (Widget c in Children)
        {
            c.SetContext(ui);
        }
    }

    /// <summary>Lay out children inside Rect (recursive).</summary>
    public virtual void Layout()
    {
        if (!AutoLayout)
        {
            foreach (UiPanel? panel in Children.OfType<UiPanel?>())
            {
                panel?.Layout();
            }

            return;
        }

        float pad = Theme.Padding;

        if (Horizontal)
        {
            int visible = VisibleCount();
            if (visible == 0) return;

            float width = ChildWidth > 0
                ? ChildWidth
                : (Rect.W - 2 * pad - (visible - 1) * Theme.Spacing) / visible;

            float x = Rect.X + pad;
            float height = Rect.H - 2 * pad;

            foreach (Widget widget in Children.Where(widget => widget.Shown))
            {
                widget.Rect = new UiRect(x, Rect.Y + pad, width, height);

                if (widget is UiPanel panel)
                {
                    panel.Layout();
                }

                x += width + Theme.Spacing;
            }
        }
        else
        {
            float y = Rect.Y + pad;
            float width = Rect.W - 2 * pad;

            foreach (Widget widget in Children.Where(c => c.Shown))
            {
                float height = widget.PreferredHeight(width);
                widget.Rect = new UiRect(Rect.X + pad, y, width, height);

                if (widget is UiPanel panel)
                {
                    panel.Layout();
                }

                y += height + Theme.Spacing;
            }
        }
    }

    public override float PreferredHeight(float w)
    {
        if (Horizontal)
        {
            return Theme.ControlHeight + 2 * Theme.Padding;
        }

        float inner = w - 2 * Theme.Padding;

        float sum = Children
            .Where(widget => widget.Shown)
            .Sum(widget => widget.PreferredHeight(inner) + Theme.Spacing);

        float h = Theme.Padding * 2 - Theme.Spacing + sum;

        return Math.Max(h, 0);
    }

    public override void Draw(Draw2D d)
    {
        if (DrawBackground)
        {
            d.Rect(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBackground);
            d.RectOutline(Rect.X, Rect.Y, Rect.W, Rect.H, Theme.PanelBorder);
        }

        foreach (Widget widget in Children.Where(widget => widget.Shown))
        {
            widget.Draw(d);
        }
    }

    /// <summary>Children that take part in layout, drawing and input.</summary>
    private int VisibleCount() => Children.Count(widget => widget.Shown);

    public override Widget? HitTest(float x, float y)
    {
        // a disabled container disables its whole subtree (the base contract)
        if (!Shown || !Enabled || !Rect.Contains(x, y))
        {
            return null;
        }

        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Widget? hit = Children[i].HitTest(x, y);

            if (hit is not null)
            {
                return hit;
            }
        }

        return this;
    }
}
