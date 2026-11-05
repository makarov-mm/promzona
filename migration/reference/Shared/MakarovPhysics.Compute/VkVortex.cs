using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute tracer-advection backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkvortex.c), the third
/// <see cref="IVortexBackend"/> beside <see cref="CudaVortex"/> and
/// <see cref="ClVortex"/>, and the eighth Vulkan family.
///
/// The substep loop runs inside one invocation per tracer, as in CUDA: a
/// tracer's position after a substep feeds the next one and no tracer reads
/// another, so splitting the loop into dispatches would buy a barrier per
/// substep and nothing else.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkVortex : IVortexBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkVortexCreate")]
    private static extern int MpVkVortexCreate(int maxTracers, int maxSnapshots, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkVortexUploadTracers")]
    private static extern int MpVkVortexUploadTracers(IntPtr handle, int tracers,
        float[]? x, float[]? y);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkVortexAdvect")]
    private static extern int MpVkVortexAdvect(IntPtr handle, int tracers, int count,
        int substeps, float dt, float eps2,
        float[]? snapX, float[]? snapY, float[]? snapG);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkVortexDownload")]
    private static extern int MpVkVortexDownload(IntPtr handle, int tracers, float[]? x, float[]? y);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkVortexDestroy")]
    private static extern int MpVkVortexDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkVortex() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkVortex? TryCreate(int maxTracers, int maxSnapshots)
    {
        if (maxTracers <= 0 || maxSnapshots <= 0 || !Available) return null;
        try
        {
            if (MpVkVortexCreate(maxTracers, maxSnapshots, out IntPtr handle) != 0
                || handle == IntPtr.Zero) return null;
            return new VkVortex { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool UploadTracers(int tracers, float[]? x, float[]? y)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkVortexUploadTracers(_handle, tracers, x, y) == 0; }
        catch (Exception) { return false; }
    }

    public bool Advect(int tracers, int count, int substeps, float dt, float eps2,
        float[]? snapX, float[]? snapY, float[]? snapG)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkVortexAdvect(_handle, tracers, count, substeps, dt, eps2,
                                      snapX, snapY, snapG) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int tracers, float[]? x, float[]? y)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkVortexDownload(_handle, tracers, x, y) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkVortexDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
