namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the n-body family fulfil - CUDA
/// (<see cref="CudaNBody"/>) and OpenCL (<see cref="ClNBody"/>): resident
/// body state, whole leapfrog iterations run without transfers, positions
/// and velocities pulled back once per rendered frame. Never-throw contract:
/// every member returns false rather than throwing, and false means "fall
/// back to the CPU", not "the program is broken".
///
/// Galaxy is the client that needed it (23.08). Until then the model held a
/// <c>CudaNBody?</c> by its concrete type, which is exactly why the module
/// ran on the CPU on every machine without an NVIDIA card - there was no
/// seam to hang a second backend on.
/// </summary>
public interface INBodyBackend : IDisposable
{
    /// <summary>Full state up; invalidates any cached acceleration.</summary>
    bool Upload(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz, float[] mass);

    /// <summary>Run whole leapfrog (KDK) iterations on the device.</summary>
    bool Step(int n, float dt, float eps2, int steps);

    /// <summary>Positions and velocities back to the host.</summary>
    bool Download(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz);
}
