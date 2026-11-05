using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the TurbulenceLab exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/turb_cuda.cu). The device owns the velocity field,
/// the pressure warm-start and the wing mask; the whole solver pipeline runs
/// in one Step call. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaTurbulence : ITurbulenceBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbCreate")]
    private static extern int MpCudaTurbCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbUpload")]
    private static extern int MpCudaTurbUpload(IntPtr handle, int cells, float[] vx, float[] vy, float[] vz, float[] p, byte[] solid);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbUploadScalars")]
    private static extern int MpCudaTurbUploadScalars(IntPtr handle, int cells, float[] k, float[] w, float[] wallDist);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbUploadSolid")]
    private static extern int MpCudaTurbUploadSolid(IntPtr handle, int cells, byte[] solid);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbStep")]
    private static extern int MpCudaTurbStep(IntPtr handle, int nx, int ny, int nz,
        float dt, float diffK, float confinement, float dx, float flowSpeed,
        float time, float domX, float domY, float domZ, int jacobiIters,
        int sgsModel, float sgsCoef, float viscosity, float kIn, float wIn, float wWall);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbDownload")]
    private static extern int MpCudaTurbDownload(IntPtr handle, int cells, float[] vx, float[] vy, float[] vz, float[] wmag, float[]? nut, float[]? k, float[]? w, float[]? p);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaTurbDestroy")]
    private static extern int MpCudaTurbDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaTurbulence? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaTurbCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaTurbulence { _handle = handle };
        }
        catch
        {
            return null;
        }
    }

    private CudaTurbulence() { }

    public bool Upload(int cells, float[] vx, float[] vy, float[] vz, float[] p, byte[] solid)
    {
        if (_handle == IntPtr.Zero) return false;
        if (vx.Length < cells || vy.Length < cells || vz.Length < cells || p.Length < cells || solid.Length < cells) return false;

        try
        {
            return MpCudaTurbUpload(_handle, cells, vx, vy, vz, p, solid) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UploadSolid(int cells, byte[] solid)
    {
        if (_handle == IntPtr.Zero || solid is null || solid.Length < cells) return false;

        try
        {
            return MpCudaTurbUploadSolid(_handle, cells, solid) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UploadScalars(int cells, float[] k, float[] w, float[] wallDist)
    {
        if (_handle == IntPtr.Zero) return false;
        if (k is null || k.Length < cells || w is null || w.Length < cells || wallDist is null || wallDist.Length < cells) return false;
        try { return MpCudaTurbUploadScalars(_handle, cells, k, w, wallDist) == 0; }
        catch { return false; }
    }

    public bool Step(int nx, int ny, int nz, float dt, float diffK, float confinement,
        float dx, float flowSpeed, float time, float domX, float domY, float domZ, int jacobiIters,
        int sgsModel, float sgsCoef, float viscosity, float kIn, float wIn, float wWall)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaTurbStep(_handle, nx, ny, nz, dt, diffK, confinement, dx, flowSpeed, time, domX, domY, domZ, jacobiIters,
                sgsModel, sgsCoef, viscosity, kIn, wIn, wWall) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int cells, float[]? vx, float[]? vy, float[]? vz, float[]? wmag, float[]? nut, float[]? k, float[]? w, float[]? p)
    {
        if (p is not null && p.Length < cells) return false;
        if (_handle == IntPtr.Zero) return false;
        if (vx is null || vx.Length < cells || vy is null || vy.Length < cells || vz is null || vz.Length < cells || wmag is null || wmag.Length < cells) return false;
        if (nut is not null && nut.Length < cells) return false;
        if (k is not null && k.Length < cells) return false;
        if (w is not null && w.Length < cells) return false;

        try
        {
            return MpCudaTurbDownload(_handle, cells, vx, vy, vz, wmag, nut, k, w, p) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaTurbDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
