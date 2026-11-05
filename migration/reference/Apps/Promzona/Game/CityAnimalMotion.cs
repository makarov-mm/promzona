namespace Promzona.Game;

/// <summary>Transient presentation state, owned by the actor, never by a reused ID.
/// Simulation time makes shadow/colour passes and paused frames idempotent.</summary>
internal sealed class CityAnimalMotion
{
    private double _time = double.NaN;
    private int _from, _to;
    private float _blend = 1;
    internal float Heading { get; private set; }
    internal float FlightPitch { get; private set; }
    internal float Pitch { get; private set; }
    internal float Roll { get; private set; }

    internal (float From, float To, float Blend) Sample(CityAnimal a, double time, float idle, float walk, float run)
    {
        int clip = CityAnimalAnimation.Clip(a);
        double elapsed = time - _time;

        if (!double.IsFinite(_time) || elapsed < 0 || elapsed > .5)
        {
            _from = _to = clip;
            _blend = 1;
            Heading = (float)a.Heading;
        }
        else if (elapsed > 0)
        {
            if (clip != _to)
            {
                if (clip == _from)
                {
                    (_from, _to) = (_to, _from);
                    _blend = 1 - _blend;
                }
                else
                {
                    _from = _blend < .5f ? _from : _to;
                    _to = clip;
                    _blend = 0;
                }
            }

            _blend = Math.Min(1, _blend + (float)(elapsed / .22));
            double turn = Math.Atan2(Math.Sin(a.Heading - Heading), Math.Cos(a.Heading - Heading));
            Heading += (float)(turn * (1 - Math.Exp(-elapsed * 14)));
        }

        if (a.Dead || a.KnockedFor > 0)
        {
            _from = _to = 0;
            _blend = 1;
        }

        // The flight clip includes extended legs. Raise the beak during takeoff,
        // then level out; landing birds settle before returning to the idle clip.
        float flight = a.Flying && !a.Dead
            ? (float)(.12 + .36 * Math.Exp(-a.FlightTime * 2)) *
              (float)Math.Clamp(a.Height / .35, 0, 1)
            : 0;

        float mix = !double.IsFinite(_time) || elapsed < 0 || elapsed > .5
            ? 1
            : (float)(1 - Math.Exp(-Math.Max(0, elapsed) * 10));

        FlightPitch += (flight - FlightPitch) * mix;
        _time = time;

        return (CityAnimalAnimation.Phase(a, _from, time, idle, walk, run), CityAnimalAnimation.Phase(a, _to, time, idle, walk, run), _blend);
    }

    internal void Ground(double slopeX, double slopeZ, bool airborne)
    {
        // Align local +Z with the direction of travel and the sampled ground.
        double s = Math.Sin(Heading), c = Math.Cos(Heading);
        Pitch = airborne ? 0 : (float)-Math.Atan(Math.Clamp(slopeX * s + slopeZ * c, -.65, .65));
        Roll = airborne ? 0 : (float)Math.Atan(Math.Clamp(slopeX * c - slopeZ * s, -.65, .65));
    }
}
