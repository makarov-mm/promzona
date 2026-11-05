namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of ChargeInField fulfil - CUDA
/// (<see cref="CudaCharge"/>) and OpenCL (<see cref="ClCharge"/>): the
/// Boris pusher in uniform E and a mirror-perturbed B. Never-throw.
/// </summary>
public interface IChargeBackend : IDisposable
{
    bool Upload(int n, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz);
    bool Step(int n, float dt, float qmdt, float ex, float ey, float ez, float bx, float by, float bz, float mirror, int steps);
    bool Download(int n, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz);
}
