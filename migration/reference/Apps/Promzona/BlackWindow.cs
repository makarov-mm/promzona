using System.Runtime.InteropServices;

namespace Promzona;

internal static class BlackWindow
{
    [DllImport("user32.dll")]
    private static extern int FillRect(nint dc, ref RECT rect, nint brush);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int index);

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    // ReSharper disable once InconsistentNaming
    private struct RECT
    {
        public int L, T, R, B;
    }

    public static void Paint(nint hwnd, nint dc = 0)
    {
        if (hwnd == 0) return;
        bool own = dc == 0;
        if (own) dc = GetDC(hwnd);

        try
        {
            if (dc != 0 && GetClientRect(hwnd, out var rect))
            {
                FillRect(dc, ref rect, GetStockObject(4));
            }
        }
        finally
        {
            if (own && dc != 0)
            {
                ReleaseDC(hwnd, dc);
            }
        }
    }
}
