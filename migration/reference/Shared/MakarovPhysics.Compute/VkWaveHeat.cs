using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute WaveHeat backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkwaveheat.c), the third
/// <see cref="IWaveHeatBackend"/> beside <see cref="CudaWaveHeat"/> and
/// <see cref="ClWaveHeat"/>, and the third Vulkan family.
///
/// The buffer rotation CUDA does with pointer swaps happens there as index
/// arithmetic on five planes inside one storage buffer: a Vulkan descriptor
/// set is built once, and rebuilding it every step to say "now read that
/// plane" would be the expensive way to move three numbers. None of that is
/// visible here - the contract is the same <see cref="IWaveHeatBackend"/> the
/// other two fulfil, argument for argument.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkWaveHeat : IWaveHeatBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkWhCreate")]
    private static extern int MpVkWhCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkWhUpload")]
    private static extern int MpVkWhUpload(IntPtr handle, int cells,
        float[]? wave, float[]? wavePrev, float[]? heat);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkWhStep")]
    private static extern int MpVkWhStep(IntPtr handle, int n, float cc, float gdt, float adt, int steps);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkWhDownload")]
    private static extern int MpVkWhDownload(IntPtr handle, int cells,
        float[]? wave, float[]? heat, float[]? wavePrev);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkWhDestroy")]
    private static extern int MpVkWhDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkWaveHeat() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkWaveHeat? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !Available) return null;
        try
        {
            if (MpVkWhCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkWaveHeat { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Upload(int cells, float[]? wave, float[]? wavePrev, float[]? heat)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkWhUpload(_handle, cells, wave, wavePrev, heat) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float cc, float gdt, float adt, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkWhStep(_handle, n, cc, gdt, adt, steps) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int cells, float[]? wave, float[] heat, float[]? wavePrev)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkWhDownload(_handle, cells, wave, heat, wavePrev) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkWhDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
