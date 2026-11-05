namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the Ising model: the checkerboard Metropolis of
/// Native/CudaNBody/ising_cuda.cu, mechanically translated (the same
/// xorshift64 per-site RNG, the same e4/e8 acceptance table), so a run
/// seeded identically produces the SAME spin trajectory bit for bit -
/// which is exactly what the SelfTest parity check pins. Second client of
/// the shared <see cref="OpenCl"/> runtime after <see cref="ClLbm2D"/>.
/// Never-throw contract: every method returns false instead of raising.
/// </summary>
public sealed class ClIsing : IIsingBackend
{
    /* ising_cuda.cu, mechanically translated:
     *   blockIdx.x * blockDim.x + threadIdx.x  ->  get_global_id(0)
     *   __global__ / __device__                ->  __kernel / plain function
     *   unsigned long long                     ->  ulong
     *   signed char                            ->  char (OpenCL char is signed)
     */
    private const string Source = @"
static float mp_ising_rand(__global ulong* s)
{
    ulong v = *s;
    v ^= v << 13; v ^= v >> 7; v ^= v << 17;
    *s = v;
    return (float)((uint)(v >> 32) >> 8) * (1.0f / 16777216.0f);
}

__kernel void mp_ising_half(
    int L, int color, float e4, float e8,
    __global char* restrict spin,
    __global ulong* restrict rng)
{
    int i = get_global_id(0);
    int n = L * L;
    if (i >= n) return;
    int x = i % L, y = i / L;
    if (((x + y) & 1) != color) return;

    int xm = x == 0 ? L - 1 : x - 1, xp = x == L - 1 ? 0 : x + 1;
    int ym = y == 0 ? L - 1 : y - 1, yp = y == L - 1 ? 0 : y + 1;
    int nb = spin[y * L + xm] + spin[y * L + xp] + spin[ym * L + x] + spin[yp * L + x];
    int s = spin[i];
    int dE = 2 * s * nb;                 /* in {-8,-4,0,4,8} */

    if (dE <= 0) { spin[i] = (char)(-s); return; }
    float p = dE == 4 ? e4 : e8;
    if (mp_ising_rand(&rng[i]) < p) spin[i] = (char)(-s);
}
";

    private IntPtr _program, _kernel, _spin, _rng;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClIsing? TryCreate(int maxSites)
    {
        if (maxSites <= 0 || !OpenCl.Available) return null;
        var b = new ClIsing();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kernel = OpenCl.Kernel(b._program, "mp_ising_half");
            if (b._kernel == IntPtr.Zero) { b.Dispose(); return null; }
            b._spin = OpenCl.Buffer(maxSites);
            b._rng = OpenCl.Buffer(8L * maxSites);
            if (b._spin == IntPtr.Zero || b._rng == IntPtr.Zero) { b.Dispose(); return null; }
            b._capacity = maxSites;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClIsing() { }

    public bool Upload(int sites, sbyte[]? spin, ulong[]? rng)
    {
        if (_kernel == IntPtr.Zero || sites <= 0 || sites > _capacity) return false;
        if (spin is null || spin.Length < sites || rng is null || rng.Length < sites) return false;
        try
        {
            return OpenCl.Write(_spin, spin, sites) && OpenCl.Write(_rng, rng, sites);
        }
        catch { return false; }
    }

    public bool Sweep(int l, float e4, float e8, int sweeps)
    {
        if (_kernel == IntPtr.Zero || l <= 0 || (long)l * l > _capacity || sweeps <= 0) return false;
        try
        {
            long n = (long)l * l;
            for (int s = 0; s < sweeps; s++)
            {
                for (int color = 0; color < 2; color++)
                {
                    bool ok =
                        OpenCl.Arg(_kernel, 0, l) && OpenCl.Arg(_kernel, 1, color) &&
                        OpenCl.Arg(_kernel, 2, e4) && OpenCl.Arg(_kernel, 3, e8) &&
                        OpenCl.Arg(_kernel, 4, _spin) && OpenCl.Arg(_kernel, 5, _rng) &&
                        OpenCl.Launch(_kernel, n);
                    if (!ok) return false;
                }
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int sites, sbyte[]? spin)
    {
        if (_kernel == IntPtr.Zero || sites <= 0 || sites > _capacity) return false;
        if (spin is null || spin.Length < sites) return false;
        try
        {
            return OpenCl.Read(_spin, spin, sites);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_spin);
        OpenCl.Release(_rng);
        _spin = _rng = IntPtr.Zero;
        if (_kernel != IntPtr.Zero) { OpenCl.ReleaseKernel(_kernel); _kernel = IntPtr.Zero; }
        if (_program != IntPtr.Zero) { OpenCl.ReleaseProgram(_program); _program = IntPtr.Zero; }
    }
}
