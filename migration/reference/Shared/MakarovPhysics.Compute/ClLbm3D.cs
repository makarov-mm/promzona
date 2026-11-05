namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the D3Q19 lattice Boltzmann fluid (Lbm3D):
/// lbm3d_cuda.cu translated - BGK collision with the full Guo forcing, pull
/// streaming with half-way bounce-back at the y-walls and on solid cells,
/// periodic x and z, the corrupted-cell reseed guard, and the half-force term
/// in the macroscopic velocity. Fourteenth client of <see cref="OpenCl"/>.
/// Never-throw contract.
///
/// THE TABLES MOVED INTO THE SOURCE. CUDA keeps the D3Q19 velocity set in
/// __constant__ arrays; OpenCL's __constant address space needs either a
/// kernel argument or a program-scope initialiser, and program-scope
/// __constant with an initialiser is only guaranteed from OpenCL 2.0. So the
/// tables are plain constant arrays inside each kernel, initialised where
/// they are declared. Every compiler folds them; on a 1.2 driver it is the
/// difference between running and not building at all.
///
/// THE POPULATION BUFFERS SWAP ON THE HOST. The .cu swaps the f and fNew
/// pointers inside its Step loop, which the device never sees - the swap is
/// host-side bookkeeping between launches. Here the same swap happens between
/// enqueues, so the kernel argument for the read buffer alternates. It has to
/// be re-set every iteration rather than once before the loop: an argument
/// set once would have every step streaming out of the same buffer, which
/// looks like a fluid that has stopped rather than like an error.
///
/// The difference from the CPU path is the .cu's own and survives unchanged:
/// float instead of double (standard LBM practice at these Mach numbers) and
/// the SoA layout f[i * cells + cell].
/// </summary>
public sealed class ClLbm3D : ILbm3DBackend
{
    private const int Q = 19;

    private const string Source = @"
#define LBM_Q 19

/* D3Q19 tables, identical to Lbm3DModel.C / W / Opp and to the .cu's
   __constant__ arrays. Declared inside the kernels: program-scope
   __constant with an initialiser is an OpenCL 2.0 guarantee, and this has
   to build on a 1.2 driver. */
#define LBM_TABLES \
    const int cx[LBM_Q] = { 0, 1,-1, 0, 0, 0, 0, 1,-1, 1,-1, 1,-1, 1,-1, 0, 0, 0, 0 }; \
    const int cy[LBM_Q] = { 0, 0, 0, 1,-1, 0, 0, 1,-1,-1, 1, 0, 0, 0, 0, 1,-1, 1,-1 }; \
    const int cz[LBM_Q] = { 0, 0, 0, 0, 0, 1,-1, 0, 0, 0, 0, 1,-1,-1, 1, 1,-1,-1, 1 }; \
    const float w[LBM_Q] = { \
        1.0f/3, \
        1.0f/18,1.0f/18,1.0f/18,1.0f/18,1.0f/18,1.0f/18, \
        1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36, \
        1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36,1.0f/36 };

__kernel void mp_lbm_collide(
    int nx, int ny, int nz, float omega, float forceX, float cs2,
    __global float* restrict f,
    __global const uchar* restrict solid)
{
    LBM_TABLES
    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;
    if (solid[c]) return;

    float fi[LBM_Q];
    float rho = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int i = 0; i < LBM_Q; i++)
    {
        float v = f[i * cells + c];
        fi[i] = v;
        rho += v;
        mx += v * cx[i]; my += v * cy[i]; mz += v * cz[i];
    }

    /* corrupted cell: reseed to rest equilibrium (CPU-model guard) */
    if (!(rho > 1e-9f) || !isfinite(rho))
    {
        for (int i = 0; i < LBM_Q; i++) f[i * cells + c] = w[i];
        return;
    }

    float ux = mx / rho + forceX / (2.0f * rho);
    float uy = my / rho;
    float uz = mz / rho;
    float usq = ux * ux + uy * uy + uz * uz;

    /* Smagorinsky LES (tail425): Hou from the D3Q19 Pi_neq, cs2 = 0 keeps the bare BGK bitwise */
    if (cs2 > 0.0f)
    {
        float pxx = 0.0f, pyy = 0.0f, pzz = 0.0f, pxy = 0.0f, pxz = 0.0f, pyz = 0.0f;
        for (int i = 1; i < LBM_Q; i++)
        {
            float cu = cx[i] * ux + cy[i] * uy + cz[i] * uz;
            float n = fi[i] - w[i] * rho * (1.0f + 3.0f * cu + 4.5f * cu * cu - 1.5f * usq);
            pxx += cx[i] * cx[i] * n; pyy += cy[i] * cy[i] * n; pzz += cz[i] * cz[i] * n;
            pxy += cx[i] * cy[i] * n; pxz += cx[i] * cz[i] * n; pyz += cy[i] * cz[i] * n;
        }
        float q = sqrt(2.0f * (pxx * pxx + pyy * pyy + pzz * pzz + 2.0f * (pxy * pxy + pxz * pxz + pyz * pyz)));
        float tau0 = 1.0f / omega;
        float tau = 0.5f * (tau0 + sqrt(tau0 * tau0 + 18.0f * cs2 * q / rho));
        omega = 1.0f / tau;
    }

