using System.Globalization;

namespace MakarovPhysics.Base;

/// <summary>
/// The two numbers at the ends of a chart's vertical axis, formatted so that
/// they differ.
///
/// The charts printed both ends with `0.###`. On a trace whose whole height
/// is a change in the fourth decimal that gives the same string twice -
/// "1.001" at the top and "1.001" at the bottom of the reactor's power graph
/// (audit 07.09, screenshot 012) - and a small change stretched over the full
/// height of a chart with no readable scale is a large change to the eye.
/// The label is now given as many decimals as the SPAN of the axis needs,
/// with two significant digits of the span always visible, and goes
/// scientific when the values themselves are far from unity.
/// </summary>
public static class RangeLabels
{
    /// <summary>Decimal places that make min and max distinct: the span
    /// gets two significant digits, capped at nine places.</summary>
    public static int Decimals(double min, double max)
    {
        double span = Math.Abs(max - min);
        if (!double.IsFinite(span) || span == 0) return 3;
        int places = (int)Math.Ceiling(-Math.Log10(span)) + 1;
        return Math.Clamp(places, 0, 9);
    }

    /// <summary>Format one end of the axis given the other.</summary>
    public static string Format(double value, double otherEnd)
    {
        if (!double.IsFinite(value)) return "-";
        double big = Math.Max(Math.Abs(value), Math.Abs(otherEnd));
        double span = Math.Abs(value - otherEnd);

        // far from unity in either direction, or a span that would need more
        // than nine places: scientific, with the span's digits kept
        if (big >= 1e6 || big is > 0 and < 1e-4 || span is > 0 and < 1e-9)
        {
            int sig = span > 0 && big > 0 ? Math.Clamp((int)Math.Ceiling(Math.Log10(big / span)) + 1, 2, 12) : 3;
            return value.ToString("0." + new string('#', Math.Max(1, sig - 1)) + "e+0", CultureInfo.InvariantCulture);
        }

        // the places the span needs are mandatory; two more are optional, so
        // an ordinary axis still prints 2.25 rather than 2.3
        int places = Decimals(value, otherEnd);
        string optional = new string('#', Math.Max(0, Math.Min(2, 9 - places)));
        return value.ToString("0." + new string('0', places) + optional, CultureInfo.InvariantCulture);
    }

    /// <summary>Both ends at once: (min, max).</summary>
    public static (string Min, string Max) Pair(double min, double max) => (Format(min, max), Format(max, min));
}
