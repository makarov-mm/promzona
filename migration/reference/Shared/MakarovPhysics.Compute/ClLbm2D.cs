namespace MakarovPhysics.Compute;

/// <summary>
/// The OpenCL twin of <see cref="CudaLbm2D"/>: the same D2Q9 kernels ported
/// VERBATIM from Native/CudaNBody/lbm2d_cuda.cu into OpenCL C (threadIdx
/// arithmetic becomes get_global_id, __constant stays __constant, the
/// conditionals are untouched), compiled by the driver at first use. Device
/// buffers ping-pong by handle swap on the host - same semantics as the .cu
/// pointer swap, including uploading f into BOTH population buffers so the
/// slots streaming never writes keep their previous contents.
///
/// The CUDA backend stays the reference: on a machine with both, the two
/// must agree to fp32 tolerance, and any disagreement is an OpenCL bug by
/// definition. Never-throw contract as everywhere.
/// </summary>
public sealed class ClLbm2D : ILbm2DBackend
{
    /* the .cu kernels, mechanically translated:
     *   blockIdx.x * blockDim.x + threadIdx.x  ->  get_global_id(0)
     *   __global__ / __device__                ->  __kernel / plain function
     *   __restrict__                           ->  restrict
     *   sqrtf/fminf/fmaxf/fmodf                ->  sqrt/fmin/fmax/fmod
     * everything numeric is byte-for-byte the same expression. */
    private const string Source = @"
__constant int L2DirX[9] = { 0, 1, 0, -1, 0, 1, -1, -1, 1 };
__constant int L2DirY[9] = { 0, 0, 1, 0, -1, 1, 1, -1, -1 };
__constant int L2Opp[9]  = { 0, 3, 4, 1, 2, 7, 8, 5, 6 };
__constant float L2W[9]  = { 4.0f / 9,  1.0f / 9, 1.0f / 9, 1.0f / 9, 1.0f / 9,
                             1.0f / 36, 1.0f / 36, 1.0f / 36, 1.0f / 36 };

inline float L2Eq(int dir, float rho, float ux, float uy)
{
    float eu = L2DirX[dir] * ux + L2DirY[dir] * uy;
    float uSq = ux * ux + uy * uy;
    return L2W[dir] * rho * (1.0f + 3.0f * eu + 4.5f * eu * eu - 1.5f * uSq);
}


/* ---- sub-grid closures (tail424): mirrors of the CUDA L2WaleKernel / L2SmagOmega ---- */
__kernel void mp_l2_wale(int w, int h, float coef,
    __global const float* restrict velx, __global const float* restrict vely,
    __global const uchar* restrict solid, __global float* restrict nut)
{
    int cell = get_global_id(0);
    if (cell >= w * h) return;
    if (solid[cell]) { nut[cell] = 0.0f; return; }
    int x = cell % w, y = cell / w;
    int xm = max(x - 1, 0), xp = min(x + 1, w - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, h - 1);
    float gxx = 0.5f * (velx[y * w + xp] - velx[y * w + xm]);
    float gxy = 0.5f * (velx[yp * w + x] - velx[ym * w + x]);
    float gyx = 0.5f * (vely[y * w + xp] - vely[y * w + xm]);
    float gyy = 0.5f * (vely[yp * w + x] - vely[ym * w + x]);
    float sxy = 0.5f * (gxy + gyx);
    float ss = gxx * gxx + gyy * gyy + 2.0f * sxy * sxy;
    float q00 = gxx * gxx + gxy * gyx, q01 = gxx * gxy + gxy * gyy;
    float q10 = gyx * gxx + gyy * gyx, q11 = gyx * gxy + gyy * gyy;
    float tr = (q00 + q11) / 3.0f;
    float sd00 = q00 - tr, sd11 = q11 - tr, sd22 = -tr, sd01 = 0.5f * (q01 + q10);
    float sdsd = sd00 * sd00 + sd11 * sd11 + sd22 * sd22 + 2.0f * sd01 * sd01;
    float denom = pow(ss, 2.5f) + pow(sdsd, 1.25f);
    nut[cell] = denom > 1e-30f ? coef * pow(sdsd, 1.5f) / denom : 0.0f;
}

inline float mp_l2_smag_omega(float omega, float rho, float pxx, float pyy, float pxy, float cs2, float* nutOut)
{
    float q = sqrt(2.0f * (pxx * pxx + pyy * pyy + 2.0f * pxy * pxy));
    float tau0 = 1.0f / omega;
    float tau = 0.5f * (tau0 + sqrt(tau0 * tau0 + 18.0f * cs2 * q / rho));
    *nutOut = (tau - tau0) / 3.0f;
    return 1.0f / tau;
}

__kernel void mp_l2_collide(int w, int h, float omega, float inflow, int mrt,
    int turb, float cs2, __global float* restrict nut,
    __global float* restrict f, __global const uchar* restrict solid,
    __global float* restrict velx, __global float* restrict vely,
    __global float* restrict rhoOut)
{
    int cell = get_global_id(0);
    int cells = w * h;
    if (cell >= cells) return;
    if (solid[cell]) return;

    float fi[9];
    float rho = 0.0f, mx = 0.0f, my = 0.0f;
    for (int d = 0; d < 9; d++)
    {
        float v = f[d * cells + cell];
        fi[d] = v;
        rho += v;
        mx += v * L2DirX[d];
        my += v * L2DirY[d];
    }

    /* corrupted-cell re-seed (the CPU guard): equilibrium at the inflow */
    if (!(rho > 1e-9f) || !isfinite(rho))
    {
        for (int d = 0; d < 9; d++)
            f[d * cells + cell] = L2Eq(d, 1.0f, inflow, 0.0f);
        velx[cell] = inflow; vely[cell] = 0.0f; rhoOut[cell] = 1.0f;
        return;
    }

    float ux = mx / rho, uy = my / rho;
    float speed = sqrt(ux * ux + uy * uy);
    if (speed > 0.35f) { ux *= 0.35f / speed; uy *= 0.35f / speed; }

    velx[cell] = ux; vely[cell] = uy; rhoOut[cell] = rho;
    if (turb == 2) omega = 1.0f / (1.0f / omega + 3.0f * nut[cell]);
    else if (turb == 0) nut[cell] = 0.0f;

    if (mrt)
    {
        float f0 = fi[0], f1 = fi[1], f2 = fi[2], f3 = fi[3], f4 = fi[4];
        float f5 = fi[5], f6 = fi[6], f7 = fi[7], f8 = fi[8];
        float diagSum = f5 + f6 + f7 + f8;
        float axisSum = f1 + f2 + f3 + f4;
        float m1 = -4.0f * f0 - axisSum + 2.0f * diagSum;
        float m2 = 4.0f * f0 - 2.0f * axisSum + diagSum;
        float m3 = f1 - f3 + f5 - f6 - f7 + f8;
        float m4 = -2.0f * f1 + 2.0f * f3 + f5 - f6 - f7 + f8;
        float m5 = f2 - f4 + f5 + f6 - f7 - f8;
        float m6 = -2.0f * f2 + 2.0f * f4 + f5 + f6 - f7 - f8;
        float m7 = f1 - f2 + f3 - f4;
        float m8 = f5 - f6 + f7 - f8;

        float jx = rho * ux, jy = rho * uy;
        float j2r = 3.0f * (jx * jx + jy * jy) / rho;
        if (turb == 1)
        {
            float tr = (m1 - (-2.0f * rho + j2r)) / 3.0f;
            float dif = m7 - (jx * jx - jy * jy) / rho;
            float pxy = m8 - jx * jy / rho;
            float nt; omega = mp_l2_smag_omega(omega, rho, 0.5f * (tr + dif), 0.5f * (tr - dif), pxy, cs2, &nt); nut[cell] = nt;
        }
        m1 -= 1.64f * (m1 - (-2.0f * rho + j2r));
        m2 -= 1.54f * (m2 - (rho - j2r));
        m3 -= omega * (m3 - jx);
        m5 -= omega * (m5 - jy);
        m4 -= 1.9f * (m4 - (-jx));
        m6 -= 1.9f * (m6 - (-jy));
        m7 -= omega * (m7 - (jx * jx - jy * jy) / rho);
        m8 -= omega * (m8 - jx * jy / rho);

        float a0 = rho / 9.0f, a1 = m1 / 36.0f, a2 = m2 / 36.0f;
        float a3 = m3 / 6.0f, a4 = m4 / 12.0f, a5 = m5 / 6.0f, a6 = m6 / 12.0f;
        float a7 = m7 / 4.0f, a8 = m8 / 4.0f;

        f[cell] = a0 - 4.0f * a1 + 4.0f * a2;
        f[cells + cell] = a0 - a1 - 2.0f * a2 + a3 - 2.0f * a4 + a7;
        f[2 * cells + cell] = a0 - a1 - 2.0f * a2 + a5 - 2.0f * a6 - a7;
        f[3 * cells + cell] = a0 - a1 - 2.0f * a2 - a3 + 2.0f * a4 + a7;
        f[4 * cells + cell] = a0 - a1 - 2.0f * a2 - a5 + 2.0f * a6 - a7;
        f[5 * cells + cell] = a0 + 2.0f * a1 + a2 + a3 + a4 + a5 + a6 + a8;
        f[6 * cells + cell] = a0 + 2.0f * a1 + a2 - a3 - a4 + a5 + a6 - a8;
        f[7 * cells + cell] = a0 + 2.0f * a1 + a2 - a3 - a4 - a5 - a6 + a8;
        f[8 * cells + cell] = a0 + 2.0f * a1 + a2 + a3 + a4 - a5 - a6 - a8;
    }
    else
    {
        float eq[9];
        for (int d = 0; d < 9; d++) eq[d] = L2Eq(d, rho, ux, uy);
        if (turb == 1)
        {
            float n1 = fi[1] - eq[1], n2 = fi[2] - eq[2], n3 = fi[3] - eq[3], n4 = fi[4] - eq[4];
            float n5 = fi[5] - eq[5], n6 = fi[6] - eq[6], n7 = fi[7] - eq[7], n8 = fi[8] - eq[8];
            float pxx = n1 + n3 + n5 + n6 + n7 + n8;
            float pyy = n2 + n4 + n5 + n6 + n7 + n8;
            float pxy = n5 - n6 + n7 - n8;
            float nt; omega = mp_l2_smag_omega(omega, rho, pxx, pyy, pxy, cs2, &nt); nut[cell] = nt;
        }
        for (int d = 0; d < 9; d++)
            f[d * cells + cell] = fi[d] + omega * (eq[d] - fi[d]);
    }
}

/* push streaming, verbatim conditionals; the destination buffer keeps its
 * previous contents for the slots nobody writes (the CPU swap semantics) */
__kernel void mp_l2_stream(int w, int h, int noSlip,
    __global const float* restrict f, __global float* restrict fs,
    __global const uchar* restrict solid)
{
    int cell = get_global_id(0);
    int cells = w * h;
    if (cell >= cells) return;
    if (solid[cell]) return;
    int x = cell % w, y = cell / w;

    for (int d = 0; d < 9; d++)
    {
        int destX = x + L2DirX[d];
        int destY = y + L2DirY[d];
        if (destY < 0 || destY >= h)
        {
            if (noSlip)
            {
                fs[L2Opp[d] * cells + cell] = f[d * cells + cell];
                continue;
            }
            destY = (destY + h) % h;
        }
        if (destX < 0 || destX >= w) continue;
        int destCell = destY * w + destX;
        if (solid[destCell])
            fs[L2Opp[d] * cells + cell] = f[d * cells + cell];
        else
            fs[d * cells + destCell] = f[d * cells + cell];
    }
}

__kernel void mp_l2_boundary(int w, int h, float inflow, __global float* restrict f)
{
    int y = get_global_id(0);
    if (y >= h) return;
    int cells = w * h;
    int inlet = y * w;
    for (int d = 0; d < 9; d++)
        f[d * cells + inlet] = L2Eq(d, 1.0f, inflow, 0.0f);
    int outlet = y * w + (w - 1);
    for (int d = 0; d < 9; d++)
        f[d * cells + outlet] = f[d * cells + (outlet - 1)];
}

/* stripe injection: alternate stripes feed the warm (A) and cool (B) dye */
__kernel void mp_l2_inject(int w, int h, int stripePeriod,
    __global float* restrict dyeA, __global float* restrict dyeB)
{
    int y = get_global_id(0);
    if (y >= h) return;
    if (y % stripePeriod < stripePeriod / 2)
    {
        int stripe = y / stripePeriod;
        __global float* dye = (stripe & 1) ? dyeB : dyeA;
        dye[y * w + 1] = 1.0f;
        dye[y * w + 2] = 1.0f;
    }
}

__kernel void mp_l2_dye(int w, int h, int noSlip,
    __global const float* restrict velx, __global const float* restrict vely,
    __global const uchar* restrict solid,
    __global const float* restrict dyeA, __global const float* restrict dyeB,
    __global float* restrict dyeAN, __global float* restrict dyeBN)
{
    int cell = get_global_id(0);
    int cells = w * h;
    if (cell >= cells) return;
    if (solid[cell]) { dyeAN[cell] = 0.0f; dyeBN[cell] = 0.0f; return; }
    int x = cell % w, y = cell / w;

    float sourceX = x - velx[cell];
    float sourceY = y - vely[cell];
    sourceX = fmin(fmax(sourceX, 0.0f), w - 1.001f);
    if (noSlip) sourceY = fmin(fmax(sourceY, 0.0f), h - 1.001f);
    else sourceY = fmod(fmod(sourceY, (float)h) + h, (float)h);

    int x0 = (int)sourceX, y0 = (int)sourceY;
    int x1 = min(x0 + 1, w - 1), y1 = (y0 + 1) % h;
    float fx = sourceX - x0, fy = sourceY - y0;

    float topA = dyeA[y0 * w + x0] * (1.0f - fx) + dyeA[y0 * w + x1] * fx;
    float botA = dyeA[y1 * w + x0] * (1.0f - fx) + dyeA[y1 * w + x1] * fx;
    dyeAN[cell] = (topA * (1.0f - fy) + botA * fy) * 0.9995f;

    float topB = dyeB[y0 * w + x0] * (1.0f - fx) + dyeB[y0 * w + x1] * fx;
    float botB = dyeB[y1 * w + x0] * (1.0f - fx) + dyeB[y1 * w + x1] * fx;
    dyeBN[cell] = (topB * (1.0f - fy) + botB * fy) * 0.9995f;
}
";

