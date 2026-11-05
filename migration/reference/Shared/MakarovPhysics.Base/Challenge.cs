namespace MakarovPhysics.Base;

/// <summary>
/// A goal set on an experiment: land the shot on the target, hold the beam
/// level, get the puck through the gate.
///
/// Three rules make the difference between a task and a game.
///
/// The check reads the MODEL, not the picture. A goal evaluated from what is
/// on screen can be met by moving the camera; one evaluated from the state can
/// only be met by doing the physics.
///
/// Success can be required to HOLD. "Balanced" is not a moment the beam passes
/// through on its way past, it is a state it stays in, and a challenge that
/// fired on the first frame the condition was true would reward overshoot.
/// Momentary goals set the hold to zero and say so.
///
/// And once met, it stays met until the run is restarted. A challenge that
/// un-completed itself the instant the experiment drifted on would be
/// unreadable: the reader looks up from the apparatus to a panel that has
/// forgotten what just happened.
/// </summary>
public sealed class Challenge(string? title, Func<ChallengeResult> evaluate)
{
    private readonly Func<ChallengeResult> _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
    private double _heldFor;
    private double _lastTime = double.NaN;
    public string Title { get; } = title ?? string.Empty;

    /// <summary>
    /// How long the goal must hold before it counts, in simulation seconds.
    /// Zero means the first instant counts, which is right for "hit the
    /// target" and wrong for "keep it balanced".
    /// </summary>
    public double HoldSeconds { get; init; }

    /// <summary>The latest reading, including the latched success.</summary>
    public ChallengeResult Result { get; private set; } = ChallengeResult.Pending(0, "");

    /// <summary>Simulation time the goal has been continuously met.</summary>
    public double HeldFor => _heldFor;

    public bool IsAchieved => Result.State == ChallengeState.Achieved;

    /// <summary>Back to square one. Called when the experiment restarts.</summary>
    public void Reset()
    {
        _heldFor = 0;
        _lastTime = double.NaN;
        Result = ChallengeResult.Pending(0, string.Empty);
    }

    /// <summary>
    /// Re-checks the goal. <paramref name="time"/> is the module clock, so a
    /// paused experiment holds nothing and a timescale of one fifth still
    /// needs the same simulated seconds.
    /// </summary>
    public void Update(double time)
    {
        if (IsAchieved) return; // latched: nothing after this changes it
        double dt = 0;

        if (double.IsFinite(_lastTime) && double.IsFinite(time))
        {
            // A rewound clock is a restart, not negative elapsed time.
            dt = time >= _lastTime ? time - _lastTime : 0;
        }

        if (double.IsFinite(time)) _lastTime = time;

        ChallengeResult reading = _evaluate();

        if (reading.State != ChallengeState.Achieved)
        {
            _heldFor = 0;
            Result = reading;
            return;
        }

        _heldFor += dt;

        Result = _heldFor >= HoldSeconds
            ? reading
            : ChallengeResult.Pending(reading.Progress, reading.Detail);
    }
}
