namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of Schrodinger2D fulfil - CUDA
/// (<see cref="CudaSchrodinger2D"/>) and OpenCL
/// (<see cref="ClSchrodinger2D"/>): the split-step method (half potential
/// kick, FFT, kinetic phase, inverse FFT, half kick, sponge).
/// Never-throw contract.
/// </summary>
public interface ISchrodinger2DBackend : IDisposable
{
    bool Upload(int n, float[] re, float[] im, float[] vBase, float[] vWalls,
        float[] k2, float[] damp);
    bool UploadWalls(int n, float[] vWalls);
    bool UploadDamp(int n, float[] damp);
    bool Step(int n, float kickF, float kinF, bool absorb);
    bool Download(int n, float[] re, float[] im);
}
