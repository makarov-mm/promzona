namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for Physarum: phy_cuda.cu mechanically translated - the
/// sense-turn-move agent kernel (bilinear trail sampling on a periodic
/// grid, the model's exact xorshift64), the trail/food deposits and the
/// separable 1-2-1 blur with decay. One real translation delta: CUDA's
/// atomicAdd(float) has no portable OpenCL 1.2 equivalent, so the deposit
/// kernels use the classic compare-and-swap loop over the bit pattern -
/// same sum, device-ordered. Sixth client of <see cref="OpenCl"/>.
/// Never-throw contract.
/// </summary>
public sealed class ClPhysarum : IPhysarumBackend
{
    private const int MaxFood = 64;   // PHY_MAX_FOOD in the .cu

    private const string Source = @"
static int phy_wrap(int i, int g)
{
    i %= g;
    return i < 0 ? i + g : i;
}

static float phy_wrapf(float v, float g)
{
    v = fmod(v, g);
    return v < 0.0f ? v + g : v;
}

static float phy_sample(__global const float* restrict field, int g, float px, float py)
{
    float fx = floor(px), fy = floor(py);
    int x0 = (int)fx, y0 = (int)fy;
    float tx = px - fx, ty = py - fy;
    int x0w = phy_wrap(x0, g), x1w = phy_wrap(x0 + 1, g);
    int y0w = phy_wrap(y0, g), y1w = phy_wrap(y0 + 1, g);
    float c00 = field[y0w * g + x0w], c10 = field[y0w * g + x1w];
    float c01 = field[y1w * g + x0w], c11 = field[y1w * g + x1w];
    float c0 = c00 * (1.0f - tx) + c10 * tx;
    float c1 = c01 * (1.0f - tx) + c11 * tx;
    return c0 * (1.0f - ty) + c1 * ty;
}

static uint phy_xorshift(ulong* s)
{
    ulong v = *s;
    v ^= v << 13; v ^= v >> 7; v ^= v << 17;
    *s = v;
    return (uint)(v >> 32);
}

static float phy_frand(ulong* s)
{
    return (phy_xorshift(s) >> 8) * (1.0f / 16777216.0f);
}

/* CUDA atomicAdd(float): no portable core equivalent in OpenCL 1.2, so the
   textbook CAS loop over the 32-bit pattern - the sum is exact, only the
   order is device-chosen (same as CUDA) */
static void phy_atomic_addf(volatile __global float* addr, float val)
{
    union { uint u; float f; } oldv, newv;
    do
    {
        oldv.f = *addr;
        newv.f = oldv.f + val;
    } while (atomic_cmpxchg((volatile __global uint*)addr, oldv.u, newv.u) != oldv.u);
}

__kernel void mp_phy_agent(
    int g, int agents,
    float sensorDist, float sensorAngle, float turnRate, float jitter, float stepSize,
    __global float* restrict posX, __global float* restrict posY,
    __global float* restrict headX, __global float* restrict headY,
    __global float* restrict trail,
    __global ulong* restrict rng,
    __global const float* restrict field)
{
    int i = get_global_id(0);
    if (i >= agents) return;

    float hx = headX[i], hy = headY[i];
    float px = posX[i], py = posY[i];
    float so = sensorDist, sa = sensorAngle;

    float cA = cos(sa), sA = sin(sa);
    float lx = hx * cA - hy * sA, ly = hx * sA + hy * cA;
    float rx = hx * cA + hy * sA, ry = -hx * sA + hy * cA;

    float vC = phy_sample(field, g, px + hx * so, py + hy * so);
    float vL = phy_sample(field, g, px + lx * so, py + ly * so);
    float vR = phy_sample(field, g, px + rx * so, py + ry * so);

    float bestVal = vC, bx = hx, by = hy;
    if (vL > bestVal) { bestVal = vL; bx = lx; by = ly; }
    if (vR > bestVal) { bestVal = vR; bx = rx; by = ry; }

    ulong r = rng[i];
    float nhx, nhy;
    if (bestVal <= 1e-6f)
    {
        float a = phy_frand(&r) * 6.2831853f;
        nhx = hx + cos(a) * 0.5f;
        nhy = hy + sin(a) * 0.5f;
    }
    else
    {
        nhx = hx + (bx - hx) * turnRate;
        nhy = hy + (by - hy) * turnRate;
        float a = phy_frand(&r) * 6.2831853f;
        nhx += cos(a) * jitter;
        nhy += sin(a) * jitter;
    }
    rng[i] = r;

    float nl = sqrt(nhx * nhx + nhy * nhy);
    if (nl > 1e-12f) { nhx /= nl; nhy /= nl; }
    else { nhx = 1.0f; nhy = 0.0f; }

    float fg = (float)g;
    posX[i] = phy_wrapf(px + nhx * stepSize, fg);
    posY[i] = phy_wrapf(py + nhy * stepSize, fg);
    headX[i] = nhx; headY[i] = nhy;
    trail[i] = bestVal;
}

__kernel void mp_phy_deposit(
    int g, int agents, float deposit,
    __global const float* restrict posX, __global const float* restrict posY,
    volatile __global float* restrict field)
{
    int i = get_global_id(0);
    if (i >= agents) return;
    int x = phy_wrap((int)rint(posX[i]), g);
    int y = phy_wrap((int)rint(posY[i]), g);
    phy_atomic_addf(&field[y * g + x], deposit);
}

__kernel void mp_phy_food(
    int g, int count, float foodDeposit,
    __global const int* restrict foodX, __global const int* restrict foodY,
    volatile __global float* restrict field)
{
    int i = get_global_id(0);
    if (i >= count) return;
    phy_atomic_addf(&field[foodY[i] * g + foodX[i]], foodDeposit);
}

__kernel void mp_phy_blurx(int g, __global const float* restrict field, __global float* restrict tmp)
{
    int i = get_global_id(0);
    int cells = g * g;
    if (i >= cells) return;
    int x = i % g, y = i / g;
    int baseI = y * g;
    float l = field[baseI + phy_wrap(x - 1, g)];
    float c = field[baseI + x];
    float r = field[baseI + phy_wrap(x + 1, g)];
    tmp[i] = 0.25f * l + 0.5f * c + 0.25f * r;
}

__kernel void mp_phy_blury(int g, float decay, __global const float* restrict tmp, __global float* restrict field)
{
    int i = get_global_id(0);
    int cells = g * g;
    if (i >= cells) return;
    int x = i % g, y = i / g;
    float l = tmp[phy_wrap(y - 1, g) * g + x];
    float c = tmp[y * g + x];
    float r = tmp[phy_wrap(y + 1, g) * g + x];
    field[i] = (0.25f * l + 0.5f * c + 0.25f * r) * decay;
}
";

