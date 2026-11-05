namespace MakarovPhysics.Charts;

/// <summary>Finite bounds and coordinates, including ranges near double's limits.</summary>
public static class ChartScale
{
    public static (double Min, double Max) Expand(double min, double max, double relativeFloor = 0)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max) return (0, 1);
        double magnitude = Math.Max(Math.Abs(min), Math.Abs(max));
        if (magnitude == 0) return (0, 1);
        // Half first: neither a same-sign midpoint nor an opposite-sign span overflows.
        double middle = min * 0.5 + max * 0.5;
        double half = max * 0.5 - min * 0.5;
        if (half <= 5e-8 * magnitude) half = Math.Max(half, 0.1 * magnitude);
        if (double.IsFinite(relativeFloor) && relativeFloor > 0) half = Math.Max(half, magnitude * (Math.Min(relativeFloor, 2) * 0.5));
        min = Finite(middle - half); max = Finite(middle + half);
        double pad = (max * 0.5 - min * 0.5) * 0.16;
        bool positive = min >= 0, negative = max <= 0;
        min = Finite(min - pad); max = Finite(max + pad);
        if (positive && min < 0) min = 0;
        if (negative && max > 0) max = 0;

        // Subnormal values may round every arithmetic pad away.
        if (min == max)
        {
            min = Finite(Math.BitDecrement(min));
            max = Finite(Math.BitIncrement(max));
        }

        return (min, max);
    }

    private static double Finite(double value) => Math.Clamp(value, -double.MaxValue, double.MaxValue);

    public static double Fraction(double value, double min, double max)
    {
        if (!double.IsFinite(value) || !double.IsFinite(min) || !double.IsFinite(max) || min >= max)
            return double.NaN;

        double span = max - min;

        return double.IsFinite(span)
            ? (value - min) / span
            : (value * 0.5 - min * 0.5) / (max * 0.5 - min * 0.5);
    }
}
