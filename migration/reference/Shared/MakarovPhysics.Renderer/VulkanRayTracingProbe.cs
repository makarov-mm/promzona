using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.Renderer;

/// <summary>
/// Managed wrapper over the native capability probe MakarovPhysics.VkRay.dll
/// (see Native/VkRay). The future hardware ray-tracing module calls this once
/// before initializing: if the answer is anything short of
/// HardwareRayTracing, it shows a friendly modal message on our OpenGL UI
/// instead of trying to start the Vulkan renderer.
///
/// The native DLL is dependency-free (imports only kernel32.dll) and loads
/// vulkan-1.dll dynamically, so calling the probe is safe on ANY machine -
/// including ones with no Vulkan driver at all.
/// </summary>
public static class VulkanRayTracingProbe
{
    public enum Support
    {
        /// <summary>MakarovPhysics.VkRay.dll is not next to the executable.</summary>
        ProbeLibraryMissing,
        /// <summary>No vulkan-1.dll - the GPU driver has no Vulkan at all.</summary>
        NoVulkanDriver,
        /// <summary>Vulkan works, but no VK_KHR_ray_tracing_pipeline devices.</summary>
        VulkanWithoutRayTracing,
        /// <summary>At least one GPU exposes hardware ray tracing.</summary>
        HardwareRayTracing,
        /// <summary>Vulkan is present but the probe failed unexpectedly.</summary>
        ProbeFailed,
    }

    [DllImport("MakarovPhysics.VkRay.dll", EntryPoint = "MpVkProbe")]
    private static extern int MpVkProbe(byte[] deviceName, int deviceNameCapacity);

    /// <summary>Never throws - a missing native DLL is a reported outcome.</summary>
    public static (Support Support, string DeviceName) Probe()
    {
        byte[] nameBuffer = new byte[256];
        int code;

        try
        {
            code = MpVkProbe(nameBuffer, nameBuffer.Length);
        }
        catch (DllNotFoundException)
        {
            return (Support.ProbeLibraryMissing, "");
        }
        catch (EntryPointNotFoundException)
        {
            return (Support.ProbeLibraryMissing, "");
        }
        catch (Exception)
        {
            // BadImageFormatException (x86/corrupted DLL), SEHException from
            // a broken driver, and anything else the interop layer can raise:
            // "never throws" means exactly that - the outcome is ProbeFailed
            return (Support.ProbeFailed, "");
        }

        int nameLength = Array.IndexOf(nameBuffer, (byte)0);

        if (nameLength < 0)
        {
            nameLength = nameBuffer.Length;
        }

        string deviceName = Encoding.UTF8.GetString(nameBuffer, 0, nameLength);

        Support support = code switch
        {
            0 => Support.NoVulkanDriver,
            1 => Support.VulkanWithoutRayTracing,
            2 => Support.HardwareRayTracing,
            _ => Support.ProbeFailed,
        };
        return (support, deviceName);
    }
}