    for (int i = 0; i < LBM_Q; i++)
    {
        float cu = cx[i] * ux + cy[i] * uy + cz[i] * uz;
        float feq = w[i] * rho * (1.0f + 3.0f * cu + 4.5f * cu * cu - 1.5f * usq);
        /* Guo (2002) forcing, full form, F = (forceX, 0, 0) */
        float force = w[i] * (1.0f - 0.5f * omega)
                    * (3.0f * (cx[i] - ux) * forceX + 9.0f * cu * cx[i] * forceX);
        f[i * cells + c] = fi[i] - omega * (fi[i] - feq) + force;
    }
}

__kernel void mp_lbm_stream(
    int nx, int ny, int nz,
    __global const float* restrict f,
    __global float* restrict fNew,
    __global const uchar* restrict solid)
{
    LBM_TABLES
    const int opp[LBM_Q] = { 0, 2,1,4,3,6,5, 8,7,10,9,12,11,14,13,16,15,18,17 };

    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;
    if (solid[c]) return;

    int z = c % nz;
    int y = (c / nz) % ny;
    int x = c / (nz * ny);

    for (int i = 0; i < LBM_Q; i++)
    {
        int sx = x - cx[i], sy = y - cy[i], sz = z - cz[i];
        /* periodic x/z (|c_i| <= 1: one wrap suffices), walls in y */
        if (sx < 0) sx += nx; else if (sx >= nx) sx -= nx;
        if (sz < 0) sz += nz; else if (sz >= nz) sz -= nz;
        bool wall = sy < 0 || sy >= ny;
        int sc = 0;
        if (!wall)
        {
            sc = (sx * ny + sy) * nz + sz;
            if (solid[sc]) wall = true;
        }
        fNew[i * cells + c] = wall ? f[opp[i] * cells + c] : f[i * cells + sc];
    }
}

