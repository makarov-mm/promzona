namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the 3D cellular automata: ca3d_cuda.cu mechanically
/// translated - one integer rule kernel (26-neighbourhood count, wrap or
/// dead-outside, survive/birth bitmasks, decaying states), double-buffered
/// with a host-side swap. Pure integers, so the SelfTest pins BIT-EXACT
/// parity. Seventh client of <see cref="OpenCl"/>. Never-throw contract.
/// </summary>
public sealed class ClAutomata3D : IAutomata3DBackend
{
    private const string Source = @"
__kernel void mp_ca3d_step(
    int n, int wrap, uint survive, uint birth, int maxState,
    __global const uchar* restrict cur, __global uchar* restrict next)
{
    int idx = get_global_id(0);
    int total = n * n * n;
    if (idx >= total) return;

    int x = idx % n;
    int y = (idx / n) % n;
    int z = idx / (n * n);
    int count = 0;

    for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                int nx = x + dx, ny = y + dy, nz = z + dz;

                if (wrap)
                {
                    if (nx < 0) nx += n; else if (nx >= n) nx -= n;
                    if (ny < 0) ny += n; else if (ny >= n) ny -= n;
                    if (nz < 0) nz += n; else if (nz >= n) nz -= n;
                }
                else if (nx < 0 || nx >= n || ny < 0 || ny >= n || nz < 0 || nz >= n)
                {
                    continue; /* outside = dead */
                }

                if (cur[(nz * n + ny) * n + nx] != 0)
                    count++;
            }

    uchar s = cur[idx];
    uchar outv;

    if (s == (uchar)maxState)
        outv = (survive >> count & 1u) ? (uchar)maxState : (uchar)(maxState - 1);
    else if (s == 0)
        outv = (birth >> count & 1u) ? (uchar)maxState : (uchar)0;
    else
        outv = (uchar)(s - 1);

    next[idx] = outv;
}
";

    private IntPtr _program, _kernel, _cur, _next;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClAutomata3D? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClAutomata3D();

        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);

            if (b._program == IntPtr.Zero)
            {
                b.Dispose();
                return null;
            }

            b._kernel = OpenCl.Kernel(b._program, "mp_ca3d_step");

            if (b._kernel == IntPtr.Zero)
            {
                b.Dispose();
                return null;
            }

            b._cur = OpenCl.Buffer(maxCells);
            b._next = OpenCl.Buffer(maxCells);

            if (b._cur == IntPtr.Zero || b._next == IntPtr.Zero)
            {
                b.Dispose(); 
                return null;
            }

            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClAutomata3D() { }

    public bool Upload(int cells, byte[]? grid)
    {
        if (_kernel == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (grid is null || grid.Length < cells) return false;

        try
        {
            return OpenCl.Write(_cur, grid, cells);
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, bool wrap, uint survive, uint birth, int maxState, int steps)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || (long)n * n * n > _capacity || steps <= 0) return false;

        try
        {
            long total = (long)n * n * n;

            for (int s = 0; s < steps; s++)
            {
                bool ok =
                    OpenCl.Arg(_kernel, 0, n)
                    &&
                    OpenCl.Arg(_kernel, 1, wrap ? 1 : 0)
                    &&
                    OpenCl.Arg(_kernel, 2, unchecked((int)survive))
                    &&
                    OpenCl.Arg(_kernel, 3, unchecked((int)birth))
                    &&
                    OpenCl.Arg(_kernel, 4, maxState)
                    &&
                    OpenCl.Arg(_kernel, 5, _cur)
                    &&
                    OpenCl.Arg(_kernel, 6, _next)
                    &&
                    OpenCl.Launch(_kernel, total);

                if (!ok) return false;
                (_cur, _next) = (_next, _cur);
            }

            return OpenCl.Finish();
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int cells, byte[]? grid)
    {
        if (_kernel == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (grid is null || grid.Length < cells) return false;

        try
        {
            return OpenCl.Read(_cur, grid, cells);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        OpenCl.Release(_cur);
        OpenCl.Release(_next);
        _cur = _next = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kernel);
        _kernel = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
