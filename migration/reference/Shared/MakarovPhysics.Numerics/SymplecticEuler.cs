namespace MakarovPhysics.Numerics;

/// <summary>Symplectic Euler, first order and inexpensive.</summary>
public static class SymplecticEuler
{
    public static void Step(Acceleration f, double t, double[] x, double[] v, double[] acc, double dt)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(acc);

        if (!double.IsFinite(t)) throw new ArgumentOutOfRangeException(nameof(t));
        if (!double.IsFinite(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
        if (v.Length != x.Length || acc.Length != x.Length) throw new ArgumentException("Position, velocity and acceleration arrays must have equal lengths.");

        f(t, x, v, acc);

        for (int i = 0; i < x.Length; i++)
        {
            v[i] += acc[i] * dt;
            x[i] += v[i] * dt;
        }
    }
}
