namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of WaveHeat fulfil - CUDA
/// (<see cref="CudaWaveHeat"/>) and OpenCL (<see cref="ClWaveHeat"/>): the
/// damped wave equation and the heat equation on one square grid. Same
/// shape as <see cref="IIsingBackend"/>; never-throw contract.
/// </summary>
public interface IWaveHeatBackend : IDisposable
{
    bool Upload(int cells, float[]? wave, float[]? wavePrev, float[]? heat);
    bool Step(int n, float cc, float gdt, float adt, int steps);
    bool Download(int cells, float[]? wave, float[] heat, float[]? wavePrev);
}
