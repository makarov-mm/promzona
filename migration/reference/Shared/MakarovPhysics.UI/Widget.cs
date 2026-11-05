using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// Base widget of the retained-mode tree. Layout is assigned by the parent (Panel),
/// drawing happens in Draw(Draw2D), input in the On* methods (return true when the
/// event is consumed).
/// </summary>
public abstract class Widget
{
    public UiRect Rect; // assigned by the parent during layout
    public bool Enabled = true;

    /// <summary>
    /// Whether the module wants this control on the panel at all.
    ///
    /// Setting it RE-LAYS OUT the panel. It used to be a plain field, and the
    /// stack is laid out once - so hiding a control stopped it being drawn
    /// while everything below kept the position it had been given, leaving a
    /// hole in the middle of the panel. Visible in the shipped build wherever
    /// a preset hides a slider: N-body's per-preset star counts, Rocket's
    /// per-stage rows (07.08).
    /// </summary>
    public bool Visible
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            RequestLayout();
        }
    } = true;

    /// <summary>
    /// A control the simple view leaves out. Marking one costs nothing until
    /// the reader asks for the simple view, and then the panel shows only what
    /// the module's headline claim actually needs.
    ///
    /// Re-lays out for the same reason as <see cref="Visible"/>.
    /// </summary>
    public bool Advanced
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            RequestLayout();
        }
    }

    /// <summary>
    /// Whether the widget takes part in layout, drawing and input at all.
    ///
    /// Two independent things keep a control off the panel: the module hid it
    /// (Visible), and the simple view does not show it (Advanced). Folding
    /// both into the one flag would mean whichever was written last wins, and
    /// a control would quietly come back the moment an unrelated switch moved.
    /// </summary>
    public bool Shown => IsShown(Visible, Advanced, Ui is null || Ui.ExpertMode);

    private static bool _relayouting;

    /// <summary>
    /// Ask for the stack to be measured again, because this widget stopped (or
    /// started) taking part in it.
    ///
    /// Through the context when there is one - that path has the re-entrancy
    /// guard and knows about overlays. Without a context the tree is laid out
    /// from its own root instead: UiContext binds itself to the widgets in
    /// InitGL, which needs a GL context, so a widget hidden before that - or
    /// in a headless SelfTest - would otherwise silently keep its slot. Found
    /// exactly that way (07.08).
    /// </summary>
    protected void RequestLayout()
    {
        if (Ui is not null)
        {
            Ui.Relayout();
            return;
        }

        if (_relayouting) return;
        _relayouting = true;

        try
        {
            Widget top = this;

            while (top.Parent is not null)
            {
                top = top.Parent;
            }

            (top as UiPanel)?.Layout();
        }
        finally
        {
            _relayouting = false;
        }
    }

    /// <summary>The rule on its own, so it can be checked without a UI at all.</summary>
    public static bool IsShown(bool visible, bool advanced, bool expert) => visible && (!advanced || expert);
    public bool Hovered { get; internal set; }
    public UiTheme Theme => Ui?.Theme ?? UiTheme.Dark;
    public UiContext? Ui { get; internal set; }
    public Widget? Parent { get; internal set; }

    /// <summary>Preferred height for width w (used by the stack layout).</summary>
    public virtual float PreferredHeight(float w) => Theme.ControlHeight;

    public abstract void Draw(Draw2D d);
    public virtual bool OnMouseDown(float x, float y, MouseButton b) => false;
    public virtual void OnMouseUp(float x, float y, MouseButton b) { }
    public virtual void OnMouseMove(float x, float y) { }
    public virtual bool OnMouseWheel(int delta) => false;
    public virtual bool OnKey(int vk) => false;
    public virtual Widget? HitTest(float x, float y) => Shown && Enabled && Rect.Contains(x, y) ? this : null;
    internal virtual void SetContext(UiContext ui) => Ui = ui;
}
