namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for Schrodinger2D: sch_cuda.cu re-staged for the thin
/// runtime. The CUDA FFT runs one work-group per line with shared memory,
/// barriers and __brev - none of which the thin runtime expresses - so the
/// OpenCL FFT is the SAME radix-2 butterfly unrolled across LAUNCHES: a
/// bit-reverse permutation kernel (out of place into a scratch pair, the
/// host swaps the roles), then log2(n) butterfly-stage launches over global
/// memory, then a 1/n scale pass for the inverse. Four FFT passes per step
/// swap the buffer roles an even number of times, so psi is back in the
/// primary pair before Download. Slower than the CUDA fused kernel, but
/// this is the fallback path, and the parity test pins it against the CPU
/// reference. Ninth client of <see cref="OpenCl"/>. Never-throw contract.
/// </summary>
public sealed class ClSchrodinger2D : ISchrodinger2DBackend
{
    private const string Source = @"
static void mp_sch_rot(__global float* re, __global float* im, int i, float c, float s)
{
    float r = re[i] * c - im[i] * s;
    float t = re[i] * s + im[i] * c;
    re[i] = r; im[i] = t;
}

/* half potential kick: psi *= exp(-i (vBase + vWalls) dt / 2hbar) */
__kernel void mp_sch_kick(
    int cells, float f,
    __global float* restrict re, __global float* restrict im,
    __global const float* restrict vBase, __global const float* restrict vWalls)
{
    int i = get_global_id(0);
    if (i >= cells) return;
    float theta = (vBase[i] + vWalls[i]) * f;
    mp_sch_rot(re, im, i, cos(theta), sin(theta));
}

/* kinetic phase in momentum space */
__kernel void mp_sch_kmul(
    int n, float f,
    __global float* restrict re, __global float* restrict im,
    __global const float* restrict k2)
{
    int i = get_global_id(0);
    int cells = n * n;
    if (i >= cells) return;
    float theta = (k2[i / n] + k2[i % n]) * f;
    mp_sch_rot(re, im, i, cos(theta), sin(theta));
}

/* separable absorbing sponge */
__kernel void mp_sch_sponge(
    int n,
    __global float* restrict re, __global float* restrict im,
    __global const float* restrict damp)
{
    int i = get_global_id(0);
    int cells = n * n;
    if (i >= cells) return;
    float d = damp[i / n] * damp[i % n];
    re[i] *= d; im[i] *= d;
}

/* bit-reversed gather of every line: dst[line, i] = src[line, brev(i)] */
__kernel void mp_sch_brev(
    int n, int bits, int stride, int lineStride,
    __global const float* restrict srcRe, __global const float* restrict srcIm,
    __global float* restrict dstRe, __global float* restrict dstIm)
{
    int t = get_global_id(0);
    int cells = n * n;
    if (t >= cells) return;
    int line = t / n, i = t % n;
    uint v = (uint)i;
    v = (v & 0x55555555u) << 1 | (v & 0xAAAAAAAAu) >> 1;
    v = (v & 0x33333333u) << 2 | (v & 0xCCCCCCCCu) >> 2;
    v = (v & 0x0F0F0F0Fu) << 4 | (v & 0xF0F0F0F0u) >> 4;
    v = (v & 0x00FF00FFu) << 8 | (v & 0xFF00FF00u) >> 8;
    v = v << 16 | v >> 16;
    int src = (int)(v >> (32 - bits));
    long baseI = (long)line * lineStride;
    long gd = baseI + (long)i * stride;
    long gs = baseI + (long)src * stride;
    dstRe[gd] = srcRe[gs];
    dstIm[gd] = srcIm[gs];
}

/* one radix-2 butterfly stage over every line, global memory */
__kernel void mp_sch_stage(
    int n, int halfLen, float sign, int stride, int lineStride,
    __global float* restrict re, __global float* restrict im)
{
    int t = get_global_id(0);
    int perLine = n / 2;
    int total = n * perLine;
    if (t >= total) return;
    int line = t / perLine, tt = t % perLine;
    int len = halfLen << 1;
    int group = tt / halfLen;
    int pos = tt - group * halfLen;
    long baseI = (long)line * lineStride;
    long i0 = baseI + (long)(group * len + pos) * stride;
    long i1 = i0 + (long)halfLen * stride;
    float ang = sign * 6.2831853071795864769f * pos / len;
    float c = cos(ang), s = sin(ang);
    float tr = re[i1] * c - im[i1] * s;
    float ti = re[i1] * s + im[i1] * c;
    re[i1] = re[i0] - tr;
    im[i1] = im[i0] - ti;
    re[i0] += tr;
    im[i0] += ti;
}

/* inverse scale by 1/n after each inverse pass */
__kernel void mp_sch_scale(int cells, float scale,
    __global float* restrict re, __global float* restrict im)
{
    int i = get_global_id(0);
    if (i >= cells) return;
    re[i] *= scale; im[i] *= scale;
}
";

