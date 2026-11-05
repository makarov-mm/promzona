namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the FDTD module: fdtd_cuda.cu mechanically translated
/// - the two Yee half-steps (H from curl E, E from curl H with PEC and
/// permittivity), the point/line source, and the loss frame that damps all
/// three fields and accumulates intensity AFTER damping (the CPU loop
/// order). Intensity clear is a fill kernel (no clEnqueueFillBuffer in the
/// thin runtime). Fifth client of <see cref="OpenCl"/>. Never-throw.
/// </summary>
public sealed class ClFdtd : IFdtdBackend
{
    private const string Source = @"
__kernel void mp_fdtd_h(int n, float s,
    __global const float* restrict ez,
    __global float* restrict hx, __global float* restrict hy)
{
    int i = get_global_id(0);
    int cells = n * n;
    if (i >= cells) return;
    int x = i % n, y = i / n;
    if (x < n - 1) hy[i] += s * (ez[i + 1] - ez[i]);
    if (y < n - 1) hx[i] -= s * (ez[i + n] - ez[i]);
}

__kernel void mp_fdtd_e(int n, float s,
    __global float* restrict ez,
    __global const float* restrict hx, __global const float* restrict hy,
    __global const float* restrict invEps, __global const uchar* restrict pec)
{
    int i = get_global_id(0);
    int cells = n * n;
    if (i >= cells) return;
    int x = i % n, y = i / n;
    if (x < 1 || y < 1) return;
    if (pec[i]) { ez[i] = 0.0f; return; }
    ez[i] += s * invEps[i] * ((hy[i] - hy[i - 1]) - (hx[i] - hx[i - n]));
}

/* point source: one work item; line source: one item per y in [y0, y1) */
__kernel void mp_fdtd_source(int n, float drive, int line,
    int srcX, int srcY, int y0, int y1,
    __global float* restrict ez, __global const uchar* restrict pec)
{
    if (line)
    {
        int y = y0 + (int)get_global_id(0);
        if (y >= y1) return;
        int i = y * n + srcX;
        if (!pec[i]) ez[i] += drive;
    }
    else
    {
        if (get_global_id(0) != 0) return;
        int i = srcY * n + srcX;
        if (!pec[i]) ez[i] += drive;
    }
}

/* loss frame + the intensity accumulation AFTER damping (CPU loop order) */
__kernel void mp_fdtd_loss(int n,
    __global float* restrict ez, __global float* restrict hx, __global float* restrict hy,
    __global const float* restrict damp, __global float* restrict ezSq)
{
    int i = get_global_id(0);
    int cells = n * n;
    if (i >= cells) return;
    float d = damp[i];
    float e = ez[i] * d;
    ez[i] = e;
    hx[i] *= d;
    hy[i] *= d;
    ezSq[i] += e * e;
}

__kernel void mp_fdtd_zero(int cells, __global float* restrict buf)
{
    int i = get_global_id(0);
    if (i < cells) buf[i] = 0.0f;
}
";

    private IntPtr _program, _kH, _kE, _kSource, _kLoss, _kZero;
    private IntPtr _ez, _hx, _hy, _invEps, _pec, _damp, _ezSq;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClFdtd? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClFdtd();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kH = OpenCl.Kernel(b._program, "mp_fdtd_h");
            b._kE = OpenCl.Kernel(b._program, "mp_fdtd_e");
            b._kSource = OpenCl.Kernel(b._program, "mp_fdtd_source");
            b._kLoss = OpenCl.Kernel(b._program, "mp_fdtd_loss");
            b._kZero = OpenCl.Kernel(b._program, "mp_fdtd_zero");
            if (b._kH == IntPtr.Zero || b._kE == IntPtr.Zero || b._kSource == IntPtr.Zero
                || b._kLoss == IntPtr.Zero || b._kZero == IntPtr.Zero) { b.Dispose(); return null; }
            long cb = 4L * maxCells;
            b._ez = OpenCl.Buffer(cb); b._hx = OpenCl.Buffer(cb); b._hy = OpenCl.Buffer(cb);
            b._invEps = OpenCl.Buffer(cb); b._damp = OpenCl.Buffer(cb); b._ezSq = OpenCl.Buffer(cb);
            b._pec = OpenCl.Buffer(maxCells);
            if (b._ez == IntPtr.Zero || b._hx == IntPtr.Zero || b._hy == IntPtr.Zero
                || b._invEps == IntPtr.Zero || b._damp == IntPtr.Zero || b._ezSq == IntPtr.Zero
                || b._pec == IntPtr.Zero) { b.Dispose(); return null; }
            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClFdtd() { }

