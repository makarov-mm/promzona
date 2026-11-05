using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Gray-Scott exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/rd_cuda.cu). Resident-state pattern and the same
/// never-throw contract as <see cref="CudaNBody"/>; CUDA availability is
/// probed once through <see cref="CudaNBody.Available"/> - one DLL, one
/// probe.
/// </summary>
public sealed class CudaReactionDiffusion : IReactionDiffusionBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaRdCreate")]
    private static extern int MpCudaRdCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaRdUpload")]
    private static extern int MpCudaRdUpload(IntPtr handle, int cells, float[] u, float[] v);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaRdStep")]
    private static extern int MpCudaRdStep(IntPtr handle, int n, float du, float dv, float feed, float kill, int steps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaRdDownload")]
    private static extern int MpCudaRdDownload(IntPtr handle, int cells, float[] u, float[] v);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaRdDestroy")]
    private static extern int MpCudaRdDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaReactionDiffusion? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;
        try
        {
            if (MpCudaRdCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaReactionDiffusion { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaReactionDiffusion() { }

    public bool Upload(int cells, float[]? u, float[]? v)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (u is null || u.Length < cells || v is null || v.Length < cells) return false;
        try { return MpCudaRdUpload(_handle, cells, u, v) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float du, float dv, float feed, float kill, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpCudaRdStep(_handle, n, du, dv, feed, kill, steps) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int cells, float[]? u, float[]? v)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (u is null || u.Length < cells || v is null || v.Length < cells) return false;
        try { return MpCudaRdDownload(_handle, cells, u, v) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaRdDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
