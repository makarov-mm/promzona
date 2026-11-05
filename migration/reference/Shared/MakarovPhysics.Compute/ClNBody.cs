namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the n-body family (Galaxy): galaxy_cuda.cu translated -
/// resident body state, three kernels per leapfrog iteration, no transfers
/// inside <see cref="Step"/>. Twelfth client of <see cref="OpenCl"/>.
/// Never-throw contract.
///
/// ONE DELIBERATE DEPARTURE FROM THE .cu. The CUDA force kernel tiles through
/// __shared__ memory with __syncthreads, which needs the block size to be
/// exactly the tile size. <see cref="OpenCl.Launch"/> passes a NULL local
/// size and lets the driver choose the work-group - correctly, because every
/// other client is a flat map with no group-wide state. A tiled kernel under
/// a driver-chosen group would fill part of the tile and read the rest stale:
/// wrong forces, no error, on somebody else's machine. So this kernel reads
/// the bodies straight from global memory. It costs the tile's cache reuse -
/// on a mid-range card the all-pairs pass is roughly half the speed of the
/// tiled CUDA one - and it is right under any work-group size the driver
/// picks. Three days from release that trade is not close.
///
/// The arithmetic is otherwise identical, term for term, including the
/// softened self-term being exactly zero (r2 = eps2 at j == i, so the body
/// pulls on itself with dx = 0 - no branch needed, same as the .cu).
/// </summary>
public sealed class ClNBody : INBodyBackend
{
    private const string Source = @"
/* Exact all-pairs accelerations, __local tiling - the CUDA ForceKernel and the
   Vulkan PASS_FORCE, same loop, same order of summation.

   It was a plain __global loop until 12.09, and the measurement on an RTX 4060
   Laptop said what that costs: CUDA 3.2 ms, Vulkan 3.5 ms, OpenCL 12.1 ms on
   the same 30 000 bodies and the same scene. Not the API's fault - each
   work-item was re-reading all N positions out of global memory while the
   other two read each tile once into on-chip memory. The kernel was the odd
   one out, not the runtime.

   MP_NB_TILE_MAX bounds the __local arrays; the STRIDE is get_local_size(0),
   so any work-group size the host passes (it passes min(256, device max))
   works without rebuilding. 4 x 256 x 4 B = 4 KB of local memory, inside the
   16 KB the spec guarantees.

   No early return for i >= n. The host rounds the global size up to a whole
   number of groups, so those work-items exist, and every work-item in a group
   must reach every barrier - returning early from some of them is undefined
   behaviour that manifests as a hang on one driver and a wrong answer on the
   next. They run the loop and write nothing. */
#define MP_NB_TILE_MAX 256

__kernel void mp_nb_force(
    int n, float eps2,
    __global const float* restrict px, __global const float* restrict py,
    __global const float* restrict pz, __global const float* restrict mass,
    __global float* restrict ax, __global float* restrict ay,
    __global float* restrict az)
{
    __local float sx[MP_NB_TILE_MAX];
    __local float sy[MP_NB_TILE_MAX];
    __local float sz[MP_NB_TILE_MAX];
    __local float sm[MP_NB_TILE_MAX];

    int i = get_global_id(0);
    int lid = get_local_id(0);
    int tile = get_local_size(0);

    float xi = 0.0f, yi = 0.0f, zi = 0.0f;
    if (i < n) { xi = px[i]; yi = py[i]; zi = pz[i]; }

    float axv = 0.0f, ayv = 0.0f, azv = 0.0f;

    for (int t = 0; t < n; t += tile)
    {
        int j = t + lid;
        sx[lid] = j < n ? px[j] : 0.0f;
        sy[lid] = j < n ? py[j] : 0.0f;
        sz[lid] = j < n ? pz[j] : 0.0f;
        sm[lid] = j < n ? mass[j] : 0.0f;
        barrier(CLK_LOCAL_MEM_FENCE);

        int count = min(n - t, tile);
        for (int k = 0; k < count; k++)
        {
            float dx = sx[k] - xi;
            float dy = sy[k] - yi;
            float dz = sz[k] - zi;
            float r2 = dx * dx + dy * dy + dz * dz + eps2;
            float inv = rsqrt(r2);
            float s = sm[k] * inv * inv * inv;
            axv += dx * s; ayv += dy * s; azv += dz * s;
        }
        barrier(CLK_LOCAL_MEM_FENCE);
    }

    if (i < n) { ax[i] = axv; ay[i] = ayv; az[i] = azv; }
}

/* half kick + drift fused: vel += acc*h; pos += vel*dt */
__kernel void mp_nb_kickdrift(
    int n, float h, float dt,
    __global float* restrict px, __global float* restrict py,
    __global float* restrict pz,
    __global float* restrict vx, __global float* restrict vy,
    __global float* restrict vz,
    __global const float* restrict ax, __global const float* restrict ay,
    __global const float* restrict az)
{
    int i = get_global_id(0);
    if (i >= n) return;
    float vxi = vx[i] + ax[i] * h;
    float vyi = vy[i] + ay[i] * h;
    float vzi = vz[i] + az[i] * h;
    vx[i] = vxi; vy[i] = vyi; vz[i] = vzi;
    px[i] += vxi * dt;
    py[i] += vyi * dt;
    pz[i] += vzi * dt;
}

/* closing half kick */
__kernel void mp_nb_kick(
    int n, float h,
    __global float* restrict vx, __global float* restrict vy,
    __global float* restrict vz,
    __global const float* restrict ax, __global const float* restrict ay,
    __global const float* restrict az)
{
    int i = get_global_id(0);
    if (i >= n) return;
    vx[i] += ax[i] * h;
    vy[i] += ay[i] * h;
    vz[i] += az[i] * h;
}
";

