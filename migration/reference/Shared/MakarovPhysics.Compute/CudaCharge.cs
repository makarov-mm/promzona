using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the charges-in-fields exports of
/// MakarovPhysics.Cuda.dll (see Native/CudaNBody/charge_cuda.cu - the
/// Boris pusher). Resident pattern; positions and velocities come down
/// once per frame. Never-throw contract as everywhere.
/// </summary>
public sealed class CudaCharge : IChargeBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaChargeCreate")]
    private static extern int MpCudaChargeCreate(int maxParticles, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaChargeUpload")]
    private static extern int MpCudaChargeUpload(IntPtr handle, int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaChargeStep")]
    private static extern int MpCudaChargeStep(IntPtr handle, int n, float dt, float qmdt, float ex, float ey, float ez, float bx, float by, float bz, float mirror, int steps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaChargeDownload")]
    private static extern int MpCudaChargeDownload(IntPtr handle, int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaChargeDestroy")]
    private static extern int MpCudaChargeDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaCharge? TryCreate(int maxParticles)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaChargeCreate(maxParticles, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaCharge
            {
                _handle = handle
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaCharge() { }

    public bool Upload(int n, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (x is null || x.Length < n || y is null || y.Length < n || z is null || z.Length < n || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n) return false;

        try
        {
            return MpCudaChargeUpload(_handle, n, x, y, z, vx, vy, vz) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, float dt, float qmdt, float ex, float ey, float ez, float bx, float by, float bz, float mirror, int steps)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaChargeStep(_handle, n, dt, qmdt, ex, ey, ez, bx, by, bz, mirror, steps) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int n, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (x is null || x.Length < n || y is null || y.Length < n || z is null || z.Length < n || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n) return false;

        try
        {
            return MpCudaChargeDownload(_handle, n, x, y, z, vx, vy, vz) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        try
        {
            MpCudaChargeDestroy(_handle);
        }
        catch { /* teardown is best-effort */ }

        _handle = IntPtr.Zero;
    }
}
