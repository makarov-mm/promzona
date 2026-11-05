namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the XPBD cloth: cloth_cuda.cu translated - the value
/// noise and curl wind verbatim, aerodynamic normal pressure, Jacobi XPBD
/// with relaxation, sphere and floor projection per iteration, contact
/// friction in the velocity recovery. Fifteenth client of
/// <see cref="OpenCl"/>. Never-throw contract. Tearing and self-collision
/// stay CPU-only features, exactly as with CUDA: the model declines the GPU
/// while either is on.
///
/// NO FLOAT ATOMICS, AND THAT IS THE WHOLE DESIGN. The CUDA constraint kernel
/// scatters its corrections with atomicAdd on floats. OpenCL 1.2 has no
/// atomic float add at all - only integers - and the usual workaround, a
/// compare-and-swap loop on the bit pattern, makes the SUM ORDER depend on
/// how the driver happens to schedule work-groups. Cloth would then look
/// slightly different on every run of the same machine, and there would be no
/// way to tell a scheduling difference from a real bug.
///
/// So the scatter is turned inside out into a GATHER. The constraint pass
/// computes each constraint's lambda update and stores one number per
/// constraint - no writes to particles at all. The apply pass then walks each
/// particle's OWN constraint list and sums its corrections itself. Same
/// arithmetic, same Jacobi iteration, deterministic on every driver, and no
/// atomic anywhere.
///
/// The particle-to-constraint list is built on the host in
/// <see cref="UploadConstraints"/>, where the constraint arrays already are.
/// It is a CSR: <c>rowStart[k]..rowStart[k+1]</c> indexes into an entry array
/// holding the constraint number with its sign folded in as (c + 1) for the
/// far end and -(c + 1) for the near one - the same +/- the scatter used.
/// </summary>
public sealed class ClCloth : IClothBackend
{
    private const string Source = @"
/* ---- verbatim wind field (float port of the CPU noise chain) ---- */

inline float ClothHash3(int x, int y, int z)
{
    int h = x * 374761393 + y * 668265263 + z * 1442695040;
    h = (h ^ (h >> 13)) * 1274126177;
    h ^= h >> 16;
    return (float)(h & 0xFFFF) / 32767.5f - 1.0f;
}

inline float ClothFade(float t) { return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f); }
inline float ClothLerp(float a, float b, float t) { return a + (b - a) * t; }

float ClothValueNoise(float px, float py, float pz)
{
    float fx = floor(px), fy = floor(py), fz = floor(pz);
    int xi = (int)fx, yi = (int)fy, zi = (int)fz;
    float xf = px - fx, yf = py - fy, zf = pz - fz;
    float u = ClothFade(xf), v = ClothFade(yf), w = ClothFade(zf);
    float c000 = ClothHash3(xi, yi, zi), c100 = ClothHash3(xi + 1, yi, zi);
    float c010 = ClothHash3(xi, yi + 1, zi), c110 = ClothHash3(xi + 1, yi + 1, zi);
    float c001 = ClothHash3(xi, yi, zi + 1), c101 = ClothHash3(xi + 1, yi, zi + 1);
    float c011 = ClothHash3(xi, yi + 1, zi + 1), c111 = ClothHash3(xi + 1, yi + 1, zi + 1);
    float x00 = ClothLerp(c000, c100, u), x10 = ClothLerp(c010, c110, u);
    float x01 = ClothLerp(c001, c101, u), x11 = ClothLerp(c011, c111, u);
    return ClothLerp(ClothLerp(x00, x10, v), ClothLerp(x01, x11, v), w);
}

#define CURL_EPS 0.12f

