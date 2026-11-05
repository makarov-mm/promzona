using System.Runtime.InteropServices;
using static System.Diagnostics.Debug;

namespace MakarovPhysics.Renderer;

/// <summary>Owns a Windows OpenGL rendering context and the device context of its window.</summary>
/// <remarks>
/// Requires unsafe code and the project's Native and PIXELFORMATDESCRIPTOR definitions.
/// Construction leaves this context current. Call GL.Init() afterwards to load rendering functions.
/// Dispose on the thread that constructed this instance, before destroying the window.
/// Calls must be serialized; context transfers between threads require the previous thread to unbind it first.
/// </remarks>
public sealed unsafe class GLContext : IDisposable
{
    /// <summary>The window that owns the device context; this class does not destroy that window.</summary>
    private readonly IntPtr _hwnd;

    /// <summary>The managed thread that acquired the window DC and must release it.</summary>
    private readonly int _ownerThreadId;

    /// <summary>The acquired window device context, or zero after release.</summary>
    private IntPtr _hdc;

    /// <summary>The adopted rendering context, including the legacy context during initialization.</summary>
    private IntPtr _hglrc;

    /// <summary>A core context awaiting activation or cleanup during construction.</summary>
    private IntPtr _pendingContext;

    /// <summary>The hidden window used to discover WGL pixel format extensions.</summary>
    private IntPtr _bootstrapWindow;

    /// <summary>The device context acquired for the hidden bootstrap window.</summary>
    private IntPtr _bootstrapDc;

    /// <summary>The temporary legacy context used to discover WGL extensions.</summary>
    private IntPtr _bootstrapContext;

    /// <summary>Whether all owned resources have been released successfully.</summary>
    private bool _disposed;

    /// <summary>Whether the requested core context was unavailable and a legacy context was retained.</summary>
    /// <remarks>This reports fallback, not the actual GL version. The host must reject fallback if its shaders require the requested core profile.</remarks>
    public bool IsLegacy { get; private set; }

    /// <summary>The driver-reported MSAA sample count; zero means no sample buffer or an unavailable query.</summary>
    /// <remarks>The selected format may provide more samples than requested. An existing window pixel format is reused unchanged.</remarks>
    public int MsaaSamples { get; private set; }

    /// <summary>Whether wglSwapIntervalEXT accepted a swap interval of one.</summary>
    /// <remarks>Driver overrides can still affect presentation. If false, the render loop should provide its own frame pacing.</remarks>
    public bool VSyncEnabled { get; private set; }

    /// <summary>Win32 error from the last failed binding or buffer swap; zero on success.</summary>
    public int LastNativeError { get; private set; }

    /// <summary>Context attribute specifying the requested OpenGL major version.</summary>
    private const int WGL_CONTEXT_MAJOR_VERSION_ARB = 0x2091;
    /// <summary>Context attribute specifying the requested OpenGL minor version.</summary>
    private const int WGL_CONTEXT_MINOR_VERSION_ARB = 0x2092;
    /// <summary>Context attribute containing creation flags.</summary>
    private const int WGL_CONTEXT_FLAGS_ARB = 0x2094;
    /// <summary>Context attribute selecting the OpenGL profile.</summary>
    private const int WGL_CONTEXT_PROFILE_MASK_ARB = 0x9126;
    /// <summary>Selects the OpenGL core profile, available from OpenGL 3.2 onward.</summary>
    private const int WGL_CONTEXT_CORE_PROFILE_BIT_ARB = 0x00000001;
    /// <summary>Requests a forward-compatible context that excludes deprecated functionality.</summary>
    private const int WGL_CONTEXT_FORWARD_COMPATIBLE_BIT_ARB = 0x00000002;

