namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the DLA dendrite fulfil - CUDA
/// (<see cref="CudaDendrite"/>) and OpenCL (<see cref="ClDendrite"/>): the
/// occupancy grid and the walker streams live on the device, a Step runs
/// whole ticks of walker moves, and the host pulls the grid, the walkers and
/// the four-slot statistics block back when it needs to draw or decide.
/// Never-throw contract: false means "fall back to the CPU walker loop".
/// </summary>
public interface IDendriteBackend : IDisposable
{
    bool Upload(int cells, int walkers, int[]? grid, int[]? wx, int[]? wy, ulong[]? rng,
        int count, int maxHeight, float maxRadius, bool edgeReached);

    bool Step(int grid, int walkers, int moves, bool pointSeed, float stickP, int target, int ticks);

    bool DownloadCells(int cells, int[]? grid);

    bool DownloadWalkers(int walkers, int[]? wx, int[]? wy, ulong[]? rng);

    bool DownloadStats(out int count, out int maxHeight, out float maxRadius, out bool edgeReached);
}
