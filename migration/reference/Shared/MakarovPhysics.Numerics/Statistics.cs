namespace MakarovPhysics.Numerics;

/// <summary>
/// Streaming statistics by Welford's recurrence: one pass, O(1) memory, no
/// catastrophic cancellation - the naive sum-of-squares formula loses every
/// digit when the mean dwarfs the spread, which is exactly a thermometer
/// reading 300.001, 299.999, ... Ideal gas, Brownian motion and the diffusion
/// board all keep running observables; this is their common counter.
/// </summary>
public sealed class RunningStatistics
{
    private double _mean;
    private double _m2;
    public long Count { get; private set; }
    public double Min { get; private set; } = double.PositiveInfinity;
    public double Max { get; private set; } = double.NegativeInfinity;
    public double Mean => Count > 0 ? _mean : 0.0;

    /// <summary>Population variance (÷N). Zero until the first sample.</summary>
    public double Variance => Count > 0 ? _m2 / Count : 0.0;

    /// <summary>Sample variance (÷(N−1)) - the estimator for a drawn sample.</summary>
    public double SampleVariance => Count > 1 ? _m2 / (Count - 1) : 0.0;

    public double StandardDeviation => Math.Sqrt(Variance);

    /// <summary>Add one observation. NaN would poison every later moment, so it throws.</summary>
    public void Push(double value)
    {
        // Infinity was let through while NaN was rejected, and it is just as
        // fatal: delta * (value - _mean) evaluates to Inf * (Inf - Inf) = NaN
        // on the very first infinite sample, so _m2 is NaN immediately and
        // _mean follows on the next GOOD push. The accumulator never recovers -
        // there is no value you can add afterwards that repairs it - so this
        // has to be refused at the door like NaN is.
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Only finite values can enter running statistics.");
        }

        Count++;
        double delta = value - _mean;
        _mean += delta / Count;
        _m2 += delta * (value - _mean);

        if (value < Min) Min = value;
        if (value > Max) Max = value;
    }

    public void Reset()
    {
        Count = 0;
        _mean = 0.0;
        _m2 = 0.0;
        Min = double.PositiveInfinity;
        Max = double.NegativeInfinity;
    }
}

/// <summary>
/// Fixed-range histogram: [min, max) split into equal bins, samples outside
/// the range counted separately instead of silently dropped or clamped into
/// the edge bins (a clamped edge bin looks like a physical peak at the wall,
/// and a Maxwell distribution does not have one).
/// </summary>
public sealed class Histogram
{
    private readonly long[] _bins;
    public double Min { get; }
    public double Max { get; }
    public int BinCount => _bins.Length;
    public long Underflow { get; private set; }
    public long Overflow { get; private set; }
    public long TotalInRange { get; private set; }

    public Histogram(double min, double max, int bins)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || !(max > min))
        {
            throw new ArgumentException($"Histogram: invalid range [{min:G6}, {max:G6}).");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bins);

        Min = min;
        Max = max;
        _bins = new long[bins];
    }

    public long this[int bin] => _bins[bin];

    /// <summary>Left edge of a bin - the x coordinate for bar charts.</summary>
    public double BinLeft(int bin) => Min + (Max - Min) * bin / BinCount;

    public double BinWidth => (Max - Min) / BinCount;

    public void Push(double value)
    {
        if (double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value), "NaN cannot enter a histogram.");

        if (value < Min)
        {
            Underflow++; 
            return;
        }

        if (value >= Max)
        {
            Overflow++; 
            return;
        }

        int bin = (int)((value - Min) / (Max - Min) * BinCount);
        if (bin == BinCount) bin--; // guard against value == Max after rounding
        _bins[bin]++;
        TotalInRange++;
    }

    /// <summary>Normalized density of a bin: count / (total · width), so the
    /// histogram overlays a probability density curve directly.</summary>
    public double Density(int bin) => TotalInRange > 0 ? _bins[bin] / (TotalInRange * BinWidth) : 0.0;

    public void Reset()
    {
        Array.Clear(_bins);
        Underflow = 0;
        Overflow = 0;
        TotalInRange = 0;
    }
}
