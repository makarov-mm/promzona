using MakarovPhysics.Compute;

namespace MakarovPhysics.UI;

/// <summary>
/// The four-way compute selector, in ONE place (22.08).
///
/// Eleven modules carry a GPU backend, and every one of them grew the same
/// twenty lines by hand: a RadioGroup of Auto/CPU/CUDA/OpenCL, two
/// ItemEnabled flags read off the runtimes, two ItemReason strings for the
/// greyed rows, and a pair of private BackendToIndex/IndexToBackend switches.
/// Four more modules are still to come. Copying it a twelfth time is not the
/// problem; the problem is that a fix then has to be made twelve times, and
/// the last rollout proved that does not happen - the reason strings drifted
/// apart before this file existed.
///
/// The order of the rows is part of the contract: index 0 is Auto and the
/// rest follow <see cref="ComputeBackend"/>, so a saved selection means the
/// same thing in every module.
/// </summary>
public static class ComputeBackendSelector
{
    /// <summary>Adds the selector to a panel and wires it to the model.</summary>
    /// <param name="title">Localised label, normally Tr("Module.Params.Backend").</param>
    /// <param name="autoLabel">Localised word for Auto; the device names are not
    /// translated, because "CUDA" and "OpenCL" are what the vendors call them.</param>
    /// <param name="current">What the model has now.</param>
    /// <param name="onChange">Called with the new request.</param>
    /// <param name="withVulkan">Add the Vulkan row. OPT-IN, and it has to be:
    /// this selector is shared by every module with a compute backend, and a
    /// Vulkan kernel exists for the n-body family alone (Galaxy). A row shown
    /// everywhere would be a row that resolves to the CPU in fourteen modules
    /// out of fifteen - the "the switch and the answer must speak the same
    /// language" failure this selector was built to end (27.08).</param>
    public static RadioGroup Add(UiPanel panel, string title, string autoLabel, ComputeBackend current, Action<ComputeBackend> onChange, bool withVulkan = false)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(onChange);

        // The Auto row says WHICH backend it resolved to, and it says it from
        // the moment the panel is built - not only in the modules that
        // remembered to append it. Twelve of the seventeen modules with this
        // selector showed a bare "Auto" (Michael, 12.09): a row that answers
        // "whatever is fastest" and then does not say what that was is the
        // same half-truth the four-way selector replaced on 27.08, one level
        // in. Built here, a module cannot get it wrong by forgetting.
        string auto = AutoLabel(autoLabel, ComputeBackends.Resolve(ComputeBackend.Auto));
        string[] rows = withVulkan
            ? [auto, "CPU", "CUDA", "OpenCL", "Vulkan"]
            : [auto, "CPU", "CUDA", "OpenCL"];
        var radio = panel.Add(new RadioGroup(title, rows, ToIndex(current)) { Advanced = true });

        radio.ItemEnabled[2] = CudaNBody.Available;
        radio.ItemEnabled[3] = OpenCl.Available;
        if (withVulkan)
        {
            radio.ItemEnabled[4] = VulkanCompute.Available;
            if (!VulkanCompute.Available) radio.ItemReason[4] = VulkanCompute.DisabledReason;
            else radio.ItemReason[4] = VulkanCompute.DeviceName;
        }
        // DisabledReason, not UnavailableReason: the panel asks why the row is
        // greyed out NOW, which the probe's answer alone cannot say once a
        // device test has flipped the software switch (23.08)
        if (!CudaNBody.Available) radio.ItemReason[2] = CudaNBody.DisabledReason;
        if (!OpenCl.Available) radio.ItemReason[3] = OpenCl.DisabledReason;

        // A runtime that IS present but whose kernel would not compile is the
        // case that used to disappear silently: the module fell back to the
        // CPU and said nothing, while the driver had explained itself in one
        // line. If a build has failed this session, that line goes under the
        // row (22.08).
        if (OpenCl.Available && OpenCl.LastBuildLog.Length > 0)
        {
            radio.ItemReason[3] = Shorten(OpenCl.LastBuildLog);
        }
        // How the OpenCL device won its place, when there was a choice to make
        // (27.08). On a laptop with an iGPU beside a real card the panel used
        // to name the winner and say nothing about the contest.
        else if (OpenCl.Available && OpenCl.SelectionNote.Length > 0)
        {
            radio.ItemReason[3] = $"{OpenCl.DeviceName} - {OpenCl.SelectionNote}";
        }

        radio.Changed += i => onChange(FromIndex(i));
        return radio;
    }

    /// <summary>
    /// The Auto caption, in one place, because it is written in two: here when
    /// the panel is built, and again by the modules that refresh it every
    /// frame. Those modules have to keep it - the library cannot know a
    /// MODEL's state, and a backend that broke mid-run makes Auto resolve to
    /// the CPU for that model alone - but they now ask for the same string
    /// instead of formatting their own, so the two cannot drift into two
    /// spellings of one label.
    /// </summary>
    public static string AutoLabel(string autoLabel, ComputeBackend resolved)
        => $"{autoLabel} ({ComputeBackends.Name(resolved)})";

    /// <summary>Row index for a backend; 0 is Auto.</summary>
    public static int ToIndex(ComputeBackend backend) => backend switch
    {
        ComputeBackend.Cpu => 1,
        ComputeBackend.Cuda => 2,
        ComputeBackend.OpenCl => 3,
        ComputeBackend.Vulkan => 4,
        _ => 0,
    };

    /// <summary>The backend a row index asks for; anything unknown is Auto.</summary>
    public static ComputeBackend FromIndex(int index) => index switch
    {
        1 => ComputeBackend.Cpu,
        2 => ComputeBackend.Cuda,
        3 => ComputeBackend.OpenCl,
        // Row 4 exists only where the caller asked for it, so this arm is
        // unreachable elsewhere - and mapping it unconditionally keeps the
        // two directions symmetric, which is what a saved setting needs.
        4 => ComputeBackend.Vulkan,
        _ => ComputeBackend.Auto,
    };

    /// <summary>A build log is a whole compiler transcript; the panel has room
    /// for its first line, which is where the error is.</summary>
    private static string Shorten(string log)
    {
        int nl = log.IndexOfAny(['\r', '\n']);
        string first = (nl < 0 ? log : log[..nl]).Trim();
        return first.Length <= 110 ? first : $"{first[..107]}...";
    }
}
