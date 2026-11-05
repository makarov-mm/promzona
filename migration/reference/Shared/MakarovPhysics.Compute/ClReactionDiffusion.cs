namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for Gray-Scott: rd_cuda.cu mechanically translated - one
/// kernel, the 9-point Laplacian (0.2 edge / 0.05 corner) on a periodic
/// grid, u/v double-buffered with a host-side swap. Fourth client of the
/// shared <see cref="OpenCl"/> runtime. Never-throw contract.
/// </summary>
public sealed class ClReactionDiffusion : IReactionDiffusionBackend
{
    private const string Source = @"
__kernel void mp_rd_step(
    int n,
    __global const float* restrict u, __global const float* restrict v,
    __global float* restrict uNext, __global float* restrict vNext,
    float du, float dv, float feed, float kill)
{
    int i = get_global_id(0);
    int cells = n * n;
    if (i >= cells) return;

    int x = i % n, y = i / n;
    int xm = x == 0 ? n - 1 : x - 1;
    int xp = x == n - 1 ? 0 : x + 1;
    int ym = y == 0 ? n - 1 : y - 1;
    int yp = y == n - 1 ? 0 : y + 1;
    int row = y * n, rowM = ym * n, rowP = yp * n;

    float uc = u[i], vc = v[i];
    float lapU = -uc
        + 0.2f * (u[row + xm] + u[row + xp] + u[rowM + x] + u[rowP + x])
        + 0.05f * (u[rowM + xm] + u[rowM + xp] + u[rowP + xm] + u[rowP + xp]);
    float lapV = -vc
        + 0.2f * (v[row + xm] + v[row + xp] + v[rowM + x] + v[rowP + x])
        + 0.05f * (v[rowM + xm] + v[rowM + xp] + v[rowP + xm] + v[rowP + xp]);

    float reaction = uc * vc * vc;
    uNext[i] = uc + (du * lapU - reaction + feed * (1.0f - uc));
    vNext[i] = vc + (dv * lapV + reaction - (feed + kill) * vc);
}
";

    private IntPtr _program, _kernel;
    private IntPtr _u, _v, _u2, _v2;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClReactionDiffusion? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClReactionDiffusion();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kernel = OpenCl.Kernel(b._program, "mp_rd_step");
            if (b._kernel == IntPtr.Zero) { b.Dispose(); return null; }
            long cb = 4L * maxCells;
            b._u = OpenCl.Buffer(cb); b._v = OpenCl.Buffer(cb);
            b._u2 = OpenCl.Buffer(cb); b._v2 = OpenCl.Buffer(cb);
            if (b._u == IntPtr.Zero || b._v == IntPtr.Zero
                || b._u2 == IntPtr.Zero || b._v2 == IntPtr.Zero) { b.Dispose(); return null; }
            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClReactionDiffusion() { }

    public bool Upload(int cells, float[]? u, float[]? v)
    {
        if (_kernel == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (u is null || u.Length < cells || v is null || v.Length < cells) return false;
        try
        {
            return OpenCl.Write(_u, u, cells) && OpenCl.Write(_v, v, cells);
        }
        catch { return false; }
    }

    public bool Step(int n, float du, float dv, float feed, float kill, int steps)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || (long)n * n > _capacity || steps <= 0) return false;
        try
        {
            long cells = (long)n * n;
            for (int s = 0; s < steps; s++)
            {
                bool ok =
                    OpenCl.Arg(_kernel, 0, n) &&
                    OpenCl.Arg(_kernel, 1, _u) && OpenCl.Arg(_kernel, 2, _v) &&
                    OpenCl.Arg(_kernel, 3, _u2) && OpenCl.Arg(_kernel, 4, _v2) &&
                    OpenCl.Arg(_kernel, 5, du) && OpenCl.Arg(_kernel, 6, dv) &&
                    OpenCl.Arg(_kernel, 7, feed) && OpenCl.Arg(_kernel, 8, kill) &&
                    OpenCl.Launch(_kernel, cells);
                if (!ok) return false;
                (_u, _u2) = (_u2, _u);
                (_v, _v2) = (_v2, _v);
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int cells, float[]? u, float[]? v)
    {
        if (_kernel == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (u is null || u.Length < cells || v is null || v.Length < cells) return false;
        try
        {
            return OpenCl.Read(_u, u, cells) && OpenCl.Read(_v, v, cells);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_u); OpenCl.Release(_v);
        OpenCl.Release(_u2); OpenCl.Release(_v2);
        _u = _v = _u2 = _v2 = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kernel);
        _kernel = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
