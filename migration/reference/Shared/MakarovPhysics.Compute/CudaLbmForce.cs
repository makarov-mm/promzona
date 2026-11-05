using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the forced-tunnel LBM exports of
/// MakarovPhysics.Cuda.dll (Native/CudaNBody/lbmforce_cuda.cu): the BetzFlow
/// lattice with an actuator-line force field. Resident-state pattern and
/// never-throw contract as <see cref="CudaLbm3D"/>.
/// </summary>
public sealed class CudaLbmForce : ILbmForceBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmForceCreate")]
    private static extern int MpCudaLbmForceCreate(int maxCells, int maxElements, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmForceUpload")]
    private static extern int MpCudaLbmForceUpload(IntPtr handle, int cells, float[] f);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmForceStep")]
    private static extern int MpCudaLbmForceStep(IntPtr handle, int nx, int ny, int nz, float tau, float uIn,
        int elements, float[] elemPos, float[] elemForce, float eps, int steps);

    /// <summary>
    /// The four destinations are NULLABLE, and saying so in the signature is
    /// the whole of it (29.08). `MpCudaLbmForceMacros` in `lbmforce_cuda.cu`
    /// skips any destination that arrives NULL - that is the point of it, the
    /// actuator line takes the three velocities every step and the density
    /// only when it hands the field back, which is a quarter less readback on
    /// a path where the readback IS the frame (tail511). The managed wrapper
    /// was changed to match that day; this declaration was not, so it went on
    /// promising the marshaller four non-null arrays and the compiler said so
    /// four times on every build (CS8604).
    ///
    /// A null array marshals to a null pointer, which is exactly what the
    /// native side is written to receive. Nothing else changes.
    /// </summary>
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmForceMacros")]
    private static extern int MpCudaLbmForceMacros(IntPtr handle, int nx, int ny, int nz,
        float[]? rho, float[]? ux, float[]? uy, float[]? uz);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmForceDownloadF")]
    private static extern int MpCudaLbmForceDownloadF(IntPtr handle, int cells, float[] f);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaLbmForceDestroy")]
    private static extern int MpCudaLbmForceDestroy(IntPtr handle);

    public const int MaxElements = 256;
    private IntPtr _handle;
    private int _capacity;

    public static CudaLbmForce? TryCreate(int maxCells)
    {
        if (!CudaNBody.Available || maxCells <= 0) return null;
        try
        {
            if (MpCudaLbmForceCreate(maxCells, MaxElements, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaLbmForce { _handle = handle, _capacity = maxCells };
        }
        catch { return null; }
    }

    private CudaLbmForce() { }

    public bool Upload(int cells, float[]? f)
    {
        if (_handle == IntPtr.Zero || cells <= 0 || cells > _capacity || f is null || f.Length < 19L * cells) return false;
        try { return MpCudaLbmForceUpload(_handle, cells, f) == 0; }
        catch { return false; }
    }

    public bool Step(int nx, int ny, int nz, float tau, float uIn,
        int elements, float[]? elemPos, float[]? elemForce, float eps, int steps)
    {
        if (_handle == IntPtr.Zero || steps <= 0 || !(tau > 0.5f) || elements < 0 || elements > MaxElements) return false;
        long cells;
        try { cells = checked((long)nx * ny * nz); }
        catch (OverflowException) { return false; }
        if (cells <= 0 || cells > _capacity) return false;
        elemPos ??= []; elemForce ??= [];
        if (elemPos.Length < 3 * elements || elemForce.Length < 3 * elements) return false;
        try { return MpCudaLbmForceStep(_handle, nx, ny, nz, tau, uIn, elements, elemPos, elemForce, eps, steps) == 0; }
        catch { return false; }
    }

    public bool Macros(int nx, int ny, int nz, float[]? rho, float[]? ux, float[]? uy, float[]? uz)
    {
        if (_handle == IntPtr.Zero) return false;
        long cells;
        try { cells = checked((long)nx * ny * nz); }
        catch (OverflowException) { return false; }
        if (cells <= 0 || cells > _capacity) return false;
        // Any of the four may be null: the caller downloads what it needs.
        // Betz takes the three velocities every step and the density only when
        // it hands the field back to the CPU - a quarter less traffic on a
        // path where the readback IS the frame (28.08).
        if (rho is not null && rho.Length < cells) return false;
        if (ux is not null && ux.Length < cells) return false;
        if (uy is not null && uy.Length < cells) return false;
        if (uz is not null && uz.Length < cells) return false;
        if (rho is null && ux is null && uy is null && uz is null) return false;
        try { return MpCudaLbmForceMacros(_handle, nx, ny, nz, rho, ux, uy, uz) == 0; }
        catch { return false; }
    }

    public bool DownloadF(int cells, float[]? f)
    {
        if (_handle == IntPtr.Zero || cells <= 0 || cells > _capacity || f is null || f.Length < 19L * cells) return false;
        try { return MpCudaLbmForceDownloadF(_handle, cells, f) == 0; }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaLbmForceDestroy(_handle); } catch { }
        _handle = IntPtr.Zero;
    }
}
