using System.Runtime.InteropServices;
using MakarovPhysics.Base;
// Spelled through an alias: the carousel has a LayoutKind of its own, and the
// enum gate (apicheck) reads a bare `LayoutKind.Sequential` as that one.
using NativeLayout = System.Runtime.InteropServices.LayoutKind;

namespace Makarov.Host;

/// <summary>Lets Windows draw its native CJK composition/candidate windows over
/// the GL surface. Default processing remains responsible for WM_CHAR results;
/// reading GCS_RESULTSTR as well would insert every committed character twice.</summary>
internal sealed class ImeInputBridge : NativeWindow, IDisposable
{
    private const int WmImeStartComposition = 0x010D, WmImeEndComposition = 0x010E;
    private const int WmImeComposition = 0x010F;
    private readonly Control _control;
    private readonly InputState _input;
    private readonly Action _beginTextInput;
    private readonly Func<Rectangle?> _caret;
    private Rectangle? _lastCaret;
    private bool _positioning;
    private bool _cancelling;
    private bool _disposed;

    public ImeInputBridge(Control control, InputState input, Action beginTextInput, Func<Rectangle?> caret)
    {
        _control = control;
        _input = input;
        _beginTextInput = beginTextInput;
        _caret = caret;
        // NoControl leaves the user's chosen input method/mode intact.
        control.ImeMode = ImeMode.NoControl;
        control.HandleCreated += HandleCreated;
        control.HandleDestroyed += HandleDestroyed;
        control.LostFocus += LostFocus;
        if (control.IsHandleCreated) AssignHandle(control.Handle);
    }

    private void HandleCreated(object? sender, EventArgs e) => AssignHandle(_control.Handle);
    private void HandleDestroyed(object? sender, EventArgs e)
    {
        if (_input.IsImeComposing) _input.EndImeComposition();
        _lastCaret = null;
        ReleaseHandle();
    }
    private void LostFocus(object? sender, EventArgs e) => Cancel();

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmImeStartComposition)
        {
            _beginTextInput();
            _input.BeginImeComposition();
            _lastCaret = null;
            RefreshPosition();
        }
        else if (message.Msg == WmImeComposition)
        {
            // Some input methods send a composition update without START.
            if (!_input.IsImeComposing)
            {
                _beginTextInput();
                _input.BeginImeComposition();
            }
            RefreshPosition();
        }
        else if (message.Msg == WmImeEndComposition)
        {
            _input.EndImeComposition();
            _lastCaret = null;
        }
        base.WndProc(ref message);
    }

    /// <summary>Coordinates are physical client pixels, never logical UI units.</summary>
    public void RefreshPosition()
    {
        if (_disposed || _positioning || Handle == IntPtr.Zero || !_input.IsImeComposing) return;
        Rectangle? caret = _caret();
        if (caret is null || caret == _lastCaret) return;
        _positioning = true;
        IntPtr context = ImmGetContext(Handle);
        try
        {
            if (context == IntPtr.Zero) return;
            Rectangle r = caret.Value;
            var composition = new CompositionForm { Style = 0x0002, Position = new NativePoint(r.Left, r.Top), Area = default };
            ImmSetCompositionWindow(context, ref composition); // CFS_POINT
            var candidates = new CandidateForm
            {
                Index = 0, Style = 0x0080, Position = new NativePoint(r.Left, r.Bottom),
                Area = new NativeRect(r.Left, r.Top, r.Right, r.Bottom)
            };
            ImmSetCandidateWindow(context, ref candidates); // CFS_EXCLUDE
            _lastCaret = caret;
        }
        finally
        {
            if (context != IntPtr.Zero) ImmReleaseContext(Handle, context);
            _positioning = false;
        }
    }

    public void Cancel()
    {
        if (_disposed || _cancelling || !_input.IsImeComposing) return;
        _cancelling = true;
        IntPtr context = Handle == IntPtr.Zero ? IntPtr.Zero : ImmGetContext(Handle);
        try
        {
            if (context != IntPtr.Zero) ImmNotifyIME(context, 0x0015, 0x0004, 0); // NI_COMPOSITIONSTR, CPS_CANCEL
        }
        finally
        {
            if (context != IntPtr.Zero) ImmReleaseContext(Handle, context);
            _input.EndImeComposition();
            _lastCaret = null;
            _cancelling = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Cancel();
        _disposed = true;
        _control.HandleCreated -= HandleCreated;
        _control.HandleDestroyed -= HandleDestroyed;
        _control.LostFocus -= LostFocus;
        ReleaseHandle();
    }

    [StructLayout(NativeLayout.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(NativeLayout.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    { public int Left = left, Top = top, Right = right, Bottom = bottom; }
    [StructLayout(NativeLayout.Sequential)]
    private struct CompositionForm { public uint Style; public NativePoint Position; public NativeRect Area; }
    [StructLayout(NativeLayout.Sequential)]
    private struct CandidateForm { public uint Index, Style; public NativePoint Position; public NativeRect Area; }

    [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmNotifyIME(IntPtr context, uint action, uint index, uint value);
}
