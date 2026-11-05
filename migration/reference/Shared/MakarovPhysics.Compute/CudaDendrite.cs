using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the DLA dendrite exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/den_cuda.cu). Resident walkers + cell grid; the
/// stats quad (count, extent, edge flag) comes down every tick, the grid
/// once per frame. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaDendrite : IDendriteBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenCreate")]
    private static extern int MpCudaDenCreate(int maxCells, int maxWalkers, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenUpload")]
    private static extern int MpCudaDenUpload(IntPtr handle, int cells, int walkers, int[] grid, int[] wx, int[] wy, ulong[] rng, int count, int maxHeight, float maxRadius, int edgeReached);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenStep")]
    private static extern int MpCudaDenStep(IntPtr handle, int grid, int walkers, int moves, int pointSeed, float stickP, int target, int ticks);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenDownloadCells")]
    private static extern int MpCudaDenDownloadCells(IntPtr handle, int cells, int[] grid);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenDownloadWalkers")]
    private static extern int MpCudaDenDownloadWalkers(IntPtr handle, int walkers, int[] wx, int[] wy, ulong[] rng);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenDownloadStats")]
    private static extern int MpCudaDenDownloadStats(IntPtr handle, out int count, out int maxHeight, out float maxRadius, out int edgeReached);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaDenDestroy")]
    private static extern int MpCudaDenDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaDendrite? TryCreate(int maxCells, int maxWalkers)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaDenCreate(maxCells, maxWalkers, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaDendrite
            {
                _handle = handle
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaDendrite() { }

    public bool Upload(int cells, int walkers, int[]? grid, int[]? wx, int[]? wy, ulong[]? rng, int count, int maxHeight, float maxRadius, bool edgeReached)
    {
        if (_handle == IntPtr.Zero) return false;
        if (grid is null || grid.Length < cells || wx is null || wx.Length < walkers || wy is null || wy.Length < walkers || rng is null || rng.Length < walkers) return false;

        try
        {
            return MpCudaDenUpload(_handle, cells, walkers, grid, wx, wy, rng, count, maxHeight, maxRadius, edgeReached ? 1 : 0) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int grid, int walkers, int moves, bool pointSeed, float stickP, int target, int ticks)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaDenStep(_handle, grid, walkers, moves, pointSeed ? 1 : 0, stickP, target, ticks) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadCells(int cells, int[]? grid)
    {
        if (_handle == IntPtr.Zero || grid is null || grid.Length < cells) return false;

        try
        {
            return MpCudaDenDownloadCells(_handle, cells, grid) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadWalkers(int walkers, int[]? wx, int[]? wy, ulong[]? rng)
    {
        if (_handle == IntPtr.Zero || wx is null || wx.Length < walkers || wy is null || wy.Length < walkers || rng is null || rng.Length < walkers) return false;

        try
        {
            return MpCudaDenDownloadWalkers(_handle, walkers, wx, wy, rng) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadStats(out int count, out int maxHeight, out float maxRadius, out bool edgeReached)
    {
        count = 0;
        maxHeight = 0;
        maxRadius = 0;
        edgeReached = false;

        if (_handle == IntPtr.Zero) return false;

        try
        {
            bool ok = MpCudaDenDownloadStats(_handle, out count, out maxHeight, out maxRadius, out int edge) == 0;
            edgeReached = edge != 0;
            return ok;
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
            MpCudaDenDestroy(_handle);
        }
        catch
        {
             /* teardown is best-effort */
        }

        _handle = IntPtr.Zero;
    }
}
