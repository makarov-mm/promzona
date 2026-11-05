using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the LBM D3Q19 exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/lbm3d_cuda.cu), resident-state edition: Upload the
/// populations and the solid mask once per reset/handoff, Step runs whole
/// LBM iterations device-side, Macros downloads the velocity and density
/// fields once per rendered frame, DownloadF hands the populations back for
/// a CUDA-to-CPU switch. Same never-throw contract as <see cref="CudaNBody"/>:
/// every environmental failure is a reported outcome and the caller falls
/// back to its CPU path. CUDA availability itself is probed once via
/// <see cref="CudaNBody.Available"/> - one DLL, one probe.
/// </summary>
public sealed class CudaLbm3D : ILbm3DBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmCreate")]
    private static extern int MpCudaLbmCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmUpload")]
    private static extern int MpCudaLbmUpload(IntPtr handle, int cells, float[] f, byte[] solid);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmStep")]
    private static extern int MpCudaLbmStep(IntPtr handle, int nx, int ny, int nz, float tau, float forceX, int steps, float cs2);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmMacros")]
    private static extern int MpCudaLbmMacros(IntPtr handle, int nx, int ny, int nz, float forceX, float[] rho, float[] ux, float[] uy, float[] uz);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmDownloadF")]
    private static extern int MpCudaLbmDownloadF(IntPtr handle, int cells, float[] f);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmDestroy")]
    private static extern int MpCudaLbmDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaLbm3D? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaLbmCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaLbm3D
            {
                _handle = handle
            };
        }
        catch
        {
            return null;
        }
    }

    private CudaLbm3D() { }

    /// <summary>Populations (SoA float, direction-major) + solid mask up.</summary>
    public bool Upload(int cells, float[]? f, byte[]? solid)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (f is null || f.Length < 19 * cells || solid is null || solid.Length < cells) return false;
        try { return MpCudaLbmUpload(_handle, cells, f, solid) == 0; }
        catch (Exception) { return false; }
    }

    /// <summary>Whole LBM iterations on the device; synchronizes at the end
    /// so the caller's stopwatch measures real GPU work.</summary>
    public bool Step(int nx, int ny, int nz, float tau, float forceX, int steps, float cs2)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaLbmStep(_handle, nx, ny, nz, tau, forceX, steps, cs2) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Macro fields down (once per rendered frame).</summary>
    public bool Macros(int nx, int ny, int nz, float forceX, float[]? rho, float[]? ux, float[]? uy, float[]? uz)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        int cells;

        try
        {
            cells = checked(nx * ny * nz);
        }
        catch (OverflowException)
        {
            return false;
        }

        // the requirement is the CELL count, not the x-extent
        if (rho is null || rho.Length < cells || ux is null || ux.Length < cells || uy is null || uy.Length < cells || uz is null || uz.Length < cells) return false;

        try
        {
            return MpCudaLbmMacros(_handle, nx, ny, nz, forceX, rho, ux, uy, uz) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Populations down (CUDA-to-CPU handoff).</summary>
    public bool DownloadF(int cells, float[]? f)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (f is null || f.Length < 19 * cells) return false;

        try
        {
            return MpCudaLbmDownloadF(_handle, cells, f) == 0;
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
            MpCudaLbmDestroy(_handle);
        }
        catch { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
