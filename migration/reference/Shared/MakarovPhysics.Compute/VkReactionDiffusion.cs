using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute Gray-Scott backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkrd.c), the third
/// <see cref="IReactionDiffusionBackend"/> beside
/// <see cref="CudaReactionDiffusion"/> and <see cref="ClReactionDiffusion"/>,
/// and the fourth Vulkan family.
///
/// The ping-pong CUDA does by swapping pointers happens there as two indices
/// in a push block over four planes of one buffer. Invisible here: the
/// contract is the same interface the other two fulfil, argument for argument.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkReactionDiffusion : IReactionDiffusionBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkRdCreate")]
    private static extern int MpVkRdCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkRdUpload")]
    private static extern int MpVkRdUpload(IntPtr handle, int cells, float[]? u, float[]? v);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkRdStep")]
    private static extern int MpVkRdStep(IntPtr handle, int n, float du, float dv,
        float feed, float kill, int steps);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkRdDownload")]
    private static extern int MpVkRdDownload(IntPtr handle, int cells, float[]? u, float[]? v);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkRdDestroy")]
    private static extern int MpVkRdDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkReactionDiffusion() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkReactionDiffusion? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !Available) return null;
        try
        {
            if (MpVkRdCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkReactionDiffusion { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Upload(int cells, float[]? u, float[]? v)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkRdUpload(_handle, cells, u, v) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float du, float dv, float feed, float kill, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkRdStep(_handle, n, du, dv, feed, kill, steps) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int cells, float[]? u, float[]? v)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkRdDownload(_handle, cells, u, v) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkRdDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