void ClothCurl(float x, float y, float z, float* cx, float* cy, float* cz)
{
    /* the three potential pairs of the .cu, inlined - a helper taking two
       float* out-parameters is legal OpenCL C but a common driver stumble */
    float pxpY = ClothValueNoise(x + CURL_EPS + 31.416f, y + 17.073f, z + 47.853f);
    float pxpZ = ClothValueNoise(x + CURL_EPS - 19.34f, y + 83.155f, z - 5.926f);
    float pxmY = ClothValueNoise(x - CURL_EPS + 31.416f, y + 17.073f, z + 47.853f);
    float pxmZ = ClothValueNoise(x - CURL_EPS - 19.34f, y + 83.155f, z - 5.926f);
    float pypX = ClothValueNoise(x, y + CURL_EPS, z);
    float pypZ = ClothValueNoise(x - 19.34f, y + CURL_EPS + 83.155f, z - 5.926f);
    float pymX = ClothValueNoise(x, y - CURL_EPS, z);
    float pymZ = ClothValueNoise(x - 19.34f, y - CURL_EPS + 83.155f, z - 5.926f);
    float pzpX = ClothValueNoise(x, y, z + CURL_EPS);
    float pzpY = ClothValueNoise(x + 31.416f, y + 17.073f, z + CURL_EPS + 47.853f);
    float pzmX = ClothValueNoise(x, y, z - CURL_EPS);
    float pzmY = ClothValueNoise(x + 31.416f, y + 17.073f, z - CURL_EPS + 47.853f);
    float inv = 1.0f / (2.0f * CURL_EPS);
    *cx = ((pypZ - pymZ) - (pzpY - pzmY)) * inv;
    *cy = ((pzpX - pzmX) - (pxpZ - pxmZ)) * inv;
    *cz = ((pxpY - pxmY) - (pypX - pymX)) * inv;
}

__kernel void mp_cloth_normals(int gridN,
    __global const float* restrict x, __global const float* restrict y,
    __global const float* restrict z,
    __global float* restrict nx, __global float* restrict ny,
    __global float* restrict nz)
{
    int k = get_global_id(0);
    int count = gridN * gridN;
    if (k >= count) return;
    int i = k / gridN, j = k % gridN;
    int il = i > 0 ? i - 1 : 0, ir = i < gridN - 1 ? i + 1 : gridN - 1;
    int jl = j > 0 ? j - 1 : 0, jr = j < gridN - 1 ? j + 1 : gridN - 1;
    int a = i * gridN + jl, b = i * gridN + jr;
    int c = il * gridN + j, d = ir * gridN + j;
    float ux = x[b] - x[a], uy = y[b] - y[a], uz = z[b] - z[a];
    float vx = x[d] - x[c], vy = y[d] - y[c], vz = z[d] - z[c];
    float rx = uy * vz - uz * vy;
    float ry = uz * vx - ux * vz;
    float rz = ux * vy - uy * vx;
    float l = sqrt(rx * rx + ry * ry + rz * rz);
    if (l > 1e-12f) { nx[k] = rx / l; ny[k] = ry / l; nz[k] = rz / l; }
    else { nx[k] = 0.0f; ny[k] = 0.0f; nz[k] = 1.0f; }
}

__kernel void mp_cloth_predict(
    int count, float dt, float gravity, float damp,
    float windSpeed, float aeroCoeff, float aeroMaxRel, float time,
    float ndx, float ndy, float ndz, float noiseFreq, float scroll, float curlStrength,
    int grabbed, float grabX, float grabY, float grabZ,
    __global float* restrict x, __global float* restrict y, __global float* restrict z,
    __global float* restrict vx, __global float* restrict vy, __global float* restrict vz,
    __global float* restrict px, __global float* restrict py, __global float* restrict pz,
    __global const uchar* restrict pinned,
    __global const float* restrict nx, __global const float* restrict ny,
    __global const float* restrict nz)
{
    int k = get_global_id(0);
    if (k >= count) return;
    px[k] = x[k]; py[k] = y[k]; pz[k] = z[k];
    if (pinned[k]) return;

    float vxi = vx[k], vyi = vy[k] - gravity * dt, vzi = vz[k];

    float wx = 0.0f, wy = 0.0f, wz = 0.0f;
    if (windSpeed > 1e-9f)
    {
        float gust = 0.55f + 0.45f * sin(time * 0.7f) * sin(time * 0.23f + 1.3f);
        float sx = x[k] * noiseFreq + ndx * time * scroll;
        float sy = y[k] * noiseFreq + ndy * time * scroll;
        float sz = z[k] * noiseFreq + ndz * time * scroll;
        float cx, cy, cz;
        ClothCurl(sx, sy, sz, &cx, &cy, &cz);
        float amp = windSpeed * gust;
        wx = (ndx + cx * curlStrength) * amp;
        wy = (ndy + cy * curlStrength) * amp;
        wz = (ndz + cz * curlStrength) * amp;
    }

    float rx = wx - vxi, ry = wy - vyi, rz = wz - vzi;
    float r2 = rx * rx + ry * ry + rz * rz;
    if (r2 > aeroMaxRel * aeroMaxRel)
    {
        float s = aeroMaxRel / sqrt(r2);
        rx *= s; ry *= s; rz *= s;
    }
    float f = nx[k] * rx + ny[k] * ry + nz[k] * rz;
    float a = aeroCoeff * f * fabs(f);
    vxi += nx[k] * a * dt;
    vyi += ny[k] * a * dt;
    vzi += nz[k] * a * dt;

    vxi *= damp; vyi *= damp; vzi *= damp;
    vx[k] = vxi; vy[k] = vyi; vz[k] = vzi;
    x[k] += vxi * dt;
    y[k] += vyi * dt;
    z[k] += vzi * dt;

    if (k == grabbed) { x[k] = grabX; y[k] = grabY; z[k] = grabZ; }
}

