using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the Vulkan compute backend in MakarovPhysics.VkRay.dll
/// (see Native/VkRay/vknbody.c), the third <see cref="INBodyBackend"/> beside
/// <see cref="CudaNBody"/> and <see cref="ClNBody"/>. Resident state, same as
/// the other two: Upload once per reset, Step runs whole leapfrog iterations
/// on the device, Download pulls positions and velocities once per rendered
/// frame.
///
/// Why a third one at all: CUDA is NVIDIA only and OpenCL is a runtime the
/// user may simply not have - on Windows a vendor can ship a driver with no
/// OpenCL ICD at all, and then a perfectly capable card ran the CPU tree.
/// vulkan-1.dll comes WITH the display driver on every card that has run a
/// game this decade, so this is the backend that is there when neither of
/// the others is. It also makes the A/B comparison in the module honest:
/// three APIs, one algorithm, one machine.
///
/// Never-throw contract, as everywhere in this library: a missing DLL, a
/// missing driver, no compute device and a failing call are all reported
/// outcomes. False means "fall back to the CPU", not "the program is broken".
/// </summary>
public sealed class VkNBody : INBodyBackend
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkNbCreate")]
    private static extern int MpVkNbCreate(int maxBodies, out IntPtr handle);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkNbUpload")]
    private static extern int MpVkNbUpload(
        IntPtr handle, int n,
        float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz,
        float[] mass);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkNbStep")]
    private static extern int MpVkNbStep(IntPtr handle, int n, float dt, float eps2, int steps);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkNbDownload")]
    private static extern int MpVkNbDownload(
        IntPtr handle, int n,
        float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz);

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkNbDestroy")]
    private static extern int MpVkNbDestroy(IntPtr handle);

    /// <summary>Whether a Vulkan compute device is usable - the question is
    /// about the API, so it is asked once, in
    /// <see cref="VulkanCompute"/>, for every family. This shim exists so the
    /// n-body call sites read like their CUDA and OpenCL neighbours.</summary>
    public static bool Available => VulkanCompute.Available;

    private IntPtr _handle;

    private VkNBody() { }

    /// <summary>
    /// The backend, or null when there is no usable device. The native side
    /// holds ONE context (there is no allocator in a CRT-free DLL, so the
    /// context is a static singleton), and a second Create while one is alive
    /// is refused rather than silently shared - which is exactly the module's
    /// own rule: switching backends disposes the old one first.
    /// </summary>
    public static VkNBody? TryCreate(int maxBodies)
    {
        if (maxBodies <= 0 || !Available) return null;
        try
        {
            if (MpVkNbCreate(maxBodies, out IntPtr handle) != 0 || handle == IntPtr.Zero) return null;
            return new VkNBody { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Upload(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz, float[] mass)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkNbUpload(_handle, n, px, py, pz, vx, vy, vz, mass) == 0; }
        catch (Exception) { return false; }
    }

    public bool Step(int n, float dt, float eps2, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkNbStep(_handle, n, dt, eps2, steps) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpVkNbDownload(_handle, n, px, py, pz, vx, vy, vz) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        IntPtr handle = _handle;
        _handle = IntPtr.Zero;
        try { MpVkNbDestroy(handle); }
        catch (Exception) { /* nothing left to do: the process is leaving anyway */ }
    }
}
