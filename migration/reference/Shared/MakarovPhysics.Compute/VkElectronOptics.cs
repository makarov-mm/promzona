using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute electron-optics backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkeopt.c), the third
/// <see cref="IElectronOpticsBackend"/> beside
/// <see cref="CudaElectronOptics"/> and <see cref="ClElectronOptics"/>, and
/// the ninth Vulkan family.
///
/// It is the first to use all FOUR storage buffers the Vulkan spec guarantees
/// per stage - float electron planes, integer electron planes, float field
/// grids, and the electrode mask - grouped by type rather than packed, because
/// the alternative is reinterpreting an integer mask as a float inside the
/// kernel.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkElectronOptics : IElectronOpticsBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkEoptCreate")]
    private static extern int MpVkEoptCreate(int maxElectrons, int maxFieldCells,
        int maxDeflCells, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkEoptUploadField")]
    private static extern int MpVkEoptUploadField(IntPtr handle, int gridCells,
        float[]? ez, float[]? er, byte[]? group, int deflCells, float[]? dz, float[]? dt);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkEoptUpload")]
    private static extern int MpVkEoptUpload(IntPtr handle, int n,
        float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz, int[]? birth);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkEoptStep")]
    private static extern int MpVkEoptStep(IntPtr handle, int n, int substeps, float dt,
        float tubeLength, float tubeRadius, float screenZ, int nz, int nr,
        float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0,
        float deflY, float deflZ, float eOverM);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkEoptDownload")]
    private static extern int MpVkEoptDownload(IntPtr handle, int n,
        float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz, byte[]? dead);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkEoptDestroy")]
    private static extern int MpVkEoptDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkElectronOptics() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkElectronOptics? TryCreate(int maxElectrons, int maxFieldCells, int maxDeflCells)
    {
        if (maxElectrons <= 0 || maxFieldCells <= 0 || maxDeflCells <= 0 || !Available) return null;
        try
        {
            if (MpVkEoptCreate(maxElectrons, maxFieldCells, maxDeflCells, out IntPtr handle) != 0
                || handle == IntPtr.Zero) return null;
            return new VkElectronOptics { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool UploadField(int gridCells, float[] ez, float[] er, byte[] group,
        int deflCells, float[] dz, float[] dt)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkEoptUploadField(_handle, gridCells, ez, er, group, deflCells, dz, dt) == 0; }
        catch (Exception) { return false; }
    }

    public bool Upload(int n, float[] x, float[] y, float[] z,
        float[] vx, float[] vy, float[] vz, int[] birth)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkEoptUpload(_handle, n, x, y, z, vx, vy, vz, birth) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, int substeps, float dt,
        float tubeLength, float tubeRadius, float screenZ, int nz, int nr,
        float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0,
        float deflY, float deflZ, float eOverM)
    {
        if (_handle == IntPtr.Zero) return false;
        try
        {
            return MpVkEoptStep(_handle, n, substeps, dt, tubeLength, tubeRadius, screenZ,
                nz, nr, deflWindow, dnz, dnt, plateYz0, plateZz0, deflY, deflZ, eOverM) == 0;
        }
        catch (Exception) { return false; }
    }

    public bool Download(int n, float[] x, float[] y, float[] z,
        float[] vx, float[] vy, float[] vz, byte[] dead)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkEoptDownload(_handle, n, x, y, z, vx, vy, vz, dead) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkEoptDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
