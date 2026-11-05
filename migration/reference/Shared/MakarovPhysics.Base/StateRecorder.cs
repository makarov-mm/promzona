namespace MakarovPhysics.Base;

/// <summary>
/// Records module states on a fixed simulation-time interval and scrubs back
/// through them.
///
/// KinematicsLab has had this since it was written, with its own arrays; this
/// is the same idea lifted out so any module that can hand over its state gets
/// a timeline for the price of two methods.
///
/// Three decisions carry it. Recording runs on SIMULATION time, not on frames,
/// so a scrub bar means the same thing at any frame rate and at any timescale.
/// Seeking snaps to a recorded sample instead of interpolating between two: an
/// interpolated state is one of the physics never passed through, and in a
/// nonlinear system it is not even close to one. And resuming from a scrubbed
/// point THROWS AWAY the recorded future, because from there on it is a future
/// that no longer happens; keeping it would leave the timeline holding two
/// different histories at once.
/// </summary>
public sealed class StateRecorder
{
    private readonly double[] _states;
    private readonly double[] _times;
    private readonly int _capacity;
    private readonly int _stateSize;
    private int _head;
    private double _lastCapture = double.NegativeInfinity;

    /// <param name="stateSize">Doubles per snapshot.</param>
    /// <param name="capacity">Snapshots kept; the oldest falls off the end.</param>
    /// <param name="interval">Simulation seconds between snapshots.</param>
    public StateRecorder(int stateSize, int capacity, double interval)
    {
        if (stateSize < 1) throw new ArgumentOutOfRangeException(nameof(stateSize));
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));

        // Infinity passes "> 0" and then never elapses: the recorder would take
        // the first sample and silently never take another.
        if (!double.IsFinite(interval) || interval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be finite and positive.");
        }

        long cells = (long)capacity * stateSize;

        if (cells > 16_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Recording buffer is too large.");
        }

        _stateSize = stateSize;
        _capacity = capacity;
        Interval = interval;
        _states = new double[cells];
        _times = new double[capacity];
    }

    /// <summary>Simulation seconds between snapshots.</summary>
    public double Interval { get; }

    /// <summary>Snapshots held right now.</summary>
    public int Count { get; private set; }

    /// <summary>Simulation time the timeline spans.</summary>
    public double Duration => Count < 2 ? 0 : TimeAt(Count - 1) - TimeAt(0);

    /// <summary>Index the last seek landed on, or -1 when running live.</summary>
    public int PlayIndex { get; private set; } = -1;

    public void Clear()
    {
        Count = 0;
        _head = 0;
        PlayIndex = -1;
        _lastCapture = double.NegativeInfinity;
    }

    /// <summary>Simulation time of snapshot i, oldest first.</summary>
    public double TimeAt(int i)
    {
        if ((uint)i >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(i), $"Index {i} is outside 0..{Count - 1}.");
        }

        return _times[Slot(i)];
    }

    /// <summary>Snapshot i, oldest first. Modules use it to plot the history they recorded.</summary>
    public ReadOnlySpan<double> StateAt(int i)
    {
        if ((uint)i >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(i), $"Index {i} is outside 0..{Count - 1}.");
        }

        return _states.AsSpan(Slot(i) * _stateSize, _stateSize);
    }

    /// <summary>
    /// Records the state if the interval has elapsed. Returns true when a
    /// snapshot was actually taken.
    /// </summary>
    public bool TryCapture(IRecordable source, double time)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(time)) return false;

        // A capture EARLIER than the last one is a rewind: a scrubbed module
        // that ran on, or a reset. The old code only forgot the interval and
        // then appended, so a history could read [10, 11, 0] with a negative
        // Duration and two branches mixed in one list (05.09, external audit
        // F04). Now the future past the new time is cut first - this is the
        // same branch policy ResumeFromPlayhead applies when the reader
        // resumes on purpose - and the capture starts a clean continuation.
        if (Count > 0 && time < TimeAt(Count - 1))
        {
            CutAfter(time);
            // the branch begins with the state it was rewound to, at once -
            // not after one more interval (the 05.09 run: the cut alone left
            // the rewound sample waiting, and the first snapshot of the new
            // branch was a whole interval late)
            _lastCapture = double.NegativeInfinity;
        }

        if (time < _lastCapture) _lastCapture = double.NegativeInfinity;
        if (time - _lastCapture < Interval) return false;

        _lastCapture = time;
        int slot = Count < _capacity ? Count : _head;
        source.CaptureState(_states.AsSpan(slot * _stateSize, _stateSize));
        _times[slot] = time;

        if (Count < _capacity)
        {
            Count++;
        }
        else
        {
            _head = (_head + 1) % _capacity;
        }

        return true;
    }

    /// <summary>
    /// Puts the module back at a point of the timeline, 0 = oldest kept, 1 =
    /// newest. Snaps to a recorded snapshot; returns its simulation time.
    /// </summary>
    public bool TrySeek(double fraction, IRecordable target, out double time)
    {
        ArgumentNullException.ThrowIfNull(target);
        time = 0;
        if (Count == 0 || !double.IsFinite(fraction)) return false;
        int index = (int)Math.Round(Math.Clamp(fraction, 0, 1) * (Count - 1));
        return TrySeekIndex(index, target, out time);
    }

    /// <summary>The same by index, oldest first.</summary>
    public bool TrySeekIndex(int index, IRecordable target, out double time)
    {
        ArgumentNullException.ThrowIfNull(target);
        time = 0;
        if (Count == 0) return false;
        index = Math.Clamp(index, 0, Count - 1);
        target.RestoreState(StateAt(index));
        time = TimeAt(index);
        PlayIndex = index;
        return true;
    }

    /// <summary>
    /// Drops everything recorded after the point the timeline is parked at.
    /// A module calls it when the simulation runs again: from a scrubbed
    /// point, the future that was recorded is no longer the future that
    /// happens.
    /// </summary>
    public void ResumeFromPlayhead()
    {
        if (PlayIndex < 0) return;
        Keep(PlayIndex + 1);
    }

    /// <summary>Drops every snapshot later than <paramref name="time"/>: a
    /// rewind starts a new branch from the last snapshot at or before it.</summary>
    public void CutAfter(double time)
    {
        int keep = 0;

        while (keep < Count && TimeAt(keep) <= time)
        {
            keep++;
        }

        Keep(keep);
    }

    /// <summary>Keeps the first <paramref name="keep"/> snapshots, compacted
    /// to the front, and forgets the playhead.</summary>
    private void Keep(int keep)
    {
        if (keep < Count)
        {
            // Compact to the front so the ring starts clean at the cut.
            if (_head != 0)
            {
                var states = new double[keep * _stateSize];
                var times = new double[keep];

                for (int i = 0; i < keep; i++)
                {
                    StateAt(i).CopyTo(states.AsSpan(i * _stateSize, _stateSize));
                    times[i] = TimeAt(i);
                }

                states.AsSpan().CopyTo(_states.AsSpan(0, states.Length));
                times.AsSpan().CopyTo(_times.AsSpan(0, keep));
                _head = 0;
            }

            Count = keep;
        }

        _lastCapture = Count > 0 ? TimeAt(Count - 1) : double.NegativeInfinity;
        PlayIndex = -1;
    }

    private int Slot(int i) => Count < _capacity ? i : (_head + i) % _capacity;
}
