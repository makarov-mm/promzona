using System.Numerics;

namespace MakarovPhysics.Numerics;

/// <summary>
/// The state vector of n qubits, and the gates that move it. Shared by the
/// quantum computing family of Math Suite (QuantumGates, Grover,
/// Teleportation, DeutschJozsa, QftShor, ErrorCorrection - Docs/QUANTUM-PLAN.md);
/// the physical qubit lives in Physics Suite and is a different object
/// entirely, a Bloch vector under a Hamiltonian with units.
///
/// Nothing here is approximate. The vector holds 2^n complex amplitudes and
/// every gate is an exact unitary acting on them, so a probability computed
/// from this state is the true probability and not an estimate. What IS
/// estimated is what a machine would actually see: Measure draws an outcome
/// from |psi|^2 through the shared counter hash, and the count of many draws
/// has to land inside three sigma of the exact number. That is the same
/// "exact fraction against a count" the fourth ring of Math is built on, and
/// it is the honest half of the subject: the amplitudes are not observable,
/// the tally is.
///
/// n is capped at 12 (4096 amplitudes). The cap is not a limit of the method
/// but of the point: a gate costs 2^n, the screen shows a bar per basis
/// state, and past twelve neither the eye nor the frame budget keeps up.
/// Shor on 15 needs eight, Grover stays interesting to ten.
///
/// Bit convention: qubit q is bit q of the basis index, so |q = 1> is exactly
/// the half of the index space with that bit set. This is what turns a gate
/// from a 2^n x 2^n matrix product into a walk over disjoint index pairs
/// (i, i | bit) - the whole library is that loop with different 2x2 numbers.
/// A control is the same walk with the pairs that lack the control bit
/// skipped.
///
/// Pinned against Tools/twins/quantum_register_twin.py, which reimplements
/// all of it in plain Python.
/// </summary>
public sealed class QuantumRegister
{
    /// <summary>Twelve qubits, 4096 amplitudes: see the class note.</summary>
    public const int MaxQubits = 12;

    private static readonly double Root = Math.Sqrt(0.5);
    private readonly Complex[] _a;

    public QuantumRegister(int qubits)
    {
        if (qubits is < 1 or > MaxQubits)
        {
            throw new ArgumentOutOfRangeException(nameof(qubits), qubits, $"QuantumRegister: 1 to {MaxQubits} qubits.");
        }

        Qubits = qubits;
        _a = new Complex[1 << qubits];
        _a[0] = Complex.One;
    }

    /// <summary>How many qubits the register carries.</summary>
    public int Qubits { get; }

    /// <summary>How many basis states, 2^Qubits.</summary>
    public int Size => _a.Length;

    /// <summary>The amplitudes, for drawing. Index i is the basis state whose
    /// qubit q is bit q of i.</summary>
    public ReadOnlySpan<Complex> Amplitudes => _a;

    /// <summary>Back to |0...0>, without allocating a new register.</summary>
    public void Reset()
    {
        Array.Clear(_a);
        _a[0] = Complex.One;
    }

    /// <summary>An independent copy. The modules use it to run a second
    /// experiment on the same state - measuring in another basis, or sweeping
    /// the number of iterations - without disturbing the one on screen.</summary>
    public QuantumRegister Clone()
    {
        var r = new QuantumRegister(Qubits);
        Array.Copy(_a, r._a, _a.Length);
        return r;
    }

    /// <summary>An arbitrary 2x2 unitary on one qubit. Every named gate below
    /// is this call with constants.</summary>
    public void Apply(int qubit, Complex m00, Complex m01, Complex m10, Complex m11) => Walk(qubit, -1, m00, m01, m10, m11);

    /// <summary>The same 2x2, applied only where the control qubit is 1.</summary>
    public void ApplyControlled(int control, int qubit, Complex m00, Complex m01, Complex m10, Complex m11)
    {
        if (control == qubit) throw new ArgumentException("QuantumRegister: a qubit cannot control itself.", nameof(control));
        Check(control);
        Walk(qubit, control, m00, m01, m10, m11);
    }

