using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>Solves the mechanisms' rows together: the plain equalities, and the drives and stops while
/// they stay inside their bounds. The matrix J·M⁻¹·Jᵀ + Γ of the rows in play is factored (LDLᵀ, in
/// double) and each sweep finds the impulses that put all of them right at once, given what the other rows
/// - contacts, couplings with their own rules - have done. A bounded row that would leave its bounds is
/// held at the bound and taken out, and the rest solved again. Row by row, the sweeps pass a load through
/// a chain of levers slowly: a three-stage scissor lift needed 2000 of them just to move without a load,
/// and a screw driving it, solved against the lift's 2500 kg of reflected inertia one row at a time, ran
/// into its force cap (measured 14.10). A row that the rows before it already fix leaves a vanishing pivot
/// and is skipped; the sweeps row by row that follow still see it.</summary>
internal sealed class MechanismBlock
{
    private readonly List<MechanismConstraint.Row> _equalities = [], _bounds = [], _rows = [];
    private readonly List<int> _active = [];
    private bool[] _bounded = [], _out = [], _skip = [];
    private double[] _matrix = [], _diagonal = [], _work = [];
    private int _count;
    private bool _dirty;

    public void Build(IReadOnlyList<MechanismConstraint> mechanisms)
    {
        _equalities.Clear();
        _bounds.Clear();
        foreach (var c in mechanisms)
        {
            c.EqualityRows(_equalities);
            c.BoundedRows(_bounds);
        }

        _rows.Clear();
        foreach (var row in _equalities)
            if (row.Equality) _rows.Add(row);
        int equalities = _rows.Count;
        foreach (var row in _bounds)
            if (row.Bounded) _rows.Add(row);
        int n = _rows.Count;
        if (_bounded.Length < n)
        {
            _bounded = new bool[n];
            _out = new bool[n];
            _skip = new bool[n];
            _diagonal = new double[n];
            _work = new double[n];
        }

        for (int i = 0; i < n; i++)
        {
            _bounded[i] = i >= equalities;
            _out[i] = false;
        }

        _dirty = true;
    }

    /// <summary>One sweep: the impulses that zero the residuals of every row in play together.</summary>
    public void Solve()
    {
        for (int round = 0; round <= _rows.Count; round++)
        {
            if (_dirty) Factor();
            int n = _count;
            if (n == 0) return;
            var x = _work;
            for (int i = 0; i < n; i++) x[i] = -_rows[_active[i]].Residual();
            Substitute(x, n);

            // a bounded row that would leave its bounds is held there and taken out; the rest solved again
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                int k = _active[i];
                if (!_bounded[k]) continue;
                var row = _rows[k];
                double next = row.Impulse + x[i];
                if (next < row.Lower || next > row.Upper)
                {
                    row.Add((float)(Math.Clamp(next, row.Lower, row.Upper) - row.Impulse));
                    _out[k] = true;
                    changed = true;
                }
            }

            if (!changed)
            {
                for (int i = 0; i < n; i++)
                    if (!double.IsFinite(x[i])) return;
                for (int i = 0; i < n; i++) _rows[_active[i]].Add((float)x[i]);
                return;
            }

            _dirty = true;
        }
    }

    private void Factor()
    {
        _dirty = false;
        _active.Clear();
        for (int k = 0; k < _rows.Count; k++)
            if (!_out[k]) _active.Add(k);
        int n = _count = _active.Count;
        if (_matrix.Length < n * n) _matrix = new double[n * n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double value = Coupling(_rows[_active[i]], _rows[_active[j]]);
                // a millionth of each row's own stiffness given up: rows that hold a part twice over, to the
                // Jacobians' rounding, can no longer ask for unbounded impulses between them
                if (i == j) value = value * (1 + 1e-6) + _rows[_active[i]].Softness;
                _matrix[i * n + j] = value;
            }

        // LDLᵀ in place in the lower triangle; a pivot that has all but vanished marks a redundant row
        for (int k = 0; k < n; k++)
        {
            double original = _matrix[k * n + k], d = original;
            for (int m = 0; m < k; m++)
                if (!_skip[m]) d -= _matrix[k * n + m] * _matrix[k * n + m] * _diagonal[m];
            // the Jacobians are single precision: a row that the others fix to rounding leaves a pivot of
            // about 1e-7 of its diagonal, not zero, and dividing by it threw six balls of a CV joint, held
            // twice over, out of their races (measured 14.10)
            _skip[k] = !(d > 1e-5 * Math.Max(original, 1e-12));
            _diagonal[k] = _skip[k] ? 0 : d;
            for (int i = k + 1; i < n; i++)
            {
                if (_skip[k])
                {
                    _matrix[i * n + k] = 0;
                    continue;
                }

                double value = _matrix[i * n + k];
                for (int m = 0; m < k; m++)
                    if (!_skip[m]) value -= _matrix[i * n + m] * _matrix[k * n + m] * _diagonal[m];
                _matrix[i * n + k] = value / d;
            }
        }
    }

    private void Substitute(double[] x, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (_skip[i])
            {
                x[i] = 0;
                continue;
            }

            double value = x[i];
            for (int m = 0; m < i; m++) value -= _matrix[i * n + m] * x[m];
            x[i] = value;
        }

        for (int i = 0; i < n; i++) x[i] = _skip[i] ? 0 : x[i] / _diagonal[i];
        for (int i = n - 1; i >= 0; i--)
        {
            if (_skip[i]) continue;
            double value = x[i];
            for (int m = i + 1; m < n; m++) value -= _matrix[m * n + i] * x[m];
            x[i] = value;
        }
    }

    /// <summary>J_a·M⁻¹·J_bᵀ over the bodies the two rows share.</summary>
    private static double Coupling(MechanismConstraint.Row a, MechanismConstraint.Row b)
    {
        double sum = 0;
        Term(a.BodyA, a.LinearA, a.AngularA, b, ref sum);
        if (a.BodyB is not null) Term(a.BodyB, a.LinearB, a.AngularB, b, ref sum);
        return sum;
    }

    private static void Term(RigidBody body, Vector3 linear, Vector3 angular, MechanismConstraint.Row b, ref double sum)
    {
        if (body.IsStatic) return;
        if (b.BodyA == body) sum += Product(body, linear, angular, b.LinearA, b.AngularA);
        if (b.BodyB == body) sum += Product(body, linear, angular, b.LinearB, b.AngularB);
    }

    private static double Product(RigidBody body, Vector3 linear, Vector3 angular, Vector3 linearOther, Vector3 angularOther) =>
        body.InvMass * (double)Vector3.Dot(linear, linearOther) + Vector3.Dot(angular, body.InvInertiaWorld * angularOther);
}
