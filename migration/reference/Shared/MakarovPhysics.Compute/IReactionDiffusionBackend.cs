namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of Gray-Scott fulfil - CUDA
/// (<see cref="CudaReactionDiffusion"/>) and OpenCL
/// (<see cref="ClReactionDiffusion"/>). Same shape as
/// <see cref="IWaveHeatBackend"/>; never-throw contract.
/// </summary>
public interface IReactionDiffusionBackend : IDisposable
{
    bool Upload(int cells, float[]? u, float[]? v);
    bool Step(int n, float du, float dv, float feed, float kill, int steps);
    bool Download(int cells, float[]? u, float[]? v);
}
