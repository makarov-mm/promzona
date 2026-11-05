namespace MakarovPhysics.Renderer;

/// <summary>
/// Turns a branch current into visible motion of charge carriers.
///
/// Two decisions carry the physics. First, the markers keep a FIXED spacing
/// and change SPEED, never the other way round: in a conductor the carrier
/// density n does not depend on the current, the drift velocity does
/// (I = n q v A). Crowding the dots together at higher current would draw the
/// wrong mechanism.
///
/// Second, the speed saturates. A circuit spans microamps to amps, and a
/// literal proportion either freezes the small currents or, at the large end,
/// moves a marker further than the gap to the next one between two frames.
/// That is the wagon-wheel effect: past half a spacing per frame the animation
/// aliases and the current appears to slow down, stop, or run backwards.
/// The cap below keeps the fastest flow at a small fraction of a spacing per
/// frame even on a slow display, so the picture can never lie about direction.
///
/// Unlike a field arrow, which keeps a floor length so a weak field still
/// shows where it points, a marker at zero current simply stops. A still dot
/// is the honest picture of no current; there is no direction to preserve.
/// </summary>
public static class CurrentFlow
{
    /// <summary>
    /// Fastest flow, in marker spacings per second. At 30 frames per second
    /// this is 0.08 of a spacing per frame, far under the half-spacing limit
    /// where the motion would start to alias.
    /// </summary>
    public const double MaxSpacingsPerSecond = 2.5;

    /// <summary>
    /// Signed flow speed in spacings per second. The reference current maps to
    /// half the maximum; zero current maps to a standstill.
    /// </summary>
    public static double DriftSpeed(double current, double reference)
    {
        if (!double.IsFinite(current) || current == 0) return 0;

        double magnitude = Math.Abs(current);
        if (double.IsPositiveInfinity(magnitude)) return Math.Sign(current) * MaxSpacingsPerSecond;
        if (!double.IsFinite(reference) || reference <= 0) return Math.Sign(current) * MaxSpacingsPerSecond;

        return Math.Sign(current) * MaxSpacingsPerSecond * magnitude / (magnitude + reference);
    }

    /// <summary>
    /// Advances a phase (in spacings) and wraps it into 0..1. Integrating the
    /// phase instead of computing it from a global clock matters: with a clock
    /// the markers would jump every time the current changed, which in an AC
    /// circuit is every step.
    /// </summary>
    public static double Advance(double phase, double speed, double dt)
    {
        if (!double.IsFinite(phase)) phase = 0;
        if (!double.IsFinite(speed) || !double.IsFinite(dt)) return Wrap(phase);

        return Wrap(phase + speed * dt);
    }

    private static double Wrap(double phase)
    {
        if (!double.IsFinite(phase)) return 0;

        phase %= 1.0;
        return phase < 0 ? phase + 1.0 : phase;
    }
}
