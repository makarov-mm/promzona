namespace MakarovPhysics.Numerics;

/// <summary>Small Newtonian system, kick-drift-kick leapfrog. Pair forces are
/// accumulated symmetrically; massless test particles exert no force. No
/// softening, mergers, tides, relativity or contact physics. Stop on close encounters.</summary>
public sealed class AstroSystem
{
    public double G { get; }
    public double[] Mass { get; }
    public AstroVector[] Position { get; }
    public AstroVector[] Velocity { get; }
    private readonly AstroVector[] _acc;
    public double Time { get; private set; }

    public AstroSystem(double gravitationalConstant, double[] mass, AstroVector[] position, AstroVector[] velocity)
    {
        if (!double.IsFinite(gravitationalConstant) || gravitationalConstant <= 0 || mass.Length < 2 ||
            mass.Length != position.Length || mass.Length != velocity.Length ||
            mass.Any(m => !double.IsFinite(m) || m < 0) || mass.Sum() <= 0 ||
            position.Any(p => !double.IsFinite(p.LengthSquared)) ||
            velocity.Any(v => !double.IsFinite(v.LengthSquared)))
        {
            throw new ArgumentException("Invalid gravitational state");
        }

        G = gravitationalConstant;
        Mass = (double[])mass.Clone();
        Position = (AstroVector[])position.Clone();
        Velocity = (AstroVector[])velocity.Clone();
        _acc = new AstroVector[mass.Length];
    }

    public void CentreBarycentre()
    {
        AstroVector p = default, v = default;
        double m = Mass.Sum();

        for (int i = 0; i < Mass.Length; i++)
        {
            p += Position[i] * Mass[i];
            v += Velocity[i] * Mass[i];
        }

        for (int i = 0; i < Mass.Length; i++)
        {
            Position[i] -= p / m;
            Velocity[i] -= v / m;
        }
    }

    private void Accelerations()
    {
        Array.Clear(_acc);

        for (int i = 0; i < Mass.Length; i++)
        {
            for (int j = i + 1; j < Mass.Length; j++)
            {
                AstroVector d = Position[j] - Position[i];
                double r = d.Length;

                if (r < 1e-14 || !double.IsFinite(r))
                {
                    throw new ArithmeticException("Unresolved gravitational encounter");
                }

                AstroVector force = d * (G / (r * r * r));
                _acc[i] += force * Mass[j];
                _acc[j] -= force * Mass[i];
            }
        }
    }

    public void Step(double dt)
    {
        if (!double.IsFinite(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
        Accelerations();

        for (int i = 0; i < Mass.Length; i++)
        {
            Velocity[i] += _acc[i] * (dt * .5);
            Position[i] += Velocity[i] * dt;
        }

        Accelerations();

        for (int i = 0; i < Mass.Length; i++)
        {
            Velocity[i] += _acc[i] * (dt * .5);
        }

        Time += dt;
    }

    public double Energy
    {
        get
        {
            double e = 0;

            for (int i = 0; i < Mass.Length; i++)
            {
                e += .5 * Mass[i] * Velocity[i].LengthSquared;

                for (int j = i + 1; j < Mass.Length; j++)
                {
                    e -= G * Mass[i] * Mass[j] / (Position[j] - Position[i]).Length;
                }
            }

            return e;
        }
    }

    public AstroVector Momentum
    {
        get
        {
            AstroVector p = default;

            for (int i = 0; i < Mass.Length; i++)
            {
                p += Velocity[i] * Mass[i];
            }

            return p;
        }
    }

    public AstroVector AngularMomentum
    {
        get
        {
            AstroVector l = default;

            for (int i = 0; i < Mass.Length; i++)
            {
                l += AstroVector.Cross(Position[i], Velocity[i]) * Mass[i];
            }

            return l;
        }
    }
}
