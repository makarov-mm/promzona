namespace MakarovPhysics.Compute;

/// <summary>
/// What the user ASKED to run the compute on. <see cref="Auto"/> is the
/// default and resolves to the fastest backend that is actually present;
/// the other three are force switches for A/B comparisons and curiosity.
/// </summary>
public enum ComputeBackend
{
    Auto = 0,
    Cpu = 1,
    Cuda = 2,
    OpenCl = 3,
    /// <summary>Vulkan compute (Native/VkRay/vknbody.c). NOT offered by every
    /// module: only the ones that actually carry a Vulkan kernel show the row,
    /// which today means Galaxy. <see cref="ComputeBackends.Resolve"/> can
    /// only ask whether the API is present, exactly as it does for CUDA, so a
    /// module that does not build a Vulkan backend would resolve this to the
    /// CPU - honest, but a row promising nothing. Hence the opt-in in
    /// ComputeBackendSelector.Add.</summary>
    Vulkan = 4,
}

/// <summary>
/// Resolution and description of compute backends, shared by every module
/// that grows the four-way selector (Lbm2D is the pilot).
/// </summary>
public static class ComputeBackends
{
    /// <summary>
    /// The backend that will actually run for a request. Auto prefers CUDA
    /// (on NVIDIA it beats the vendor's own OpenCL), then OpenCL, then the
    /// CPU. A forced backend that is not present resolves to the CPU rather
    /// than pretending - the caller can show the reason from
    /// <see cref="CudaNBody.UnavailableReason"/> / <see cref="OpenCl.UnavailableReason"/>.
    /// </summary>
    public static ComputeBackend Resolve(ComputeBackend requested) => requested switch
    {
        // Auto DOES NOT consider Vulkan, and that is now a DECISION rather
        // than a stopgap (Michael, 12.09): CUDA when it is there, OpenCL
        // otherwise - OpenCL has a kernel in every family that shows this
        // selector, so the second arm always has an answer.
        //
        // The reason it cannot simply be added: Auto answers for all
        // SEVENTEEN families, and availability here is a property of the API,
        // not of the caller - CudaNBody.Available says a CUDA device
        // answered, never that THIS module has a CUDA kernel. While every
        // selector module had both CUDA and OpenCL the two questions
        // coincided. Vulkan breaks the coincidence: the API is present
        // wherever a display driver is, the kernel exists in one family. Auto
        // returning Vulkan in Lbm2D would hit `want == Cuda ? CudaLbm2D :
        // ClLbm2D`, build an OPENCL backend and label it Vulkan - the 27.08
        // defect again, moved from the switch into the answer.
        //
        // The known cost of the decision, stated rather than hidden: on a
        // machine with a driver but neither CUDA nor an OpenCL ICD - the
        // machine the Vulkan backend was written for - Auto in Galaxy answers
        // CPU while a working Vulkan row sits below it. Fixing that needs
        // Resolve to be TOLD what the caller can build (the fact
        // ComputeBackendSelector.Add already carries as withVulkan), and it
        // waits for a second Vulkan family - Docs/VULKAN.md, last section.
        ComputeBackend.Auto => CudaNBody.Available ? ComputeBackend.Cuda
                             : OpenCl.Available ? ComputeBackend.OpenCl
                             : ComputeBackend.Cpu,
        ComputeBackend.Cuda when !CudaNBody.Available => ComputeBackend.Cpu,
        ComputeBackend.OpenCl when !OpenCl.Available => ComputeBackend.Cpu,
        ComputeBackend.Vulkan when !VulkanCompute.Available => ComputeBackend.Cpu,
        _ => requested,
    };

    /// <summary>Short display string for a RESOLVED backend: the API name
    /// plus the device, "CPU" plain. Localization of "CPU" is the caller's
    /// business; API and device names are proper nouns everywhere.</summary>
    public static string Describe(ComputeBackend resolved) => resolved switch
    {
        ComputeBackend.Cuda => $"CUDA · {CudaNBody.DeviceName}",
        ComputeBackend.OpenCl => $"OpenCL · {OpenCl.DeviceName}",
        ComputeBackend.Vulkan => $"Vulkan · {VulkanCompute.DeviceName}",
        _ => "CPU",
    };

    /// <summary>Just the API name, for places too narrow for a device name
    /// (the Auto radio caption; the full device is on the info line).</summary>
    public static string Name(ComputeBackend resolved) => resolved switch
    {
        ComputeBackend.Cuda => "CUDA",
        ComputeBackend.OpenCl => "OpenCL",
        ComputeBackend.Vulkan => "Vulkan",
        _ => "CPU",
    };
}
