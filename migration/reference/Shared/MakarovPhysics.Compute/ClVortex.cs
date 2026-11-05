namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for PointVortices: vortex_cuda.cu mechanically translated
/// - one kernel advecting every tracer through `substeps` frozen vortex
/// snapshots with the midpoint (RK2) scheme over the O(count) Biot-Savart
/// sum. Tenth client of <see cref="OpenCl"/>. Never-throw contract.
/// </summary>
public sealed class ClVortex : IVortexBackend
{
    /// <summary>Mirror of VX_MAX_VORTICES in the .cu / <see cref="CudaVortex.MaxVortices"/>.</summary>
    public const int MaxVortices = 64;

    private const string Source = @"
#define VX_MAX_VORTICES 64
#define VX_TWO_PI 6.2831853071795864769f

__kernel void mp_vx_advect(
    int tracers, int count, int substeps, float dt, float eps2,
    __global float* restrict tracerX, __global float* restrict tracerY,
    __global const float* restrict snapX, __global const float* restrict snapY,
    __global const float* restrict snapG)
{
    int t = get_global_id(0);
    if (t >= tracers) return;

    float x = tracerX[t];
    float y = tracerY[t];

    for (int s = 0; s < substeps; s++)
    {
        __global const float* sx = snapX + (long)s * VX_MAX_VORTICES;
        __global const float* sy = snapY + (long)s * VX_MAX_VORTICES;
        __global const float* sg = snapG + (long)s * VX_MAX_VORTICES;

        float vx1 = 0.0f, vy1 = 0.0f;
        for (int j = 0; j < count; j++)
        {
            float dx = x - sx[j], dy = y - sy[j];
            float c = sg[j] / (VX_TWO_PI * (dx * dx + dy * dy + eps2));
            vx1 -= c * dy;
            vy1 += c * dx;
        }

        float mx = x + 0.5f * dt * vx1;
        float my = y + 0.5f * dt * vy1;

        float vx2 = 0.0f, vy2 = 0.0f;
        for (int j = 0; j < count; j++)
        {
            float dx = mx - sx[j], dy = my - sy[j];
            float c = sg[j] / (VX_TWO_PI * (dx * dx + dy * dy + eps2));
            vx2 -= c * dy;
            vy2 += c * dx;
        }

        x += dt * vx2;
        y += dt * vy2;
    }

    tracerX[t] = x;
    tracerY[t] = y;
}
";

    private IntPtr _program, _kernel;
    private IntPtr _tx, _ty, _snapX, _snapY, _snapG;
    private int _maxTracers, _maxSnapshots;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClVortex? TryCreate(int maxTracers, int maxSnapshots)
    {
        if (maxTracers <= 0 || maxSnapshots <= 0 || !OpenCl.Available) return null;
        var b = new ClVortex();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kernel = OpenCl.Kernel(b._program, "mp_vx_advect");
            if (b._kernel == IntPtr.Zero) { b.Dispose(); return null; }
            long tb = 4L * maxTracers;
            long sb = 4L * maxSnapshots * MaxVortices;
            b._tx = OpenCl.Buffer(tb); b._ty = OpenCl.Buffer(tb);
            b._snapX = OpenCl.Buffer(sb); b._snapY = OpenCl.Buffer(sb); b._snapG = OpenCl.Buffer(sb);
            if (b._tx == IntPtr.Zero || b._ty == IntPtr.Zero || b._snapX == IntPtr.Zero
                || b._snapY == IntPtr.Zero || b._snapG == IntPtr.Zero) { b.Dispose(); return null; }
            b._maxTracers = maxTracers;
            b._maxSnapshots = maxSnapshots;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClVortex() { }

    public bool UploadTracers(int tracers, float[]? x, float[]? y)
    {
        if (_kernel == IntPtr.Zero || tracers < 0 || tracers > _maxTracers) return false;
        if (x is null || x.Length < tracers || y is null || y.Length < tracers) return false;
        if (tracers == 0) return true;
        try { return OpenCl.Write(_tx, x, tracers) && OpenCl.Write(_ty, y, tracers); }
        catch { return false; }
    }

    public bool Advect(int tracers, int count, int substeps, float dt, float eps2,
        float[]? snapX, float[]? snapY, float[]? snapG)
    {
        if (_kernel == IntPtr.Zero || tracers <= 0 || tracers > _maxTracers) return false;
        if (count <= 0 || count > MaxVortices) return false;
        if (substeps <= 0 || substeps > _maxSnapshots) return false;
        int need = substeps * MaxVortices;
        if (snapX is null || snapX.Length < need || snapY is null || snapY.Length < need
            || snapG is null || snapG.Length < need) return false;
        try
        {
            bool ok = OpenCl.Write(_snapX, snapX, need)
                   && OpenCl.Write(_snapY, snapY, need)
                   && OpenCl.Write(_snapG, snapG, need);
            if (!ok) return false;
            ok = OpenCl.Arg(_kernel, 0, tracers) && OpenCl.Arg(_kernel, 1, count) &&
                 OpenCl.Arg(_kernel, 2, substeps) && OpenCl.Arg(_kernel, 3, dt) &&
                 OpenCl.Arg(_kernel, 4, eps2) &&
                 OpenCl.Arg(_kernel, 5, _tx) && OpenCl.Arg(_kernel, 6, _ty) &&
                 OpenCl.Arg(_kernel, 7, _snapX) && OpenCl.Arg(_kernel, 8, _snapY) &&
                 OpenCl.Arg(_kernel, 9, _snapG) &&
                 OpenCl.Launch(_kernel, tracers);
            return ok && OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int tracers, float[]? x, float[]? y)
    {
        if (_kernel == IntPtr.Zero || tracers < 0 || tracers > _maxTracers) return false;
        if (x is null || x.Length < tracers || y is null || y.Length < tracers) return false;
        if (tracers == 0) return true;
        try { return OpenCl.Read(_tx, x, tracers) && OpenCl.Read(_ty, y, tracers); }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_tx); OpenCl.Release(_ty);
        OpenCl.Release(_snapX); OpenCl.Release(_snapY); OpenCl.Release(_snapG);
        _tx = _ty = _snapX = _snapY = _snapG = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kernel);
        _kernel = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
