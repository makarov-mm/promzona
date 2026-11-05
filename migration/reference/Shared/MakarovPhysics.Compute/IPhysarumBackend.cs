namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of Physarum fulfil - CUDA
/// (<see cref="CudaPhysarum"/>) and OpenCL (<see cref="ClPhysarum"/>): the
/// agent sense-turn-move kernel, the trail deposit, the food deposit and
/// the separable blur with decay. Never-throw contract.
/// </summary>
public interface IPhysarumBackend : IDisposable
{
    bool Upload(int grid, int agents, float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng, float[] field);
    bool UploadFood(int count, int[] foodX, int[] foodY);
    bool Step(int grid, int agents,
        float sensorDist, float sensorAngle, float turnRate, float jitter, float stepSize,
        float deposit, float decay, float foodDeposit, int foodCount, int steps);
    bool DownloadField(int cells, float[] field);
    bool DownloadAgents(int agents, float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng);
}
