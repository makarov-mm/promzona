using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the 3D cellular-automaton exports of
/// MakarovPhysics.Cuda.dll (see Native/CudaNBody/ca3d_cuda.cu). Resident
/// pattern; the per-frame download is the whole byte grid for the voxel
/// renderer. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaAutomata3D : IAutomata3DBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaCa3dCreate")]
    private static extern int MpCudaCa3dCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaCa3dUpload")]
    private static extern int MpCudaCa3dUpload(IntPtr handle, int cells, byte[] grid);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaCa3dStep")]
    private static extern int MpCudaCa3dStep(IntPtr handle, int n, int wrap, uint survive, uint birth, int maxState, int steps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaCa3dDownload")]
    private static extern int MpCudaCa3dDownload(IntPtr handle, int cells, byte[] grid);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaCa3dDestroy")]
    private static extern int MpCudaCa3dDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaAutomata3D? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaCa3dCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaAutomata3D
            {
                _handle = handle
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaAutomata3D() { }

    public bool Upload(int cells, byte[]? grid)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (grid is null || grid.Length < cells) return false;

        try
        {
            return MpCudaCa3dUpload(_handle, cells, grid) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, bool wrap, uint survive, uint birth, int maxState, int steps)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaCa3dStep(_handle, n, wrap ? 1 : 0, survive, birth, maxState, steps) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int cells, byte[]? grid)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (grid is null || grid.Length < cells) return false;

        try
        {
            return MpCudaCa3dDownload(_handle, cells, grid) == 0;
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
            MpCudaCa3dDestroy(_handle);
        }
        catch { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
