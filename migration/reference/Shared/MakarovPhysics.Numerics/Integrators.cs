namespace MakarovPhysics.Numerics;

/// <summary>
/// Classic RK4 for a vector state. For Hamiltonian systems on long runs,
/// prefer a symplectic method such as <see cref="VelocityVerlet"/>.
/// </summary>
public sealed class Rk4
{
    private readonly double[] _k1, _k2, _k3, _k4, _tmp;
    public int Dimension => _k1.Length;

    public Rk4(int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        _k1 = new double[dimension];
        _k2 = new double[dimension];
        _k3 = new double[dimension];
        _k4 = new double[dimension];
        _tmp = new double[dimension];
    }

    public void Step(OdeRhs f, double t, double[] y, double dt)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(y);
        ValidateFinite(t, nameof(t));
        ValidateFinite(dt, nameof(dt));
        ValidateLength(y, Dimension, nameof(y), "state");

        int n = Dimension;
        f(t, y, _k1);

        for (int i = 0; i < n; i++)
        {
            _tmp[i] = y[i] + 0.5 * dt * _k1[i];
        }

        f(t + 0.5 * dt, _tmp, _k2);

        for (int i = 0; i < n; i++)
        {
            _tmp[i] = y[i] + 0.5 * dt * _k2[i];
        }

        f(t + 0.5 * dt, _tmp, _k3);

        for (int i = 0; i < n; i++)
        {
            _tmp[i] = y[i] + dt * _k3[i];
        }

        f(t + dt, _tmp, _k4);

        for (int i = 0; i < n; i++)
        {
            y[i] += dt / 6.0 * (_k1[i] + 2.0 * _k2[i] + 2.0 * _k3[i] + _k4[i]);
        }
    }

    private static void ValidateLength(double[] values, int expected, string paramName, string role)
    {
        if (values.Length != expected)
        {
            throw new ArgumentException($"RK4 {role} has {values.Length} components; the integrator was created for {expected}.", paramName);
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
