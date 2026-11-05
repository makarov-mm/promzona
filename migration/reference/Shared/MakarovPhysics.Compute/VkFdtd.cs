using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute FDTD backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkfdtd.c), the third
/// <see cref="IFdtdBackend"/> beside <see cref="CudaFdtd"/> and
/// <see cref="ClFdtd"/>. The second Vulkan family, and the one that proved the
/// shared core: the native file declares two buffers and four passes and knows
/// nothing about devices or descriptors.
///
/// Seven arrays ride in two bindings, packed by lifetime - fields change every
/// step, material does not - because the spec guarantees only four storage
/// buffers per stage. The packing is invisible here: the contract is the same
/// <see cref="IFdtdBackend"/> the other two fulfil, argument for argument.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkFdtd : IFdtdBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdCreate")]
    private static extern int MpVkFdtdCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdUpload")]
    private static extern int MpVkFdtdUpload(IntPtr handle, int cells,
        float[]? ez, float[]? hx, float[]? hy, float[]? invEps, byte[]? pec,
        float[]? damp, float[]? ezSq);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdUploadDamp")]
    private static extern int MpVkFdtdUploadDamp(IntPtr handle, int cells, float[]? damp);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdClearIntensity")]
    private static extern int MpVkFdtdClearIntensity(IntPtr handle, int cells);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdStep")]
    private static extern int MpVkFdtdStep(IntPtr handle, int n, float s, float drive,
        int line, int srcX, int srcY, int y0, int y1);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdDownloadEz")]
    private static extern int MpVkFdtdDownloadEz(IntPtr handle, int cells, float[]? ez);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdDownloadAll")]
    private static extern int MpVkFdtdDownloadAll(IntPtr handle, int cells,
        float[]? ez, float[]? hx, float[]? hy);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdDownloadIntensity")]
    private static extern int MpVkFdtdDownloadIntensity(IntPtr handle, int cells, float[]? ezSq);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkFdtdDestroy")]
    private static extern int MpVkFdtdDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkFdtd() { }

    /// <summary>
    /// The backend, or null when there is no usable device. The native side
    /// holds ONE context per family and the core holds ONE device: a second
    /// Create while one is alive is refused rather than silently shared, which
    /// is also the module's own rule - switching backends disposes the old one
    /// first, and closing a module screen disposes it too.
    /// </summary>
    public static VkFdtd? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !Available) return null;
        try
        {
            if (MpVkFdtdCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkFdtd { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Upload(int cells, float[]? ez, float[]? hx, float[]? hy,
        float[]? invEps, byte[]? pec, float[]? damp, float[]? ezSq)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdUpload(_handle, cells, ez, hx, hy, invEps, pec, damp, ezSq) == 0; }
        catch (Exception) { return false; }
    }

    public bool UploadDamp(int cells, float[]? damp)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdUploadDamp(_handle, cells, damp) == 0; }
        catch (Exception) { return false; }
    }

    public bool ClearIntensity(int cells)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdClearIntensity(_handle, cells) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float s, float drive, bool line, int srcX, int srcY, int y0, int y1)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdStep(_handle, n, s, drive, line ? 1 : 0, srcX, srcY, y0, y1) == 0; }
        catch (Exception) { return false; }
    }

    public bool DownloadEz(int cells, float[]? ez)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdDownloadEz(_handle, cells, ez) == 0; }
        catch (Exception) { return false; }
    }

    public bool DownloadAll(int cells, float[]? ez, float[]? hx, float[]? hy)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdDownloadAll(_handle, cells, ez, hx, hy) == 0; }
        catch (Exception) { return false; }
    }

    public bool DownloadIntensity(int cells, float[]? ezSq)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkFdtdDownloadIntensity(_handle, cells, ezSq) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkFdtdDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
