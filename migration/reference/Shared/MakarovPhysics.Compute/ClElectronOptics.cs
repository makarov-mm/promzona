namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for ElectronOptics: eopt_cuda.cu mechanically translated
/// - velocity Verlet per electron in the solved (Ez, Er) grids plus the two
/// plate windows, birth substep and dead flag per electron, one kernel
/// launch per substep. Never-throw contract.
/// </summary>
public sealed class ClElectronOptics : IElectronOpticsBackend
{
    private const string Source = @"
static int clampi(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

static void field_at(
    float tubeLength, float tubeRadius, int nz, int nr, float deflWindow, int dnz, int dnt,
    float plateYz0, float plateZz0, float deflY, float deflZ,
    __global const float* restrict ezGrid, __global const float* restrict erGrid,
    __global const float* restrict dzGrid, __global const float* restrict dtGrid,
    float x, float y, float z, float* ex, float* ey, float* ez)
{
    float r = sqrt(y * y + z * z);
    float hz = tubeLength / (nz - 1), hr = tubeRadius / (nr - 1);
    int iz = clampi((int)rint(x / hz), 1, nz - 2);
    int ir = clampi((int)rint(r / hr), 0, nr - 2);
    int k = iz * nr + ir;
    *ex = ezGrid[k];
    float er = erGrid[k];
    *ey = 0.0f; *ez = 0.0f;
    if (r > 1e-12f) { *ey = er * (y / r); *ez = er * (z / r); }
    float hzd = deflWindow / (dnz - 1), ht = 2.0f * tubeRadius / (dnt - 1);
    if (plateYz0 >= 0.0f && deflY != 0.0f)
    {
        float zl = x - plateYz0;
        if (zl > 0.0f && zl < deflWindow)
        {
            int jz = clampi((int)rint(zl / hzd), 1, dnz - 2);
            int jt = clampi((int)rint((y + tubeRadius) / ht), 1, dnt - 2);
            int kk = jz * dnt + jt;
            *ex += deflY * dzGrid[kk];
            *ey += deflY * dtGrid[kk];
        }
    }
    if (plateZz0 >= 0.0f && deflZ != 0.0f)
    {
        float zl = x - plateZz0;
        if (zl > 0.0f && zl < deflWindow)
        {
            int jz = clampi((int)rint(zl / hzd), 1, dnz - 2);
            int jt = clampi((int)rint((z + tubeRadius) / ht), 1, dnt - 2);
            int kk = jz * dnt + jt;
            *ex += deflZ * dzGrid[kk];
            *ez += deflZ * dtGrid[kk];
        }
    }
}

__kernel void mp_eopt_step(
    int n, int k, float dt,
    float tubeLength, float tubeRadius, float screenZ, int nz, int nr,
    float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0, float deflY, float deflZ, float eOverM,
    __global const float* restrict ezGrid, __global const float* restrict erGrid,
    __global const float* restrict dzGrid, __global const float* restrict dtGrid,
    __global const uchar* restrict group,
    __global float* restrict x, __global float* restrict y, __global float* restrict z,
    __global float* restrict vx, __global float* restrict vy, __global float* restrict vz,
    __global const int* restrict birth, __global uchar* restrict dead)
{
    int i = get_global_id(0);
    if (i >= n) return;
    if (dead[i] || birth[i] > k) return;

    float xx = x[i], yy = y[i], zz = z[i];
    float ex, ey, ez;
    field_at(tubeLength, tubeRadius, nz, nr, deflWindow, dnz, dnt, plateYz0, plateZz0, deflY, deflZ,
             ezGrid, erGrid, dzGrid, dtGrid, xx, yy, zz, &ex, &ey, &ez);
    float ax = -eOverM * ex, ay = -eOverM * ey, az = -eOverM * ez;
    float vxh = vx[i] + 0.5f * ax * dt, vyh = vy[i] + 0.5f * ay * dt, vzh = vz[i] + 0.5f * az * dt;
    xx += vxh * dt; yy += vyh * dt; zz += vzh * dt;
    field_at(tubeLength, tubeRadius, nz, nr, deflWindow, dnz, dnt, plateYz0, plateZz0, deflY, deflZ,
             ezGrid, erGrid, dzGrid, dtGrid, xx, yy, zz, &ex, &ey, &ez);
    vx[i] = vxh + 0.5f * (-eOverM * ex) * dt;
    vy[i] = vyh + 0.5f * (-eOverM * ey) * dt;
    vz[i] = vzh + 0.5f * (-eOverM * ez) * dt;
    x[i] = xx; y[i] = yy; z[i] = zz;

    float r = sqrt(yy * yy + zz * zz);
    if (xx >= screenZ - 0.002f || xx < 0.0f || r >= tubeRadius - 1e-4f) { dead[i] = 1; return; }
    int iz = clampi((int)rint(xx / tubeLength * (nz - 1)), 0, nz - 1);
    int ir = clampi((int)rint(r / tubeRadius * (nr - 1)), 0, nr - 1);
    if (group[iz * nr + ir] != 0) dead[i] = 1;
}
";

    private IntPtr _program, _kernel;
    private IntPtr _x, _y, _z, _vx, _vy, _vz, _birth, _dead, _ez, _er, _dz, _dt, _group;
    private int _capacity, _gridCap, _deflCap;

    public static ClElectronOptics? TryCreate(int maxParticles, int gridCells, int deflCells)
    {
        if (maxParticles <= 0 || gridCells <= 0 || deflCells <= 0 || !OpenCl.Available) return null;
        var b = new ClElectronOptics();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kernel = OpenCl.Kernel(b._program, "mp_eopt_step");
            if (b._kernel == IntPtr.Zero) { b.Dispose(); return null; }
            long pb = 4L * maxParticles;
            b._x = OpenCl.Buffer(pb); b._y = OpenCl.Buffer(pb); b._z = OpenCl.Buffer(pb);
            b._vx = OpenCl.Buffer(pb); b._vy = OpenCl.Buffer(pb); b._vz = OpenCl.Buffer(pb);
            b._birth = OpenCl.Buffer(pb); b._dead = OpenCl.Buffer(maxParticles);
            b._ez = OpenCl.Buffer(4L * gridCells); b._er = OpenCl.Buffer(4L * gridCells); b._group = OpenCl.Buffer(gridCells);
            b._dz = OpenCl.Buffer(4L * deflCells); b._dt = OpenCl.Buffer(4L * deflCells);
            if (b._x == IntPtr.Zero || b._y == IntPtr.Zero || b._z == IntPtr.Zero || b._vx == IntPtr.Zero || b._vy == IntPtr.Zero || b._vz == IntPtr.Zero
                || b._birth == IntPtr.Zero || b._dead == IntPtr.Zero || b._ez == IntPtr.Zero || b._er == IntPtr.Zero || b._group == IntPtr.Zero
                || b._dz == IntPtr.Zero || b._dt == IntPtr.Zero)
            { b.Dispose(); return null; }
            b._capacity = maxParticles; b._gridCap = gridCells; b._deflCap = deflCells;
            return b;
        }
        catch { b.Dispose(); return null; }
    }

