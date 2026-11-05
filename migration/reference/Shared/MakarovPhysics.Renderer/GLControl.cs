using System.ComponentModel;

namespace MakarovPhysics.Renderer;

public class GLControl : Control
{
    private GLContext? _ctx;
    private bool _animating;
    private bool _rendering;
    private bool _inIdle;

    /// <summary>Context is created and current - build GL resources here (shaders, meshes).</summary>
    public event Action<GLControl>? ContextReady;

    /// <summary>
    /// The GPU/driver cannot provide OpenGL 3.3 core. Raised INSTEAD of
    /// ContextReady, before any modern entry points are loaded - the host
    /// shows a message and closes. Without a handler an exception is thrown.
    /// </summary>
    public event Action<GLControl>? ContextFailed;

    /// <summary>Fired once when the context is LOST at runtime (device
    /// removal, display change, driver reset): wglMakeCurrent or SwapBuffers
    /// started failing. The control stops its render loop and refuses further
    /// frames; the host should stop touching GL resources and shut the window
    /// down or rebuild the whole GL graph (calling GL entry
    /// points without a current context risks driver access violations, not
    /// just an invisible frame).</summary>
    public event Action<GLControl, string>? ContextLost;

    /// <summary>True after a runtime context loss - the control is inert.</summary>
    public bool IsContextLost { get; private set; }

    /// <summary>Raised every frame. The context has already been made current.</summary>
    public event Action<GLControl>? Render;

    private bool _failed;

    // Runtime-only properties: hidden from the WinForms designer serializer (WFO1000)
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GLContext? Context => _ctx;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Major { get; init; } = 3;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minor { get; init; } = 3;

    public GLControl()
    {
        // GL fully owns rendering of the window content
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= 0x20; // CS_OWNDC - a dedicated DC per window (required for GL)
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (DesignMode) return;
        _ctx = new GLContext(Handle, Major, Minor);

        if (!_ctx.MakeCurrent())
        {
            // the context was just created and verified current inside the
            // GLContext constructor - a failure HERE means the environment is
            // already broken; treat it like a failed context, not a crash
            _failed = true;

            if (ContextFailed is { } failedNow)
            {
                failedNow(this);
            }
            else
            {
                throw new InvalidOperationException("wglMakeCurrent failed right after context creation.");
            }

            return;
        }

        // check BEFORE GL.Init(): on a legacy context loading the modern entry
        // points would throw an obscure exception instead of our clear message
        if (_ctx.IsLegacy)
        {
            _failed = true;

            if (ContextFailed is { } failed)
            {
                failed(this);
            }
            else
            {
                throw new InvalidOperationException("OpenGL 3.3 core profile is required.");
            }

            return;
        }

        GL.Init();

        // The context asks for a 4x multisampled pixel format, but nothing ever
        // switched multisample rasterisation on. It defaults to enabled in the
        // core profile, yet several drivers only honour it after an explicit
        // call - and it costs one line to stop depending on that.
        if (_ctx.MsaaSamples > 1)
        {
            GL.Enable(GL.MULTISAMPLE);
        }

        ContextReady?.Invoke(this);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            ReleaseContext();
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // HandleDestroyed is not an ownership guarantee: another native
        // window procedure can prevent that notification from reaching us.
        // Also unsubscribe Idle when Start preceded creation of any handle.
        if (disposing) ReleaseContext();
        base.Dispose(disposing);
    }

    private void ReleaseContext()
    {
        Stop();
        _ctx?.Dispose();
        // A failed native deletion must retain ownership for a retry.
        _ctx = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (DesignMode)
        {
            e.Graphics.Clear(BackColor); 
            return;
        }

        DoRender();
    }

    // Never clear the background - that would cause flicker
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (DesignMode) base.OnPaintBackground(e);
    }

    /// <summary>Render a single frame.</summary>
    public void DoRender()
    {
        var context = _ctx;
        if (_rendering || context is null || !CanRender(context)) return;
        _rendering = true;

        try
        {
            if (!context.MakeCurrent())
            {
                FailContextAtRuntime($"wglMakeCurrent failed (Win32 error {context.LastNativeError})");
                return;
            }

            Render?.Invoke(this);

            // A callback can close the window or recreate its handle. Never
            // present through the old context after returning from user code.
            if (!CanRender(context)) return;
            if (!context.SwapBuffers())
            {
                FailContextAtRuntime($"SwapBuffers failed (Win32 error {context.LastNativeError})");
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private bool CanRender(GLContext context) =>
        ReferenceEquals(_ctx, context) 
        && 
        !_failed
        && 
        !IsContextLost
        && 
        IsHandleCreated 
        && 
        !IsDisposed 
        && 
        !Disposing;

    /// <summary>Terminal runtime fault: stop the loop, mark the control inert
    /// and tell the host exactly once.</summary>
    private void FailContextAtRuntime(string reason)
    {
        if (IsContextLost) return;
        IsContextLost = true;
        Stop();
        ContextLost?.Invoke(this, reason);
    }

    /// <summary>Start the continuous render loop (via Application.Idle).</summary>
    public void Start()
    {
        if (_animating || IsDisposed || Disposing || _failed || IsContextLost) return;
        _animating = true;
        Application.Idle += OnIdle;
    }

    public void Stop()
    {
        if (!_animating) return;
        _animating = false;
        Application.Idle -= OnIdle;
    }

    // fallback frame limiter for the no-VSync case (~120 fps target)
    private readonly System.Diagnostics.Stopwatch _frameClock = System.Diagnostics.Stopwatch.StartNew();
    private const double TargetFrameMs = 1000.0 / 120.0;

    private void OnIdle(object? sender, EventArgs e)
    {
        // Modal dialogs can pump another Idle/paint event inside Render or a
        // context-loss handler. One control must have only one active frame.
        if (_inIdle || _rendering) return;
        _inIdle = true;

        try
        {
            while (_animating && Native.AppStillIdle())
            {
                var context = _ctx;
                if (context is null || !CanRender(context)) break;
                _frameClock.Restart();
                DoRender();

                // ContextLost synchronously closes HostWindow and clears _ctx.
                // This check must follow DoRender, before the frame limiter.
                if (!_animating || !CanRender(context)) break;

                // No VSync: throttle without burning a full CPU core.
                if (!context.VSyncEnabled)
                {
                    double left = TargetFrameMs - _frameClock.Elapsed.TotalMilliseconds;

                    if (left > 2)
                    {
                        Thread.Sleep((int)(left - 1));
                    }

                    while (_frameClock.Elapsed.TotalMilliseconds < TargetFrameMs)
                    {
                        Thread.SpinWait(64);
                    }
                }
            }
        }
        finally
        {
            _inIdle = false;
        }
    }
}
