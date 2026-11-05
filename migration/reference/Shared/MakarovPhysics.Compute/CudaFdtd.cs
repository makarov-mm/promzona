using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the FDTD exports of MakarovPhysics.Cuda.dll (see
/// Native/CudaNBody/fdtd_cuda.cu). Resident pattern with split downloads:
/// Ez alone feeds the renderer each frame, the full field triple comes down
/// only for the energy observable and the CPU handoff, and the intensity
/// accumulator has its own lazy path. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaFdtd : IFdtdBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdCreate")]
    private static extern int MpCudaFdtdCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdUpload")]
    private static extern int MpCudaFdtdUpload(IntPtr handle, int cells,
        float[] ez, float[] hx, float[] hy, float[] invEps, byte[] pec, float[] damp, float[] ezSq);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdUploadDamp")]
    private static extern int MpCudaFdtdUploadDamp(IntPtr handle, int cells, float[] damp);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdClearIntensity")]
    private static extern int MpCudaFdtdClearIntensity(IntPtr handle, int cells);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdStep")]
    private static extern int MpCudaFdtdStep(IntPtr handle, int n, float s, float drive,
        int line, int srcX, int srcY, int y0, int y1);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdDownloadEz")]
    private static extern int MpCudaFdtdDownloadEz(IntPtr handle, int cells, float[] ez);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdDownloadAll")]
    private static extern int MpCudaFdtdDownloadAll(IntPtr handle, int cells, float[] ez, float[] hx, float[] hy);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdDownloadIntensity")]
    private static extern int MpCudaFdtdDownloadIntensity(IntPtr handle, int cells, float[] ezSq);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaFdtdDestroy")]
    private static extern int MpCudaFdtdDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaFdtd? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaFdtdCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaFdtd
            {
                _handle = handle
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaFdtd() { }

    public bool Upload(int cells, float[]? ez, float[]? hx, float[]? hy, float[]? invEps, byte[]? pec, float[]? damp, float[]? ezSq)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead

        if (ez is null || ez.Length < cells || hx is null || hx.Length < cells || hy is null || hy.Length < cells || invEps is null || invEps.Length < cells || pec is null || pec.Length < cells || damp is null || damp.Length < cells || ezSq is null || ezSq.Length < cells) return false;
        
        try
        {
            return MpCudaFdtdUpload(_handle, cells, ez, hx, hy, invEps, pec, damp, ezSq) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UploadDamp(int cells, float[]? damp)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (damp is null || damp.Length < cells) return false;

        try
        {
            return MpCudaFdtdUploadDamp(_handle, cells, damp) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool ClearIntensity(int cells)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaFdtdClearIntensity(_handle, cells) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, float s, float drive, bool line, int srcX, int srcY, int y0, int y1)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaFdtdStep(_handle, n, s, drive, line ? 1 : 0, srcX, srcY, y0, y1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadEz(int cells, float[]? ez)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (ez is null || ez.Length < cells) return false;

        try
        {
            return MpCudaFdtdDownloadEz(_handle, cells, ez) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadAll(int cells, float[]? ez, float[]? hx, float[]? hy)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (ez is null || ez.Length < cells || hx is null || hx.Length < cells || hy is null || hy.Length < cells) return false;

        try
        {
            return MpCudaFdtdDownloadAll(_handle, cells, ez, hx, hy) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool DownloadIntensity(int cells, float[]? ezSq)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (ezSq is null || ezSq.Length < cells) return false;

        try
        {
            return MpCudaFdtdDownloadIntensity(_handle, cells, ezSq) == 0;
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
            MpCudaFdtdDestroy(_handle);
        }
        catch { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
