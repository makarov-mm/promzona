namespace MakarovPhysics.Compute;

/// <summary>
/// The contract of the electron-optics GPU backends (CUDA
/// <see cref="CudaElectronOptics"/>, OpenCL <see cref="ClElectronOptics"/>):
/// the tube's solved field goes up once per field change as (Ez, Er) grids
/// plus the deflection unit gradients and the electrode mask; every frame
/// the electrons go up with their birth substep, the frame's substeps run
/// on the device, and positions, velocities and the dead flags come down.
/// An electron that reaches the screen, the wall or an electrode is frozen
/// where it happened and flagged, so the host's Collect scores it there.
/// Never-throw contract: false means "fall back to the CPU".
/// </summary>
public interface IElectronOpticsBackend : IDisposable
{
    bool UploadField(int gridCells, float[] ez, float[] er, byte[] group, int deflCells, float[] dz, float[] dt);

    bool Upload(int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, int[] birth);

    bool Step(int n, int substeps, float dt,
        float tubeLength, float tubeRadius, float screenZ, int nz, int nr,
        float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0, float deflY, float deflZ, float eOverM);

    bool Download(int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, byte[] dead);
}
