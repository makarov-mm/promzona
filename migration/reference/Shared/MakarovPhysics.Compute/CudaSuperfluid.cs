using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the split-step Gross-Pitaevskii exports of
/// MakarovPhysics.Cuda.dll (see Native/CudaNBody/sf_cuda.cu, with the
/// in-house radix-2 FFT - no cuFFT dependency). Resident-state pattern and
/// the never-throw contract as everywhere: a failure returns false and the
/// model keeps stepping on the CPU with the host state it already has.
///
/// CUDA only - there is no OpenCL kernel for this family, so the model shows
/// the one-checkbox switch of the Avalanche backend rather than the four-way
/// selector (a selector row that resolves to the CPU is the lie the selector
/// was built to end).
///
/// The device works in SINGLE precision while the CPU model is double. The
/// two paths are the same physics and not the same digits: the norm drifts
/// ~1e-7 per step on the device, which is why the self-test measures the
/// device by the energy, the density and the vortex count against the CPU
/// run, with tolerances taken from Tools/twins/superfluid_cuda_twin.py.
/// </summary>
public sealed class CudaSuperfluid : IDisposable
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSfCreate")]
    private static extern int MpCudaSfCreate(int maxN, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSfUpload")]
    private static extern int MpCudaSfUpload(IntPtr handle, int n,
        float[] re, float[] im, float[] vTrap, float[] k2);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSfStep")]
    private static extern int MpCudaSfStep(IntPtr handle, int n, float kickDt, float kinF, float g,
        float h, float half, int stir, float sx, float sy, float stirStrength, float stirInvWidth2,
        int imaginary);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSfNormalise")]
    private static extern int MpCudaSfNormalise(IntPtr handle, int n, float cell);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSfDownload")]
    private static extern int MpCudaSfDownload(IntPtr handle, int n, float[] re, float[] im);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaSfDestroy")]
    private static extern int MpCudaSfDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>The largest grid side this context can hold.</summary>
    public int MaxN { get; private set; }

    /// <summary>Null when the backend cannot start (also for non-power-of-two
    /// grids - the FFT kernel is radix-2 only).</summary>
    public static CudaSuperfluid? TryCreate(int maxN)
    {
        if (!CudaNBody.Available) return null;
        if (maxN < 4 || (maxN & (maxN - 1)) != 0) return null;
        try
        {
            if (MpCudaSfCreate(maxN, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            return new CudaSuperfluid { _handle = handle, MaxN = maxN };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaSuperfluid() { }

    public bool Upload(int n, float[] re, float[] im, float[] vTrap, float[] k2)
    {
        if (_handle == IntPtr.Zero) return false;
        // a short array would let the native copy run past the managed
        // buffer (heap corruption) - refuse instead; n is the grid SIDE and
        // the three field arrays hold n*n
        int cells;
        try { cells = checked(n * n); } catch (OverflowException) { return false; }
        if (re is null || re.Length < cells || im is null || im.Length < cells
            || vTrap is null || vTrap.Length < cells || k2 is null || k2.Length < n) return false;
        try { return MpCudaSfUpload(_handle, n, re, im, vTrap, k2) == 0; }
        catch (Exception) { return false; }
    }

    /// <summary>One split step. <paramref name="kickDt"/> is the model's half
    /// step dt/2, <paramref name="kinF"/> is dt/(2m); <paramref name="imaginary"/>
    /// selects the relaxation scheme. The stirrer is a gaussian of strength
    /// <paramref name="stirStrength"/> at (sx, sy) with 1/(2 w^2) =
    /// <paramref name="stirInvWidth2"/>, added only when <paramref name="stir"/>.</summary>
    public bool Step(int n, float kickDt, float kinF, float g, float h, float half,
        bool stir, float sx, float sy, float stirStrength, float stirInvWidth2, bool imaginary)
    {
        if (_handle == IntPtr.Zero) return false;
        try
        {
            return MpCudaSfStep(_handle, n, kickDt, kinF, g, h, half, stir ? 1 : 0,
                sx, sy, stirStrength, stirInvWidth2, imaginary ? 1 : 0) == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Scale psi on the device so that sum |psi|^2 * cell = 1. False
    /// when the norm is not positive and finite (psi is then untouched).</summary>
    public bool Normalise(int n, float cell)
    {
        if (_handle == IntPtr.Zero) return false;
        try { return MpCudaSfNormalise(_handle, n, cell) == 0; }
        catch (Exception) { return false; }
    }

    public bool Download(int n, float[] re, float[] im)
    {
        if (_handle == IntPtr.Zero) return false;
        int cells;
        try { cells = checked(n * n); } catch (OverflowException) { return false; }
        if (re is null || re.Length < cells || im is null || im.Length < cells) return false;
        try { return MpCudaSfDownload(_handle, n, re, im) == 0; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        try { MpCudaSfDestroy(_handle); }
        catch (Exception) { /* teardown is best-effort */ }
        _handle = IntPtr.Zero;
    }
}