    public bool Upload(int cells, float[]? ez, float[]? hx, float[]? hy, float[]? invEps, byte[]? pec, float[]? damp, float[]? ezSq)
    {
        if (_kH == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (ez is null || ez.Length < cells || hx is null || hx.Length < cells
            || hy is null || hy.Length < cells || invEps is null || invEps.Length < cells
            || pec is null || pec.Length < cells || damp is null || damp.Length < cells
            || ezSq is null || ezSq.Length < cells) return false;
        try
        {
            return OpenCl.Write(_ez, ez, cells) && OpenCl.Write(_hx, hx, cells)
                && OpenCl.Write(_hy, hy, cells) && OpenCl.Write(_invEps, invEps, cells)
                && OpenCl.Write(_pec, pec, cells) && OpenCl.Write(_damp, damp, cells)
                && OpenCl.Write(_ezSq, ezSq, cells);
        }
        catch { return false; }
    }

    public bool UploadDamp(int cells, float[]? damp)
    {
        if (_kH == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (damp is null || damp.Length < cells) return false;
        try { return OpenCl.Write(_damp, damp, cells); }
        catch { return false; }
    }

    public bool ClearIntensity(int cells)
    {
        if (_kZero == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        try
        {
            return OpenCl.Arg(_kZero, 0, cells) && OpenCl.Arg(_kZero, 1, _ezSq)
                && OpenCl.Launch(_kZero, cells) && OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Step(int n, float s, float drive, bool line, int srcX, int srcY, int y0, int y1)
    {
        if (_kH == IntPtr.Zero || n <= 0 || (long)n * n > _capacity) return false;
        try
        {
            long cells = (long)n * n;
            bool ok =
                OpenCl.Arg(_kH, 0, n) && OpenCl.Arg(_kH, 1, s) &&
                OpenCl.Arg(_kH, 2, _ez) && OpenCl.Arg(_kH, 3, _hx) && OpenCl.Arg(_kH, 4, _hy) &&
                OpenCl.Launch(_kH, cells);
            if (!ok) return false;

            ok = OpenCl.Arg(_kE, 0, n) && OpenCl.Arg(_kE, 1, s) &&
                 OpenCl.Arg(_kE, 2, _ez) && OpenCl.Arg(_kE, 3, _hx) && OpenCl.Arg(_kE, 4, _hy) &&
                 OpenCl.Arg(_kE, 5, _invEps) && OpenCl.Arg(_kE, 6, _pec) &&
                 OpenCl.Launch(_kE, cells);
            if (!ok) return false;

            int span = line ? y1 - y0 : 1;
            if (span > 0)
            {
                ok = OpenCl.Arg(_kSource, 0, n) && OpenCl.Arg(_kSource, 1, drive) &&
                     OpenCl.Arg(_kSource, 2, line ? 1 : 0) &&
                     OpenCl.Arg(_kSource, 3, srcX) && OpenCl.Arg(_kSource, 4, srcY) &&
                     OpenCl.Arg(_kSource, 5, y0) && OpenCl.Arg(_kSource, 6, y1) &&
                     OpenCl.Arg(_kSource, 7, _ez) && OpenCl.Arg(_kSource, 8, _pec) &&
                     OpenCl.Launch(_kSource, span);
                if (!ok) return false;
            }

            ok = OpenCl.Arg(_kLoss, 0, n) &&
                 OpenCl.Arg(_kLoss, 1, _ez) && OpenCl.Arg(_kLoss, 2, _hx) && OpenCl.Arg(_kLoss, 3, _hy) &&
                 OpenCl.Arg(_kLoss, 4, _damp) && OpenCl.Arg(_kLoss, 5, _ezSq) &&
                 OpenCl.Launch(_kLoss, cells);
            if (!ok) return false;

            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool DownloadEz(int cells, float[]? ez)
    {
        if (_kH == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (ez is null || ez.Length < cells) return false;
        try { return OpenCl.Read(_ez, ez, cells); }
        catch { return false; }
    }

    public bool DownloadAll(int cells, float[]? ez, float[]? hx, float[]? hy)
    {
        if (_kH == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (ez is null || ez.Length < cells || hx is null || hx.Length < cells
            || hy is null || hy.Length < cells) return false;
        try
        {
            return OpenCl.Read(_ez, ez, cells) && OpenCl.Read(_hx, hx, cells)
                && OpenCl.Read(_hy, hy, cells);
        }
        catch { return false; }
    }

    public bool DownloadIntensity(int cells, float[]? ezSq)
    {
        if (_kH == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (ezSq is null || ezSq.Length < cells) return false;
        try { return OpenCl.Read(_ezSq, ezSq, cells); }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_ez); OpenCl.Release(_hx); OpenCl.Release(_hy);
        OpenCl.Release(_invEps); OpenCl.Release(_pec); OpenCl.Release(_damp); OpenCl.Release(_ezSq);
        _ez = _hx = _hy = _invEps = _pec = _damp = _ezSq = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kH); OpenCl.ReleaseKernel(_kE); OpenCl.ReleaseKernel(_kSource);
        OpenCl.ReleaseKernel(_kLoss); OpenCl.ReleaseKernel(_kZero);
        _kH = _kE = _kSource = _kLoss = _kZero = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
