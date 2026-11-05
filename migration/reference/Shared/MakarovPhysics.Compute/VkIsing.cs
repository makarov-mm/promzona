using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute Ising backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkising.c), the third
/// <see cref="IIsingBackend"/> beside <see cref="CudaIsing"/> and
/// <see cref="ClIsing"/>, and the sixth Vulkan family.
///
/// The per-site xorshift64 runs on the device WITHOUT 64-bit integers:
/// `shaderInt64` is an optional Vulkan 1.0 feature, so the state is split into
/// two uints and shifted by hand, bit-for-bit the same sequence. That is why
/// the SelfTest can compare this backend exactly rather than statistically -
/// a stream that drifted would show as differing spins, not as a plausible
/// magnetisation.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkIsing : IIsingBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkIsingCreate")]
    private static extern int MpVkIsingCreate(int maxSites, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkIsingUpload")]
    private static extern int MpVkIsingUpload(IntPtr handle, int sites, sbyte[]? spin, ulong[]? rng);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkIsingSweep")]
    private static extern int MpVkIsingSweep(IntPtr handle, int l, float e4, float e8, int sweeps);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkIsingDownload")]
    private static extern int MpVkIsingDownload(IntPtr handle, int sites, sbyte[]? spin);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkIsingDestroy")]
    private static extern int MpVkIsingDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkIsing() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkIsing? TryCreate(int maxSites)
    {
        if (maxSites <= 0 || !Available) return null;
        try
        {
            if (MpVkIsingCreate(maxSites, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkIsing { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Spins and the per-site streams. A null <paramref name="rng"/>
    /// is refused rather than seeded with zeros: xorshift64 started at zero
    /// stays at zero for ever, and a lattice that quietly stopped drawing
    /// random numbers still looks like a lattice.</summary>
    public bool Upload(int sites, sbyte[]? spin, ulong[]? rng)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkIsingUpload(_handle, sites, spin, rng) == 0; }
        catch (Exception) { return false; }
    }

    public bool Sweep(int l, float e4, float e8, int sweeps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkIsingSweep(_handle, l, e4, e8, sweeps) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int sites, sbyte[]? spin)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkIsingDownload(_handle, sites, spin) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkIsingDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