    private IntPtr _program, _kCollide, _kStream, _kBoundary, _kInject, _kDye, _kWale;
    private IntPtr _f, _fs, _dyeA, _dyeB, _dyeAN, _dyeBN, _velx, _vely, _rho, _solid, _nut;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClLbm2D? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;

        // `b` is declared OUTSIDE the try, so the catch can still dispose it:
        // inside, an exception between the allocation and the return left the
        // GL-side handles unreachable - the pilot was written before the
        // pattern settled and the ten later backends all do it this way
        // (22.08). The runtime never throws by contract, which is exactly why
        // nobody noticed for eleven ports.
        var b = new ClLbm2D { _capacity = maxCells };
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }

            b._kCollide = OpenCl.Kernel(b._program, "mp_l2_collide");
            b._kStream = OpenCl.Kernel(b._program, "mp_l2_stream");
            b._kBoundary = OpenCl.Kernel(b._program, "mp_l2_boundary");
            b._kInject = OpenCl.Kernel(b._program, "mp_l2_inject");
            b._kDye = OpenCl.Kernel(b._program, "mp_l2_dye");
            b._kWale = OpenCl.Kernel(b._program, "mp_l2_wale");
            if (b._kWale == IntPtr.Zero) { b.Dispose(); return null; }
            if (b._kCollide == IntPtr.Zero || b._kStream == IntPtr.Zero ||
                b._kBoundary == IntPtr.Zero || b._kInject == IntPtr.Zero ||
                b._kDye == IntPtr.Zero)
            { b.Dispose(); return null; }

            long cb = (long)maxCells * sizeof(float);
            b._f = OpenCl.Buffer(cb * 9);
            b._fs = OpenCl.Buffer(cb * 9);
            b._dyeA = OpenCl.Buffer(cb);
            b._dyeB = OpenCl.Buffer(cb);
            b._dyeAN = OpenCl.Buffer(cb);
            b._dyeBN = OpenCl.Buffer(cb);
            b._velx = OpenCl.Buffer(cb);
            b._vely = OpenCl.Buffer(cb);
            b._rho = OpenCl.Buffer(cb);
            b._nut = OpenCl.Buffer(cb);
            b._solid = OpenCl.Buffer(maxCells);
            if (b._f == IntPtr.Zero || b._fs == IntPtr.Zero || b._dyeA == IntPtr.Zero ||
                b._dyeB == IntPtr.Zero || b._dyeAN == IntPtr.Zero || b._dyeBN == IntPtr.Zero ||
                b._velx == IntPtr.Zero || b._vely == IntPtr.Zero || b._rho == IntPtr.Zero ||
                b._nut == IntPtr.Zero || b._solid == IntPtr.Zero)
            { b.Dispose(); return null; }
            // every field a kernel READS before another kernel writes it starts
            // defined: nut for collide, velx/vely/rho for the first WALE pass
            var zero = new float[maxCells];
            OpenCl.Write(b._nut, zero, maxCells);
            OpenCl.Write(b._velx, zero, maxCells);
            OpenCl.Write(b._vely, zero, maxCells);
            OpenCl.Write(b._rho, zero, maxCells);

            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClLbm2D() { }

    public bool Upload(int cells, float[]? f, byte[]? solid, float[]? dyeA, float[]? dyeB)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        // a short array would let the driver read past the managed buffer -
        // refuse instead (the same guard as the CUDA wrapper)
        if (f is null || f.Length < 9 * cells || solid is null || solid.Length < cells ||
            dyeA is null || dyeA.Length < cells || dyeB is null || dyeB.Length < cells) return false;

        try
        {
            /* the stream target must carry the same contents: unwritten slots keep it */
            return OpenCl.Write(_f, f, 9L * cells)
                && OpenCl.Write(_fs, f, 9L * cells)
                && OpenCl.Write(_solid, solid, cells)
                && OpenCl.Write(_dyeA, dyeA, cells)
                && OpenCl.Write(_dyeB, dyeB, cells)
                && OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool UploadVelocity(int cells, float[]? velx, float[]? vely)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (velx is null || velx.Length < cells || vely is null || vely.Length < cells) return false;
        try { return OpenCl.Write(_velx, velx, cells) && OpenCl.Write(_vely, vely, cells) && OpenCl.Finish(); }
        catch { return false; }
    }

    public bool Step(int w, int h, float omega, float inflow, bool mrt, bool noSlip,
        bool inject, int stripePeriod, int steps, int turb, float cs2)
    {
        long cells = (long)w * h;
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity || steps <= 0) return false;

        try
        {
            for (int s = 0; s < steps; s++)
            {
                if (turb == 2)
                {
                    bool okW = OpenCl.Arg(_kWale, 0, w) && OpenCl.Arg(_kWale, 1, h) &&
                        OpenCl.Arg(_kWale, 2, cs2) && OpenCl.Arg(_kWale, 3, _velx) &&
                        OpenCl.Arg(_kWale, 4, _vely) && OpenCl.Arg(_kWale, 5, _solid) &&
                        OpenCl.Arg(_kWale, 6, _nut) && OpenCl.Launch(_kWale, cells);
                    if (!okW) return false;
                }
                bool ok =
                    OpenCl.Arg(_kCollide, 0, w) && OpenCl.Arg(_kCollide, 1, h) &&
                    OpenCl.Arg(_kCollide, 2, omega) && OpenCl.Arg(_kCollide, 3, inflow) &&
                    OpenCl.Arg(_kCollide, 4, mrt ? 1 : 0) &&
                    OpenCl.Arg(_kCollide, 5, turb) && OpenCl.Arg(_kCollide, 6, cs2) &&
                    OpenCl.Arg(_kCollide, 7, _nut) &&
                    OpenCl.Arg(_kCollide, 8, _f) && OpenCl.Arg(_kCollide, 9, _solid) &&
                    OpenCl.Arg(_kCollide, 10, _velx) && OpenCl.Arg(_kCollide, 11, _vely) &&
                    OpenCl.Arg(_kCollide, 12, _rho) &&
                    OpenCl.Launch(_kCollide, cells);

                ok = ok &&
                    OpenCl.Arg(_kStream, 0, w) && OpenCl.Arg(_kStream, 1, h) &&
                    OpenCl.Arg(_kStream, 2, noSlip ? 1 : 0) &&
                    OpenCl.Arg(_kStream, 3, _f) && OpenCl.Arg(_kStream, 4, _fs) &&
                    OpenCl.Arg(_kStream, 5, _solid) &&
                    OpenCl.Launch(_kStream, cells);
                (_f, _fs) = (_fs, _f);

                ok = ok &&
                    OpenCl.Arg(_kBoundary, 0, w) && OpenCl.Arg(_kBoundary, 1, h) &&
                    OpenCl.Arg(_kBoundary, 2, inflow) && OpenCl.Arg(_kBoundary, 3, _f) &&
                    OpenCl.Launch(_kBoundary, h);

                if (inject)
                {
                    ok = ok &&
                        OpenCl.Arg(_kInject, 0, w) && OpenCl.Arg(_kInject, 1, h) &&
                        OpenCl.Arg(_kInject, 2, stripePeriod) &&
                        OpenCl.Arg(_kInject, 3, _dyeA) && OpenCl.Arg(_kInject, 4, _dyeB) &&
                        OpenCl.Launch(_kInject, h);
                }

                ok = ok &&
                    OpenCl.Arg(_kDye, 0, w) && OpenCl.Arg(_kDye, 1, h) &&
                    OpenCl.Arg(_kDye, 2, noSlip ? 1 : 0) &&
                    OpenCl.Arg(_kDye, 3, _velx) && OpenCl.Arg(_kDye, 4, _vely) &&
                    OpenCl.Arg(_kDye, 5, _solid) &&
                    OpenCl.Arg(_kDye, 6, _dyeA) && OpenCl.Arg(_kDye, 7, _dyeB) &&
                    OpenCl.Arg(_kDye, 8, _dyeAN) && OpenCl.Arg(_kDye, 9, _dyeBN) &&
                    OpenCl.Launch(_kDye, cells);
                (_dyeA, _dyeAN) = (_dyeAN, _dyeA);
                (_dyeB, _dyeBN) = (_dyeBN, _dyeB);

                if (!ok) return false;
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool DownloadDye(int cells, float[]? dyeA, float[]? dyeB)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (dyeA is null || dyeA.Length < cells || dyeB is null || dyeB.Length < cells) return false;
        try { return OpenCl.Read(_dyeA, dyeA, cells) && OpenCl.Read(_dyeB, dyeB, cells); }
        catch { return false; }
    }

    public bool DownloadVelocity(int cells, float[]? velx, float[]? vely)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (velx is null || velx.Length < cells || vely is null || vely.Length < cells) return false;
        try { return OpenCl.Read(_velx, velx, cells) && OpenCl.Read(_vely, vely, cells); }
        catch { return false; }
    }

    public bool DownloadNut(int cells, float[]? nut)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (nut is null || nut.Length < cells) return false;
        try { return OpenCl.Read(_nut, nut, cells); }
        catch { return false; }
    }

    public bool DownloadDensity(int cells, float[]? rho)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (rho is null || rho.Length < cells) return false;
        try { return OpenCl.Read(_rho, rho, cells); }
        catch { return false; }
    }

    public bool DownloadF(int cells, float[]? f)
    {
        if (_program == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (f is null || f.Length < 9 * cells) return false;
        try { return OpenCl.Read(_f, f, 9L * cells); }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_f); OpenCl.Release(_fs);
        OpenCl.Release(_dyeA); OpenCl.Release(_dyeB);
        OpenCl.Release(_dyeAN); OpenCl.Release(_dyeBN);
        OpenCl.Release(_velx); OpenCl.Release(_vely); OpenCl.Release(_rho); OpenCl.Release(_nut);
        OpenCl.Release(_solid);
        OpenCl.ReleaseKernel(_kCollide); OpenCl.ReleaseKernel(_kStream);
        OpenCl.ReleaseKernel(_kBoundary); OpenCl.ReleaseKernel(_kInject);
        OpenCl.ReleaseKernel(_kDye); OpenCl.ReleaseKernel(_kWale);
        OpenCl.ReleaseProgram(_program);
        _f = _fs = _dyeA = _dyeB = _dyeAN = _dyeBN = _velx = _vely = _rho = _nut = _solid = IntPtr.Zero;
        _kCollide = _kStream = _kBoundary = _kInject = _kDye = _kWale = _program = IntPtr.Zero;
    }
}