    private void Walk(int qubit, int control, Complex m00, Complex m01, Complex m10, Complex m11)
    {
        Check(qubit);
        int bit = 1 << qubit, cbit = control < 0 ? 0 : 1 << control;

        for (int i = 0; i < _a.Length; i++)
        {
            if ((i & bit) != 0) continue;
            if (cbit != 0 && (i & cbit) == 0) continue;

            int j = i | bit;
            Complex x = _a[i], y = _a[j];
            _a[i] = m00 * x + m01 * y;
            _a[j] = m10 * x + m11 * y;
        }
    }

    /// <summary>Hadamard: the gate that makes superposition, and the gate that
    /// takes it away again.</summary>
    public void H(int qubit) => Walk(qubit, -1, Root, Root, Root, -Root);

    /// <summary>Hadamard on every qubit: |0...0> becomes the flat
    /// superposition of all 2^n states, which is where most algorithms
    /// start.</summary>
    public void HAll()
    {
        for (int q = 0; q < Qubits; q++) H(q);
    }

    public void X(int qubit) => Walk(qubit, -1, 0, 1, 1, 0);
    public void Y(int qubit) => Walk(qubit, -1, 0, -Complex.ImaginaryOne, Complex.ImaginaryOne, 0);
    public void Z(int qubit) => Walk(qubit, -1, 1, 0, 0, -1);

    /// <summary>A quarter turn about Z: diag(1, i).</summary>
    public void S(int qubit) => Walk(qubit, -1, 1, 0, 0, Complex.ImaginaryOne);

    /// <summary>An eighth turn: diag(1, (1 + i)/sqrt 2). Written as the pair
    /// of equal components rather than exp(i pi / 4) so that T twice is S to
    /// the last bit in both this and the twin.</summary>
    public void T(int qubit) => Walk(qubit, -1, 1, 0, 0, new Complex(Root, Root));

    /// <summary>diag(1, exp(i angle)) - the QFT's controlled rotations are
    /// this gate under a control.</summary>
    public void Phase(int qubit, double angle) =>
        Walk(qubit, -1, 1, 0, 0, Complex.FromPolarCoordinates(1, angle));

    public void Rx(int qubit, double angle)
    {
        double c = Math.Cos(angle / 2), s = Math.Sin(angle / 2);
        Walk(qubit, -1, c, new Complex(0, -s), new Complex(0, -s), c);
    }

    public void Ry(int qubit, double angle)
    {
        double c = Math.Cos(angle / 2), s = Math.Sin(angle / 2);
        Walk(qubit, -1, c, -s, s, c);
    }

    public void Rz(int qubit, double angle)
    {
        double c = Math.Cos(angle / 2), s = Math.Sin(angle / 2);
        Walk(qubit, -1, new Complex(c, -s), 0, 0, new Complex(c, s));
    }

    /// <summary>Controlled NOT: the gate that entangles. After H on the
    /// control, this is a Bell pair.</summary>
    public void Cnot(int control, int target) => ApplyControlled(control, target, 0, 1, 1, 0);

    /// <summary>Controlled Z. Symmetric in its two arguments, unlike its
    /// name.</summary>
    public void Cz(int control, int target) => ApplyControlled(control, target, 1, 0, 0, -1);

    /// <summary>Controlled phase, the QFT's workhorse.</summary>
    public void ControlledPhase(int control, int target, double angle) =>
        ApplyControlled(control, target, 1, 0, 0, Complex.FromPolarCoordinates(1, angle));

    /// <summary>Two controls and a flip: the reversible AND, and the reason a
    /// classical circuit can be run on this machine at all.</summary>
    public void Toffoli(int control1, int control2, int target)
    {
        Check(control1); Check(control2); Check(target);
        if (control1 == control2 || control1 == target || control2 == target)
            throw new ArgumentException("QuantumRegister: Toffoli needs three different qubits.");

        int b1 = 1 << control1, b2 = 1 << control2, bt = 1 << target;
        for (int i = 0; i < _a.Length; i++)
            if ((i & b1) != 0 && (i & b2) != 0 && (i & bt) == 0)
                (_a[i], _a[i | bt]) = (_a[i | bt], _a[i]);
    }

