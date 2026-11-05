using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the 2D LBM exports of MakarovPhysics.Cuda.dll (see
/// Native/CudaNBody/lbm2d_cuda.cu). Resident pattern with per-view split
/// downloads (dye pair / velocity pair / density); the population block
/// comes down only on a CPU handoff. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaLbm2D : ILbm2DBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2Create")]
    private static extern int MpCudaLbm2Create(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2Upload")]
    private static extern int MpCudaLbm2Upload(IntPtr handle, int cells, float[] f, byte[] solid, float[] dyeA, float[] dyeB);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2Step")]
    private static extern int MpCudaLbm2Step(IntPtr handle, int w, int h, float omega, float inflow, int mrt, int noSlip, int inject, int stripePeriod, int steps, int turb, float cs2);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2DownloadNut")]
    private static extern int MpCudaLbm2DownloadNut(IntPtr handle, int cells, float[] nut);
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2UploadVelocity")]
    private static extern int MpCudaLbm2UploadVelocity(IntPtr handle, int cells, float[] velx, float[] vely);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2DownloadDye")]
    private static extern int MpCudaLbm2DownloadDye(IntPtr handle, int cells, float[] dyeA, float[] dyeB);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2DownloadVelocity")]
    private static extern int MpCudaLbm2DownloadVelocity(IntPtr handle, int cells, float[] velx, float[] vely);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2DownloadDensity")]
    private static extern int MpCudaLbm2DownloadDensity(IntPtr handle, int cells, float[] rho);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2DownloadF")]
    private static extern int MpCudaLbm2DownloadF(IntPtr handle, int cells, float[] f);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbm2Destroy")]
    private static extern int MpCudaLbm2Destroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaLbm2D? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaLbm2Create(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaLbm2D
            {
                _handle = handle
            };
        }
        catch
        {
            return null;
        }
    }

    private CudaLbm2D() { }

    public bool Upload(int cells, float[]? f, byte[]? solid, float[]? dyeA, float[]? dyeB)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (f is null || f.Length < 9 * cells || solid is null || solid.Length < cells || dyeA is null || dyeA.Length < cells || dyeB is null || dyeB.Length < cells) return false;
        try { return MpCudaLbm2Upload(_handle, cells, f, solid, dyeA, dyeB) == 0; }
        catch (Exception) { return false; }
    }

    public bool UploadVelocity(int cells, float[]? velx, float[]? vely)
    {
        if (_handle == IntPtr.Zero || velx is null || velx.Length < cells || vely is null || vely.Length < cells) return false;
        // an older native DLL has no such entry point: the kernel zeroes the
        // field at creation, and the caller accepts a zero start
        try { return MpCudaLbm2UploadVelocity(_handle, cells, velx, vely) == 0; }
        catch (Exception) { return false; }
    }

    public bool DownloadNut(int cells, float[]? nut)
    {
        if (_handle == IntPtr.Zero || nut is null || nut.Length < cells) return false;
        try { return MpCudaLbm2DownloadNut(_handle, cells, nut) == 0; }
        catch { return false; }
    }

    public bool Step(int w, int h, float omega, float inflow, bool mrt, bool noSlip,
        bool inject, int stripePeriod, int steps, int turb, float cs2)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaLbm2Step(_handle, w, h, omega, inflow, mrt ? 1 : 0, noSlip ? 1 : 0, inject ? 1 : 0, stripePeriod, steps, turb, cs2) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadDye(int cells, float[]? dyeA, float[]? dyeB)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (dyeA is null || dyeA.Length < cells || dyeB is null || dyeB.Length < cells) return false;

        try
        {
            return MpCudaLbm2DownloadDye(_handle, cells, dyeA, dyeB) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadVelocity(int cells, float[]? velx, float[]? vely)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (velx is null || velx.Length < cells || vely is null || vely.Length < cells) return false;

        try
        {
            return MpCudaLbm2DownloadVelocity(_handle, cells, velx, vely) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadDensity(int cells, float[]? rho)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (rho is null || rho.Length < cells) return false;

        try
        {
            return MpCudaLbm2DownloadDensity(_handle, cells, rho) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadF(int cells, float[]? f)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (f is null || f.Length < 9 * cells) return false;

        try
        {
            return MpCudaLbm2DownloadF(_handle, cells, f) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        try
        {
            MpCudaLbm2Destroy(_handle);
        }
        catch (Exception) { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