    /// <summary>Pixel format attribute requiring support for rendering to a window.</summary>
    private const int WGL_DRAW_TO_WINDOW_ARB = 0x2001;
    /// <summary>Pixel format attribute requiring OpenGL support.</summary>
    private const int WGL_SUPPORT_OPENGL_ARB = 0x2010;
    /// <summary>Pixel format attribute requiring a front and back color buffer.</summary>
    private const int WGL_DOUBLE_BUFFER_ARB = 0x2011;
    /// <summary>Pixel format attribute selecting the color representation.</summary>
    private const int WGL_PIXEL_TYPE_ARB = 0x2013;
    /// <summary>Pixel format attribute specifying the requested number of color bits.</summary>
    private const int WGL_COLOR_BITS_ARB = 0x2014;
    /// <summary>Pixel format attribute specifying the requested depth precision in bits.</summary>
    private const int WGL_DEPTH_BITS_ARB = 0x2022;
    /// <summary>Pixel format attribute specifying the requested stencil precision in bits.</summary>
    private const int WGL_STENCIL_BITS_ARB = 0x2023;
    /// <summary>Selects RGBA pixels rather than color-index pixels.</summary>
    private const int WGL_TYPE_RGBA_ARB = 0x202B;
    /// <summary>Pixel format attribute specifying whether a multisample buffer is present.</summary>
    private const int WGL_SAMPLE_BUFFERS_ARB = 0x2041;
    /// <summary>Pixel format attribute specifying or querying samples per pixel.</summary>
    private const int WGL_SAMPLES_ARB = 0x2042;
    /// <summary>Creates a top-level popup window; without WS_VISIBLE it remains hidden.</summary>
    private const uint WS_POPUP = 0x80000000;

    /// <summary>Returns an extension function address for the current OpenGL context.</summary>
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr wglGetProcAddress(string name);

    /// <summary>Returns the rendering context current on the calling thread, or zero.</summary>
    [DllImport("opengl32.dll", ExactSpelling = true)]
    private static extern IntPtr wglGetCurrentContext();

    /// <summary>Returns the device context associated with the calling thread's current rendering context.</summary>
    [DllImport("opengl32.dll", ExactSpelling = true)]
    private static extern IntPtr wglGetCurrentDC();

    /// <summary>Returns a DC's assigned pixel format index, or zero when no format is assigned.</summary>
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetPixelFormat(IntPtr hdc);

    /// <summary>Creates a context for an existing window, preferring the requested core profile and MSAA.</summary>
    /// <param name="hwnd">A valid, nonzero window handle that remains alive until this instance is disposed.</param>
    /// <param name="major">Requested OpenGL major version; the version must be at least 3.2.</param>
    /// <param name="minor">Requested nonnegative OpenGL minor version.</param>
    /// <param name="samples">Requested minimum MSAA samples; zero or one skips the MSAA format request.</param>
    /// <remarks>A new pixel format cannot be changed once assigned to the window, even if later initialization fails.</remarks>
    public GLContext(IntPtr hwnd, int major = 3, int minor = 3, int samples = 4)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("A nonzero window handle is required.", nameof(hwnd));
        if (major < 3) throw new ArgumentOutOfRangeException(nameof(major), "A core profile requires OpenGL 3.2 or later.");
        if (minor < 0 || (major == 3 && minor < 2)) throw new ArgumentOutOfRangeException(nameof(minor), "A core profile requires OpenGL 3.2 or later.");
        if (samples < 0) throw new ArgumentOutOfRangeException(nameof(samples));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("GLContext requires Windows.");

        _hwnd = hwnd;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        IntPtr previousContext = wglGetCurrentContext();
        IntPtr previousDc = wglGetCurrentDC();
        _hdc = Native.GetDC(hwnd);
        if (_hdc == IntPtr.Zero) throw new InvalidOperationException("GetDC returned NULL.");

