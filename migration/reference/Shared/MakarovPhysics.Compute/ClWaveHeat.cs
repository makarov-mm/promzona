namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for WaveHeat: waveheat_cuda.cu mechanically translated -
/// the damped leapfrog wave update and the explicit heat step share one
/// clamped-edge Laplacian. Deterministic arithmetic, so the SelfTest pins
/// BIT-EXACT parity against a verbatim C# reference. Third client of the
/// shared <see cref="OpenCl"/> runtime. Never-throw contract.
/// </summary>
public sealed class ClWaveHeat : IWaveHeatBackend
{
    private const string Source = @"
static float mp_wh_lap(__global const float* restrict f, int n, int i, int j)
{
    int im = i > 0 ? i - 1 : 0, ip = i < n - 1 ? i + 1 : n - 1;
    int jm = j > 0 ? j - 1 : 0, jp = j < n - 1 ? j + 1 : n - 1;
    return f[im * n + j] + f[ip * n + j] + f[i * n + jm] + f[i * n + jp] - 4.0f * f[i * n + j];
}

__kernel void mp_wh_wave(int n, float cc, float gdt,
    __global const float* restrict wave, __global const float* restrict wavePrev,
    __global float* restrict waveNext)
{
    int k = get_global_id(0);
    int cells = n * n;
    if (k >= cells) return;
    int j = k % n, i = k / n;
    float lap = mp_wh_lap(wave, n, i, j);
    waveNext[k] = 2.0f * wave[k] - wavePrev[k] + cc * lap - gdt * (wave[k] - wavePrev[k]);
}

__kernel void mp_wh_heat(int n, float adt,
    __global const float* restrict heat, __global float* restrict heatNext)
{
    int k = get_global_id(0);
    int cells = n * n;
    if (k >= cells) return;
    int j = k % n, i = k / n;
    heatNext[k] = heat[k] + adt * mp_wh_lap(heat, n, i, j);
}
";

    private IntPtr _program, _kWave, _kHeat;
    private IntPtr _wave, _wavePrev, _waveNext, _heat, _heatNext;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClWaveHeat? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClWaveHeat();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kWave = OpenCl.Kernel(b._program, "mp_wh_wave");
            b._kHeat = OpenCl.Kernel(b._program, "mp_wh_heat");
            if (b._kWave == IntPtr.Zero || b._kHeat == IntPtr.Zero) { b.Dispose(); return null; }
            long cb = 4L * maxCells;
            b._wave = OpenCl.Buffer(cb);
            b._wavePrev = OpenCl.Buffer(cb);
            b._waveNext = OpenCl.Buffer(cb);
            b._heat = OpenCl.Buffer(cb);
            b._heatNext = OpenCl.Buffer(cb);
            if (b._wave == IntPtr.Zero || b._wavePrev == IntPtr.Zero || b._waveNext == IntPtr.Zero
                || b._heat == IntPtr.Zero || b._heatNext == IntPtr.Zero) { b.Dispose(); return null; }
            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClWaveHeat() { }

    public bool Upload(int cells, float[]? wave, float[]? wavePrev, float[]? heat)
    {
        if (_kWave == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (wave is null || wave.Length < cells || wavePrev is null || wavePrev.Length < cells
            || heat is null || heat.Length < cells) return false;
        try
        {
            return OpenCl.Write(_wave, wave, cells)
                && OpenCl.Write(_wavePrev, wavePrev, cells)
                && OpenCl.Write(_heat, heat, cells);
        }
        catch { return false; }
    }

    public bool Step(int n, float cc, float gdt, float adt, int steps)
    {
        if (_kWave == IntPtr.Zero || n <= 0 || (long)n * n > _capacity || steps <= 0) return false;
        try
        {
            long cells = (long)n * n;
            for (int s = 0; s < steps; s++)
            {
                bool ok =
                    OpenCl.Arg(_kWave, 0, n) && OpenCl.Arg(_kWave, 1, cc) &&
                    OpenCl.Arg(_kWave, 2, gdt) &&
                    OpenCl.Arg(_kWave, 3, _wave) && OpenCl.Arg(_kWave, 4, _wavePrev) &&
                    OpenCl.Arg(_kWave, 5, _waveNext) &&
                    OpenCl.Launch(_kWave, cells);
                if (!ok) return false;
                // rotate: prev <- cur, cur <- next, next <- old prev
                (_wavePrev, _wave, _waveNext) = (_wave, _waveNext, _wavePrev);

                ok = OpenCl.Arg(_kHeat, 0, n) && OpenCl.Arg(_kHeat, 1, adt) &&
                     OpenCl.Arg(_kHeat, 2, _heat) && OpenCl.Arg(_kHeat, 3, _heatNext) &&
                     OpenCl.Launch(_kHeat, cells);
                if (!ok) return false;
                (_heat, _heatNext) = (_heatNext, _heat);
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int cells, float[]? wave, float[] heat, float[]? wavePrev)
    {
        if (_kWave == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (wave is not null && wave.Length < cells) return false;
        if (heat is null || heat.Length < cells) return false;
        if (wavePrev is not null && wavePrev.Length < cells) return false;
        try
        {
            if (wave is not null && !OpenCl.Read(_wave, wave, cells)) return false;
            if (!OpenCl.Read(_heat, heat, cells)) return false;
            if (wavePrev is not null && !OpenCl.Read(_wavePrev, wavePrev, cells)) return false;
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_wave); OpenCl.Release(_wavePrev); OpenCl.Release(_waveNext);
        OpenCl.Release(_heat); OpenCl.Release(_heatNext);
        _wave = _wavePrev = _waveNext = _heat = _heatNext = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kWave); OpenCl.ReleaseKernel(_kHeat);
        _kWave = _kHeat = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
