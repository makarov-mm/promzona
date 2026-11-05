namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the D3Q19 fluid fulfil - CUDA
/// (<see cref="CudaLbm3D"/>) and OpenCL (<see cref="ClLbm3D"/>): the
/// populations and the solid mask live on the device, a Step runs whole LBM
/// iterations there, Macros computes and hands back the density and velocity
/// fields once per rendered frame, and DownloadF returns the populations for
/// a handoff back to the CPU lattice. Never-throw contract: false means
/// "fall back", not "the program is broken".
/// </summary>
public interface ILbm3DBackend : IDisposable
{
    bool Upload(int cells, float[]? f, byte[]? solid);

    /// <param name="cs2">Smagorinsky C_s² (0 = no closure) - tail425.</param>
    bool Step(int nx, int ny, int nz, float tau, float forceX, int steps, float cs2);

    bool Macros(int nx, int ny, int nz, float forceX,
        float[]? rho, float[]? ux, float[]? uy, float[]? uz);

    bool DownloadF(int cells, float[]? f);
}
