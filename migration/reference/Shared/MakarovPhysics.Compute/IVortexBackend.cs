namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of PointVortices fulfil - CUDA
/// (<see cref="CudaVortex"/>) and OpenCL (<see cref="ClVortex"/>): tracer
/// advection through per-substep vortex snapshots with the midpoint (RK2)
/// scheme. Never-throw contract.
/// </summary>
public interface IVortexBackend : IDisposable
{
    bool UploadTracers(int tracers, float[]? x, float[]? y);
    bool Advect(int tracers, int count, int substeps, float dt, float eps2,
        float[]? snapX, float[]? snapY, float[]? snapG);
    bool Download(int tracers, float[]? x, float[]? y);
}
