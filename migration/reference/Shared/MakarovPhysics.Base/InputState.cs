namespace MakarovPhysics.Base;

/// <summary>
/// Per-frame input snapshot. The host translates WinForms events into it,
/// so modules and the UI layer never depend on WinForms.
/// </summary>
public sealed class InputState
{
    public int MouseX { get; set; }
    public int MouseY { get; set; }
    public int MouseDx { get; set; }
    public int MouseDy { get; set; }
    public MouseButton Buttons { get; set; }
    public MouseButton Pressed { get; set; } // pressed this frame
    public MouseButton Released { get; set; } // released this frame
    public int WheelDelta { get; set; } // in wheel notches (±1, ±2...)

    /// <summary>true - this frame's input was consumed by the UI layer (scene must ignore it).</summary>
    public bool ConsumedByUi { get; set; }

    /// <summary>true - a UI overlay (popup/dialog) is open; screens skip hotkeys too.</summary>
    public bool ModalOpen { get; set; }

    /// <summary>A dialog, text editor or native IME owns keys. Merely hovering
    /// a panel consumes pointer events, not the flight/drive keyboard.</summary>
    public bool SceneKeyboardBlocked => ModalOpen || ImeOwnsKeyboard;

    /// <summary>The native Windows IME owns navigation/confirmation keys while
    /// composing, including the frame in which it commits or cancels.</summary>
    public bool IsImeComposing { get; private set; }
    public bool ImeHandledThisFrame { get; private set; }
    public bool ImeOwnsKeyboard => IsImeComposing || ImeHandledThisFrame;

    public void BeginImeComposition()
    {
        IsImeComposing = true;
        ImeHandledThisFrame = true;
        // The keystroke starting composition may have arrived before WM_IME_STARTCOMPOSITION.
        _keysDown.Clear();
        _keysPressed.Clear();
        _typed.RemoveAll(char.IsControl);
    }

    public void EndImeComposition()
    {
        IsImeComposing = false;
        ImeHandledThisFrame = true;
        _keysDown.Clear();
        _keysPressed.Clear();
    }

    private readonly HashSet<int> _keysDown = [];
    private readonly HashSet<int> _keysPressed = [];
    public bool IsKeyDown(int vk) => _keysDown.Contains(vk);
    public bool WasKeyPressed(int vk) => _keysPressed.Contains(vk);

    public void KeyDown(int vk)
    {
        if (ImeOwnsKeyboard || vk == 0xE5) return; // VK_PROCESSKEY belongs to the IME
        if (_keysDown.Add(vk))
        {
            _keysPressed.Add(vk);
        }
    }

    public void KeyUp(int vk) => _keysDown.Remove(vk);

    /// <summary>
    /// Characters typed this frame (host KeyPress events, auto-repeat included).
    /// Control characters pass through too: '\b' = backspace, '\r' = enter -
    /// text fields consume them from this stream instead of tracking VK codes.
    /// </summary>
    public IReadOnlyList<char> Typed => _typed;

    private readonly List<char> _typed = [];
    public void TypeChar(char c)
    {
        // Default IME processing emits committed Unicode through WM_CHAR.
        // Keep those characters, but never interpret its Enter/Backspace as UI commands.
        if (!ImeOwnsKeyboard || !char.IsControl(c)) _typed.Add(c);
    }

    /// <summary>Reset one-shot fields - called by the host at the end of a frame.</summary>
    public void EndFrame()
    {
        MouseDx = MouseDy = 0;
        WheelDelta = 0;
        Pressed = Released = MouseButton.None;
        _keysPressed.Clear();
        _typed.Clear();
        ImeHandledThisFrame = false;
    }

    /// <summary>
    /// Forget EVERYTHING that is held down. The host calls this when the window
    /// loses focus or mouse capture: the matching KeyUp/MouseUp will never
    /// arrive, and a key or button stuck "down" would keep steering a camera
    /// or dragging a slider until the user presses it again.
    /// </summary>
    public void ResetAll()
    {
        EndFrame();
        Buttons = MouseButton.None;
        _keysDown.Clear();
        IsImeComposing = ImeHandledThisFrame = false;
    }
}