/* Jacobi XPBD, half one: every constraint updates its own lambda and leaves
   ONE number behind. No particle is written, so no atomic is needed. */
__kernel void mp_cloth_constraint(
    int constraintCount,
    __global const int* restrict ci, __global const int* restrict cj,
    __global const float* restrict l0, __global const uchar* restrict ctype,
    float aStruct, float aShear, float aBend, int grabbed,
    __global const float* restrict x, __global const float* restrict y,
    __global const float* restrict z,
    __global const uchar* restrict pinned,
    __global float* restrict lambda,
    __global float* restrict scale)
{
    int c = get_global_id(0);
    if (c >= constraintCount) return;
    scale[c] = 0.0f;

    int i = ci[c], j = cj[c];
    float w1 = (pinned[i] || i == grabbed) ? 0.0f : 1.0f;
    float w2 = (pinned[j] || j == grabbed) ? 0.0f : 1.0f;
    if (w1 + w2 == 0.0f) return;

    float ddx = x[j] - x[i], ddy = y[j] - y[i], ddz = z[j] - z[i];
    float len = sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
    if (len < 1e-12f) return;

    float alphaTilde = ctype[c] == 0 ? aStruct : (ctype[c] == 1 ? aShear : aBend);
    float constraintValue = len - l0[c];
    float deltaLambda = (-constraintValue - alphaTilde * lambda[c]) / (w1 + w2 + alphaTilde);
    lambda[c] += deltaLambda;
    scale[c] = deltaLambda / len;
}

/* Jacobi XPBD, half two: each particle GATHERS its own constraints, applies
   the relaxed sum, then the sphere and the floor. The entry list folds the
   sign in: +(c+1) means this particle is the far end, -(c+1) the near one -
   the same +/- the CUDA scatter used. */
__kernel void mp_cloth_apply(
    int count, float relax,
    int sphereOn, float scx, float scy, float scz, float srad, float floorY,
    __global float* restrict x, __global float* restrict y, __global float* restrict z,
    __global const int* restrict rowStart, __global const int* restrict entries,
    __global const int* restrict ci, __global const int* restrict cj,
    __global const float* restrict scale,
    __global const uchar* restrict pinned,
    __global uchar* restrict contact)
{
    int k = get_global_id(0);
    if (k >= count) return;
    if (pinned[k]) return;

    float sx = 0.0f, sy = 0.0f, sz = 0.0f;
    int from = rowStart[k], to = rowStart[k + 1];
    for (int e = from; e < to; e++)
    {
        int packed = entries[e];
        int c = (packed > 0 ? packed : -packed) - 1;
        float s = scale[c];
        if (s == 0.0f) continue;
        int i = ci[c], j = cj[c];
        float ddx = x[j] - x[i], ddy = y[j] - y[i], ddz = z[j] - z[i];
        float sign = packed > 0 ? 1.0f : -1.0f;
        sx += sign * s * ddx;
        sy += sign * s * ddy;
        sz += sign * s * ddz;
    }

    float xi = x[k] + sx * relax;
    float yi = y[k] + sy * relax;
    float zi = z[k] + sz * relax;

    if (sphereOn)
    {
        float rx = xi - scx, ry = yi - scy, rz = zi - scz;
        float d2 = rx * rx + ry * ry + rz * rz;
        if (d2 < srad * srad && d2 > 1e-12f)
        {
            float d = sqrt(d2);
            float push = srad / d;
            xi = scx + rx * push;
            yi = scy + ry * push;
            zi = scz + rz * push;
            contact[k] = 1;
        }
    }
    if (yi < floorY) { yi = floorY; contact[k] = 1; }

    x[k] = xi; y[k] = yi; z[k] = zi;
}

