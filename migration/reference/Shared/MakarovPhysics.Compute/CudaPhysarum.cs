using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Physarum exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/phy_cuda.cu). Resident-state pattern: agents and
/// the trail field live on the device; the renderer needs only the field
/// each frame, so agents come back down solely on a CPU handoff. Same
/// never-throw contract as everywhere; availability probed once via
/// <see cref="CudaNBody.Available"/>.
/// </summary>
public sealed class CudaPhysarum : IPhysarumBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyCreate")]
    private static extern int MpCudaPhyCreate(int maxAgents, int maxGrid, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyUpload")]
    private static extern int MpCudaPhyUpload(IntPtr handle, int grid, int agents,
        float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng, float[] field);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyUploadFood")]
    private static extern int MpCudaPhyUploadFood(IntPtr handle, int count, int[] foodX, int[] foodY);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyStep")]
    private static extern int MpCudaPhyStep(IntPtr handle, int grid, int agents,
        float sensorDist, float sensorAngle, float turnRate, float jitter, float stepSize,
        float deposit, float decay, float foodDeposit, int foodCount, int steps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyDownloadField")]
    private static extern int MpCudaPhyDownloadField(IntPtr handle, int cells, float[] field);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyDownloadAgents")]
    private static extern int MpCudaPhyDownloadAgents(IntPtr handle, int agents,
        float[] px, float[] py, float[] hx, float[] hy, float[] trail, ulong[] rng);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaPhyDestroy")]
    private static extern int MpCudaPhyDestroy(IntPtr handle);

    /// <summary>Mirrors PHY_MAX_FOOD in the native context.</summary>
    public const int MaxFood = 64;

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaPhysarum? TryCreate(int maxAgents, int maxGrid)
    {
        if (!CudaNBody.Available) return null;
        try
        {
            if (MpCudaPhyCreate(maxAgents, maxGrid, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaPhysarum { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaPhysarum() { }

    public bool Upload(int grid, int agents, float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng, float[] field)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (px is null || px.Length < agents || py is null || py.Length < agents || hx is null || hx.Length < agents || hy is null || hy.Length < agents || trail is null || trail.Length < agents || rng is null || rng.Length < agents || field is null || field.Length < grid * grid) return false;
        try { return MpCudaPhyUpload(_handle, grid, agents, px, py, hx, hy, trail, rng, field) == 0; }
        catch (Exception) { return false; }
    }

    public bool UploadFood(int count, int[] foodX, int[] foodY)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (foodX is null || foodX.Length < count || foodY is null || foodY.Length < count) return false;
        try { return MpCudaPhyUploadFood(_handle, count, foodX, foodY) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int grid, int agents,
        float sensorDist, float sensorAngle, float turnRate, float jitter, float stepSize,
        float deposit, float decay, float foodDeposit, int foodCount, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try
        {
            return MpCudaPhyStep(_handle, grid, agents, sensorDist, sensorAngle, turnRate,
                jitter, stepSize, deposit, decay, foodDeposit, foodCount, steps) == 0;
        }
        catch (Exception) { return false; }
    }

    public bool DownloadField(int cells, float[] field)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (field is null || field.Length < cells) return false;
        try { return MpCudaPhyDownloadField(_handle, cells, field) == 0; }
        catch (Exception) { return false; }
    }

    public bool DownloadAgents(int agents, float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (px is null || px.Length < agents || py is null || py.Length < agents || hx is null || hx.Length < agents || hy is null || hy.Length < agents || trail is null || trail.Length < agents || rng is null || rng.Length < agents) return false;
        try { return MpCudaPhyDownloadAgents(_handle, agents, px, py, hx, hy, trail, rng) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaPhyDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
