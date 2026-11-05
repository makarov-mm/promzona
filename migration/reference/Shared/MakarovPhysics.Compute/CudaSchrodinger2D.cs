using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the split-step Schrodinger exports of
/// MakarovPhysics.Cuda.dll (see Native/CudaNBody/sch_cuda.cu, including the
/// in-house radix-2 shared-memory FFT - no cuFFT dependency). Resident-state
/// pattern and the never-throw contract as everywhere; CUDA availability is
/// probed once through <see cref="CudaNBody.Available"/>.
/// </summary>
public sealed class CudaSchrodinger2D : ISchrodinger2DBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchCreate")]
    private static extern int MpCudaSchCreate(int maxN, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchUpload")]
    private static extern int MpCudaSchUpload(IntPtr handle, int n,
        float[] re, float[] im, float[] vBase, float[] vWalls, float[] k2, float[] damp);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchUploadWalls")]
    private static extern int MpCudaSchUploadWalls(IntPtr handle, int n, float[] vWalls);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchUploadDamp")]
    private static extern int MpCudaSchUploadDamp(IntPtr handle, int n, float[] damp);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchStep")]
    private static extern int MpCudaSchStep(IntPtr handle, int n, float kickF, float kinF, int absorb);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchDownload")]
    private static extern int MpCudaSchDownload(IntPtr handle, int n, float[] re, float[] im);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSchDestroy")]
    private static extern int MpCudaSchDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start (also for non-power-of-two
    /// grids - the FFT kernel is radix-2 only).</summary>
    public static CudaSchrodinger2D? TryCreate(int maxN)
    {
        if (!CudaNBody.Available) return null;
        if (maxN < 4 || (maxN & (maxN - 1)) != 0) return null;
        try
        {
            if (MpCudaSchCreate(maxN, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaSchrodinger2D { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaSchrodinger2D() { }

    public bool Upload(int n, float[] re, float[] im, float[] vBase, float[] vWalls,
        float[] k2, float[] damp)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        int cells;
        try { cells = checked(n * n); } catch (OverflowException) { return false; }
        // n is the grid SIDE - the four field arrays hold n*n
        // (the old guard under-checked by a factor of n; a short array would
        // pin fewer elements than the native n*n memcpy touches)
        if (re is null || re.Length < cells || im is null || im.Length < cells
            || vBase is null || vBase.Length < cells || vWalls is null || vWalls.Length < cells
            || k2 is null || k2.Length < n || damp is null || damp.Length < n) return false;
        try { return MpCudaSchUpload(_handle, n, re, im, vBase, vWalls, k2, damp) == 0; }
        catch (Exception) { return false; }
    }

    public bool UploadWalls(int n, float[] vWalls)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        int wcells;
        try { wcells = checked(n * n); } catch (OverflowException) { return false; }
        if (vWalls is null || vWalls.Length < wcells) return false;
        try { return MpCudaSchUploadWalls(_handle, n, vWalls) == 0; }
        catch (Exception) { return false; }
    }

    public bool UploadDamp(int n, float[] damp)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (damp is null || damp.Length < n) return false;
        try { return MpCudaSchUploadDamp(_handle, n, damp) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float kickF, float kinF, bool absorb)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpCudaSchStep(_handle, n, kickF, kinF, absorb ? 1 : 0) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int n, float[] re, float[] im)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        int dcells;
        try { dcells = checked(n * n); } catch (OverflowException) { return false; }
        if (re is null || re.Length < dcells || im is null || im.Length < dcells) return false;
        try { return MpCudaSchDownload(_handle, n, re, im) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaSchDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