__kernel void mp_cloth_velocity(
    int count, float invDt, float friction,
    __global const float* restrict x, __global const float* restrict y,
    __global const float* restrict z,
    __global const float* restrict px, __global const float* restrict py,
    __global const float* restrict pz,
    __global float* restrict vx, __global float* restrict vy,
    __global float* restrict vz,
    __global const uchar* restrict pinned,
    __global const uchar* restrict contact)
{
    int k = get_global_id(0);
    if (k >= count) return;
    if (pinned[k]) { vx[k] = 0.0f; vy[k] = 0.0f; vz[k] = 0.0f; return; }
    float f = contact[k] ? friction : 1.0f;
    vx[k] = (x[k] - px[k]) * invDt * f;
    vy[k] = (y[k] - py[k]) * invDt * f;
    vz[k] = (z[k] - pz[k]) * invDt * f;
}
";

    private IntPtr _program, _normals, _predict, _constraint, _apply, _velocity;
    private IntPtr _x, _y, _z, _vx, _vy, _vz, _px, _py, _pz;
    private IntPtr _nx, _ny, _nz;
    private IntPtr _ci, _cj, _l0, _ctype, _lambda, _scale;
    private IntPtr _rowStart, _entries;
    private IntPtr _pinned, _contact;
    private int _maxParticles, _maxConstraints;
    private int _particleCount; // rows the CSR was built for
    private float[]? _zeroFloats;
    private byte[]? _zeroBytes;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU
    /// Gauss-Seidel solver.</summary>
    public static ClCloth? TryCreate(int maxParticles, int maxConstraints)
    {
        if (maxParticles <= 0 || maxConstraints <= 0 || !OpenCl.Available) return null;
        var b = new ClCloth();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._normals = OpenCl.Kernel(b._program, "mp_cloth_normals");
            b._predict = OpenCl.Kernel(b._program, "mp_cloth_predict");
            b._constraint = OpenCl.Kernel(b._program, "mp_cloth_constraint");
            b._apply = OpenCl.Kernel(b._program, "mp_cloth_apply");
            b._velocity = OpenCl.Kernel(b._program, "mp_cloth_velocity");

            if (b._normals == IntPtr.Zero || 
                b._predict == IntPtr.Zero || 
                b._constraint == IntPtr.Zero ||
                b._apply == IntPtr.Zero || 
                b._velocity == IntPtr.Zero)
            {
                b.Dispose();
                return null;
            }

            long pf = 4L * maxParticles;
            long cf = 4L * maxConstraints;

            IntPtr[] particleFloats =
            [
                b._x = OpenCl.Buffer(pf),
                b._y = OpenCl.Buffer(pf),
                b._z = OpenCl.Buffer(pf),
                b._vx = OpenCl.Buffer(pf),
                b._vy = OpenCl.Buffer(pf),
                b._vz = OpenCl.Buffer(pf),
                b._px = OpenCl.Buffer(pf),
                b._py = OpenCl.Buffer(pf),
                b._pz = OpenCl.Buffer(pf),
                b._nx = OpenCl.Buffer(pf),
                b._ny = OpenCl.Buffer(pf),
                b._nz = OpenCl.Buffer(pf)
            ];

            b._ci = OpenCl.Buffer(cf);
            b._cj = OpenCl.Buffer(cf);
            b._l0 = OpenCl.Buffer(cf);
            b._ctype = OpenCl.Buffer(maxConstraints);
            b._lambda = OpenCl.Buffer(cf);
            b._scale = OpenCl.Buffer(cf);
            b._rowStart = OpenCl.Buffer(4L * (maxParticles + 1));
            b._entries = OpenCl.Buffer(8L * maxConstraints); // two ends each
            b._pinned = OpenCl.Buffer(maxParticles);
            b._contact = OpenCl.Buffer(maxParticles);

            if (particleFloats.Any(p => p == IntPtr.Zero))
            {
                b.Dispose();
                return null;
            }

            if (b._ci == IntPtr.Zero ||
                b._cj == IntPtr.Zero ||
                b._l0 == IntPtr.Zero ||
                b._ctype == IntPtr.Zero ||
                b._lambda == IntPtr.Zero ||
                b._scale == IntPtr.Zero ||
                b._rowStart == IntPtr.Zero ||
                b._entries == IntPtr.Zero ||
                b._pinned == IntPtr.Zero ||
                b._contact == IntPtr.Zero)
            {
                b.Dispose();
                return null;
            }

            b._maxParticles = maxParticles;
            b._maxConstraints = maxConstraints;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClCloth() { }

    public bool Upload(int count, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz, byte[]? pinned)
    {
        if (_normals == IntPtr.Zero || count <= 0 || count > _maxParticles) return false;

        if (x is null ||
            x.Length < count ||
            y is null ||
            y.Length < count ||
            z is null ||
            z.Length < count ||
            vx is null ||
            vx.Length < count ||
            vy is null ||
            vy.Length < count ||
            vz is null ||
            vz.Length < count ||
            pinned is null ||
            pinned.Length < count)
        {
            return false;
        }
        
        try
        {
            _particleCount = count;

            return OpenCl.Write(_x, x, count) && 
                   OpenCl.Write(_y, y, count) && 
                   OpenCl.Write(_z, z, count) && 
                   OpenCl.Write(_vx, vx, count) &&
                   OpenCl.Write(_vy, vy, count) && 
                   OpenCl.Write(_vz, vz, count) && 
                   OpenCl.Write(_pinned, pinned, count);
        }
        catch
        {
            return false;
        }
    }

    public bool UploadConstraints(int count, int[]? ci, int[]? cj, float[]? l0, byte[]? ctype)
    {
        if (_normals == IntPtr.Zero || count <= 0 || count > _maxConstraints) return false;

        if (ci is null || ci.Length < count || cj is null || cj.Length < count || l0 is null || l0.Length < count || ctype is null || ctype.Length < count)
            return false;

        if (_particleCount <= 0) return false; // Upload comes first: the CSR needs the row count

        try
        {
            // Build the particle -> constraint list. Two passes, both O(count):
            // count the entries per particle, prefix-sum them into rowStart,
            // then fill. The sign of a stored entry says which end this
            // particle is, so the kernel needs no second lookup.
            int particles = _particleCount;
            int[] rowStart = new int[particles + 1];

            for (int c = 0; c < count; c++)
            {
                int i = ci[c], j = cj[c];
                if (i < 0 || i >= particles || j < 0 || j >= particles) return false;
                rowStart[i + 1]++;
                rowStart[j + 1]++;
            }

            for (int k = 0; k < particles; k++)
            {
                rowStart[k + 1] += rowStart[k];
            }

            int[] cursor = (int[])rowStart.Clone();
            int[] entries = new int[2 * count];

            for (int c = 0; c < count; c++)
            {
                entries[cursor[ci[c]]++] = -(c + 1); // near end: subtract
                entries[cursor[cj[c]]++] = c + 1; // far end: add
            }

            return OpenCl.Write(_ci, ci, count) &&
                   OpenCl.Write(_cj, cj, count) && 
                   OpenCl.Write(_l0, l0, count) &&
                   OpenCl.Write(_ctype, ctype, count) && 
                   OpenCl.Write(_rowStart, rowStart, particles + 1) &&
                   OpenCl.Write(_entries, entries, 2 * count);
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int gridN, int count, int constraintCount,
        float dt, float gravity, float damp,
        float windSpeed, float aeroCoeff, float aeroMaxRel, float time,
        float ndx, float ndy, float ndz, float noiseFreq, float scroll, float curlStrength,
        int grabbed, float grabX, float grabY, float grabZ,
        float aStruct, float aShear, float aBend, int iterations, float relax,
        bool sphereOn, float scx, float scy, float scz, float srad, float floorY,
        float friction)
    {
        if (_normals == IntPtr.Zero) return false;
        if (count <= 0 || count > _maxParticles || count != gridN * gridN) return false;
        if (constraintCount <= 0 || constraintCount > _maxConstraints) return false;
        if (iterations <= 0 || dt <= 0f) return false;
        if (count != _particleCount) return false;   // the CSR belongs to another cloth

        try
        {
            // lambda and contact start each step at zero, as the .cu's memsets
            // do. OpenCL 1.2 has clEnqueueFillBuffer but the helper layer does
            // not expose it, and a written array of zeros costs one transfer
            // of a few tens of kilobytes against a step that runs dozens of
            // kernels - not the place to spend a new P/Invoke three days out.
            if (_zeroFloats is null || _zeroFloats.Length < constraintCount)
                _zeroFloats = new float[Math.Max(constraintCount, 1)];
            if (_zeroBytes is null || _zeroBytes.Length < count)
                _zeroBytes = new byte[Math.Max(count, 1)];
            Array.Clear(_zeroFloats, 0, constraintCount);
            Array.Clear(_zeroBytes, 0, count);
            if (!OpenCl.Write(_lambda, _zeroFloats, constraintCount)) return false;
            if (!OpenCl.Write(_contact, _zeroBytes, count)) return false;

            bool ok = OpenCl.Arg(_normals, 0, gridN)
                   && OpenCl.Arg(_normals, 1, _x) && OpenCl.Arg(_normals, 2, _y)
                   && OpenCl.Arg(_normals, 3, _z)
                   && OpenCl.Arg(_normals, 4, _nx) && OpenCl.Arg(_normals, 5, _ny)
                   && OpenCl.Arg(_normals, 6, _nz)
                   && OpenCl.Launch(_normals, count);
            if (!ok) return false;

            ok = OpenCl.Arg(_predict, 0, count) && OpenCl.Arg(_predict, 1, dt)
              && OpenCl.Arg(_predict, 2, gravity) && OpenCl.Arg(_predict, 3, damp)
              && OpenCl.Arg(_predict, 4, windSpeed) && OpenCl.Arg(_predict, 5, aeroCoeff)
              && OpenCl.Arg(_predict, 6, aeroMaxRel) && OpenCl.Arg(_predict, 7, time)
              && OpenCl.Arg(_predict, 8, ndx) && OpenCl.Arg(_predict, 9, ndy)
              && OpenCl.Arg(_predict, 10, ndz) && OpenCl.Arg(_predict, 11, noiseFreq)
              && OpenCl.Arg(_predict, 12, scroll) && OpenCl.Arg(_predict, 13, curlStrength)
              && OpenCl.Arg(_predict, 14, grabbed) && OpenCl.Arg(_predict, 15, grabX)
              && OpenCl.Arg(_predict, 16, grabY) && OpenCl.Arg(_predict, 17, grabZ)
              && OpenCl.Arg(_predict, 18, _x) && OpenCl.Arg(_predict, 19, _y)
              && OpenCl.Arg(_predict, 20, _z)
              && OpenCl.Arg(_predict, 21, _vx) && OpenCl.Arg(_predict, 22, _vy)
              && OpenCl.Arg(_predict, 23, _vz)
              && OpenCl.Arg(_predict, 24, _px) && OpenCl.Arg(_predict, 25, _py)
              && OpenCl.Arg(_predict, 26, _pz)
              && OpenCl.Arg(_predict, 27, _pinned)
              && OpenCl.Arg(_predict, 28, _nx) && OpenCl.Arg(_predict, 29, _ny)
              && OpenCl.Arg(_predict, 30, _nz)
              && OpenCl.Launch(_predict, count);
            if (!ok) return false;

            ok = OpenCl.Arg(_constraint, 0, constraintCount)
              && OpenCl.Arg(_constraint, 1, _ci) && OpenCl.Arg(_constraint, 2, _cj)
              && OpenCl.Arg(_constraint, 3, _l0) && OpenCl.Arg(_constraint, 4, _ctype)
              && OpenCl.Arg(_constraint, 5, aStruct) && OpenCl.Arg(_constraint, 6, aShear)
              && OpenCl.Arg(_constraint, 7, aBend) && OpenCl.Arg(_constraint, 8, grabbed)
              && OpenCl.Arg(_constraint, 9, _x) && OpenCl.Arg(_constraint, 10, _y)
              && OpenCl.Arg(_constraint, 11, _z)
              && OpenCl.Arg(_constraint, 12, _pinned)
              && OpenCl.Arg(_constraint, 13, _lambda) && OpenCl.Arg(_constraint, 14, _scale);
            if (!ok) return false;

            ok = OpenCl.Arg(_apply, 0, count) && OpenCl.Arg(_apply, 1, relax)
              && OpenCl.Arg(_apply, 2, sphereOn ? 1 : 0)
              && OpenCl.Arg(_apply, 3, scx) && OpenCl.Arg(_apply, 4, scy)
              && OpenCl.Arg(_apply, 5, scz) && OpenCl.Arg(_apply, 6, srad)
              && OpenCl.Arg(_apply, 7, floorY)
              && OpenCl.Arg(_apply, 8, _x) && OpenCl.Arg(_apply, 9, _y)
              && OpenCl.Arg(_apply, 10, _z)
              && OpenCl.Arg(_apply, 11, _rowStart) && OpenCl.Arg(_apply, 12, _entries)
              && OpenCl.Arg(_apply, 13, _ci) && OpenCl.Arg(_apply, 14, _cj)
              && OpenCl.Arg(_apply, 15, _scale)
              && OpenCl.Arg(_apply, 16, _pinned) && OpenCl.Arg(_apply, 17, _contact);
            if (!ok) return false;

            for (int it = 0; it < iterations; it++)
            {
                if (!OpenCl.Launch(_constraint, constraintCount)) return false;
                if (!OpenCl.Launch(_apply, count)) return false;
            }

            ok = OpenCl.Arg(_velocity, 0, count) && OpenCl.Arg(_velocity, 1, 1.0f / dt)
              && OpenCl.Arg(_velocity, 2, friction)
              && OpenCl.Arg(_velocity, 3, _x) && OpenCl.Arg(_velocity, 4, _y)
              && OpenCl.Arg(_velocity, 5, _z)
              && OpenCl.Arg(_velocity, 6, _px) && OpenCl.Arg(_velocity, 7, _py)
              && OpenCl.Arg(_velocity, 8, _pz)
              && OpenCl.Arg(_velocity, 9, _vx) && OpenCl.Arg(_velocity, 10, _vy)
              && OpenCl.Arg(_velocity, 11, _vz)
              && OpenCl.Arg(_velocity, 12, _pinned) && OpenCl.Arg(_velocity, 13, _contact)
              && OpenCl.Launch(_velocity, count);
            if (!ok) return false;

            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int count, float[]? x, float[]? y, float[]? z,
        float[]? vx, float[]? vy, float[]? vz)
    {
        if (_normals == IntPtr.Zero || count <= 0 || count > _maxParticles) return false;
        if (x is null || x.Length < count || y is null || y.Length < count
            || z is null || z.Length < count || vx is null || vx.Length < count
            || vy is null || vy.Length < count || vz is null || vz.Length < count)
            return false;
        try
        {
            return OpenCl.Read(_x, x, count) && OpenCl.Read(_y, y, count)
                && OpenCl.Read(_z, z, count)
                && OpenCl.Read(_vx, vx, count) && OpenCl.Read(_vy, vy, count)
                && OpenCl.Read(_vz, vz, count);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        foreach (IntPtr b in (IntPtr[]) 
        [
            _x, _y, _z, _vx, _vy, _vz, _px, _py, _pz, _nx, _ny, _nz,
            _ci, _cj, _l0, _ctype, _lambda, _scale, _rowStart, _entries,
            _pinned, _contact
        ])
        {
            OpenCl.Release(b);
        }

        _x = _y = _z = _vx = _vy = _vz = _px = _py = _pz = IntPtr.Zero;
        _nx = _ny = _nz = IntPtr.Zero;
        _ci = _cj = _l0 = _ctype = _lambda = _scale = IntPtr.Zero;
        _rowStart = _entries = _pinned = _contact = IntPtr.Zero;

        OpenCl.ReleaseKernel(_normals); OpenCl.ReleaseKernel(_predict);
        OpenCl.ReleaseKernel(_constraint); OpenCl.ReleaseKernel(_apply);
        OpenCl.ReleaseKernel(_velocity);
        _normals = _predict = _constraint = _apply = _velocity = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
