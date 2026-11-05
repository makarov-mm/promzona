namespace MakarovPhysics.Numerics;

/// <summary>
/// Velocity Verlet, symplectic and second order for acceleration depending on position.
/// It is the standard choice for molecular dynamics, N-body systems and oscillator lattices.
/// </summary>
public sealed class VelocityVerlet
{
    private readonly double[] _acceleration;
    private bool _primed;
    public int Dimension => _acceleration.Length;

    public VelocityVerlet(int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        _acceleration = new double[dimension];
    }

    public void Step(Acceleration f, double t, double[] x, double[] v, double dt)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(v);
        ValidateFinite(t, nameof(t));
        ValidateFinite(dt, nameof(dt));
        ValidateState(x, v, Dimension);

        if (!_primed)
        {
            f(t, x, v, _acceleration);
            _primed = true;
        }

        for (int i = 0; i < Dimension; i++)
        {
            x[i] += v[i] * dt + 0.5 * _acceleration[i] * dt * dt;
        }

        // Half-kick with the old acceleration, then the second half with the new one.
        for (int i = 0; i < Dimension; i++)
        {
            v[i] += 0.5 * _acceleration[i] * dt;
        }

        f(t + dt, x, v, _acceleration);

        for (int i = 0; i < Dimension; i++)
        {
            v[i] += 0.5 * _acceleration[i] * dt;
        }
    }

    /// <summary>Call after manually changing position, velocity, mass or force parameters.</summary>
    public void Invalidate() => _primed = false;

    private static void ValidateState(double[] x, double[] v, int dimension)
    {
        if (x.Length != dimension)
        {
            throw new ArgumentException($"Position has {x.Length} components; expected {dimension}.", nameof(x));
        }

        if (v.Length != dimension)
        {
            throw new ArgumentException($"Velocity has {v.Length} components; expected {dimension}.", nameof(v));
        }
    }

    private static void ValidateFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite.");
        }
    }
}
