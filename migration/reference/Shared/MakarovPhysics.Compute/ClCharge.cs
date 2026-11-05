namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for ChargeInField: charge_cuda.cu mechanically translated
/// - one Boris-pusher kernel (half electric kick, magnetic rotation via the
/// t/s vectors, half kick, drift) in uniform E and the mirror-perturbed B
/// the CPU FieldB computes. Eighth client of <see cref="OpenCl"/>.
/// Never-throw contract.
/// </summary>
public sealed class ClCharge : IChargeBackend
{
    private const string Source = @"
__kernel void mp_charge_boris(
    int n, float dt, float qmdt,
    float ex, float ey, float ez,
    float bx0, float by0, float bz0, float mirror,
    __global float* restrict x, __global float* restrict y, __global float* restrict z,
    __global float* restrict vx, __global float* restrict vy, __global float* restrict vz)
{
    int i = get_global_id(0);
    if (i >= n) return;

    float xx = x[i], yy = y[i], zz = z[i];
    float s0 = bz0 * mirror * zz;               /* -Br/r, mirrors the CPU FieldB */
    float bx = bx0 - s0 * xx, by = by0 - s0 * yy;
    float bz = bz0 * (1.0f + mirror * zz * zz);

    float vmx = vx[i] + qmdt * ex, vmy = vy[i] + qmdt * ey, vmz = vz[i] + qmdt * ez;
    float tx = qmdt * bx, ty = qmdt * by, tz = qmdt * bz;
    float t2 = tx * tx + ty * ty + tz * tz;
    float sx = 2.0f * tx / (1.0f + t2), sy = 2.0f * ty / (1.0f + t2), sz = 2.0f * tz / (1.0f + t2);
    float vpx = vmx + (vmy * tz - vmz * ty);
    float vpy = vmy + (vmz * tx - vmx * tz);
    float vpz = vmz + (vmx * ty - vmy * tx);
    float vplx = vmx + (vpy * sz - vpz * sy);
    float vply = vmy + (vpz * sx - vpx * sz);
    float vplz = vmz + (vpx * sy - vpy * sx);
    float nvx = vplx + qmdt * ex, nvy = vply + qmdt * ey, nvz = vplz + qmdt * ez;
    vx[i] = nvx; vy[i] = nvy; vz[i] = nvz;
    x[i] += nvx * dt; y[i] += nvy * dt; z[i] += nvz * dt;
}
";

    private IntPtr _program, _kernel;
    private IntPtr _x, _y, _z, _vx, _vy, _vz;
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClCharge? TryCreate(int maxParticles)
    {
        if (maxParticles <= 0 || !OpenCl.Available) return null;
        var b = new ClCharge();

        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);

            if (b._program == IntPtr.Zero)
            {
                b.Dispose(); 
                return null;
            }

            b._kernel = OpenCl.Kernel(b._program, "mp_charge_boris");

            if (b._kernel == IntPtr.Zero)
            {
                b.Dispose(); 
                return null;
            }

            long pb = 4L * maxParticles;
            b._x = OpenCl.Buffer(pb); b._y = OpenCl.Buffer(pb); b._z = OpenCl.Buffer(pb);
            b._vx = OpenCl.Buffer(pb); b._vy = OpenCl.Buffer(pb); b._vz = OpenCl.Buffer(pb);

            if (b._x == IntPtr.Zero
                ||
                b._y == IntPtr.Zero
                ||
                b._z == IntPtr.Zero
                ||
                b._vx == IntPtr.Zero
                ||
                b._vy == IntPtr.Zero
                ||
                b._vz == IntPtr.Zero)
            {
                b.Dispose(); 
                return null;
            }

            b._capacity = maxParticles;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClCharge() { }

    public bool Upload(int n, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || n > _capacity) return false;

        if (x is null 
            ||
            x.Length < n
            ||
            y is null
            ||
            y.Length < n
            ||
            z is null
            ||
            z.Length < n
            ||
            vx is null
            ||
            vx.Length < n
            ||
            vy is null
            ||
            vy.Length < n
            ||
            vz is null
            ||
            vz.Length < n)
        {
            return false;
        }

        try
        {
            return OpenCl.Write(_x, x, n)
                   &&
                   OpenCl.Write(_y, y, n)
                   &&
                   OpenCl.Write(_z, z, n)
                   &&
                   OpenCl.Write(_vx, vx, n)
                   &&
                   OpenCl.Write(_vy, vy, n)
                   &&
                   OpenCl.Write(_vz, vz, n);
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, float dt, float qmdt, float ex, float ey, float ez, float bx, float by, float bz, float mirror, int steps)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || n > _capacity || steps <= 0) return false;

        try
        {
            for (int s = 0; s < steps; s++)
            {
                bool ok =
                    OpenCl.Arg(_kernel, 0, n) && OpenCl.Arg(_kernel, 1, dt) &&
                    OpenCl.Arg(_kernel, 2, qmdt) &&
                    OpenCl.Arg(_kernel, 3, ex) && OpenCl.Arg(_kernel, 4, ey) &&
                    OpenCl.Arg(_kernel, 5, ez) &&
                    OpenCl.Arg(_kernel, 6, bx) && OpenCl.Arg(_kernel, 7, by) &&
                    OpenCl.Arg(_kernel, 8, bz) && OpenCl.Arg(_kernel, 9, mirror) &&
                    OpenCl.Arg(_kernel, 10, _x) && OpenCl.Arg(_kernel, 11, _y) &&
                    OpenCl.Arg(_kernel, 12, _z) &&
                    OpenCl.Arg(_kernel, 13, _vx) && OpenCl.Arg(_kernel, 14, _vy) &&
                    OpenCl.Arg(_kernel, 15, _vz) &&
                    OpenCl.Launch(_kernel, n);

                if (!ok) return false;
            }

            return OpenCl.Finish();
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int n, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz)
    {
        if (_kernel == IntPtr.Zero || n <= 0 || n > _capacity) return false;

        if (x is null || x.Length < n || y is null || y.Length < n || z is null || z.Length < n || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n) return false;

        try
        {
            return OpenCl.Read(_x, x, n) && 
                   OpenCl.Read(_y, y, n) && 
                   OpenCl.Read(_z, z, n) && 
                   OpenCl.Read(_vx, vx, n) &&
                   OpenCl.Read(_vy, vy, n) && 
                   OpenCl.Read(_vz, vz, n);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        OpenCl.Release(_x);
        OpenCl.Release(_y);
        OpenCl.Release(_z);
        OpenCl.Release(_vx);
        OpenCl.Release(_vy);
        OpenCl.Release(_vz);

        _x = _y = _z = _vx = _vy = _vz = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kernel);
        _kernel = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
