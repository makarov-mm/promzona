namespace MakarovPhysics.RigidBodies;

/// <summary>A position servo for a hinge: it swings the hinge A·sin(2πt/T) about its starting angle, or
/// holds it at +A. The target is a centre plus a swing: the centre moves to 0 (swinging) or to A
/// (holding) along a half-cosine at up to <see cref="RampSpeed"/>, the swing's amplitude follows A or
/// dies away with a time constant of <see cref="Ease"/>, so the target never jumps and never changes
/// speed in one step - a sine begun at full speed from rest is an impulse that a three-loop linkage took
/// a millimetre of joint gap to absorb (measured 14.10). Each substep the target's own step is fed forward
/// and what is left of the error closed in 0.1 s, at no more than <see cref="MaxCorrection"/>, all
/// within the hinge's torque limit. Targets outside [<see cref="Lower"/>, <see cref="Upper"/>] are clipped.</summary>
public sealed class HingeServo
{
    /// <summary>Swing amplitude, or the held angle, rad.</summary>
    public double Amplitude;
    /// <summary>Period of the swing, s.</summary>
    public double Period = 4;
    public bool Swing = true;
    public double Lower = double.NegativeInfinity, Upper = double.PositiveInfinity;
    /// <summary>Largest speed the error correction asks for, rad/s.</summary>
    public double MaxCorrection = 1.5;
    /// <summary>Peak speed of the centre's moves, rad/s.</summary>
    public double RampSpeed = 1;
    /// <summary>Time constant with which the swing's amplitude follows <see cref="Amplitude"/>, s.</summary>
    public double Ease = .5;
    private double _phase, _swing, _from, _to, _time, _duration = 1;

    private double Centre => _from + (_to - _from) * (1 - Math.Cos(Math.PI * Math.Min(1, _time / _duration))) / 2;

    /// <summary>The angle the servo aims at now, rad.</summary>
    public double Target => Math.Clamp(Centre + _swing * Math.Sin(_phase), Lower, Upper);

    public void Reset()
    {
        _phase = _swing = _from = _to = _time = 0;
        _duration = 1;
    }

    /// <summary>Advances the target by one substep and sets the hinge's drive for it.</summary>
    public void Drive(HingeConstraint hinge, float maxTorque, double dt)
    {
        double now = Target, goal = Swing ? 0 : Amplitude;
        if (goal != _to)
        {
            _from = Centre;
            _to = goal;
            _time = 0;
            _duration = Math.Max(.3, Math.PI / 2 * Math.Abs(_to - _from) / RampSpeed);
        }

        _time += dt;
        _swing += ((Swing ? Amplitude : 0) - _swing) * (1 - Math.Exp(-dt / Ease));
        _phase += 2 * Math.PI * dt / Period;
        double next = Target;
        hinge.MotorEnabled = true;
        hinge.MaxTorque = maxTorque;
        hinge.TargetSpeed = (float)((next - now) / dt + Math.Clamp(10 * (now - hinge.Angle), -MaxCorrection, MaxCorrection));
    }
}
