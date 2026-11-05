using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the point-vortex dye exports of MakarovPhysics.Cuda.dll
/// (Native/CudaNBody/vortex_cuda.cu). Same never-throw contract as the other
/// backends: a failure returns false and the model keeps advecting on the CPU.
///
/// Only the dye lives on the device. The vortices stay on the host in double
/// precision, because they are twenty points at most and they carry the
/// conserved quantities the module is judged by.
/// </summary>
public sealed class CudaVortex : IVortexBackend
{
    /// <summary>Vortex slots per snapshot; must match VX_MAX_VORTICES in the .cu.</summary>
    public const int MaxVortices = 64;

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaVortexCreate")]
    private static extern int MpCudaVortexCreate(int maxTracers, int maxSnapshots, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaVortexUploadTracers")]
    private static extern int MpCudaVortexUploadTracers(IntPtr handle, int tracers, float[] x, float[] y);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaVortexAdvect")]
    private static extern int MpCudaVortexAdvect(IntPtr handle, int tracers, int count, int substeps,
        float dt, float eps2, float[] snapX, float[] snapY, float[] snapG);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaVortexDownload")]
    private static extern int MpCudaVortexDownload(IntPtr handle, int tracers, float[] x, float[] y);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaVortexDestroy")]
    private static extern int MpCudaVortexDestroy(IntPtr handle);

    private IntPtr _handle;
    private int _maxTracers;
    private int _maxSnapshots;

    public static CudaVortex? TryCreate(int maxTracers, int maxSnapshots)
    {
        if (!CudaNBody.Available || maxTracers <= 0 || maxSnapshots <= 0) return null;

        try
        {
            if (MpCudaVortexCreate(maxTracers, maxSnapshots, out IntPtr handle) != 0
                || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaVortex { _handle = handle, _maxTracers = maxTracers, _maxSnapshots = maxSnapshots };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaVortex() { }

    public bool UploadTracers(int tracers, float[]? x, float[]? y)
    {
        if (_handle == IntPtr.Zero || tracers < 0 || tracers > _maxTracers) return false;
        // a short array would let the native copy run past the managed buffer
        if (x is null || x.Length < tracers || y is null || y.Length < tracers) return false;

        try { return MpCudaVortexUploadTracers(_handle, tracers, x, y) == 0; }
        catch { return false; }
    }

    /// <summary>
    /// Advect every tracer through a whole frame: the caller passes the vortex
    /// configuration AT THE START OF EACH SUBSTEP, laid out as `substeps`
    /// blocks of <see cref="MaxVortices"/> floats.
    /// </summary>
    public bool Advect(int tracers, int count, int substeps, float dt, float eps2,
        float[]? snapX, float[]? snapY, float[]? snapG)
    {
        if (_handle == IntPtr.Zero || tracers <= 0 || tracers > _maxTracers) return false;
        if (count <= 0 || count > MaxVortices) return false;
        if (substeps <= 0 || substeps > _maxSnapshots) return false;

        int need = substeps * MaxVortices;
        if (snapX is null || snapX.Length < need || snapY is null || snapY.Length < need
            || snapG is null || snapG.Length < need) return false;

        try { return MpCudaVortexAdvect(_handle, tracers, count, substeps, dt, eps2, snapX, snapY, snapG) == 0; }
        catch { return false; }
    }

    public bool Download(int tracers, float[]? x, float[]? y)
    {
        if (_handle == IntPtr.Zero || tracers < 0 || tracers > _maxTracers) return false;
        if (x is null || x.Length < tracers || y is null || y.Length < tracers) return false;

        try { return MpCudaVortexDownload(_handle, tracers, x, y) == 0; }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        try { MpCudaVortexDestroy(_handle); }
        catch { /* the process is going down either way */ }
        _handle = IntPtr.Zero;
    }
}
