namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Stopwatch bookkeeping, kept apart from the widget so it can be tested
/// without a GL context.
///
/// It counts SIMULATION time, not wall-clock time: the caller feeds it the
/// module's own clock every frame. That is the only version that means
/// anything here, because pausing the experiment, single stepping it or
/// running it at one fifth speed must all show up honestly on the dial.
/// </summary>
public sealed class StopwatchState
{
    /// <summary>How many laps are kept; the display has room for a few.</summary>
    public const int MaxLaps = 3;

    private readonly List<double> _laps = [];
    private double _lastSample;
    private bool _hasSample;

    /// <summary>Accumulated measured time, seconds.</summary>
    public double Elapsed { get; private set; }

    public bool Running { get; private set; }

    public IReadOnlyList<double> Laps => _laps;

    /// <summary>
    /// Feeds the module clock. Only forward motion counts: Stop() on a module
    /// rewinds it's time to zero, and a stopwatch that subtracted that would
    /// show a negative reading or wipe out a measurement the user was taking.
    /// </summary>
    public void Sample(double simulationTime)
    {
        if (!double.IsFinite(simulationTime)) return;

        if (!_hasSample)
        {
            _lastSample = simulationTime;
            _hasSample = true;
            return;
        }

        double delta = simulationTime - _lastSample;
        _lastSample = simulationTime;

        if (!(delta > 0)) return;
        if (Running) Elapsed += delta;
    }

    public void Start() => Running = true;
    public void Stop() => Running = false;
    public void Toggle() => Running = !Running;

    /// <summary>Records the current reading and keeps counting.</summary>
    public void Lap()
    {
        if (_laps.Count == MaxLaps) _laps.RemoveAt(0);
        _laps.Add(Elapsed);
    }

    /// <summary>Back to zero, stopped, laps cleared.</summary>
    public void Reset()
    {
        Elapsed = 0;
        Running = false;
        _laps.Clear();
    }
}
