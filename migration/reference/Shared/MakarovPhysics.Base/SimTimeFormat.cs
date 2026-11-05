namespace MakarovPhysics.Base;

/// <summary>
/// The header's readout of simulated time and playback scale, chosen by
/// magnitude.
///
/// The header used to print `{time:0.00} s   ×{scale:0.##}` for every module.
/// For a pendulum that is the right thing; for the cyclotron, the mass
/// spectrometer and the e/m bench, whose runs last microseconds at a
/// TimeScale of 1e-8, both numbers round to zero and the header reads
/// "Running   Time: 0.00 s   ×0" over a scene that is visibly moving (audit
/// 07.09, screenshots 014 and 016: "the statuses contradict each other").
/// The time is right, the format was not.
///
/// Time takes the SI prefix its magnitude asks for, down to picoseconds, and
/// the scale is printed as a plain factor when it is one a reader can hold
/// (0.01 to 1000) and in scientific form otherwise. Pure functions, in Base
/// so the SelfTest can pin them; the host only calls.
/// </summary>
public static class SimTimeFormat
{
    /// <summary>Simulated time with the unit its magnitude asks for: "12.34 s",
    /// "3.82 ms", "38.16 µs", "120.0 ns". Zero is "0.00 s".</summary>
    public static string Time(double seconds)
    {
        if (!double.IsFinite(seconds)) return "-";
        double a = Math.Abs(seconds);
        if (a == 0) return "0.00 s";
        if (a >= 1) return $"{seconds:0.00} s";
        if (a >= 1e-3) return $"{seconds * 1e3:0.00} ms";
        if (a >= 1e-6) return $"{seconds * 1e6:0.00} µs";
        if (a >= 1e-9) return $"{seconds * 1e9:0.00} ns";
        return $"{seconds * 1e12:0.00} ps";
    }

    /// <summary>Playback scale: "×1", "×2.5", "×0.35", and "×1e-8" or "×2.5e+4"
    /// once a plain factor would lose its digits.</summary>
    public static string Scale(double scale)
    {
        if (!double.IsFinite(scale)) return "×-";
        double a = Math.Abs(scale);
        if (a == 0) return "×0";
        if (a is >= 0.01 and < 1000) return $"×{scale:0.##}";
        return $"×{scale:0.#e+0}";
    }

    /// <summary>A duration on the human scale - a half-life, a period - with
    /// the unit that leaves three digits in front of it: "12.1 min" rather
    /// than "0.01 d" (Rn-222's Gamow half-life on the alpha-decay panel,
    /// audit 07.09, screenshot 017: a rounded 0.01 hides the factor of 457
    /// the panel is there to show). Microseconds to years; beyond ten
    /// thousand years the number goes scientific.</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds)) return "-";
        double a = Math.Abs(seconds);
        if (a < 1e-3) return $"{seconds * 1e6:0.###} µs";
        if (a < 1) return $"{seconds * 1e3:0.###} ms";
        if (a < 60) return $"{seconds:0.###} s";
        if (a < 3600) return $"{seconds / 60:0.##} min";
        if (a < 86400) return $"{seconds / 3600:0.##} h";
        if (a < 365.25 * 86400) return $"{seconds / 86400:0.##} d";
        double years = seconds / (365.25 * 86400);
        if (Math.Abs(years) < 1e4) return $"{years:0.##} yr";
        return $"{years:0.###e+0} yr";
    }

    /// <summary>The quantity the header compares between frames to decide
    /// whether the label needs rebuilding: the displayed time rounded to the
    /// resolution of its own unit, so a run in microseconds refreshes as
    /// often as one in seconds and no more.</summary>
    public static double Shown(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds == 0) return seconds;
        double a = Math.Abs(seconds);
        double unit = a >= 1 ? 1 : a >= 1e-3 ? 1e-3 : a >= 1e-6 ? 1e-6 : a >= 1e-9 ? 1e-9 : 1e-12;
        return Math.Round(seconds / unit, 2) * unit;
    }
}
