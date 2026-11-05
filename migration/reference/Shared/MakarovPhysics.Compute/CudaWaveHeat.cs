using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the WaveHeat exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/waveheat_cuda.cu). Resident pattern; the per-frame
/// download carries wave + heat, and additionally wavePrev on a CPU handoff
/// so the leapfrog continues the exact trajectory. Never-throw contract.
/// </summary>
public sealed class CudaWaveHeat : IWaveHeatBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaWhCreate")]
    private static extern int MpCudaWhCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaWhUpload")]
    private static extern int MpCudaWhUpload(IntPtr handle, int cells, float[] wave, float[] wavePrev, float[] heat);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaWhStep")]
    private static extern int MpCudaWhStep(IntPtr handle, int n, float cc, float gdt, float adt, int steps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaWhDownload")]
    private static extern int MpCudaWhDownload(IntPtr handle, int cells, float[] wave, float[] heat, float[]? wavePrev);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaWhDestroy")]
    private static extern int MpCudaWhDestroy(IntPtr handle);
    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaWaveHeat? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaWhCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;

            return new CudaWaveHeat { _handle = handle };
        }
        catch
        {
            return null;
        }
    }

    private CudaWaveHeat() { }

    public bool Upload(int cells, float[]? wave, float[]? wavePrev, float[]? heat)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (wave is null || wave.Length < cells || wavePrev is null || wavePrev.Length < cells || heat is null || heat.Length < cells) return false;

        try
        {
            return MpCudaWhUpload(_handle, cells, wave, wavePrev, heat) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, float cc, float gdt, float adt, int steps)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaWhStep(_handle, n, cc, gdt, adt, steps) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int cells, float[]? wave, float[] heat, float[]? wavePrev)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if ((wave is not null && wave.Length < cells) || heat.Length < cells || (wavePrev is not null && wavePrev.Length < cells)) return false;

        try
        {
            return MpCudaWhDownload(_handle, cells, wave!, heat, wavePrev) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaWhDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
