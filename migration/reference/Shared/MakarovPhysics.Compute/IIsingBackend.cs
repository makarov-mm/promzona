namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the Ising model fulfil - CUDA
/// (<see cref="CudaIsing"/>) and OpenCL (<see cref="ClIsing"/>), both
/// running the checkerboard Metropolis with per-site xorshift64 states.
/// Same shape as <see cref="ILbm2DBackend"/>: one surface keeps the model's
/// resident/demotion protocol single-pathed. Never-throw contract.
/// </summary>
public interface IIsingBackend : IDisposable
{
    bool Upload(int sites, sbyte[]? spin, ulong[]? rng);
    bool Sweep(int l, float e4, float e8, int sweeps);
    bool Download(int sites, sbyte[]? spin);
}
