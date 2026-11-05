namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for the DLA dendrite: den_cuda.cu translated - per-walker
/// xorshift64 streams, the point-seed kill circle and long jump, the
/// line-electrode kill band and re-injection, the occupied-stay rule, the
/// sticking probability and the extent bookkeeping. Thirteenth client of
/// <see cref="OpenCl"/>. Never-throw contract.
///
/// THE ATOMICS ARE THE POINT. A dendrite grows by walkers COMMITTING cells,
/// and two walkers reaching for the same cell in the same tick must not both
/// get it. The CUDA kernel owns the cell with atomicCAS, takes a sequence
/// number with atomicAdd, and reverts if the number is past the target, so
/// Count equals the number of occupied cells exactly and growth stops AT the
/// target rather than somewhere past it. All three - cmpxchg, add, max - are
/// core OpenCL 1.1 builtins on 32-bit integers, so the protocol translates
/// literally rather than being approximated.
///
/// The one documented backend difference is the .cu's own and survives the
/// translation unchanged: when walkers race, the commit ORDER (which is the
/// colour ramp) and the microscopic growth set differ from the serial CPU
/// reference. The aggregate physics - D ~ 1.71, screening, morphology
/// against p - is the same. A walker that loses the race stays put, which is
/// the reference's occupied-stay rule.
///
/// The max-radius slot holds the FLOAT BITS of the radius and is maximised
/// with an integer atomic. That works because the radius is non-negative and
/// IEEE-754 bit patterns of non-negative floats order the same way as the
/// unsigned integers they alias - the same trick as the .cu, with as_uint /
/// as_float in place of __float_as_uint / __uint_as_float.
/// </summary>
public sealed class ClDendrite : IDendriteBackend
{
    private const int SlotCount = 0;
    private const int SlotMaxH = 1;
    private const int SlotMaxRBits = 2;
    private const int SlotEdge = 3;
    private const int Slots = 4;