    private IntPtr _program, _kKick, _kKMul, _kSponge, _kBrev, _kStage, _kScale;
    private IntPtr _re, _im, _re2, _im2, _vBase, _vWalls, _k2, _damp;
    private int _capacity;   // max n (grid side)

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClSchrodinger2D? TryCreate(int maxN)
    {
        if (maxN < 4 || !OpenCl.Available) return null;
        var b = new ClSchrodinger2D();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kKick = OpenCl.Kernel(b._program, "mp_sch_kick");
            b._kKMul = OpenCl.Kernel(b._program, "mp_sch_kmul");
            b._kSponge = OpenCl.Kernel(b._program, "mp_sch_sponge");
            b._kBrev = OpenCl.Kernel(b._program, "mp_sch_brev");
            b._kStage = OpenCl.Kernel(b._program, "mp_sch_stage");
            b._kScale = OpenCl.Kernel(b._program, "mp_sch_scale");
            if (b._kKick == IntPtr.Zero || b._kKMul == IntPtr.Zero || b._kSponge == IntPtr.Zero
                || b._kBrev == IntPtr.Zero || b._kStage == IntPtr.Zero || b._kScale == IntPtr.Zero)
            { b.Dispose(); return null; }
            long cb = 4L * maxN * maxN;
            b._re = OpenCl.Buffer(cb); b._im = OpenCl.Buffer(cb);
            b._re2 = OpenCl.Buffer(cb); b._im2 = OpenCl.Buffer(cb);
            b._vBase = OpenCl.Buffer(cb); b._vWalls = OpenCl.Buffer(cb);
            b._k2 = OpenCl.Buffer(4L * maxN); b._damp = OpenCl.Buffer(4L * maxN);
            if (b._re == IntPtr.Zero || b._im == IntPtr.Zero || b._re2 == IntPtr.Zero
                || b._im2 == IntPtr.Zero || b._vBase == IntPtr.Zero || b._vWalls == IntPtr.Zero
                || b._k2 == IntPtr.Zero || b._damp == IntPtr.Zero) { b.Dispose(); return null; }
            b._capacity = maxN;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClSchrodinger2D() { }

    private static int Log2(int n)
    {
        int b = 0;
        while (1 << b < n) b++;
        return b;
    }

    /// <summary>One full FFT pass over every line: bit-reverse gather into
    /// the scratch pair, then log2(n) butterfly stages in place there. The
    /// scratch becomes the CURRENT pair - the caller swaps the fields.</summary>
    private bool FftPass(int n, int bits, int stride, int lineStride, bool inverse)
    {
        long cells = (long)n * n;
        bool ok = OpenCl.Arg(_kBrev, 0, n) && OpenCl.Arg(_kBrev, 1, bits) &&
                  OpenCl.Arg(_kBrev, 2, stride) && OpenCl.Arg(_kBrev, 3, lineStride) &&
                  OpenCl.Arg(_kBrev, 4, _re) && OpenCl.Arg(_kBrev, 5, _im) &&
                  OpenCl.Arg(_kBrev, 6, _re2) && OpenCl.Arg(_kBrev, 7, _im2) &&
                  OpenCl.Launch(_kBrev, cells);
        if (!ok) return false;
        (_re, _re2) = (_re2, _re);
        (_im, _im2) = (_im2, _im);

        float sign = inverse ? 1f : -1f;
        for (int half = 1; half < n; half <<= 1)
        {
            ok = OpenCl.Arg(_kStage, 0, n) && OpenCl.Arg(_kStage, 1, half) &&
                 OpenCl.Arg(_kStage, 2, sign) &&
                 OpenCl.Arg(_kStage, 3, stride) && OpenCl.Arg(_kStage, 4, lineStride) &&
                 OpenCl.Arg(_kStage, 5, _re) && OpenCl.Arg(_kStage, 6, _im) &&
                 OpenCl.Launch(_kStage, cells / 2);
            if (!ok) return false;
        }

        if (inverse)
        {
            ok = OpenCl.Arg(_kScale, 0, (int)cells) && OpenCl.Arg(_kScale, 1, 1f / n) &&
                 OpenCl.Arg(_kScale, 2, _re) && OpenCl.Arg(_kScale, 3, _im) &&
                 OpenCl.Launch(_kScale, cells);
            if (!ok) return false;
        }
        return true;
    }

    public bool Upload(int n, float[] re, float[] im, float[] vBase, float[] vWalls,
        float[] k2, float[] damp)
    {
        if (_kKick == IntPtr.Zero || n < 4 || n > _capacity || (n & (n - 1)) != 0) return false;
        long cells = (long)n * n;
        if (re is null || re.Length < cells || im is null || im.Length < cells
            || vBase is null || vBase.Length < cells || vWalls is null || vWalls.Length < cells
            || k2 is null || k2.Length < n || damp is null || damp.Length < n) return false;
        try
        {
            return OpenCl.Write(_re, re, cells) && OpenCl.Write(_im, im, cells)
                && OpenCl.Write(_vBase, vBase, cells) && OpenCl.Write(_vWalls, vWalls, cells)
                && OpenCl.Write(_k2, k2, n) && OpenCl.Write(_damp, damp, n);
        }
        catch { return false; }
    }

    public bool UploadWalls(int n, float[] vWalls)
    {
        if (_kKick == IntPtr.Zero || n < 4 || n > _capacity) return false;
        long cells = (long)n * n;
        if (vWalls is null || vWalls.Length < cells) return false;
        try { return OpenCl.Write(_vWalls, vWalls, cells); }
        catch { return false; }
    }

    public bool UploadDamp(int n, float[] damp)
    {
        if (_kKick == IntPtr.Zero || n < 4 || n > _capacity) return false;
        if (damp is null || damp.Length < n) return false;
        try { return OpenCl.Write(_damp, damp, n); }
        catch { return false; }
    }

    public bool Step(int n, float kickF, float kinF, bool absorb)
    {
        if (_kKick == IntPtr.Zero || n < 4 || n > _capacity || (n & (n - 1)) != 0) return false;
        try
        {
            long cells = (long)n * n;
            int bits = Log2(n);

            bool ok = OpenCl.Arg(_kKick, 0, (int)cells) && OpenCl.Arg(_kKick, 1, kickF) &&
                      OpenCl.Arg(_kKick, 2, _re) && OpenCl.Arg(_kKick, 3, _im) &&
                      OpenCl.Arg(_kKick, 4, _vBase) && OpenCl.Arg(_kKick, 5, _vWalls) &&
                      OpenCl.Launch(_kKick, cells);
            if (!ok) return false;

            if (!FftPass(n, bits, 1, n, inverse: false)) return false;   // rows
            if (!FftPass(n, bits, n, 1, inverse: false)) return false;   // columns

            ok = OpenCl.Arg(_kKMul, 0, n) && OpenCl.Arg(_kKMul, 1, kinF) &&
                 OpenCl.Arg(_kKMul, 2, _re) && OpenCl.Arg(_kKMul, 3, _im) &&
                 OpenCl.Arg(_kKMul, 4, _k2) &&
                 OpenCl.Launch(_kKMul, cells);
            if (!ok) return false;

            if (!FftPass(n, bits, 1, n, inverse: true)) return false;
            if (!FftPass(n, bits, n, 1, inverse: true)) return false;

            ok = OpenCl.Arg(_kKick, 0, (int)cells) && OpenCl.Arg(_kKick, 1, kickF) &&
                 OpenCl.Arg(_kKick, 2, _re) && OpenCl.Arg(_kKick, 3, _im) &&
                 OpenCl.Arg(_kKick, 4, _vBase) && OpenCl.Arg(_kKick, 5, _vWalls) &&
                 OpenCl.Launch(_kKick, cells);
            if (!ok) return false;

            if (absorb)
            {
                ok = OpenCl.Arg(_kSponge, 0, n) &&
                     OpenCl.Arg(_kSponge, 1, _re) && OpenCl.Arg(_kSponge, 2, _im) &&
                     OpenCl.Arg(_kSponge, 3, _damp) &&
                     OpenCl.Launch(_kSponge, cells);
                if (!ok) return false;
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int n, float[] re, float[] im)
    {
        if (_kKick == IntPtr.Zero || n < 4 || n > _capacity) return false;
        long cells = (long)n * n;
        if (re is null || re.Length < cells || im is null || im.Length < cells) return false;
        try { return OpenCl.Read(_re, re, cells) && OpenCl.Read(_im, im, cells); }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_re); OpenCl.Release(_im); OpenCl.Release(_re2); OpenCl.Release(_im2);
        OpenCl.Release(_vBase); OpenCl.Release(_vWalls); OpenCl.Release(_k2); OpenCl.Release(_damp);
        _re = _im = _re2 = _im2 = _vBase = _vWalls = _k2 = _damp = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kKick); OpenCl.ReleaseKernel(_kKMul); OpenCl.ReleaseKernel(_kSponge);
        OpenCl.ReleaseKernel(_kBrev); OpenCl.ReleaseKernel(_kStage); OpenCl.ReleaseKernel(_kScale);
        _kKick = _kKMul = _kSponge = _kBrev = _kStage = _kScale = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