__kernel void mp_lbm_macro(
    int nx, int ny, int nz, float forceX,
    __global const float* restrict f,
    __global const uchar* restrict solid,
    __global float* restrict rhoOut,
    __global float* restrict uxOut,
    __global float* restrict uyOut,
    __global float* restrict uzOut)
{
    LBM_TABLES
    int cells = nx * ny * nz;
    int c = get_global_id(0);
    if (c >= cells) return;

    if (solid[c])
    {
        rhoOut[c] = 1.0f; uxOut[c] = 0.0f; uyOut[c] = 0.0f; uzOut[c] = 0.0f;
        return;
    }

    float rho = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int i = 0; i < LBM_Q; i++)
    {
        float v = f[i * cells + c];
        rho += v;
        mx += v * cx[i]; my += v * cy[i]; mz += v * cz[i];
    }
    if (!(rho > 1e-9f) || !isfinite(rho))
    {
        rhoOut[c] = 1.0f; uxOut[c] = 0.0f; uyOut[c] = 0.0f; uzOut[c] = 0.0f;
        return;
    }
    rhoOut[c] = rho;
    /* Guo half-force in the macroscopic velocity, same as the CPU model */
    uxOut[c] = (mx + 0.5f * forceX) / rho;
    uyOut[c] = my / rho;
    uzOut[c] = mz / rho;
}
";

    private IntPtr _program, _collide, _stream, _macro;
    private IntPtr _f, _fNew, _solid, _rho, _ux, _uy, _uz;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU
    /// lattice.</summary>
    public static ClLbm3D? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClLbm3D();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._collide = OpenCl.Kernel(b._program, "mp_lbm_collide");
            b._stream = OpenCl.Kernel(b._program, "mp_lbm_stream");
            b._macro = OpenCl.Kernel(b._program, "mp_lbm_macro");
            if (b._collide == IntPtr.Zero || b._stream == IntPtr.Zero || b._macro == IntPtr.Zero)
            { b.Dispose(); return null; }

            long pop = 4L * Q * maxCells;
            long field = 4L * maxCells;
            b._f = OpenCl.Buffer(pop);
            b._fNew = OpenCl.Buffer(pop);
            b._solid = OpenCl.Buffer(maxCells);
            b._rho = OpenCl.Buffer(field);
            b._ux = OpenCl.Buffer(field);
            b._uy = OpenCl.Buffer(field);
            b._uz = OpenCl.Buffer(field);
            if (b._f == IntPtr.Zero || b._fNew == IntPtr.Zero || b._solid == IntPtr.Zero
                || b._rho == IntPtr.Zero || b._ux == IntPtr.Zero || b._uy == IntPtr.Zero
                || b._uz == IntPtr.Zero) { b.Dispose(); return null; }

            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClLbm3D() { }

    public bool Upload(int cells, float[]? f, byte[]? solid)
    {
        if (_collide == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (f is null || f.Length < (long)Q * cells || solid is null || solid.Length < cells)
            return false;
        try
        {
            return OpenCl.Write(_f, f, (long)Q * cells) && OpenCl.Write(_solid, solid, cells);
        }
        catch { return false; }
    }

    public bool Step(int nx, int ny, int nz, float tau, float forceX, int steps, float cs2)
    {
        if (_collide == IntPtr.Zero || steps <= 0 || !(tau > 0.5f)) return false;
        long cells;
        try { cells = checked((long)nx * ny * nz); }
        catch (OverflowException) { return false; }
        if (cells <= 0 || cells > _capacity) return false;

        float omega = 1.0f / tau;
        try
        {
            for (int s = 0; s < steps; s++)
            {
                bool ok = OpenCl.Arg(_collide, 0, nx) && OpenCl.Arg(_collide, 1, ny)
                       && OpenCl.Arg(_collide, 2, nz) && OpenCl.Arg(_collide, 3, omega)
                       && OpenCl.Arg(_collide, 4, forceX) && OpenCl.Arg(_collide, 5, cs2)
                       && OpenCl.Arg(_collide, 6, _f) && OpenCl.Arg(_collide, 7, _solid)
                       && OpenCl.Launch(_collide, cells);
                if (!ok) return false;

                ok = OpenCl.Arg(_stream, 0, nx) && OpenCl.Arg(_stream, 1, ny)
                  && OpenCl.Arg(_stream, 2, nz)
                  && OpenCl.Arg(_stream, 3, _f) && OpenCl.Arg(_stream, 4, _fNew)
                  && OpenCl.Arg(_stream, 5, _solid)
                  && OpenCl.Launch(_stream, cells);
                if (!ok) return false;

                // the .cu swaps its two device pointers here; the swap is
                // host-side bookkeeping either way, and the arguments above are
                // re-set every iteration precisely because of it
                (_f, _fNew) = (_fNew, _f);
            }

            // finish before returning: the caller's stopwatch then measures real
            // GPU work, and a queue error surfaces in Step rather than smearing
            // into the next Macros
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Macros(int nx, int ny, int nz, float forceX,
        float[]? rho, float[]? ux, float[]? uy, float[]? uz)
    {
        if (_collide == IntPtr.Zero) return false;
        long cells;
        try { cells = checked((long)nx * ny * nz); }
        catch (OverflowException) { return false; }
        if (cells <= 0 || cells > _capacity) return false;
        if (rho is null || rho.Length < cells || ux is null || ux.Length < cells
            || uy is null || uy.Length < cells || uz is null || uz.Length < cells) return false;

        try
        {
            bool ok = OpenCl.Arg(_macro, 0, nx) && OpenCl.Arg(_macro, 1, ny)
                   && OpenCl.Arg(_macro, 2, nz) && OpenCl.Arg(_macro, 3, forceX)
                   && OpenCl.Arg(_macro, 4, _f) && OpenCl.Arg(_macro, 5, _solid)
                   && OpenCl.Arg(_macro, 6, _rho) && OpenCl.Arg(_macro, 7, _ux)
                   && OpenCl.Arg(_macro, 8, _uy) && OpenCl.Arg(_macro, 9, _uz)
                   && OpenCl.Launch(_macro, cells);
            if (!ok) return false;

            return OpenCl.Read(_rho, rho, cells) && OpenCl.Read(_ux, ux, cells)
                && OpenCl.Read(_uy, uy, cells) && OpenCl.Read(_uz, uz, cells);
        }
        catch { return false; }
    }

    public bool DownloadF(int cells, float[]? f)
    {
        if (_collide == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (f is null || f.Length < (long)Q * cells) return false;
        try { return OpenCl.Read(_f, f, (long)Q * cells); }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_f); OpenCl.Release(_fNew); OpenCl.Release(_solid);
        OpenCl.Release(_rho); OpenCl.Release(_ux); OpenCl.Release(_uy); OpenCl.Release(_uz);
        _f = _fNew = _solid = _rho = _ux = _uy = _uz = IntPtr.Zero;
        OpenCl.ReleaseKernel(_collide); OpenCl.ReleaseKernel(_stream); OpenCl.ReleaseKernel(_macro);
        _collide = _stream = _macro = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