    private const string Source = @"
#define DEN_COUNT 0
#define DEN_MAXH  1
#define DEN_MAXR_BITS 2
#define DEN_EDGE  3

inline uint DenXorshift(ulong* s)
{
    ulong v = *s;
    v ^= v << 13; v ^= v >> 7; v ^= v << 17;
    *s = v;
    return (uint)(v >> 32);
}

inline float DenFrand(ulong* s)
{
    return (DenXorshift(s) >> 8) * (1.0f / 16777216.0f);
}

__kernel void mp_den_step(
    int grid, int walkers, int moves, int pointSeed, float stickP, int target,
    __global int* cells, __global int* wx, __global int* wy,
    __global ulong* rng, __global uint* stats)
{
    int i = get_global_id(0);
    if (i >= walkers) return;

    ulong r = rng[i];
    int x = wx[i], y = wy[i];
    int c = grid / 2;

    for (int m = 0; m < moves; m++)
    {
        int maxH = (int)stats[DEN_MAXH];
        float maxR = as_float(stats[DEN_MAXR_BITS]);
        int nx, ny;

        if (pointSeed)
        {
            float dx = (float)(x - c), dy = (float)(y - c);
            float d = sqrt(dx * dx + dy * dy);
            float kill = fmin((float)(grid / 2 - 2), fmax(2.0f * maxR + 20.0f, 60.0f));
            if (d > kill)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float rad = fmin(maxR + 6.0f, (float)(grid / 2 - 8));
                    float a = DenFrand(&r) * 6.2831853f;
                    int sx = min(max(c + (int)rint(rad * cos(a)), 1), grid - 2);
                    int sy = min(max(c + (int)rint(rad * sin(a)), 1), grid - 2);
                    if (cells[sy * grid + sx] == 0) { x = sx; y = sy; break; }
                    if (attempt == 7) { x = sx; y = sy; }
                }
                continue;
            }

            if (d > maxR + 8.0f)
            {
                float len = d - maxR - 6.0f;
                float a = DenFrand(&r) * 6.2831853f;
                nx = x + (int)rint(len * cos(a));
                ny = y + (int)rint(len * sin(a));
                nx = min(max(nx, 1), grid - 2);
                ny = min(max(ny, 1), grid - 2);
            }
            else
            {
                uint dir = DenXorshift(&r) & 3u;
                nx = x + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                ny = y + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                nx = min(max(nx, 1), grid - 2);
                ny = min(max(ny, 1), grid - 2);
            }
        }
        else
        {
            int killY = min(grid - 4, maxH + 48);
            if (y > killY)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int sx = (int)(DenFrand(&r) * grid) % grid;
                    int sy = min(maxH + 6, grid - 8);
                    if (cells[sy * grid + sx] == 0) { x = sx; y = sy; break; }
                    if (attempt == 7) { x = sx; y = sy; }
                }
                continue;
            }

            if (y > maxH + 8)
            {
                nx = (int)(DenFrand(&r) * grid) % grid;
                ny = min(maxH + 6, grid - 8);
            }
            else
            {
                uint dir = DenXorshift(&r) & 3u;
                nx = x + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                ny = y + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                nx = (nx + grid) % grid;
                ny = min(max(ny, 1), grid - 2);
            }
        }

        int nidx = ny * grid + nx;
        if (cells[nidx] != 0) continue;

        bool adjacent;
        if (pointSeed)
        {
            adjacent =
                (nx > 0 && cells[nidx - 1] != 0) ||
                (nx < grid - 1 && cells[nidx + 1] != 0) ||
                (ny > 0 && cells[nidx - grid] != 0) ||
                (ny < grid - 1 && cells[nidx + grid] != 0);
        }
        else
        {
            int left = ny * grid + ((nx - 1 + grid) % grid);
            int right = ny * grid + ((nx + 1) % grid);
            adjacent =
                ny == 1 ||
                cells[left] != 0 || cells[right] != 0 ||
                cells[nidx - grid] != 0 ||
                (ny < grid - 1 && cells[nidx + grid] != 0);
        }

        if (adjacent && (stickP >= 1.0f || DenFrand(&r) < stickP))
        {
            if (atomic_cmpxchg(&cells[nidx], 0, -1) == 0)
            {
                int order = (int)atomic_add(&stats[DEN_COUNT], 1u) + 1;
                if (order > target)
                {
                    atomic_sub(&stats[DEN_COUNT], 1u);
                    cells[nidx] = 0;
                    continue;
                }
                cells[nidx] = order;
                if (pointSeed)
                {
                    float dx = (float)(nx - c), dy = (float)(ny - c);
                    float rad = sqrt(dx * dx + dy * dy);
                    atomic_max(&stats[DEN_MAXR_BITS], as_uint(rad));
                    if (rad >= (float)(grid / 2 - 12)) stats[DEN_EDGE] = 1u;
                }
                else
                {
                    atomic_max(&stats[DEN_MAXH], (uint)ny);
                    if (ny >= grid - 16) stats[DEN_EDGE] = 1u;
                }
                float maxR2 = as_float(stats[DEN_MAXR_BITS]);
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int sx, sy;
                    if (pointSeed)
                    {
                        float rad = fmin(maxR2 + 6.0f, (float)(grid / 2 - 8));
                        float a = DenFrand(&r) * 6.2831853f;
                        sx = min(max(c + (int)rint(rad * cos(a)), 1), grid - 2);
                        sy = min(max(c + (int)rint(rad * sin(a)), 1), grid - 2);
                    }
                    else
                    {
                        sx = (int)(DenFrand(&r) * grid) % grid;
                        sy = min((int)stats[DEN_MAXH] + 6, grid - 8);
                    }
                    if (cells[sy * grid + sx] == 0) { x = sx; y = sy; break; }
                    if (attempt == 7) { x = sx; y = sy; }
                }
                continue;
            }
            continue;
        }

        x = nx; y = ny;
    }

    wx[i] = x; wy[i] = y; rng[i] = r;
}
";

    private IntPtr _program, _kernel;
    private IntPtr _cells, _wx, _wy, _rng, _stats;
    private int _cellCap, _walkerCap;
    private readonly int[] _statScratch = new int[Slots];

    /// <summary>Null when the backend cannot start - the caller keeps its CPU
    /// walker loop.</summary>
    public static ClDendrite? TryCreate(int maxCells, int maxWalkers)
    {
        if (maxCells <= 0 || maxWalkers <= 0 || !OpenCl.Available) return null;
        var b = new ClDendrite();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kernel = OpenCl.Kernel(b._program, "mp_den_step");
            if (b._kernel == IntPtr.Zero) { b.Dispose(); return null; }

            b._cells = OpenCl.Buffer(4L * maxCells);
            b._wx = OpenCl.Buffer(4L * maxWalkers);
            b._wy = OpenCl.Buffer(4L * maxWalkers);
            b._rng = OpenCl.Buffer(8L * maxWalkers);
            b._stats = OpenCl.Buffer(4L * Slots);
            if (b._cells == IntPtr.Zero || b._wx == IntPtr.Zero || b._wy == IntPtr.Zero || b._rng == IntPtr.Zero || b._stats == IntPtr.Zero) { b.Dispose(); return null; }

            b._cellCap = maxCells;
            b._walkerCap = maxWalkers;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClDendrite() { }

    public bool Upload(int cells, int walkers, int[]? grid, int[]? wx, int[]? wy, ulong[]? rng, int count, int maxHeight, float maxRadius, bool edgeReached)
    {
        if (_kernel == IntPtr.Zero) return false;
        if (cells <= 0 || cells > _cellCap || walkers <= 0 || walkers > _walkerCap) return false;
        if (grid is null || grid.Length < cells || wx is null || wx.Length < walkers || wy is null || wy.Length < walkers || rng is null || rng.Length < walkers) return false;

        try
        {
            _statScratch[SlotCount] = count;
            _statScratch[SlotMaxH] = maxHeight;
            _statScratch[SlotMaxRBits] = BitConverter.SingleToInt32Bits(maxRadius);
            _statScratch[SlotEdge] = edgeReached ? 1 : 0;

            return OpenCl.Write(_cells, grid, cells)
                && OpenCl.Write(_wx, wx, walkers)
                && OpenCl.Write(_wy, wy, walkers)
                && OpenCl.Write(_rng, rng, walkers)
                && OpenCl.Write(_stats, _statScratch, Slots);
        }
        catch { return false; }
    }

    public bool Step(int grid, int walkers, int moves, bool pointSeed, float stickP, int target, int ticks)
    {
        if (_kernel == IntPtr.Zero) return false;
        long cells = (long)grid * grid;

        if (cells <= 0 || cells > _cellCap || walkers <= 0 || walkers > _walkerCap || ticks <= 0)
        {
            return false;
        }

        try
        {
            bool ok = 
                OpenCl.Arg(_kernel, 0, grid) && 
                OpenCl.Arg(_kernel, 1, walkers) && 
                OpenCl.Arg(_kernel, 2, moves) &&
                OpenCl.Arg(_kernel, 3, pointSeed ? 1 : 0) &&
                OpenCl.Arg(_kernel, 4, stickP) && 
                OpenCl.Arg(_kernel, 5, target) && 
                OpenCl.Arg(_kernel, 6, _cells) && 
                OpenCl.Arg(_kernel, 7, _wx) &&
                OpenCl.Arg(_kernel, 8, _wy) && 
                OpenCl.Arg(_kernel, 9, _rng) && 
                OpenCl.Arg(_kernel, 10, _stats);

            if (!ok) return false;

            // one launch per tick, exactly as the .cu does: a tick is the unit
            // the growth target and the colour order are counted in, and
            // merging ticks would let more walkers commit against one stale
            // maxH/maxR than the reference ever allows
            for (int t = 0; t < ticks; t++)
            {
                if (!OpenCl.Launch(_kernel, walkers))
                {
                    return false;
                }
            }

            return OpenCl.Finish();
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadCells(int cells, int[]? grid)
    {
        if (_kernel == IntPtr.Zero || cells <= 0 || cells > _cellCap) return false;
        if (grid is null || grid.Length < cells) return false;

        try
        {
            return OpenCl.Read(_cells, grid, cells);
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadWalkers(int walkers, int[]? wx, int[]? wy, ulong[]? rng)
    {
        if (_kernel == IntPtr.Zero || walkers <= 0 || walkers > _walkerCap) return false;

        if (wx is null || 
            wx.Length < walkers || 
            wy is null || 
            wy.Length < walkers || 
            rng is null ||
            rng.Length < walkers)
        {
            return false;
        }

        try
        {
            return OpenCl.Read(_wx, wx, walkers) &&
                   OpenCl.Read(_wy, wy, walkers) && 
                   OpenCl.Read(_rng, rng, walkers);
        }
        catch
        {
            return false;
        }
    }

    public bool DownloadStats(out int count, out int maxHeight, out float maxRadius, out bool edgeReached)
    {
        count = 0;
        maxHeight = 0;
        maxRadius = 0;
        edgeReached = false;
        if (_kernel == IntPtr.Zero) return false;

        try
        {
            if (!OpenCl.Read(_stats, _statScratch, Slots)) return false;
            count = _statScratch[SlotCount];
            maxHeight = _statScratch[SlotMaxH];
            maxRadius = BitConverter.Int32BitsToSingle(_statScratch[SlotMaxRBits]);
            edgeReached = _statScratch[SlotEdge] != 0;
            return true;
        }
        catch
        { 
            return false; 
        }
    }

    public void Dispose()
    {
        OpenCl.Release(_cells);
        OpenCl.Release(_wx);
        OpenCl.Release(_wy);
        OpenCl.Release(_rng);
        OpenCl.Release(_stats);
        _cells = _wx = _wy = _rng = _stats = IntPtr.Zero;

        OpenCl.ReleaseKernel(_kernel);
        _kernel = IntPtr.Zero;

        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
