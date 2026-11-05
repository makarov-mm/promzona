using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the MLS-MPM exports of MakarovPhysics.Cuda.dll (see
/// Native/CudaNBody/mpm_cuda.cu). Resident pattern and the never-throw contract
/// as everywhere: a failure returns false and the model keeps stepping on the
/// CPU with the host state it already has.
///
/// The device works in SINGLE precision while the CPU model is double, and P2G
/// is an atomicAdd race rather than a fixed-order reduce. The two paths are
/// therefore the same physics and not the same numbers - documented, and the
/// reason the self-test measures the GPU path by conserved quantities.
/// </summary>
public sealed class CudaMpm : IDisposable
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmCreate")]
    private static extern int MpCudaMpmCreate(int maxParticles, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmUploadParticles")]
    private static extern int MpCudaMpmUploadParticles(IntPtr handle, int count,
        float[] posX, float[] posY, float[] velX, float[] velY,
        float[] cMat, float[] fMat, float[] jp, byte[] mat);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmUploadSolids")]
    private static extern int MpCudaMpmUploadSolids(IntPtr handle, byte[] solid, float[] nx, float[] ny);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmStep")]
    private static extern int MpCudaMpmStep(IntPtr handle, int count, int substeps, float dt,
        float gravity, float stiffness, float sandAngleDeg, float terrainFriction,
        int push, float pushX, float pushY);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmDownload")]
    private static extern int MpCudaMpmDownload(IntPtr handle, int count,
        float[] posX, float[] posY, float[] velX, float[] velY);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmDownloadState")]
    private static extern int MpCudaMpmDownloadState(IntPtr handle, int count,
        float[] posX, float[] posY, float[] velX, float[] velY,
        float[] cMat, float[] fMat, float[] jp);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaMpmDestroy")]
    private static extern int MpCudaMpmDestroy(IntPtr handle);

    private IntPtr _handle;
    private int _maxParticles;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaMpm? TryCreate(int maxParticles)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaMpmCreate(maxParticles, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaMpm { _handle = handle, _maxParticles = maxParticles };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaMpm() { }

    public bool UploadParticles(int count, float[]? posX, float[]? posY, float[]? velX, float[]? velY,
        float[]? cMat, float[]? fMat, float[]? jp, byte[]? mat)
    {
        if (_handle == IntPtr.Zero || count < 0 || count > _maxParticles) return false;

        // a short array would let the native copy run past the managed buffer
        // (heap corruption) - refuse instead
        if (posX is null || posX.Length < count || posY is null || posY.Length < count
            || velX is null || velX.Length < count || velY is null || velY.Length < count
            || cMat is null || cMat.Length < 4 * count || fMat is null || fMat.Length < 4 * count
            || jp is null || jp.Length < count || mat is null || mat.Length < count) return false;

        try
        {
            return MpCudaMpmUploadParticles(_handle, count, posX, posY, velX, velY, cMat, fMat, jp, mat) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UploadSolids(int nodes, byte[]? solid, float[]? nx, float[]? ny)
    {
        if (_handle == IntPtr.Zero) return false;
        if (solid is null || solid.Length < nodes || nx is null || nx.Length < nodes
            || ny is null || ny.Length < nodes) return false;

        try
        {
            return MpCudaMpmUploadSolids(_handle, solid, nx, ny) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int count, int substeps, float dt, float gravity, float stiffness,
        float sandAngleDeg, float terrainFriction, bool push, float pushX, float pushY)
    {
        if (_handle == IntPtr.Zero || count <= 0 || substeps <= 0) return false;

        try
        {
            return MpCudaMpmStep(_handle, count, substeps, dt, gravity, stiffness, sandAngleDeg,
                terrainFriction, push ? 1 : 0, pushX, pushY) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int count, float[]? posX, float[]? posY, float[]? velX, float[]? velY)
    {
        if (_handle == IntPtr.Zero || count < 0 || count > _maxParticles) return false;
        if (posX is null || posX.Length < count || posY is null || posY.Length < count
            || velX is null || velX.Length < count || velY is null || velY.Length < count) return false;

        try
        {
            return MpCudaMpmDownload(_handle, count, posX, posY, velX, velY) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The whole particle state, deformation history included - for the ONE
    /// moment it is needed: the host is about to stop using the card.
    ///
    /// <see cref="Download"/> deliberately brings back only position and
    /// velocity, because that is what the renderer reads and it runs every
    /// frame. F, C and Jp are not caches, though: they are what the material
    /// remembers, and a host that resumes on the CPU without them continues a
    /// collapsed pile of snow from the deformation state it had before the
    /// device ever touched it.
    /// </summary>
    public bool DownloadState(int count, float[]? posX, float[]? posY, float[]? velX, float[]? velY,
        float[]? cMat, float[]? fMat, float[]? jp)
    {
        if (_handle == IntPtr.Zero || count < 0 || count > _maxParticles) return false;
        if (posX is null || posX.Length < count || posY is null || posY.Length < count
            || velX is null || velX.Length < count || velY is null || velY.Length < count
            || cMat is null || cMat.Length < 4 * count || fMat is null || fMat.Length < 4 * count
            || jp is null || jp.Length < count) return false;

        try
        {
            return MpCudaMpmDownloadState(_handle, count, posX, posY, velX, velY, cMat, fMat, jp) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        try { MpCudaMpmDestroy(_handle); }
        catch { /* the process is going down either way */ }
        _handle = IntPtr.Zero;
    }
}
