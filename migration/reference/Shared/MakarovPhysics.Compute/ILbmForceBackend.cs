namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the FORCED D3Q19 tunnel fulfil - CUDA
/// (<see cref="CudaLbmForce"/>) and OpenCL (<see cref="ClLbmForce"/>) - for
/// BetzFlow's actuator line. Unlike <see cref="ILbm3DBackend"/> the body
/// force is not one number but a FIELD: Step takes the actuator elements
/// (position and force on the air, lattice units) and the kernel width, and
/// the device deposits them with the Gaussian kernel before every collision.
/// Boundaries are the tunnel's - velocity inlet at x = 0, copy-outlet at
/// x = NX-1, periodic y and z. Macros hands back rho and u (with the Guo
/// half-force) for the rotor's next sampling and for the renderer. Never
/// throw: false means "fall back".
/// </summary>
public interface ILbmForceBackend : IDisposable
{
    bool Upload(int cells, float[]? f);

    /// <param name="elemPos">3 floats per element (x, y, z), lattice units</param>
    /// <param name="elemForce">3 floats per element: force ON THE AIR</param>
    bool Step(int nx, int ny, int nz, float tau, float uIn,
        int elements, float[]? elemPos, float[]? elemForce, float eps, int steps);

    /// <summary>Download macroscopic fields. Any argument may be NULL and is
    /// then not transferred - the caller asks for what it needs, and on a
    /// large lattice the transfer is the frame (28.08). All four null is an
    /// error, because it means the caller asked for nothing.</summary>
    bool Macros(int nx, int ny, int nz, float[]? rho, float[]? ux, float[]? uy, float[]? uz);

    bool DownloadF(int cells, float[]? f);
}
