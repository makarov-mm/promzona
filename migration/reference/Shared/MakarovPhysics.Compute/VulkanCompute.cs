using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.Compute;

/// <summary>
/// The Vulkan compute API itself - is there a driver, is there a device with a
/// compute queue, what is it called. ONE probe for all families, because that
/// is what it is a property of.
///
/// It started inside <see cref="VkNBody"/>, which was right while n bodies
/// were the only family. The second family (FDTD, 12.09) made the shape
/// visible: two classes would have probed the same device twice, answered from
/// two caches, and carried two SoftDisable flags for the SelfTest to forget
/// one of. CUDA and OpenCL have exactly this split already - CudaNBody holds
/// the device probe for every CUDA family and OpenCl for every OpenCL one -
/// and this is the same thing named honestly.
///
/// Never-throw: a missing DLL, a missing driver and no compute device are all
/// reported outcomes.
/// </summary>
public static class VulkanCompute
{
    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkcProbe")]
    private static extern int MpVkcProbe(byte[] deviceName, int capacity);

    private static bool? _available;
    private static string _deviceName = "";
    private static string _unavailableReason = "";

    /// <summary>
    /// Pretend there is no Vulkan device, for callers that must exercise the
    /// CPU path. Same switch and same reason as
    /// <see cref="CudaNBody.SoftDisable"/>: the SelfTest's thresholds were
    /// written against the double-precision host solvers, and a
    /// single-precision device measured against them fails a dozen suites at
    /// once. One flag for every Vulkan family - the container has lavapipe,
    /// which is a perfectly real Vulkan device, so an unpinned family would be
    /// the one quietly answering for host thresholds.
    /// </summary>
    public static bool SoftDisable { get; set; }

    /// <summary>Why the PROBE said no, or empty when it said yes.</summary>
    public static string UnavailableReason
    {
        get { _ = Available; return _unavailableReason; }
    }

    /// <summary>Why the row is greyed out NOW - which includes the test
    /// switch, unlike <see cref="UnavailableReason"/>.</summary>
    public static string DisabledReason =>
        SoftDisable ? "disabled for this test run" : UnavailableReason;

    /// <summary>One-time environment probe, cached. Never throws.</summary>
    public static bool Available
    {
        get
        {
            if (SoftDisable) return false;
            if (_available.HasValue) return _available.Value;

            try
            {
                var buffer = new byte[256];
                int rc = MpVkcProbe(buffer, buffer.Length);
                if (rc == 1)
                {
                    _deviceName = Decode(buffer);
                    _available = true;
                    _unavailableReason = "";
                    return true;
                }
                _available = false;
                _unavailableReason = rc == 0
                    ? "no vulkan-1.dll, or no device with a compute queue"
                    : $"the Vulkan probe failed (code {rc})";
            }
            catch (DllNotFoundException)
            {
                _available = false;
                _unavailableReason = "MakarovPhysics.VkRay.dll is not beside the executable";
            }
            catch (BadImageFormatException)
            {
                _available = false;
                _unavailableReason = "MakarovPhysics.VkRay.dll is the wrong architecture (x64 expected)";
            }
            catch (EntryPointNotFoundException)
            {
                // The DLL is there and older than this build: OpticsLab's
                // tracer lives in the same file, so a stale copy exports
                // MpVkPt* and nothing else. Naming it beats "no backend".
                _available = false;
                _unavailableReason = "MakarovPhysics.VkRay.dll predates the Vulkan compute core - rebuild it";
            }
            catch (Exception)
            {
                _available = false;
                _unavailableReason = "the Vulkan probe failed unexpectedly";
            }
            return false;
        }
    }

    /// <summary>The device the backends would run on, empty until probed.</summary>
    public static string DeviceName
    {
        get { _ = Available; return _deviceName; }
    }

    private static string Decode(byte[] buffer)
    {
        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0) end = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, end);
    }
}
