namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of TurbulenceLab fulfil - CUDA
/// (<see cref="CudaTurbulence"/>) and OpenCL (<see cref="ClTurbulence"/>):
/// the semi-Lagrangian advect, vorticity confinement, inflow/walls, and
/// the Jacobi pressure projection. Never-throw contract.
/// </summary>
public interface ITurbulenceBackend : IDisposable
{
    bool Upload(int cells, float[] vx, float[] vy, float[] vz, float[] p, byte[] solid);
    bool UploadSolid(int cells, byte[] solid);
    /// <summary>SST state up: k, ω and the wall distance (tail421).</summary>
    bool UploadScalars(int cells, float[] k, float[] w, float[] wallDist);
    /// <param name="sgsModel">0 none, 1 Smagorinsky, 2 WALE, 3 k-ω SST, 4 DDES (26.08).</param>
    /// <param name="sgsCoef">(C Δ)² of the LES closures (unused for SST).</param>
    /// <param name="viscosity">Molecular ν (the face form adds it to ν_t).</param>
    /// <param name="kIn">SST inflow k, ω and the wall ω (unused otherwise).</param>
    bool Step(int nx, int ny, int nz, float dt, float diffK, float confinement,
        float dx, float flowSpeed, float time, float domX, float domY, float domZ, int jacobiIters,
        int sgsModel, float sgsCoef, float viscosity, float kIn, float wIn, float wWall);
    /// <param name="nut">ν_t of the last step; null when not wanted.</param>
    /// <param name="k">SST k and ω of the last step; null when not wanted.</param>
    /// <param name="p">The projected pressure of the last step (body forces); null when not wanted.</param>
    bool Download(int cells, float[]? vx, float[]? vy, float[]? vz, float[]? wmag, float[]? nut, float[]? k, float[]? w, float[]? p);
}
