using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// Root of the UI layer: owns the theme, the atlas and the batcher; routes input
/// and draws the widget tree on top of the scene. One per application.
/// </summary>
public sealed class UiContext : IDisposable
{
    public UiTheme Theme => UiTheme.Dark;
    public UiPanel Root { get; } = new() { DrawBackground = false };

    /// <summary>Tool icons, shared by every widget that draws one.</summary>
    public UiIconCache Icons { get; } = new();

    /// <summary>
    /// false switches every panel to the simple view: controls marked Advanced
    /// step out of it entirely, rather than being greyed out. Greying keeps
    /// the clutter and adds a puzzle; leaving them out is what makes a short
    /// panel short.
    ///
    /// Expert by default, so nothing a reader already knows moves on its own.
    /// </summary>
    public bool ExpertMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Root.Layout();
        }
    } = true;

    public Draw2D Draw { get; } = new();
    public FontAtlas? Font { get; private set; }
    public string LanguageCode { get; private set; } = "en";
    private Widget? _captured; // widget that captured the mouse (slider drag)
    private Widget? _hovered;
    private float _w, _h; // logical size
    private int _physW, _physH; // physical pixels

    /// <summary>UI scale (physical px / logical unit); driven by the window DPI.</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>
    /// Overlay widget (dropdown popup, modal dialog). While set, it receives
    /// all input; a click outside it closes it. Rendered on top of Root.
    /// </summary>
    public Widget? Overlay { get; private set; }

    /// <summary>true - dim the screen behind the overlay (modal dialogs).</summary>
    public bool OverlayDim { get; private set; }

    public void ShowOverlay(Widget overlay, bool dim)
    {
        Overlay = overlay;
        OverlayDim = dim;
        overlay.SetContext(this);
        (overlay as UiPanel)?.Layout();
    }

    public void CloseOverlay()
    {
        if (_hovered is not null && IsInOverlay(_hovered))
        {
            _hovered.Hovered = false;
            _hovered = null;
        }

        Overlay = null;
        OverlayDim = false;
    }

    private bool IsInOverlay(Widget w)
    {
        for (Widget? p = w; p is not null; p = p.Parent)
        {
            if (p == Overlay)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Create GL resources (context is current).</summary>
    public void InitGL()
    {
        Font = new FontAtlas("Segoe UI", 14f, Scale, languageCode: LanguageCode);
        Draw.InitGL(Font);
        Root.SetContext(this);
    }

    /// <summary>
    /// Change the UI scale (e.g. on a DPI change). Rebuilds the font atlas at
    /// the new physical size; the GL context must be current.
    /// </summary>
    public void SetScale(float scale)
    {
        scale = Math.Clamp(scale, 0.5f, 4f);
        if (Math.Abs(scale - Scale) < 0.01f) return;

        // Before InitGL there is no atlas yet: just record the scale and let
        // InitGL build the one and only atlas at the right size. (The host
        // calls SetScale(DeviceDpi/96) before InitGL; at 100% DPI the early
        // return above hid the fact that this path dereferenced a null Font -
        // on any 125/150% monitor startup crashed with an NRE, and building
        // an atlas here just to replace it in InitGL leaked its GL texture.)
        if (Font is null)
        {
            Scale = scale;
            return;
        }

        // Transactional: build the NEW atlas first. If that throws, the old
        // atlas is still alive and still set on Draw - the UI keeps rendering
        // at the previous scale instead of pointing at a disposed texture.
        var next = new FontAtlas("Segoe UI", 14f, scale, languageCode: LanguageCode);
        FontAtlas old = Font;
        Font = next;
        Draw.SetAtlas(next);
        Scale = scale; // only after the new atlas is in place
        old.Dispose();
        Resize(_physW, _physH);
    }

    /// <summary>Choose regional CJK font forms. Before InitGL this only records
    /// the code; after InitGL the host must call it with its GL context current.</summary>
    public void SetLanguage(string code)
    {
        code = string.IsNullOrWhiteSpace(code) ? "en" : code;
        if (string.Equals(LanguageCode, code, StringComparison.OrdinalIgnoreCase)) return;
        if (Font is null) { LanguageCode = code; return; }
        var next = new FontAtlas("Segoe UI", 14f, Scale, languageCode: code);
        FontAtlas old = Font;
        Draw.SetAtlas(next);
        Font = next;
        LanguageCode = code;
        old.Dispose();
        Relayout();
    }

    /// <summary>Drop hover/capture/overlay - call when the widget tree is rebuilt.</summary>
    public void ResetInteraction()
    {
        _hovered?.Hovered = false;
        _hovered = null;
        _captured = null;
        Overlay = null;
        OverlayDim = false;
        BlurText(commit: false); // the field may not exist anymore - discard
    }

    /// <summary>The text field currently receiving keyboard input, if any.</summary>
    public TextField? TextFocus { get; private set; }

    /// <summary>Native input must finish/cancel composition before the target
    /// field disappears or another field receives committed characters.</summary>
    public event Action? TextFocusChanging;

    public void FocusText(TextField field)
    {
        if (TextFocus == field) return;
        BlurText(commit: true);
        TextFocus = field;
    }

    public void BlurText(bool commit)
    {
        TextField? f = TextFocus;
        if (f is null) return;
        TextFocusChanging?.Invoke();
        TextFocus = null; // clear FIRST - Committed may rebuild the UI
        f.OnBlur(commit);
    }

    public void Capture(Widget w) => _captured = w;

    public void ReleaseCapture(Widget w)
    {
        if (_captured == w)
        {
            _captured = null;
        }
    }

    public UiContext()
    {
        Root.AutoLayout = false; // top-level panels are positioned manually
    }

    /// <summary>Screen size changed: update the root and re-layout the panels.</summary>
    public event Action<int, int>? Resized;

    /// <summary>Logical width (physical / Scale) - all widget math uses this.</summary>
    public float Width => _w;
    public float Height => _h;

    public void Resize(int physW, int physH)
    {
        _physW = physW; _physH = physH;
        _w = physW / Scale;
        _h = physH / Scale;
        Root.Rect = new UiRect(0, 0, _w, _h);
        Resized?.Invoke((int)_w, (int)_h);
        Root.Layout();
    }

    private bool _laying;

    /// <summary>
    /// Re-layout the tree after the panel set changes.
    ///
    /// Re-entrant calls are dropped: a widget may discover during LAYOUT or
    /// DRAW that it needs a different height (a wrapped label counting its
    /// lines) and ask for another pass. Without the guard that is an infinite
    /// recursion inside a frame; with it, the request is simply satisfied by
    /// the pass already running or by the next one.
    /// </summary>
    public void Relayout()
    {
        if (_laying) return;
        _laying = true;

        try
        {
            Root.Layout();
        }
        finally
        {
            _laying = false;
        }
    }

    /// <summary>Process this frame's input. true - consumed by the UI (do not pass to the scene).</summary>
    public bool ProcessInput(InputState input)
    {
        // the UI lives in logical units; the mouse arrives in physical pixels
        float mx = input.MouseX / Scale, my = input.MouseY / Scale;

        // a focused text field eats the keyboard: characters (with '\b'/'\r'
        // control codes) and Escape. A click outside the field commits first,
        // then falls through so the click still lands where the user aimed.
        if (TextFocus is not null)
        {
            foreach (char c in input.Typed)
            {
                TextFocus?.HandleChar(c); // Enter inside HandleChar blurs
                if (TextFocus is null) break;
            }

            if (TextFocus is not null && !input.ImeOwnsKeyboard && input.WasKeyPressed(0x1B))
            {
                BlurText(commit: false); // Escape reverts the edit
            }

            if (TextFocus is not null && (input.Pressed & MouseButton.Left) != 0)
            {
                // A field can be OWNED by another widget rather than laid out
                // itself - a Slider editing its own value keeps a field the
                // hit test can never return (27.08). Ask the owner instead of
                // comparing identities, or the first click on the slider's own
                // track would commit and blur the edit it just opened.
                Widget? target = (Overlay ?? Root).HitTest(mx, my);
                bool insideOwner = target is not null && ReferenceEquals(TextFocus.Owner, target);
                if (target != TextFocus && !insideOwner) BlurText(commit: true);
            }
        }

        if (_captured is not null) // slider drag etc. - everything goes to that widget
        {
            _captured.OnMouseMove(mx, my);

            if ((input.Released & MouseButton.Left) != 0)
            {
                _captured.OnMouseUp(mx, my, MouseButton.Left);
            }

            return true;
        }

        // an open overlay owns all input; a click outside it closes it
        if (Overlay is not null)
        {
            Widget? overlayHit = Overlay.HitTest(mx, my);
            UpdateHover(overlayHit);
            overlayHit?.OnMouseMove(mx, my);

            if (overlayHit is not null)
            {
                if ((input.Pressed & MouseButton.Left) != 0)
                {
                    overlayHit.OnMouseDown(mx, my, MouseButton.Left);
                }

                if ((input.Released & MouseButton.Left) != 0)
                {
                    overlayHit.OnMouseUp(mx, my, MouseButton.Left);
                }

                if (input.WheelDelta != 0)
                {
                    BubbleWheel(overlayHit, input.WheelDelta);
                }
            }
            else if ((input.Pressed & MouseButton.Left) != 0)
            {
                CloseOverlay();
            }

            return true;
        }

        Widget? hit = Root.HitTest(mx, my);
        UpdateHover(hit);
        hit?.OnMouseMove(mx, my);

        // opaque = a widget itself or a panel with a background; fully covers the scene
        bool overOpaque = 
            hit is not null 
            && 
            hit != Root 
            &&
            (hit is not UiPanel p || p.DrawBackground);

        if (hit is not null && hit != Root)
        {
            if ((input.Pressed & MouseButton.Left) != 0)
            {
                hit.OnMouseDown(mx, my, MouseButton.Left);
            }

            if ((input.Released & MouseButton.Left) != 0)
            {
                hit.OnMouseUp(mx, my, MouseButton.Left);
            }

            if (input.WheelDelta != 0)
            {
                BubbleWheel(hit, input.WheelDelta);
            }
        }

        return overOpaque;
    }

    /// <summary>
    /// Wheel routing. Inside a scrollable ScrollArea the wheel ALWAYS scrolls
    /// the panel - otherwise scrolling a long parameter list would silently
    /// drag whatever slider happens to sit under the cursor (dangerous for a
    /// simulator: mass or damping changes without the user noticing). Sliders
    /// keep their wheel fine-tuning only where nothing needs to scroll.
    /// </summary>
    private static void BubbleWheel(Widget start, int delta)
    {
        for (Widget? w = start; w is not null; w = w.Parent)
        {
            if (w is ScrollArea { CanScroll: true } sa)
            {
                sa.OnMouseWheel(delta);
                return;
            }
        }

        for (Widget? w = start; w is not null; w = w.Parent)
        {
            if (w.OnMouseWheel(delta))
            {
                return;
            }
        }
    }

    private void UpdateHover(Widget? hit)
    {
        if (_hovered == hit) return;
        _hovered?.Hovered = false;
        _hovered = hit;
        _hovered?.Hovered = true;
    }

    /// <summary>Draw the tree (at the end of the frame, on top of the scene).</summary>
    public void Render()
    {
        if (_physW == 0 || _physH == 0) return;
        Draw.BeginFrame(_w, _h, _physW, _physH, Scale);
        Root.Draw(Draw);

        if (Overlay is not null)
        {
            if (OverlayDim)
            {
                Draw.Rect(0, 0, _w, _h, new UiColor(0, 0, 0, 0.45f));
            }

            Overlay.Draw(Draw);
        }

        Draw.Flush();
    }

    public void Dispose()
    {
        // On a machine without OpenGL 3.3 InitGL() never ran, so
        // Font is still null here - this path must shut down cleanly with the
        // "OpenGL 3.3 required" message instead of a NullReferenceException.
        // Also, idempotent: a second Dispose is a no-op.
        Icons.Dispose();
        Draw.Dispose();
        Font?.Dispose();
        Font = null!;
    }
}
