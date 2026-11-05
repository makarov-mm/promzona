namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the FORCED D3Q19 tunnel (BetzFlow): lbmforce_cuda.cu
/// translated - actuator elements deposited into a per-cell force field by
/// a Gaussian kernel, macros with the half-force, Guo collision with the
/// field, periodic pull streaming, velocity inlet and copy-outlet. Tables
/// inside the kernels (the 1.2-driver rule of <see cref="ClLbm3D"/>), the
/// population buffers swap on the host between enqueues, float and SoA as
/// the .cu. Never-throw contract.
/// </summary>
public sealed class ClLbmForce : ILbmForceBackend
{
    private const int Q = 19;
    public const int MaxElements = 256;

    private const string Source = @"
#define LBM_Q 19
#define LBM_TABLES \
    const int cx[LBM_Q] = { 0, 1,-1, 0, 0, 0, 0, 1,-1, 1,-1, 1,-1, 1,-1, 0, 0, 0, 0 }; \
    const int cy[LBM_Q] = { 0, 0, 0, 1,-1, 0, 0, 1,-1,-1, 1, 0, 0, 0, 0, 1,-1, 1,-1 }; \
    const int cz[LBM_Q] = { 0, 0, 0, 0, 0, 1,-1, 0, 0, 0, 0, 1,-1,-1, 1, 1,-1,-1, 1 }; \
    const float w[LBM_Q] = { \
        1.0f/3, \
        1.0f/18,1.0f/18,1.0f/18,1.0f/18,1.0f/18,1.0f/18, \
        1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36, \
        1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36 };

inline float mp_feq(int i, float rho, float ux, float uy, float uz, int cxi, int cyi, int czi, float wi)
{
    float cu = cxi * ux + cyi * uy + czi * uz;
    float usq = ux * ux + uy * uy + uz * uz;
    return wi * rho * (1.0f + 3.0f * cu + 4.5f * cu * cu - 1.5f * usq);
}

__kernel void mp_lbmf_deposit(
    int nx, int ny, int nz, int elements, float eps,
    __global const float* ePos, __global const float* eForce,
    __global float* Fx, __global float* Fy, __global float* Fz)
{
    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;
    int z = c % nz;
    int y = (c / nz) % ny;
    int x = c / (nz * ny);
    // NO continuous normalisation here: the host pre-divides each element's
    // force by the weights that actually land on cells, because a gather
    // kernel cannot sum its own weights and the two backends must deposit the
    // same total (28.08). Keeping 1/(eps^3 pi^1.5) as well would apply it
    // twice.
    float reach = 3.0f * eps + 1.0f;
    float fx = 0.0f, fy = 0.0f, fz = 0.0f;
    for (int e = 0; e < elements; e++)
    {
        float dx = x - ePos[3 * e];
        if (fabs(dx) > reach) continue;
        float dy = y - ePos[3 * e + 1];
        if (dy > ny * 0.5f) dy -= ny; else if (dy < -ny * 0.5f) dy += ny;
        if (fabs(dy) > reach) continue;
        float dz = z - ePos[3 * e + 2];
        if (dz > nz * 0.5f) dz -= nz; else if (dz < -nz * 0.5f) dz += nz;
        if (fabs(dz) > reach) continue;
        float k = exp(-(dx * dx + dy * dy + dz * dz) / (eps * eps));
        fx += eForce[3 * e] * k; fy += eForce[3 * e + 1] * k; fz += eForce[3 * e + 2] * k;
    }
    Fx[c] = fx; Fy[c] = fy; Fz[c] = fz;
}

__kernel void mp_lbmf_macro(
    int nx, int ny, int nz, float uIn,
    __global float* f,
    __global const float* Fx, __global const float* Fy, __global const float* Fz,
    __global float* rhoOut, __global float* uxOut, __global float* uyOut, __global float* uzOut)
{
    LBM_TABLES
    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;
    float rho = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int i = 0; i < LBM_Q; i++)
    {
        float v = f[i * cells + c];
        rho += v; mx += v * cx[i]; my += v * cy[i]; mz += v * cz[i];
    }
    if (!(rho > 1e-9f) || !isfinite(rho))
    {
        for (int i = 0; i < LBM_Q; i++) f[i * cells + c] = mp_feq(i, 1.0f, uIn, 0.0f, 0.0f, cx[i], cy[i], cz[i], w[i]);
        rho = 1.0f; mx = uIn; my = 0.0f; mz = 0.0f;
    }
    rhoOut[c] = rho;
    uxOut[c] = (mx + 0.5f * Fx[c]) / rho;
    uyOut[c] = (my + 0.5f * Fy[c]) / rho;
    uzOut[c] = (mz + 0.5f * Fz[c]) / rho;
}

