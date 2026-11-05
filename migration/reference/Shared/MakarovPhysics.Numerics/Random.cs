namespace MakarovPhysics.Numerics;

/// <summary>
/// xoshiro256** - a fast PRNG reproducible by seed
/// (important for demos: same seed → same picture).
/// </summary>
public sealed class Xoshiro256ss
{
    private ulong _s0, _s1, _s2, _s3;
    private double _gaussSpare;
    private bool _hasSpare;

    public Xoshiro256ss(ulong seed)
    {
        // splitmix64 to warm up the state
        ulong z = seed;

        ulong Next()
        {
            z += 0x9E3779B97F4A7C15UL;
            ulong t = z;
            t = (t ^ (t >> 30)) * 0xBF58476D1CE4E5B9UL;
            t = (t ^ (t >> 27)) * 0x94D049BB133111EBUL;
            return t ^ (t >> 31);
        }

        _s0 = Next(); _s1 = Next(); _s2 = Next(); _s3 = Next();
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextUInt64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3; _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
    public double NextDouble(double minInclusive, double maxExclusive)
    {
        if (!double.IsFinite(minInclusive)) throw new ArgumentOutOfRangeException(nameof(minInclusive));
        if (!double.IsFinite(maxExclusive)) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        if (maxExclusive < minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Upper bound must not be smaller than the lower bound.");
        return minInclusive + (maxExclusive - minInclusive) * NextDouble();
    }

    /// <summary>Uniform integer in [0, maxExclusive) - rejection sampling, no modulo bias.</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        ulong bound = (ulong)maxExclusive;
        ulong limit = ulong.MaxValue - ulong.MaxValue % bound; // largest multiple of bound
        ulong r;

        do
        {
            r = NextUInt64();
        } while (r >= limit);

        return (int)(r % bound);
    }

    /// <summary>N(0,1), Box - Muller (polar variant).</summary>
    public double NextGaussian()
    {
        if (_hasSpare)
        {
            _hasSpare = false; 
            return _gaussSpare;
        }

        double u, v, s;

        do
        {
            u = 2 * NextDouble() - 1;
            v = 2 * NextDouble() - 1;
            s = u * u + v * v;
        } while (s is >= 1 or 0);

        double m = Math.Sqrt(-2 * Math.Log(s) / s);
        _gaussSpare = v * m;
        _hasSpare = true;
        return u * m;
    }

    /// <summary>
    /// Velocity component from the Maxwell distribution: v_i ~ N(0, √(kT/m)).
    /// </summary>
    public double NextMaxwellComponent(double kT, double mass)
    {
        if (!(kT >= 0.0) || !double.IsFinite(kT)) throw new ArgumentOutOfRangeException(nameof(kT));
        if (!(mass > 0.0) || !double.IsFinite(mass)) throw new ArgumentOutOfRangeException(nameof(mass));
        return NextGaussian() * Math.Sqrt(kT / mass);
    }
}