    /// <summary>Exchange two qubits. Done as an index permutation rather than
    /// three CNOTs: same state, no arithmetic, so it cannot drift.</summary>
    public void Swap(int p, int q)
    {
        Check(p); Check(q);
        if (p == q) return;

        int bp = 1 << p, bq = 1 << q;
        for (int i = 0; i < _a.Length; i++)
            if ((i & bp) != 0 && (i & bq) == 0)
            {
                int j = (i & ~bp) | bq;
                (_a[i], _a[j]) = (_a[j], _a[i]);
            }
    }

    /// <summary>Negate one basis amplitude: the marking half of an oracle. The
    /// classical answer is untouched - a sign is not a probability - which is
    /// why one call of it can still be worth 2^n classical calls.</summary>
    public void PhaseFlip(int index)
    {
        if (index < 0 || index >= _a.Length) throw new ArgumentOutOfRangeException(nameof(index));
        _a[index] = -_a[index];
    }

    /// <summary>Negate every amplitude whose basis index the predicate marks.
    /// The general phase oracle: Deutsch-Jozsa hands it a function, Grover a
    /// single index.</summary>
    public void PhaseOracle(Func<int, bool> marked)
    {
        ArgumentNullException.ThrowIfNull(marked);
        for (int i = 0; i < _a.Length; i++)
            if (marked(i)) _a[i] = -_a[i];
    }

    /// <summary>2|0><0| - I: keeps the all-zero amplitude and negates the
    /// rest. Written without the global sign so that H-all, this, H-all is
    /// exactly InvertAboutMean and not merely equal up to a phase.</summary>
    public void PhaseFlipZero()
    {
        for (int i = 1; i < _a.Length; i++) _a[i] = -_a[i];
    }

    /// <summary>Inversion about the mean, 2|s><s| - I: the amplitude that sits
    /// below the average comes back above it by as much. This is Grover's
    /// diffusion in one line; the register also builds it out of gates, and
    /// the two agree to 1e-14 (SelfTest).</summary>
    public void InvertAboutMean()
    {
        // Divided by a REAL length, and scaled by a real 2 below: both pick
        // the (Complex, double) operators, which touch the two components
        // separately. Complex-by-complex division would take a different
        // route through Smith's algorithm and part company with the twin in
        // the last bit.
        Complex mean = Complex.Zero;
        for (int i = 0; i < _a.Length; i++) mean += _a[i];
        mean /= (double)_a.Length;

        for (int i = 0; i < _a.Length; i++) _a[i] = 2.0 * mean - _a[i];
    }

    /// <summary>
    /// Relabel the basis states by a classical reversible function: the
    /// amplitude at i moves to map(i), untouched.
    ///
    /// This is what a classical oracle IS - a permutation of the labels - and
    /// period finding needs one that no small circuit expresses: |x>|y> going
    /// to |x>|y a^x mod N>. Compiling modular exponentiation into Toffoli
    /// gates is a real subject and a different one; it would put nothing on
    /// the screen that this does not, and it would cost the frame budget of
    /// every module that borrows it.
    ///
    /// The map must be a PERMUTATION and is checked to be one. A map that
    /// sends two labels to the same place is not reversible, would not be
    /// unitary, and would quietly destroy the norm - so it throws rather than
    /// summing the two amplitudes, which is what an unguarded version of this
    /// would do and would be very hard to see afterwards.
    /// </summary>
    public void Permute(Func<int, int> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var moved = new Complex[_a.Length];
        var taken = new bool[_a.Length];
        for (int i = 0; i < _a.Length; i++)
        {
            int j = map(i);
            if (j < 0 || j >= _a.Length)
                throw new ArgumentException($"QuantumRegister: the map sent {i} to {j}, outside the register.", nameof(map));
            if (taken[j])
                throw new ArgumentException($"QuantumRegister: the map sends two labels to {j}, so it is not reversible.", nameof(map));

            taken[j] = true;
            moved[j] = _a[i];
        }

        Array.Copy(moved, _a, _a.Length);
    }

    /// <summary>The length of the state vector. Unitary gates keep it at 1;
    /// the SelfTest holds it there through 1200 gates.</summary>
    public double Norm()
    {
        double s = 0;
        for (int i = 0; i < _a.Length; i++) s += Squared(_a[i]);
        return Math.Sqrt(s);
    }