    private IntPtr _program, _force, _kickDrift, _kick;
    private IntPtr _px, _py, _pz, _m, _vx, _vy, _vz, _ax, _ay, _az;
    private int _capacity;

    /// <summary>Whether the acceleration buffer matches the current positions.
    /// Mirrors accValid in the .cu: the first Step after an Upload has to run
    /// the force pass before the opening half kick, or the leapfrog starts
    /// from whatever the last run left there.</summary>
    private bool _accValid;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU
    /// path, exactly as with a missing CUDA DLL.</summary>
    public static ClNBody? TryCreate(int maxBodies)
    {
        if (maxBodies <= 0 || !OpenCl.Available) return null;
        var b = new ClNBody();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._force = OpenCl.Kernel(b._program, "mp_nb_force");
            b._kickDrift = OpenCl.Kernel(b._program, "mp_nb_kickdrift");
            b._kick = OpenCl.Kernel(b._program, "mp_nb_kick");
            if (b._force == IntPtr.Zero || b._kickDrift == IntPtr.Zero || b._kick == IntPtr.Zero)
            { b.Dispose(); return null; }

            long bytes = 4L * maxBodies;
            b._px = OpenCl.Buffer(bytes); b._py = OpenCl.Buffer(bytes); b._pz = OpenCl.Buffer(bytes);
            b._m = OpenCl.Buffer(bytes);
            b._vx = OpenCl.Buffer(bytes); b._vy = OpenCl.Buffer(bytes); b._vz = OpenCl.Buffer(bytes);
            b._ax = OpenCl.Buffer(bytes); b._ay = OpenCl.Buffer(bytes); b._az = OpenCl.Buffer(bytes);
            if (b._px == IntPtr.Zero || b._py == IntPtr.Zero || b._pz == IntPtr.Zero
                || b._m == IntPtr.Zero
                || b._vx == IntPtr.Zero || b._vy == IntPtr.Zero || b._vz == IntPtr.Zero
                || b._ax == IntPtr.Zero || b._ay == IntPtr.Zero || b._az == IntPtr.Zero)
            { b.Dispose(); return null; }

            b._capacity = maxBodies;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClNBody() { }

    public bool Upload(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz, float[] mass)
    {
        if (_force == IntPtr.Zero || n <= 0 || n > _capacity) return false;
        // a short array would read past the managed buffer - refuse instead,
        // the same guard CudaNBody keeps against the native copy
        if (px is null || px.Length < n || py is null || py.Length < n || pz is null || pz.Length < n
            || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n
            || mass is null || mass.Length < n) return false;
        try
        {
            bool ok = OpenCl.Write(_px, px, n) && OpenCl.Write(_py, py, n) && OpenCl.Write(_pz, pz, n)
                   && OpenCl.Write(_vx, vx, n) && OpenCl.Write(_vy, vy, n) && OpenCl.Write(_vz, vz, n)
                   && OpenCl.Write(_m, mass, n);
            _accValid = false;
            return ok;
        }
        catch { return false; }
    }

    public bool Step(int n, float dt, float eps2, int steps)
    {
        if (_force == IntPtr.Zero || n <= 0 || n > _capacity || steps <= 0) return false;
        float h = 0.5f * dt;
        try
        {
            if (!_accValid)
            {
                if (!Force(n, eps2)) return false;
                _accValid = true;
            }

            for (int s = 0; s < steps; s++)
            {
                bool ok = OpenCl.Arg(_kickDrift, 0, n) && OpenCl.Arg(_kickDrift, 1, h)
                       && OpenCl.Arg(_kickDrift, 2, dt)
                       && OpenCl.Arg(_kickDrift, 3, _px) && OpenCl.Arg(_kickDrift, 4, _py)
                       && OpenCl.Arg(_kickDrift, 5, _pz)
                       && OpenCl.Arg(_kickDrift, 6, _vx) && OpenCl.Arg(_kickDrift, 7, _vy)
                       && OpenCl.Arg(_kickDrift, 8, _vz)
                       && OpenCl.Arg(_kickDrift, 9, _ax) && OpenCl.Arg(_kickDrift, 10, _ay)
                       && OpenCl.Arg(_kickDrift, 11, _az)
                       && OpenCl.Launch(_kickDrift, n);
                if (!ok) return false;

                if (!Force(n, eps2)) return false;

                ok = OpenCl.Arg(_kick, 0, n) && OpenCl.Arg(_kick, 1, h)
                  && OpenCl.Arg(_kick, 2, _vx) && OpenCl.Arg(_kick, 3, _vy)
                  && OpenCl.Arg(_kick, 4, _vz)
                  && OpenCl.Arg(_kick, 5, _ax) && OpenCl.Arg(_kick, 6, _ay)
                  && OpenCl.Arg(_kick, 7, _az)
                  && OpenCl.Launch(_kick, n);
                if (!ok) return false;
            }

            // Finish before returning, for the same two reasons the .cu
            // synchronizes: the caller's stopwatch then measures real GPU work,
            // and a queue error surfaces in Step instead of smearing into the
            // next Download.
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    /// <summary>Work-group size for the tiled force pass: the tile the kernel's
    /// __local arrays hold, or the largest group the device accepts, whichever
    /// is smaller. The spec guarantees only 1, so this is a query and not a
    /// constant; a device that answers something silly still gets a launch,
    /// with a tile of 1, which is slow and correct rather than refused.</summary>
    private static int Tile()
    {
        int max = OpenCl.MaxWorkGroupSize;
        if (max <= 0) return 64;              // the probe said nothing: stay modest
        return max < 256 ? max : 256;
    }

    private bool Force(int n, float eps2)
        => OpenCl.Arg(_force, 0, n) && OpenCl.Arg(_force, 1, eps2)
        && OpenCl.Arg(_force, 2, _px) && OpenCl.Arg(_force, 3, _py)
        && OpenCl.Arg(_force, 4, _pz) && OpenCl.Arg(_force, 5, _m)
        && OpenCl.Arg(_force, 6, _ax) && OpenCl.Arg(_force, 7, _ay)
        && OpenCl.Arg(_force, 8, _az)
        // The tiled kernel needs the work-group size named, and the global size
        // rounded up to it - Launch does the rounding.
        && OpenCl.Launch(_force, n, Tile());

    public bool Download(int n, float[] px, float[] py, float[] pz,
        float[] vx, float[] vy, float[] vz)
    {
        if (_force == IntPtr.Zero || n <= 0 || n > _capacity) return false;
        if (px is null || px.Length < n || py is null || py.Length < n || pz is null || pz.Length < n
            || vx is null || vx.Length < n || vy is null || vy.Length < n || vz is null || vz.Length < n)
            return false;
        try
        {
            return OpenCl.Read(_px, px, n) && OpenCl.Read(_py, py, n) && OpenCl.Read(_pz, pz, n)
                && OpenCl.Read(_vx, vx, n) && OpenCl.Read(_vy, vy, n) && OpenCl.Read(_vz, vz, n);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_px); OpenCl.Release(_py); OpenCl.Release(_pz); OpenCl.Release(_m);
        OpenCl.Release(_vx); OpenCl.Release(_vy); OpenCl.Release(_vz);
        OpenCl.Release(_ax); OpenCl.Release(_ay); OpenCl.Release(_az);
        _px = _py = _pz = _m = _vx = _vy = _vz = _ax = _ay = _az = IntPtr.Zero;
        OpenCl.ReleaseKernel(_force); OpenCl.ReleaseKernel(_kickDrift); OpenCl.ReleaseKernel(_kick);
        _force = _kickDrift = _kick = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
