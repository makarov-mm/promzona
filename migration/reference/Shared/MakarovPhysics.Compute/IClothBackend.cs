namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the XPBD cloth fulfil - CUDA
/// (<see cref="CudaCloth"/>) and OpenCL (<see cref="ClCloth"/>): particle
/// state and the constraint graph live on the device, a Step runs the whole
/// substep there (normals, prediction, the Jacobi iterations, velocity
/// recovery), and Download hands positions and velocities back. Never-throw
/// contract: false means "fall back to the CPU Gauss-Seidel solver".
///
/// Upload comes BEFORE UploadConstraints: the OpenCL backend builds a
/// particle-to-constraint list at that point and needs to know how many
/// particles there are.
/// </summary>
public interface IClothBackend : IDisposable
{
    bool Upload(int count, float[]? x, float[]? y, float[]? z,
        float[]? vx, float[]? vy, float[]? vz, byte[]? pinned);

    bool UploadConstraints(int count, int[]? ci, int[]? cj, float[]? l0, byte[]? ctype);

    bool Step(int gridN, int count, int constraintCount,
        float dt, float gravity, float damp,
        float windSpeed, float aeroCoeff, float aeroMaxRel, float time,
        float ndx, float ndy, float ndz, float noiseFreq, float scroll, float curlStrength,
        int grabbed, float grabX, float grabY, float grabZ,
        float aStruct, float aShear, float aBend, int iterations, float relax,
        bool sphereOn, float scx, float scy, float scz, float srad, float floorY,
        float friction);

    bool Download(int count, float[]? x, float[]? y, float[]? z,
        float[]? vx, float[]? vy, float[]? vz);
}