        try
        {
            Construct(major, minor, samples);
        }
        catch (Exception error)
        {
            // Preserve the original error while recording any additional cleanup failures.
            try { Dispose(); }
            catch (Exception cleanupError) { error.Data["GLContext.CleanupError"] = cleanupError; }
            try { RestoreCurrentContext(previousDc, previousContext); }
            catch (Exception restoreError) { error.Data["GLContext.RestoreError"] = restoreError; }
            throw;
        }
    }

    /// <summary>Assigns or reuses the pixel format, creates a context, and configures presentation.</summary>
    private void Construct(int major, int minor, int samples)
    {
        PIXELFORMATDESCRIPTOR pfd = PIXELFORMATDESCRIPTOR.Default();
        int format = GetPixelFormat(_hdc);

        if (format == 0)
        {
            format = samples > 1 ? TryChooseMultisampleFormat(samples) : 0;
            if (format == 0) format = Native.ChoosePixelFormat(_hdc, ref pfd);
            if (format == 0) throw NativeFailure("ChoosePixelFormat");
            if (Native.DescribePixelFormat(_hdc, format, (uint)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(), ref pfd) == 0) throw NativeFailure("DescribePixelFormat");
            if (!Native.SetPixelFormat(_hdc, format, ref pfd)) throw NativeFailure("SetPixelFormat");
        }

        // Track ownership immediately, so every later failure can release the legacy context.
        _hglrc = Native.wglCreateContext(_hdc);
        if (_hglrc == IntPtr.Zero) throw NativeFailure("wglCreateContext");
        if (!Native.wglMakeCurrent(_hdc, _hglrc)) throw NativeFailure("wglMakeCurrent (legacy)");
        IsLegacy = true;
        string extensions = GetExtensions(_hdc);

        if (HasExtension(extensions, "WGL_ARB_create_context") && HasExtension(extensions, "WGL_ARB_create_context_profile"))
        {
            TryCreateCoreContext(major, minor);
        }

        // Function addresses and extension support are queried again for the adopted context.
        extensions = GetExtensions(_hdc);
        MsaaSamples = QueryMsaaSamples(format, extensions);
        TryEnableVSync(extensions);
    }

    /// <summary>Attempts to replace the current legacy context with the requested core context.</summary>
    private void TryCreateCoreContext(int major, int minor)
    {
        IntPtr address = GetExtensionProc("wglCreateContextAttribsARB");
        if (address == IntPtr.Zero) return;
        var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int*, IntPtr>)address;

        int* attributes = stackalloc int[]
        {
            WGL_CONTEXT_MAJOR_VERSION_ARB, major,
            WGL_CONTEXT_MINOR_VERSION_ARB, minor,
            WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB,
            WGL_CONTEXT_FLAGS_ARB, WGL_CONTEXT_FORWARD_COMPATIBLE_BIT_ARB,
            0
        };

        _pendingContext = create(_hdc, IntPtr.Zero, attributes);
        if (_pendingContext == IntPtr.Zero) return;

        if (Native.wglMakeCurrent(_hdc, _pendingContext))
        {
            DeleteContext(ref _hglrc);
            _hglrc = _pendingContext;
            _pendingContext = IntPtr.Zero;
            IsLegacy = false;
            return;
        }

        WriteLine($"wglMakeCurrent (core) failed, Win32 error {Marshal.GetLastWin32Error()}; restoring the legacy context.");
        DeleteContext(ref _pendingContext);

        // A failed wglMakeCurrent clears the previous binding, so explicitly restore the fallback.
        if (!Native.wglMakeCurrent(_hdc, _hglrc)) throw NativeFailure("wglMakeCurrent (legacy fallback)");
    }

    /// <summary>Uses a hidden legacy context to request an MSAA pixel format for the real window DC.</summary>
    /// <remarks>Returns zero if MSAA selection is unavailable. Always attempts to restore the previous context and release temporary resources.</remarks>
    private int TryChooseMultisampleFormat(int samples)
    {
        IntPtr previousContext = wglGetCurrentContext();
        IntPtr previousDc = wglGetCurrentDC();

        try
        {
            _bootstrapWindow = Native.CreateWindowExW(0, "STATIC", "", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_bootstrapWindow == IntPtr.Zero) return 0;
            _bootstrapDc = Native.GetDC(_bootstrapWindow);
            if (_bootstrapDc == IntPtr.Zero) return 0;

            PIXELFORMATDESCRIPTOR pfd = PIXELFORMATDESCRIPTOR.Default();
            int basic = Native.ChoosePixelFormat(_bootstrapDc, ref pfd);
            if (basic == 0) return 0;
            if (Native.DescribePixelFormat(_bootstrapDc, basic, (uint)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(), ref pfd) == 0)
                return 0;
            if (!Native.SetPixelFormat(_bootstrapDc, basic, ref pfd)) return 0;
            _bootstrapContext = Native.wglCreateContext(_bootstrapDc);
            if (_bootstrapContext == IntPtr.Zero) return 0;
            if (!Native.wglMakeCurrent(_bootstrapDc, _bootstrapContext)) return 0;

            string extensions = GetExtensions(_bootstrapDc);
            if (!HasExtension(extensions, "WGL_ARB_pixel_format") || !SupportsMultisampling(extensions)) return 0;
            IntPtr address = GetExtensionProc("wglChoosePixelFormatARB");
            if (address == IntPtr.Zero) return 0;
            var choose = (delegate* unmanaged[Stdcall]<IntPtr, int*, float*, uint, int*, uint*, int>)address;

            int* attributes = stackalloc int[]
            {
                WGL_DRAW_TO_WINDOW_ARB, 1,
                WGL_SUPPORT_OPENGL_ARB, 1,
                WGL_DOUBLE_BUFFER_ARB, 1,
                WGL_PIXEL_TYPE_ARB, WGL_TYPE_RGBA_ARB,
                WGL_COLOR_BITS_ARB, 32,
                WGL_DEPTH_BITS_ARB, 24,
                WGL_STENCIL_BITS_ARB, 8,
                WGL_SAMPLE_BUFFERS_ARB, 1,
                WGL_SAMPLES_ARB, samples,
                0
            };

            int format = 0;
            uint count = 0;

            // The format belongs to the real window's device, not the temporary window's DC.
            return choose(_hdc, attributes, null, 1, &format, &count) != 0 && count > 0 && format > 0 ? format : 0;
        }
        finally
        {
            try
            {
                RestoreCurrentContext(previousDc, previousContext);
            }
            finally
            {
                ReleaseBootstrapResources();
            }
        }
    }

    /// <summary>Queries the selected format's actual sample count, returning zero if unavailable.</summary>
    private int QueryMsaaSamples(int format, string extensions)
    {
        if (!HasExtension(extensions, "WGL_ARB_pixel_format") || !SupportsMultisampling(extensions)) return 0;
        IntPtr address = GetExtensionProc("wglGetPixelFormatAttribivARB");
        if (address == IntPtr.Zero) return 0;
        var query = (delegate* unmanaged[Stdcall]<IntPtr, int, int, uint, int*, int*, int>)address;
        int* attributes = stackalloc int[] { WGL_SAMPLE_BUFFERS_ARB, WGL_SAMPLES_ARB };
        int* values = stackalloc int[] { 0, 0 };
        if (query(_hdc, format, 0, 2, attributes, values) == 0 || values[0] <= 0) return 0;
        return Math.Max(0, values[1]);
    }

    /// <summary>Requests a swap interval of one when WGL_EXT_swap_control is supported.</summary>
    private void TryEnableVSync(string extensions)
    {
        VSyncEnabled = false;
        if (!HasExtension(extensions, "WGL_EXT_swap_control")) return;
        IntPtr address = GetExtensionProc("wglSwapIntervalEXT");
        if (address == IntPtr.Zero) return;
        var swapInterval = (delegate* unmanaged[Stdcall]<int, int>)address;
        VSyncEnabled = swapInterval(1) != 0;
    }

    /// <summary>Returns a valid WGL function address, treating known failure sentinels as unavailable.</summary>
    private static IntPtr GetExtensionProc(string name)
    {
        IntPtr address = wglGetProcAddress(name);
        return new[]{IntPtr.Zero, 1, 2, 3, -1}.Any(i => i == address) ? IntPtr.Zero : address;
    }

    /// <summary>Reads the current driver's WGL extension list, preferring the DC-specific ARB query.</summary>
    private static string GetExtensions(IntPtr dc)
    {
        IntPtr address = GetExtensionProc("wglGetExtensionsStringARB");

        if (address != IntPtr.Zero)
        {
            var get = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)address;
            IntPtr value = get(dc);
            if (value != IntPtr.Zero) return Marshal.PtrToStringAnsi(value) ?? string.Empty;
        }

        address = GetExtensionProc("wglGetExtensionsStringEXT");
        if (address == IntPtr.Zero) return string.Empty;
        var getExt = (delegate* unmanaged[Stdcall]<IntPtr>)address;
        return Marshal.PtrToStringAnsi(getExt()) ?? string.Empty;
    }

    /// <summary>Checks a space-delimited extension list for an exact extension name.</summary>
    private static bool HasExtension(string extensions, string name) => extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(extension => string.Equals(extension, name, StringComparison.Ordinal));

    /// <summary>Checks whether the driver advertises ARB or EXT multisample pixel format attributes.</summary>
    private static bool SupportsMultisampling(string extensions) => HasExtension(extensions, "WGL_ARB_multisample") || HasExtension(extensions, "WGL_EXT_multisample");

    /// <summary>Restores a saved thread-local context binding, throwing if restoration fails.</summary>
    private static void RestoreCurrentContext(IntPtr dc, IntPtr context)
    {
        if (wglGetCurrentContext() == context && wglGetCurrentDC() == dc) return;
        if (!Native.wglMakeCurrent(dc, context)) throw NativeFailure("wglMakeCurrent (restore previous context)");
    }

    /// <summary>Deletes an owned context and clears its handle only after successful deletion.</summary>
    /// <remarks>WGL unbinds this context if it is current on the calling thread, without unbinding any unrelated context.</remarks>
    private static void DeleteContext(ref IntPtr context)
    {
        if (context == IntPtr.Zero) return;
        if (!Native.wglDeleteContext(context)) throw NativeFailure("wglDeleteContext");
        context = IntPtr.Zero;
    }

    /// <summary>Releases the temporary context, its DC, and its hidden window in dependency order.</summary>
    private void ReleaseBootstrapResources()
    {
        DeleteContext(ref _bootstrapContext);

        if (_bootstrapDc != IntPtr.Zero)
        {
            Native.ReleaseDC(_bootstrapWindow, _bootstrapDc);
            _bootstrapDc = IntPtr.Zero;
        }

        if (_bootstrapWindow != IntPtr.Zero)
        {
            if (!Native.DestroyWindow(_bootstrapWindow)) throw NativeFailure("DestroyWindow (bootstrap)");
            _bootstrapWindow = IntPtr.Zero;
        }
    }

    /// <summary>Creates an initialization or cleanup error containing the last Win32 error code.</summary>
    private static InvalidOperationException NativeFailure(string operation) => new($"{operation} failed, Win32 error {Marshal.GetLastWin32Error()}.");

    /// <summary>Rejects calls after disposal or after a partial cleanup removed the rendering context.</summary>
    private void ThrowIfDisposed()
    {
        if (_disposed || _hdc == IntPtr.Zero || _hglrc == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(GLContext));
        }
    }

    /// <summary>Makes this rendering context current on the calling thread.</summary>
    /// <returns>True on success; false on a native failure. Do not issue rendering calls after a failed result.</returns>
    /// <remarks>The context must not be current on another thread. A failed WGL call can clear the calling thread's previous binding.</remarks>
    public bool MakeCurrent()
    {
        ThrowIfDisposed();
        bool success = Native.wglMakeCurrent(_hdc, _hglrc);
        LastNativeError = success ? 0 : Marshal.GetLastPInvokeError();
        return success;
    }

    /// <summary>Presents the window's back buffer by swapping its front and back buffers.</summary>
    /// <returns>True if the native swap succeeds; false otherwise.</returns>
    public bool SwapBuffers()
    {
        ThrowIfDisposed();
        bool success = Native.SwapBuffers(_hdc);
        LastNativeError = success ? 0 : Marshal.GetLastPInvokeError();
        return success;
    }

    /// <summary>Releases owned contexts and DCs without detaching an unrelated current context.</summary>
    /// <remarks>
    /// Call on the construction thread after rendering has stopped and before destroying the window.
    /// A context current on another thread must first be unbound there. Failed context deletion retains
    /// its handle and throws, allowing cleanup to be retried. Repeated successful disposal has no effect.
    /// No finalizer is used because a finalizer thread cannot safely perform this cleanup.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Dispose GLContext on the thread that constructed it.");
        }

        DeleteContext(ref _pendingContext);
        DeleteContext(ref _hglrc);
        ReleaseBootstrapResources();

        if (_hdc != IntPtr.Zero)
        {
            // ReleaseDC is intentionally used for GetDC; private/class DCs do not require release.
            Native.ReleaseDC(_hwnd, _hdc);
            _hdc = IntPtr.Zero;
        }

        _disposed = true;
    }
}
