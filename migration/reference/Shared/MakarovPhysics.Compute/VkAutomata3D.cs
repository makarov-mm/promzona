using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute 3D automata backend in
/// MakarovPhysics.VkRay.dll (see Native/VkRay/vkca3d.c), the third
/// <see cref="IAutomata3DBackend"/> beside <see cref="CudaAutomata3D"/> and
/// <see cref="ClAutomata3D"/>, and the fifth Vulkan family.
///
/// The grid is bytes on this side and uints on the device: std430 has no byte
/// arrays, and widening once on the host beats packing and unpacking inside a
/// kernel that reads twenty-six neighbours per cell. Nothing of that shows
/// here - the contract is the same interface the other two fulfil.
///
/// Never-throw contract: false means "fall back to the CPU", never "the
/// program is broken".
/// </summary>
public sealed class VkAutomata3D : IAutomata3DBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkCa3dCreate")]
    private static extern int MpVkCa3dCreate(int maxCells, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkCa3dUpload")]
    private static extern int MpVkCa3dUpload(IntPtr handle, int cells, byte[]? grid);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkCa3dStep")]
    private static extern int MpVkCa3dStep(IntPtr handle, int n, int wrap, uint survive, uint birth, int maxState, int steps);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkCa3dDownload")]
    private static extern int MpVkCa3dDownload(IntPtr handle, int cells, byte[]? grid);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkCa3dDestroy")]
    private static extern int MpVkCa3dDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - asked once for the
    /// API in <see cref="VulkanCompute"/>, not once per family.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;
    private VkAutomata3D() { }

    /// <summary>The backend, or null when there is no usable device.</summary>
    public static VkAutomata3D? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !Available) return null;

        try
        {
            if (MpVkCa3dCreate(maxCells, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkAutomata3D
            {
                _handle = handle
            };
        }
        catch
        {
            return null;
        }
    }

    public bool Upload(int cells, byte[]? grid)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpVkCa3dUpload(_handle, cells, grid) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, bool wrap, uint survive, uint birth, int maxState, int steps)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpVkCa3dStep(_handle, n, wrap ? 1 : 0, survive, birth, maxState, steps) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int cells, byte[]? grid)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpVkCa3dDownload(_handle, cells, grid) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;

        try
        {
            MpVkCa3dDestroy(handle);
        }
        catch
        {
             /* nothing left to do: the process is leaving anyway */
        }
    }
}