__kernel void mp_lbmf_collide(
    int nx, int ny, int nz, float omega,
    __global float* f,
    __global const float* rho, __global const float* ux, __global const float* uy, __global const float* uz,
    __global const float* Fx, __global const float* Fy, __global const float* Fz)
{
    LBM_TABLES
    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;
    float r = rho[c], vx = ux[c], vy = uy[c], vz = uz[c];
    float fx = Fx[c], fy = Fy[c], fz = Fz[c];
    float uF = vx * fx + vy * fy + vz * fz;
    for (int i = 0; i < LBM_Q; i++)
    {
        float cu = cx[i] * vx + cy[i] * vy + cz[i] * vz;
        float cF = cx[i] * fx + cy[i] * fy + cz[i] * fz;
        float feq = mp_feq(i, r, vx, vy, vz, cx[i], cy[i], cz[i], w[i]);
        float force = w[i] * (1.0f - 0.5f * omega) * (3.0f * (cF - uF) + 9.0f * cu * cF);
        float fi = f[i * cells + c];
        f[i * cells + c] = fi - omega * (fi - feq) + force;
    }
}

__kernel void mp_lbmf_stream(
    int nx, int ny, int nz,
    __global const float* f, __global float* fNew)
{
    LBM_TABLES
    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;
    int z = c % nz;
    int y = (c / nz) % ny;
    int x = c / (nz * ny);
    for (int i = 0; i < LBM_Q; i++)
    {
        int sx = x - cx[i], sy = y - cy[i], sz = z - cz[i];
        if (sx < 0) sx += nx; else if (sx >= nx) sx -= nx;
        if (sy < 0) sy += ny; else if (sy >= ny) sy -= ny;
        if (sz < 0) sz += nz; else if (sz >= nz) sz -= nz;
        fNew[i * cells + c] = f[i * cells + (sx * ny + sy) * nz + sz];
    }
}

