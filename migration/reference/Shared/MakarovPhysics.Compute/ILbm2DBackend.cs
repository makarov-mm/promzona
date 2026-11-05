namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of Lbm2D fulfil - CUDA
/// (<see cref="CudaLbm2D"/>) and OpenCL (<see cref="ClLbm2D"/>). One surface
/// keeps the model's resident/demotion protocol single-pathed: the model
/// holds an ILbm2DBackend and does not care which API is underneath.
/// Never-throw contract: every method returns false instead of raising.
/// </summary>
public interface ILbm2DBackend : IDisposable
{
    bool Upload(int cells, float[]? f, byte[]? solid, float[]? dyeA, float[]? dyeB);
    /// <summary>The velocity field WALE reads on its first pass after an
    /// upload: the model's own u of the previous step. Without it the first
    /// WALE pass read whatever the buffer held - never written on OpenCL
    /// (external audit 22.09, A02). Returns false when the backend cannot
    /// take it; the caller then falls back to a zero field.</summary>
    bool UploadVelocity(int cells, float[]? velx, float[]? vely);
    /// <param name="turb">0 none, 1 Smagorinsky, 2 WALE (tail424).</param>
    /// <param name="cs2">C_s² (Smagorinsky) or C_w² (WALE).</param>
    bool Step(int w, int h, float omega, float inflow, bool mrt, bool noSlip,
        bool inject, int stripePeriod, int steps, int turb, float cs2);
    bool DownloadNut(int cells, float[]? nut);
    bool DownloadDye(int cells, float[]? dyeA, float[]? dyeB);
    bool DownloadVelocity(int cells, float[]? velx, float[]? vely);
    bool DownloadDensity(int cells, float[]? rho);
    bool DownloadF(int cells, float[]? f);
}