    private IntPtr _program, _kAgent, _kDeposit, _kFood, _kBlurX, _kBlurY;
    private IntPtr _px, _py, _hx, _hy, _trail, _rng, _field, _tmp, _foodX, _foodY;
    private int _agentCap, _gridCap;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClPhysarum? TryCreate(int maxAgents, int maxGrid)
    {
        if (maxAgents <= 0 || maxGrid <= 0 || !OpenCl.Available) return null;
        var b = new ClPhysarum();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kAgent = OpenCl.Kernel(b._program, "mp_phy_agent");
            b._kDeposit = OpenCl.Kernel(b._program, "mp_phy_deposit");
            b._kFood = OpenCl.Kernel(b._program, "mp_phy_food");
            b._kBlurX = OpenCl.Kernel(b._program, "mp_phy_blurx");
            b._kBlurY = OpenCl.Kernel(b._program, "mp_phy_blury");
            if (b._kAgent == IntPtr.Zero || b._kDeposit == IntPtr.Zero || b._kFood == IntPtr.Zero
                || b._kBlurX == IntPtr.Zero || b._kBlurY == IntPtr.Zero) { b.Dispose(); return null; }
            long ab = 4L * maxAgents;
            long cb = 4L * maxGrid * maxGrid;
            b._px = OpenCl.Buffer(ab); b._py = OpenCl.Buffer(ab);
            b._hx = OpenCl.Buffer(ab); b._hy = OpenCl.Buffer(ab);
            b._trail = OpenCl.Buffer(ab);
            b._rng = OpenCl.Buffer(8L * maxAgents);
            b._field = OpenCl.Buffer(cb); b._tmp = OpenCl.Buffer(cb);
            b._foodX = OpenCl.Buffer(4L * MaxFood); b._foodY = OpenCl.Buffer(4L * MaxFood);
            if (b._px == IntPtr.Zero || b._py == IntPtr.Zero || b._hx == IntPtr.Zero
                || b._hy == IntPtr.Zero || b._trail == IntPtr.Zero || b._rng == IntPtr.Zero
                || b._field == IntPtr.Zero || b._tmp == IntPtr.Zero
                || b._foodX == IntPtr.Zero || b._foodY == IntPtr.Zero) { b.Dispose(); return null; }
            b._agentCap = maxAgents;
            b._gridCap = maxGrid;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClPhysarum() { }

    public bool Upload(int grid, int agents, float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng, float[] field)
    {
        if (_kAgent == IntPtr.Zero || grid <= 0 || grid > _gridCap
            || agents <= 0 || agents > _agentCap) return false;
        if (px is null || px.Length < agents || py is null || py.Length < agents
            || hx is null || hx.Length < agents || hy is null || hy.Length < agents
            || trail is null || trail.Length < agents || rng is null || rng.Length < agents
            || field is null || field.Length < grid * grid) return false;
        try
        {
            return OpenCl.Write(_px, px, agents) && OpenCl.Write(_py, py, agents)
                && OpenCl.Write(_hx, hx, agents) && OpenCl.Write(_hy, hy, agents)
                && OpenCl.Write(_trail, trail, agents) && OpenCl.Write(_rng, rng, agents)
                && OpenCl.Write(_field, field, (long)grid * grid);
        }
        catch { return false; }
    }

    public bool UploadFood(int count, int[] foodX, int[] foodY)
    {
        if (_kAgent == IntPtr.Zero || count < 0 || count > MaxFood) return false;
        if (count == 0) return true;
        if (foodX is null || foodX.Length < count || foodY is null || foodY.Length < count) return false;
        try
        {
            return OpenCl.Write(_foodX, foodX, count) && OpenCl.Write(_foodY, foodY, count);
        }
        catch { return false; }
    }

    public bool Step(int grid, int agents,
        float sensorDist, float sensorAngle, float turnRate, float jitter, float stepSize,
        float deposit, float decay, float foodDeposit, int foodCount, int steps)
    {
        if (_kAgent == IntPtr.Zero || grid <= 0 || grid > _gridCap
            || agents <= 0 || agents > _agentCap
            || steps <= 0 || foodCount < 0 || foodCount > MaxFood) return false;
        try
        {
            long cells = (long)grid * grid;
            for (int s = 0; s < steps; s++)
            {
                bool ok =
                    OpenCl.Arg(_kAgent, 0, grid) && OpenCl.Arg(_kAgent, 1, agents) &&
                    OpenCl.Arg(_kAgent, 2, sensorDist) && OpenCl.Arg(_kAgent, 3, sensorAngle) &&
                    OpenCl.Arg(_kAgent, 4, turnRate) && OpenCl.Arg(_kAgent, 5, jitter) &&
                    OpenCl.Arg(_kAgent, 6, stepSize) &&
                    OpenCl.Arg(_kAgent, 7, _px) && OpenCl.Arg(_kAgent, 8, _py) &&
                    OpenCl.Arg(_kAgent, 9, _hx) && OpenCl.Arg(_kAgent, 10, _hy) &&
                    OpenCl.Arg(_kAgent, 11, _trail) && OpenCl.Arg(_kAgent, 12, _rng) &&
                    OpenCl.Arg(_kAgent, 13, _field) &&
                    OpenCl.Launch(_kAgent, agents);
                if (!ok) return false;

                ok = OpenCl.Arg(_kDeposit, 0, grid) && OpenCl.Arg(_kDeposit, 1, agents) &&
                     OpenCl.Arg(_kDeposit, 2, deposit) &&
                     OpenCl.Arg(_kDeposit, 3, _px) && OpenCl.Arg(_kDeposit, 4, _py) &&
                     OpenCl.Arg(_kDeposit, 5, _field) &&
                     OpenCl.Launch(_kDeposit, agents);
                if (!ok) return false;

                if (foodCount > 0 && foodDeposit > 0f)
                {
                    ok = OpenCl.Arg(_kFood, 0, grid) && OpenCl.Arg(_kFood, 1, foodCount) &&
                         OpenCl.Arg(_kFood, 2, foodDeposit) &&
                         OpenCl.Arg(_kFood, 3, _foodX) && OpenCl.Arg(_kFood, 4, _foodY) &&
                         OpenCl.Arg(_kFood, 5, _field) &&
                         OpenCl.Launch(_kFood, foodCount);
                    if (!ok) return false;
                }

                ok = OpenCl.Arg(_kBlurX, 0, grid) && OpenCl.Arg(_kBlurX, 1, _field) &&
                     OpenCl.Arg(_kBlurX, 2, _tmp) &&
                     OpenCl.Launch(_kBlurX, cells);
                if (!ok) return false;

                ok = OpenCl.Arg(_kBlurY, 0, grid) && OpenCl.Arg(_kBlurY, 1, decay) &&
                     OpenCl.Arg(_kBlurY, 2, _tmp) && OpenCl.Arg(_kBlurY, 3, _field) &&
                     OpenCl.Launch(_kBlurY, cells);
                if (!ok) return false;
            }
            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool DownloadField(int cells, float[] field)
    {
        if (_kAgent == IntPtr.Zero || cells <= 0 || cells > _gridCap * _gridCap) return false;
        if (field is null || field.Length < cells) return false;
        try { return OpenCl.Read(_field, field, cells); }
        catch { return false; }
    }

    public bool DownloadAgents(int agents, float[] px, float[] py, float[] hx, float[] hy,
        float[] trail, ulong[] rng)
    {
        if (_kAgent == IntPtr.Zero || agents <= 0 || agents > _agentCap) return false;
        if (px is null || px.Length < agents || py is null || py.Length < agents
            || hx is null || hx.Length < agents || hy is null || hy.Length < agents
            || trail is null || trail.Length < agents || rng is null || rng.Length < agents) return false;
        try
        {
            return OpenCl.Read(_px, px, agents) && OpenCl.Read(_py, py, agents)
                && OpenCl.Read(_hx, hx, agents) && OpenCl.Read(_hy, hy, agents)
                && OpenCl.Read(_trail, trail, agents) && OpenCl.Read(_rng, rng, agents);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_px); OpenCl.Release(_py); OpenCl.Release(_hx); OpenCl.Release(_hy);
        OpenCl.Release(_trail); OpenCl.Release(_rng); OpenCl.Release(_field); OpenCl.Release(_tmp);
        OpenCl.Release(_foodX); OpenCl.Release(_foodY);
        _px = _py = _hx = _hy = _trail = _rng = _field = _tmp = _foodX = _foodY = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kAgent); OpenCl.ReleaseKernel(_kDeposit); OpenCl.ReleaseKernel(_kFood);
        OpenCl.ReleaseKernel(_kBlurX); OpenCl.ReleaseKernel(_kBlurY);
        _kAgent = _kDeposit = _kFood = _kBlurX = _kBlurY = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