__kernel void mp_lbmf_boundary(int nx, int ny, int nz, float uIn, __global float* f)
{
    LBM_TABLES
    int cells = nx * ny * nz;
    int yz = get_global_id(0);
    if (yz >= ny * nz) return;
    int z = yz % nz, y = yz / nz;
    int cin = (0 * ny + y) * nz + z;
    int cout = ((nx - 1) * ny + y) * nz + z;
    int cprev = ((nx - 2) * ny + y) * nz + z;
    for (int i = 0; i < LBM_Q; i++)
    {
        f[i * cells + cin] = mp_feq(i, 1.0f, uIn, 0.0f, 0.0f, cx[i], cy[i], cz[i], w[i]);
        f[i * cells + cout] = f[i * cells + cprev];
    }
}
";

    private IntPtr _program, _deposit, _macro, _collide, _stream, _boundary;
    private IntPtr _f, _fNew, _Fx, _Fy, _Fz, _rho, _ux, _uy, _uz, _ePos, _eForce;
    private int _capacity;

    public static ClLbmForce? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClLbmForce();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._deposit = OpenCl.Kernel(b._program, "mp_lbmf_deposit");
            b._macro = OpenCl.Kernel(b._program, "mp_lbmf_macro");
            b._collide = OpenCl.Kernel(b._program, "mp_lbmf_collide");
            b._stream = OpenCl.Kernel(b._program, "mp_lbmf_stream");
            b._boundary = OpenCl.Kernel(b._program, "mp_lbmf_boundary");
            if (b._deposit == IntPtr.Zero || b._macro == IntPtr.Zero || b._collide == IntPtr.Zero
                || b._stream == IntPtr.Zero || b._boundary == IntPtr.Zero) { b.Dispose(); return null; }

            long pop = 4L * Q * maxCells, field = 4L * maxCells, elems = 4L * 3 * MaxElements;
            b._f = OpenCl.Buffer(pop); b._fNew = OpenCl.Buffer(pop);
            b._Fx = OpenCl.Buffer(field); b._Fy = OpenCl.Buffer(field); b._Fz = OpenCl.Buffer(field);
            b._rho = OpenCl.Buffer(field); b._ux = OpenCl.Buffer(field); b._uy = OpenCl.Buffer(field); b._uz = OpenCl.Buffer(field);
            b._ePos = OpenCl.Buffer(elems); b._eForce = OpenCl.Buffer(elems);
            if (b._f == IntPtr.Zero || b._fNew == IntPtr.Zero || b._Fx == IntPtr.Zero || b._Fy == IntPtr.Zero
                || b._Fz == IntPtr.Zero || b._rho == IntPtr.Zero || b._ux == IntPtr.Zero || b._uy == IntPtr.Zero
                || b._uz == IntPtr.Zero || b._ePos == IntPtr.Zero || b._eForce == IntPtr.Zero)
            { b.Dispose(); return null; }
            // the field starts at zero: a Macros before the first Step must not read garbage
            var zero = new float[maxCells];
            if (!OpenCl.Write(b._Fx, zero, maxCells) || !OpenCl.Write(b._Fy, zero, maxCells)
                || !OpenCl.Write(b._Fz, zero, maxCells)) { b.Dispose(); return null; }
            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClLbmForce() { }

    public bool Upload(int cells, float[]? f)
    {
        if (_collide == IntPtr.Zero || cells <= 0 || cells > _capacity || f is null || f.Length < (long)Q * cells) return false;
        try { return OpenCl.Write(_f, f, (long)Q * cells); }
        catch { return false; }
    }

    public bool Step(int nx, int ny, int nz, float tau, float uIn,
        int elements, float[]? elemPos, float[]? elemForce, float eps, int steps)
    {
        if (_collide == IntPtr.Zero || steps <= 0 || !(tau > 0.5f) || !(eps > 0) || nx < 3) return false;
        if (elements < 0 || elements > MaxElements) return false;
        long cells;
        try { cells = checked((long)nx * ny * nz); }
        catch (OverflowException) { return false; }
        if (cells <= 0 || cells > _capacity) return false;
        elemPos ??= []; elemForce ??= [];
        if (elemPos.Length < 3 * elements || elemForce.Length < 3 * elements) return false;

        float omega = 1.0f / tau;
        long yz = (long)ny * nz;
        // the inflow the solver is running with, remembered for the read-back
        // pass: the macro kernel repairs a corrupt cell by reseeding it to
        // equilibrium at uIn, and the read-back used to pass zero - a rare
        // corruption found while downloading became a hole of still air in a
        // moving tunnel (29.08)
        _lastUIn = uIn;
        try
        {
            if (elements > 0 && (!OpenCl.Write(_ePos, elemPos, 3L * elements) || !OpenCl.Write(_eForce, elemForce, 3L * elements)))
                return false;
            for (int s = 0; s < steps; s++)
            {
                bool ok = OpenCl.Arg(_deposit, 0, nx) && OpenCl.Arg(_deposit, 1, ny) && OpenCl.Arg(_deposit, 2, nz)
                       && OpenCl.Arg(_deposit, 3, elements) && OpenCl.Arg(_deposit, 4, eps)
                       && OpenCl.Arg(_deposit, 5, _ePos) && OpenCl.Arg(_deposit, 6, _eForce)
                       && OpenCl.Arg(_deposit, 7, _Fx) && OpenCl.Arg(_deposit, 8, _Fy) && OpenCl.Arg(_deposit, 9, _Fz)
                       && OpenCl.Launch(_deposit, cells);
                if (!ok) return false;
                if (!MacroLaunch(nx, ny, nz, uIn, cells)) return false;
                ok = OpenCl.Arg(_collide, 0, nx) && OpenCl.Arg(_collide, 1, ny) && OpenCl.Arg(_collide, 2, nz)
                  && OpenCl.Arg(_collide, 3, omega) && OpenCl.Arg(_collide, 4, _f)
                  && OpenCl.Arg(_collide, 5, _rho) && OpenCl.Arg(_collide, 6, _ux) && OpenCl.Arg(_collide, 7, _uy) && OpenCl.Arg(_collide, 8, _uz)
                  && OpenCl.Arg(_collide, 9, _Fx) && OpenCl.Arg(_collide, 10, _Fy) && OpenCl.Arg(_collide, 11, _Fz)
                  && OpenCl.Launch(_collide, cells);
                if (!ok) return false;
                ok = OpenCl.Arg(_stream, 0, nx) && OpenCl.Arg(_stream, 1, ny) && OpenCl.Arg(_stream, 2, nz)
                  && OpenCl.Arg(_stream, 3, _f) && OpenCl.Arg(_stream, 4, _fNew)
                  && OpenCl.Launch(_stream, cells);
                if (!ok) return false;
                (_f, _fNew) = (_fNew, _f);   // host-side swap, arguments re-set every iteration
                ok = OpenCl.Arg(_boundary, 0, nx) && OpenCl.Arg(_boundary, 1, ny) && OpenCl.Arg(_boundary, 2, nz)
                  && OpenCl.Arg(_boundary, 3, uIn) && OpenCl.Arg(_boundary, 4, _f)
                  && OpenCl.Launch(_boundary, yz);
                if (!ok) return false;
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    /// <summary>Inflow of the most recent solver step - see Macros.</summary>
    private float _lastUIn;

    private bool MacroLaunch(int nx, int ny, int nz, float uIn, long cells)
        => OpenCl.Arg(_macro, 0, nx) && OpenCl.Arg(_macro, 1, ny) && OpenCl.Arg(_macro, 2, nz)
        && OpenCl.Arg(_macro, 3, uIn) && OpenCl.Arg(_macro, 4, _f)
        && OpenCl.Arg(_macro, 5, _Fx) && OpenCl.Arg(_macro, 6, _Fy) && OpenCl.Arg(_macro, 7, _Fz)
        && OpenCl.Arg(_macro, 8, _rho) && OpenCl.Arg(_macro, 9, _ux) && OpenCl.Arg(_macro, 10, _uy) && OpenCl.Arg(_macro, 11, _uz)
        && OpenCl.Launch(_macro, cells);

    public bool Macros(int nx, int ny, int nz, float[]? rho, float[]? ux, float[]? uy, float[]? uz)
    {
        if (_collide == IntPtr.Zero) return false;
        long cells;
        try { cells = checked((long)nx * ny * nz); }
        catch (OverflowException) { return false; }
        if (cells <= 0 || cells > _capacity) return false;
        // Any of the four may be null: the caller downloads what it needs.
        // Betz takes the three velocities every step and the density only when
        // it hands the field back to the CPU - a quarter less traffic on a
        // path where the readback IS the frame (28.08).
        if (rho is not null && rho.Length < cells) return false;
        if (ux is not null && ux.Length < cells) return false;
        if (uy is not null && uy.Length < cells) return false;
        if (uz is not null && uz.Length < cells) return false;
        if (rho is null && ux is null && uy is null && uz is null) return false;
        try
        {
            if (!MacroLaunch(nx, ny, nz, _lastUIn, cells)) return false;
            return (rho is null || OpenCl.Read(_rho, rho, cells))
                && (ux is null || OpenCl.Read(_ux, ux, cells))
                && (uy is null || OpenCl.Read(_uy, uy, cells))
                && (uz is null || OpenCl.Read(_uz, uz, cells));
        }
        catch { return false; }
    }

    public bool DownloadF(int cells, float[]? f)
    {
        if (_collide == IntPtr.Zero || cells <= 0 || cells > _capacity || f is null || f.Length < (long)Q * cells) return false;
        try { return OpenCl.Read(_f, f, (long)Q * cells); }
        catch { return false; }
    }

    public void Dispose()
    {
        foreach (var b in new[] { _f, _fNew, _Fx, _Fy, _Fz, _rho, _ux, _uy, _uz, _ePos, _eForce }) OpenCl.Release(b);
        _f = _fNew = _Fx = _Fy = _Fz = _rho = _ux = _uy = _uz = _ePos = _eForce = IntPtr.Zero;
        foreach (var k in new[] { _deposit, _macro, _collide, _stream, _boundary }) OpenCl.ReleaseKernel(k);
        _deposit = _macro = _collide = _stream = _boundary = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