    private ClElectronOptics() { }

    public bool UploadField(int gridCells, float[] ez, float[] er, byte[] group, int deflCells, float[] dz, float[] dt)
    {
        if (_kernel == IntPtr.Zero || gridCells <= 0 || gridCells > _gridCap || deflCells <= 0 || deflCells > _deflCap) return false;
        if (ez.Length < gridCells || er.Length < gridCells || group.Length < gridCells || dz.Length < deflCells || dt.Length < deflCells) return false;
        try
        {
            return OpenCl.Write(_ez, ez, gridCells) && OpenCl.Write(_er, er, gridCells) && OpenCl.Write(_group, group, gridCells)
                && OpenCl.Write(_dz, dz, deflCells) && OpenCl.Write(_dt, dt, deflCells);
        }
        catch { return false; }
    }

    private byte[] _zeroDead = [];

    public bool Upload(int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, int[] birth)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || n > _capacity) return false;
        if (x.Length < n || y.Length < n || z.Length < n || vx.Length < n || vy.Length < n || vz.Length < n || birth.Length < n) return false;
        try
        {
            if (_zeroDead.Length < n) _zeroDead = new byte[_capacity];
            return OpenCl.Write(_x, x, n) && OpenCl.Write(_y, y, n) && OpenCl.Write(_z, z, n)
                && OpenCl.Write(_vx, vx, n) && OpenCl.Write(_vy, vy, n) && OpenCl.Write(_vz, vz, n)
                && OpenCl.Write(_birth, birth, n) && OpenCl.Write(_dead, _zeroDead, n);
        }
        catch { return false; }
    }

    public bool Step(int n, int substeps, float dt,
        float tubeLength, float tubeRadius, float screenZ, int nz, int nr,
        float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0, float deflY, float deflZ, float eOverM)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || n > _capacity || substeps <= 0) return false;
        try
        {
            bool ok = OpenCl.Arg(_kernel, 0, n) && OpenCl.Arg(_kernel, 2, dt)
                && OpenCl.Arg(_kernel, 3, tubeLength) && OpenCl.Arg(_kernel, 4, tubeRadius) && OpenCl.Arg(_kernel, 5, screenZ)
                && OpenCl.Arg(_kernel, 6, nz) && OpenCl.Arg(_kernel, 7, nr) && OpenCl.Arg(_kernel, 8, deflWindow)
                && OpenCl.Arg(_kernel, 9, dnz) && OpenCl.Arg(_kernel, 10, dnt) && OpenCl.Arg(_kernel, 11, plateYz0)
                && OpenCl.Arg(_kernel, 12, plateZz0) && OpenCl.Arg(_kernel, 13, deflY) && OpenCl.Arg(_kernel, 14, deflZ)
                && OpenCl.Arg(_kernel, 15, eOverM)
                && OpenCl.Arg(_kernel, 16, _ez) && OpenCl.Arg(_kernel, 17, _er) && OpenCl.Arg(_kernel, 18, _dz) && OpenCl.Arg(_kernel, 19, _dt)
                && OpenCl.Arg(_kernel, 20, _group)
                && OpenCl.Arg(_kernel, 21, _x) && OpenCl.Arg(_kernel, 22, _y) && OpenCl.Arg(_kernel, 23, _z)
                && OpenCl.Arg(_kernel, 24, _vx) && OpenCl.Arg(_kernel, 25, _vy) && OpenCl.Arg(_kernel, 26, _vz)
                && OpenCl.Arg(_kernel, 27, _birth) && OpenCl.Arg(_kernel, 28, _dead);
            if (!ok) return false;
            for (int k = 0; k < substeps; k++)
            {
                if (!OpenCl.Arg(_kernel, 1, k) || !OpenCl.Launch(_kernel, n)) return false;
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, byte[] dead)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || n > _capacity) return false;
        if (x.Length < n || y.Length < n || z.Length < n || vx.Length < n || vy.Length < n || vz.Length < n || dead.Length < n) return false;
        try
        {
            return OpenCl.Read(_x, x, n) && OpenCl.Read(_y, y, n) && OpenCl.Read(_z, z, n)
                && OpenCl.Read(_vx, vx, n) && OpenCl.Read(_vy, vy, n) && OpenCl.Read(_vz, vz, n)
                && OpenCl.Read(_dead, dead, n);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        foreach (IntPtr m in new[] { _x, _y, _z, _vx, _vy, _vz, _birth, _dead, _ez, _er, _dz, _dt, _group }) OpenCl.Release(m);
        _x = _y = _z = _vx = _vy = _vz = _birth = _dead = _ez = _er = _dz = _dt = _group = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kernel); _kernel = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program); _program = IntPtr.Zero;
    }
}
