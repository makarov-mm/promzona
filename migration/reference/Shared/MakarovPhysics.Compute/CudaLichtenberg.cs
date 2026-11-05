using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Lichtenberg/DBM exports of
/// MakarovPhysics.Cuda.dll (see Native/CudaNBody/lich_cuda.cu). The device
/// owns the potential and runs red-black SOR; growth and painting poke
/// single sites; the potential comes down per tick for the CPU growth
/// pass and the renderer. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaLichtenberg : IDisposable
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLichCreate")]
    private static extern int MpCudaLichCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLichUpload")]
    private static extern int MpCudaLichUpload(IntPtr handle, int cells, float[] phi, byte[] state);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLichRelax")]
    private static extern int MpCudaLichRelax(IntPtr handle, int l, float omega, int sweeps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLichSetSite")]
    private static extern int MpCudaLichSetSite(IntPtr handle, int idx, float phi, byte state);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLichDownloadPhi")]
    private static extern int MpCudaLichDownloadPhi(IntPtr handle, int cells, float[] phi);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLichDestroy")]
    private static extern int MpCudaLichDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaLichtenberg? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaLichCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaLichtenberg
            {
                _handle = handle
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaLichtenberg() { }

    public bool Upload(int cells, float[]? phi, byte[]? state)
    {
        if (_handle == IntPtr.Zero || phi is null || phi.Length < cells || state is null || state.Length < cells) return false;

        try
        {
            return MpCudaLichUpload(_handle, cells, phi, state) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Relax(int l, float omega, int sweeps)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaLichRelax(_handle, l, omega, sweeps) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool SetSite(int idx, float phi, byte state)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaLichSetSite(_handle, idx, phi, state) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadPhi(int cells, float[]? phi)
    {
        if (_handle == IntPtr.Zero || phi is null || phi.Length < cells) return false;

        try
        {
            return MpCudaLichDownloadPhi(_handle, cells, phi) == 0;
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
            MpCudaLichDestroy(_handle);
        }
        catch { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