    /// <summary>The exact probability of one basis state.</summary>
    public double Probability(int index)
    {
        if (index < 0 || index >= _a.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return Squared(_a[index]);
    }

    /// <summary>The exact probability that one qubit reads 1, the rest
    /// ignored.</summary>
    public double ProbabilityOfOne(int qubit)
    {
        Check(qubit);
        int bit = 1 << qubit;
        double s = 0;
        for (int i = 0; i < _a.Length; i++)
            if ((i & bit) != 0) s += Squared(_a[i]);
        return s;
    }

    /// <summary>One run of the machine: a basis state drawn from |psi|^2 by
    /// the shared counter hash, so run k of a seed is reproducible on its own
    /// and identical in the twin. The state is NOT collapsed - the modules
    /// take thousands of draws from one prepared state, which is what a real
    /// experiment does by preparing it again.</summary>
    public int Measure(ulong seed, long k)
    {
        double u = HashRandom.Uniform(seed, k), acc = 0;
        for (int i = 0; i < _a.Length; i++)
        {
            acc += Squared(_a[i]);
            if (u < acc) return i;
        }

        return _a.Length - 1;
    }

    /// <summary>Measure one qubit and collapse: the branch that did not happen
    /// is set to zero and what remains is renormalised. This one DOES change
    /// the state, because that is the whole content of teleportation and of
    /// error correction - the partner of a measured qubit is decided at the
    /// same instant, and the syndrome is read without reading the data.</summary>
    public int MeasureQubit(int qubit, ulong seed, long k)
    {
        double p1 = ProbabilityOfOne(qubit);
        bool one = HashRandom.Uniform(seed, k) < p1;
        double p = one ? p1 : 1 - p1;
        if (p <= 0) throw new InvalidOperationException("QuantumRegister: measuring an outcome of probability zero.");

        int bit = 1 << qubit;
        double scale = 1 / Math.Sqrt(p);
        for (int i = 0; i < _a.Length; i++)
            _a[i] = ((i & bit) != 0) == one ? _a[i] * scale : Complex.Zero;

        return one ? 1 : 0;
    }

    /// <summary>&lt;Z&gt; on one qubit: +1 if it certainly reads 0, -1 if it
    /// certainly reads 1.</summary>
    public double ExpectationZ(int qubit)
    {
        Check(qubit);
        int bit = 1 << qubit;
        double s = 0;
        for (int i = 0; i < _a.Length; i++) s += ((i & bit) != 0 ? -1 : 1) * Squared(_a[i]);
        return s;
    }

    /// <summary>&lt;ZZ&gt; on two qubits: +1 when they always agree, -1 when
    /// they always disagree, 0 when neither. For the other bases, turn them
    /// into Z first - H before ZZ measures XX, S then Z then H measures YY -
    /// on a Clone, since those rotations are gates like any other.</summary>
    public double CorrelationZZ(int p, int q)
    {
        Check(p); Check(q);
        int bp = 1 << p, bq = 1 << q;
        double s = 0;
        for (int i = 0; i < _a.Length; i++)
        {
            int sign = ((i & bp) != 0 ? -1 : 1) * ((i & bq) != 0 ? -1 : 1);
            s += sign * Squared(_a[i]);
        }

        return s;
    }

    /// <summary>The Bloch vector of one qubit, from its reduced density
    /// matrix. Its LENGTH is the interesting number: 1 while the qubit has a
    /// state of its own, 0 for either half of a Bell pair. That zero is
    /// entanglement stated as a measurement - each half alone is a fair coin,
    /// and everything the pair knows is in the correlation.</summary>
    public (double X, double Y, double Z) Bloch(int qubit)
    {
        Check(qubit);
        int bit = 1 << qubit;
        Complex r01 = Complex.Zero;

        for (int i = 0; i < _a.Length; i++)
        {
            if ((i & bit) == 0)
            {
                r01 += _a[i] * Complex.Conjugate(_a[i | bit]);
            }
        }

        return (2 * r01.Real, -2 * r01.Imaginary, ExpectationZ(qubit));
    }

    private static double Squared(Complex z) => z.Real * z.Real + z.Imaginary * z.Imaginary;

    private void Check(int qubit)
    {
        if (qubit < 0 || qubit >= Qubits)
        {
            throw new ArgumentOutOfRangeException(nameof(qubit), qubit, $"QuantumRegister: qubit 0 to {Qubits - 1}.");
        }
    }
}
