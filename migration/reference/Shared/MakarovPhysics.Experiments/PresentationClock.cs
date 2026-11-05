namespace MakarovPhysics.Experiments;

/// <summary>Deterministic unscaled time for weather and material detail. Pause
/// freezes it; reset returns the initial weather. No dependence on TimeScale.</summary>
public sealed class PresentationClock
{
    public double Seconds { get; private set; }
    public void Advance(double realDt, bool running)
    {
        if (running && double.IsFinite(realDt) && realDt > 0)
            Seconds += Math.Min(realDt, .05);
    }
    public void Reset() => Seconds = 0;
}
