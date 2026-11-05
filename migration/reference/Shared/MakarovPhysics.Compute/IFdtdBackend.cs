namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the FDTD module fulfil - CUDA
/// (<see cref="CudaFdtd"/>) and OpenCL (<see cref="ClFdtd"/>): the Yee
/// half-steps, the source, the loss frame with intensity accumulation.
/// Same shape as the other backend interfaces; never-throw contract.
/// </summary>
public interface IFdtdBackend : IDisposable
{
    bool Upload(int cells, float[]? ez, float[]? hx, float[]? hy, float[]? invEps, byte[]? pec, float[]? damp, float[]? ezSq);
    bool UploadDamp(int cells, float[]? damp);
    bool ClearIntensity(int cells);
    bool Step(int n, float s, float drive, bool line, int srcX, int srcY, int y0, int y1);
    bool DownloadEz(int cells, float[]? ez);
    bool DownloadAll(int cells, float[]? ez, float[]? hx, float[]? hy);
    bool DownloadIntensity(int cells, float[]? ezSq);
}
