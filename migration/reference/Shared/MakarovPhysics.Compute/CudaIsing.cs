using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Ising exports of MakarovPhysics.Cuda.dll (see
/// Native/CudaNBody/ising_cuda.cu - the checkerboard Metropolis). Resident
/// pattern and the never-throw contract as everywhere; availability probed
/// once via <see cref="CudaNBody.Available"/>.
/// </summary>
public sealed class CudaIsing : IIsingBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaIsingCreate")]
    private static extern int MpCudaIsingCreate(int maxSites, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaIsingUpload")]
    private static extern int MpCudaIsingUpload(IntPtr handle, int sites, sbyte[] spin, ulong[] rng);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaIsingSweep")]
    private static extern int MpCudaIsingSweep(IntPtr handle, int l, float e4, float e8, int sweeps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaIsingDownload")]
    private static extern int MpCudaIsingDownload(IntPtr handle, int sites, sbyte[] spin);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaIsingDestroy")]
    private static extern int MpCudaIsingDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaIsing? TryCreate(int maxSites)
    {
        if (!CudaNBody.Available) return null;
        try
        {
            if (MpCudaIsingCreate(maxSites, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaIsing { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaIsing() { }

    public bool Upload(int sites, sbyte[]? spin, ulong[]? rng)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (spin is null || spin.Length < sites || rng is null || rng.Length < sites) return false;

        try
        {
            return MpCudaIsingUpload(_handle, sites, spin, rng) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Sweep(int l, float e4, float e8, int sweeps)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaIsingSweep(_handle, l, e4, e8, sweeps) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int sites, sbyte[]? spin)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (spin is null || spin.Length < sites) return false;

        try
        {
            return MpCudaIsingDownload(_handle, sites, spin) == 0;
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
            MpCudaIsingDestroy(_handle);
        }
        catch { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
