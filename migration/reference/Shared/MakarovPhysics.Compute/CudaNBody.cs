using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over MakarovPhysics.Cuda.dll (see Native/CudaNBody),
/// resident-state edition: Upload once per reset, Step runs whole leapfrog
/// iterations on the device with zero transfers, Download pulls positions
/// and velocities once per rendered frame. Follows the
/// VulkanRayTracingProbe contract - nothing here ever throws for
/// environmental reasons: a missing DLL, driver or device and a failing call
/// are all reported outcomes, and the caller falls back to its CPU path.
/// </summary>
public sealed class CudaNBody : INBodyBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaProbe")]
    private static extern int MpCudaProbe(byte[] deviceName, int capacity);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaWarmup")]
    private static extern int MpCudaWarmup();

    /// <summary>Force the driver/primary-context initialization the first
    /// real workload would otherwise pay for (typically 100-400 ms). Safe
    /// to call from any thread and when CUDA is absent (returns false).
    /// The splash screen calls this so the first module open is instant.</summary>
    public static bool Warmup()
    {
        if (!Available) return false;
        try { return MpCudaWarmup() == 0; }
        catch (Exception) { return false; }
    }

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaNBodyCreate")]
    private static extern int MpCudaNBodyCreate(int maxBodies, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaNBodyUpload")]
    private static extern int MpCudaNBodyUpload(
        IntPtr handle, int n,
        float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz,
        float[] mass);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaNBodyStep")]
    private static extern int MpCudaNBodyStep(IntPtr handle, int n, float dt, float eps2, int steps);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaNBodyDownload")]
    private static extern int MpCudaNBodyDownload(
        IntPtr handle, int n,
        float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaNBodyDestroy")]
    private static extern int MpCudaNBodyDestroy(IntPtr handle);

    private static bool? _available;
    private static string _deviceName = "";

    /// <summary>One-time environment probe, cached. Never throws.</summary>
    /// <summary>
    /// Pretend there is no card, for callers that must exercise the CPU path
    /// (07.08).
    ///
    /// The SelfTest sets it at startup. Its physics assertions were written
    /// against the CPU solvers and are as tight as double precision allows -
    /// discrete eigenvalues to 1e-9, lattice mass to 1e-6, seeded runs
    /// reproduced exactly. Most CUDA clients default their switch to ON, so
    /// the moment the harness got the DLL next to it (which it never had
    /// before) a dozen suites started measuring a single-precision device
    /// against thresholds meant for the host and failed in a heap.
    ///
    /// The answer is not to loosen those thresholds - they are the reason the
    /// CPU solvers are trustworthy - but to say which path is under test. A
    /// device test clears this flag for its own duration and compares the two
    /// backends on the terms the backends actually promise.
    /// </summary>
    public static bool SoftDisable { get; set; }

    /// <summary>
    /// Why the device is not being used, in a sentence, or empty when it is.
    ///
    /// "No CUDA backend" was the only thing anyone could learn, and the four
    /// ways of getting there are four different problems with four different
    /// fixes: the DLL is missing, the DLL is the wrong architecture, the
    /// driver is broken, or there is genuinely no card. On 07.08 an RTX 4060
    /// ran a whole session on the CPU path and the log said nothing at all -
    /// the reason was never found, because nothing recorded one (09.08).
    ///
    /// Set once, when the probe runs. Modules may show it; the log always
    /// carries it.
    /// </summary>
    /// <summary>Why the PROBE said no - a missing DLL, the wrong architecture,
    /// a driver that would not answer. It says nothing about
    /// <see cref="SoftDisable"/>, which is a test switch and not a property of
    /// the machine; use <see cref="DisabledReason"/> for that.</summary>
    public static string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>Why the backend is unavailable RIGHT NOW, probe or switch,
    /// which is the question a panel is actually asking.</summary>
    public static string DisabledReason => SoftDisable
        ? "switched off in software (a device test is comparing backends)"
        : UnavailableReason;

    public static bool Available
    {
        get
        {
            if (SoftDisable)
            {
                // NOT written into UnavailableReason: overwriting the probe's
                // answer left this object saying "switched off in software"
                // long after the switch went back, so Available == true came
                // with a reason still set (external analysis, 23.08).
                return false;
            }

            if (_available is bool cached) return cached;

            try
            {
                byte[] name = new byte[256];
                int code = MpCudaProbe(name, name.Length);
                if (code == 1)
                {
                    int len = Array.IndexOf(name, (byte)0);
                    if (len < 0) len = name.Length;
                    _deviceName = Encoding.UTF8.GetString(name, 0, len);
                    UnavailableReason = string.Empty;
                }
                else
                {
                    UnavailableReason = code == 0
                        ? "the backend loaded but reported no usable device - no card, or a driver too old for it"
                        : $"the backend refused to start (probe returned {code})";
                }
                _available = code == 1;
            }
            catch (DllNotFoundException)
            {
                UnavailableReason = "MakarovPhysics.Cuda.dll is not next to the executable - "
                                  + "build it with Native\\CudaNBody\\build-cuda.cmd";
                _available = false;
            }
            catch (BadImageFormatException)
            {
                UnavailableReason = "MakarovPhysics.Cuda.dll is the wrong architecture for this process (x64 expected)";
                _available = false;
            }
            catch (EntryPointNotFoundException e)
            {
                UnavailableReason = $"MakarovPhysics.Cuda.dll is older than this build: {e.Message}";
                _available = false;
            }
            catch (Exception e)
            {
                // SEH from a broken driver reaches here, and its type is the
                // only clue there is
                UnavailableReason = $"the CUDA backend threw {e.GetType().Name} while probing: {e.Message}";
                _available = false;
            }

            // NOT logged here. This library references nothing, and that is
            // load-bearing: stubcheck compiles it for real instead of stubbing
            // it, which is why every module that uses a GPU is checkable at
            // all. Reaching for the logger would trade that for a line of
            // convenience. The reason is REPORTED; the host writes it down
            // (see Loader.cs).
            return _available.Value;
        }
    }

    /// <summary>The CUDA device name once <see cref="Available"/> is true.</summary>
    public static string DeviceName => _deviceName;

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaNBody? TryCreate(int maxBodies)
    {
        if (!Available) return null;
        try
        {
            if (MpCudaNBodyCreate(maxBodies, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaNBody { _handle = handle };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaNBody() { }

    /// <summary>Push the full body state to the device. False = fall back.</summary>
    public bool Upload(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz, float[] mass)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (px is null || px.Length < n || py is null || py.Length < n || pz is null || pz.Length < n || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n || mass is null || mass.Length < n) return false;
        try { return MpCudaNBodyUpload(_handle, n, px, py, pz, vx, vy, vz, mass) == 0; }
        catch (Exception) { return false; }
    }

    /// <summary>Run whole leapfrog iterations on the device (no transfers).
    /// False = the backend failed and the caller should fall back.</summary>
    public bool Step(int n, float dt, float eps2, int steps)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpCudaNBodyStep(_handle, n, dt, eps2, steps) == 0; }
        catch (Exception) { return false; }
    }

    /// <summary>Pull positions and velocities back (once per rendered frame,
    /// and on any CUDA→CPU handoff). False = fall back.</summary>
    public bool Download(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (px is null || px.Length < n || py is null || py.Length < n || pz is null || pz.Length < n || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n) return false;
        try { return MpCudaNBodyDownload(_handle, n, px, py, pz, vx, vy, vz) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaNBodyDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
