using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute Boris-pusher backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkcharge.c), the third
/// <see cref="IChargeBackend"/> beside <see cref="CudaCharge"/> and
/// <see cref="ClCharge"/>, and the seventh Vulkan family.
///
/// Six planes in one storage buffer and no neighbourhood at all: every
/// particle reads and writes its own slot, so the step runs in place.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkCharge : IChargeBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkChargeCreate")]
    private static extern int MpVkChargeCreate(int maxParticles, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkChargeUpload")]
    private static extern int MpVkChargeUpload(IntPtr handle, int n,
        float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkChargeStep")]
    private static extern int MpVkChargeStep(IntPtr handle, int n, float dt, float qmdt,
        float ex, float ey, float ez, float bx, float by, float bz, float mirror, int steps);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkChargeDownload")]
    private static extern int MpVkChargeDownload(IntPtr handle, int n,
        float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkChargeDestroy")]
    private static extern int MpVkChargeDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkCharge() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkCharge? TryCreate(int maxParticles)
    {
        if (maxParticles <= 0 || !Available) return null;
        try
        {
            if (MpVkChargeCreate(maxParticles, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkCharge { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Upload(int n, float[]? x, float[]? y, float[]? z,
        float[]? vx, float[]? vy, float[]? vz)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkChargeUpload(_handle, n, x, y, z, vx, vy, vz) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float dt, float qmdt, float ex, float ey, float ez,
        float bx, float by, float bz, float mirror, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkChargeStep(_handle, n, dt, qmdt, ex, ey, ez, bx, by, bz, mirror, steps) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int n, float[]? x, float[]? y, float[]? z,
        float[]? vx, float[]? vy, float[]? vz)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkChargeDownload(_handle, n, x, y, z, vx, vy, vz) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkChargeDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
