using System.Runtime.InteropServices;

namespace MakarovPhysics.Renderer;

internal static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    // A throwaway hidden window ("STATIC" class, so no class registration is
    // needed) - used once at startup to query wglChoosePixelFormatARB, because
    // SetPixelFormat may only be called once per window.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] public static extern int DescribePixelFormat(IntPtr hdc, int iPixelFormat, uint nBytes, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll", SetLastError = true)] public static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("opengl32.dll")] public static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] public static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport("opengl32.dll", SetLastError = true)] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    // Game loop via Application.Idle: render while the message queue is empty
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    /// <summary>true when the message queue is empty and the next frame can be drawn.</summary>
    public static bool AppStillIdle() => !PeekMessage(out _, IntPtr.Zero, 0, 0, 0 /*PM_NOREMOVE*/);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PIXELFORMATDESCRIPTOR
{
    public ushort nSize;
    public ushort nVersion;
    public uint dwFlags;
    public byte iPixelType;
    public byte cColorBits;
    public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift;
    public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
    public byte cDepthBits;
    public byte cStencilBits;
    public byte cAuxBuffers;
    public byte iLayerType;
    public byte bReserved;
    public uint dwLayerMask;
    public uint dwVisibleMask;
    public uint dwDamageMask;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;

    public static PIXELFORMATDESCRIPTOR Default()
    {
        return new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = 0,   // PFD_TYPE_RGBA
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = 0    // PFD_MAIN_PLANE
        };
    }
}
