using System.Numerics;

namespace MakarovPhysics.RigidBodies;
/// <summary>Equal solid spheres: 3D impulses, spin and Coulomb friction.
/// A stamped uniform grid replaces the sandbox's general compound broad phase.
/// Position projection makes dense piles affordable; this is not a calibrated
/// soft-grain DEM and contacts have no elastic/history spring.</summary>
public sealed class GranularWorld
{
    public Vector3[] Positions { get; }
    public Vector3[] Velocities { get; }
    public Vector3[] Spins { get; }
    public float Radius { get; }
    public float Friction { get; set; } = .35f;
    public float Restitution { get; set; } = .08f;
    public float RollingFriction { get; set; } = .025f;
    public int Iterations { get; set; } = 8;
    public long PairCandidates { get; private set; }
    public int Contacts { get; private set; }

    private readonly int[] _heads, _stamps, _next, _cell, _offsets;
    private readonly int _side;
    private readonly float _inverseCell, _extent;
    private int _generation;
    private readonly Vector3[] _previous, _wallImpulse, _wallVelocity, _wallFriction, _pairFriction;
    private readonly int[] _pairA, _pairB;
    private readonly float[] _normalImpulse, _bounce;
    private int _pairCount;
    private bool _projecting;
    private float _step;
    public GranularWorld(int count, float radius, float extent = 2.15f)
    {
        if (count < 1 || count > 20000 || !float.IsFinite(radius) || radius <= 0 || extent <= radius)
            throw new ArgumentOutOfRangeException(nameof(count));
        Radius = radius;
        _extent = extent;
        _inverseCell = 1 / (2.2f * radius);
        _side = (int)MathF.Ceiling(2 * extent * _inverseCell) + 3;
        int cells = checked(_side * _side * _side);
        if (cells > 4000000)
            throw new ArgumentOutOfRangeException(nameof(radius));
        _heads = new int[cells];
        _stamps = new int[cells];
        _next = new int[count];
        _cell = new int[count];
        var offsets = new List<int>
        {
            0
        };
        for (int z = 0; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (z > 0 || y > 0 || (y == 0 && x > 0))
                        offsets.Add((z * _side + y) * _side + x);
        _offsets = offsets.ToArray();
        Positions = new Vector3[count];
        Velocities = new Vector3[count];
        Spins = new Vector3[count];
        _previous = new Vector3[count];
        _wallImpulse = new Vector3[count];
        _wallVelocity = new Vector3[count];
        _wallFriction = new Vector3[count];
        _pairA = new int[count * 32];
        _pairB = new int[count * 32];
        _normalImpulse = new float[count * 32];
        _bounce = new float[count * 32];
        _pairFriction = new Vector3[count * 32];
    }

    private int Cell(float x) => Math.Clamp((int)((x + _extent) * _inverseCell) + 1, 1, _side - 2);
    private void BuildGrid()
    {
        if (_generation == int.MaxValue)
        {
            Array.Clear(_stamps);
            _generation = 0;
        }

        _generation++;
        for (int i = 0; i < Positions.Length; i++)
        {
            var p = Positions[i];
            int key = (Cell(p.Z) * _side + Cell(p.Y)) * _side + Cell(p.X);
            _cell[i] = key;
            if (_stamps[key] != _generation)
            {
                _stamps[key] = _generation;
                _heads[key] = -1;
            }

            _next[i] = _heads[key];
            _heads[key] = i;
        }
    }

    public void Step(float dt, Vector3 gravity, Action<int>? constrain = null)
    {
        if (!float.IsFinite(dt) || dt < 0 || dt > 1f / 120 + 1e-7f)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (dt == 0)
            return;
        PairCandidates = 0;
        Contacts = 0;
        _step = dt;
        _projecting = true;
        Array.Clear(_wallImpulse);
        Array.Clear(_wallFriction);
        for (int i = 0; i < Positions.Length; i++)
        {
            _previous[i] = Positions[i];
            Velocities[i] += gravity * dt;
            Positions[i] += Velocities[i] * dt;
            constrain?.Invoke(i);
        }

        // Cache a contact neighbourhood once per substep. A small skin catches
        // pairs that come into contact during projection; eight cheap sweeps
        // then replace repeated broad-phase searches.
        BuildGrid();
        _pairCount = 0;
        float reach2 = 2.2f * Radius * 2.2f * Radius;
        for (int i = 0; i < Positions.Length; i++)
        {
            // Visit each unordered cell pair once. Cached cell membership
            // belongs to this sweep; the grid is rebuilt after projection.
            foreach (int offset in _offsets)
            {
                int key = _cell[i] + offset;
                if (_stamps[key] != _generation)
                    continue;
                for (int j = _heads[key]; j >= 0; j = _next[j])
                {
                    if (offset == 0 && j <= i)
                        continue;
                    PairCandidates++;
                    if (Vector3.DistanceSquared(Positions[i], Positions[j]) >= reach2)
                        continue;
                    if (_pairCount == _pairA.Length)
                        throw new InvalidOperationException("Granular contact budget exceeded");
                    int k = _pairCount++;
                    _pairA[k] = i;
                    _pairB[k] = j;
                    _normalImpulse[k] = 0;
                    _pairFriction[k] = Vector3.Zero;
                    var delta = Positions[j] - Positions[i];
                    float length = delta.Length();
                    float vn = Vector3.Dot(Velocities[j] - Velocities[i], length > 1e-8f ? delta / length : Vector3.UnitY);
                    _bounce[k] = vn < -.25f ? -Restitution * vn : 0;
                }
            }
        }

        for (int pass = 0; pass < Iterations; pass++)
        {
            for (int k = 0; k < _pairCount; k++)
            {
                int a = _pairA[k], b = _pairB[k];
                if (!SphereContacts.TryContact(Positions[a], Radius, Positions[b], Radius, 0, out var normal, out _, out float depth))
                    continue;
                float correction = Math.Max(0, depth - Radius * .0005f) * .49f;
                Positions[a] -= normal * correction;
                Positions[b] += normal * correction;
                _normalImpulse[k] += correction / dt;
                Contacts++;
            }

            // Finish with the vessel constraint: the last pair correction
            // must not leave a grain on the outside of the glass.
            if (constrain is not null)
                for (int i = 0; i < Positions.Length; i++)
                    constrain(i);
        }

        _projecting = false;
        // Position changes ARE contact impulses. Keeping the old free-fall
        // velocity here allowed a dense pile to compress on every next step.
        for (int i = 0; i < Positions.Length; i++)
            Velocities[i] = (Positions[i] - _previous[i]) / dt;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < _pairCount; k++)
                SolveCachedVelocity(k);
            for (int i = 0; i < Positions.Length; i++)
                SolveWallVelocity(i);
        }

        for (int k = 0; k < _pairCount; k++)
        {
            DampRolling(_pairA[k], _normalImpulse[k]);
            DampRolling(_pairB[k], _normalImpulse[k]);
        }

        for (int i = 0; i < Positions.Length; i++)
            DampRolling(i, _wallImpulse[i].Length());
    }

    private void SolveCachedVelocity(int k)
    {
        int a = _pairA[k], b = _pairB[k];
        if (!SphereContacts.TryContact(Positions[a], Radius, Positions[b], Radius, .2f * Radius, out var n, out _, out float depth))
            return;
        if (_normalImpulse[k] <= 0 && depth < 0)
            return;
        var relative = Velocities[b] - Velocities[a] - Radius * Vector3.Cross(Spins[a] + Spins[b], n);
        float vn = Vector3.Dot(relative, n), jn = Math.Max(0, (_bounce[k] - vn) * .5f);
        _normalImpulse[k] += jn;
        var candidate = _pairFriction[k] - (relative - vn * n) / 7;
        candidate -= n * Vector3.Dot(candidate, n);
        float limit = Friction * _normalImpulse[k], length = candidate.Length();
        if (length > limit && length > 1e-8f)
            candidate *= limit / length;
        var jt = candidate - _pairFriction[k];
        _pairFriction[k] = candidate;
        var impulse = jn * n + jt;
        Velocities[a] -= impulse;
        Velocities[b] += impulse;
        var torque = (2.5f / Radius) * Vector3.Cross(n, jt);
        Spins[a] -= torque;
        Spins[b] -= torque;
    }

    private void SolveWallVelocity(int i)
    {
        float force = _wallImpulse[i].Length();
        if (force < 1e-8f)
            return;
        var normal = _wallImpulse[i] / force;
        var relative = Velocities[i] + Radius * Vector3.Cross(Spins[i], normal) - _wallVelocity[i];
        float vn = Vector3.Dot(relative, normal), jn = Math.Max(0, vn);
        var candidate = _wallFriction[i] + (relative - vn * normal) / 3.5f;
        candidate -= normal * Vector3.Dot(candidate, normal);
        float limit = Friction * (force + jn), length = candidate.Length();
        if (length > limit && length > 1e-8f)
            candidate *= limit / length;
        var jt = candidate - _wallFriction[i];
        _wallFriction[i] = candidate;
        Velocities[i] -= normal * jn + jt;
        Spins[i] -= (2.5f / Radius) * Vector3.Cross(normal, jt);
    }

    public void ResolvePair(int a, int b)
    {
        if (!SphereContacts.TryContact(Positions[a], Radius, Positions[b], Radius, 0, out var n, out _, out float depth))
            return;
        Contacts++;
        var correction = n * (Math.Max(0, depth - Radius * .0005f) * .49f);
        Positions[a] -= correction;
        Positions[b] += correction;
        var relative = Velocities[b] - Velocities[a] - Radius * Vector3.Cross(Spins[a] + Spins[b], n);
        float vn = Vector3.Dot(relative, n);
        if (vn >= 0)
            return;
        float jn = -(1 + (vn < -.25f ? Restitution : 0)) * vn * .5f;
        var tangent = relative - vn * n;
        float speed = tangent.Length();
        // 1/m + 1/m + 2 r²/I = 7/m for solid spheres with I=2mr²/5.
        var jt = speed > 1e-8f ? -tangent * (Math.Min(speed / 7, Friction * jn) / speed) : Vector3.Zero;
        var impulse = n * jn + jt;
        Velocities[a] -= impulse;
        Velocities[b] += impulse;
        var torque = (2.5f / Radius) * Vector3.Cross(n, jt);
        Spins[a] -= torque;
        Spins[b] -= torque;
        DampRolling(a, jn);
        DampRolling(b, jn);
    }

    public void ProjectWall(int i, Vector3 outward, float penetration, Vector3 wallVelocity)
    {
        if (penetration <= 0)
            return;
        Positions[i] -= outward * penetration;
        if (_projecting)
        {
            _wallImpulse[i] += outward * (penetration / _step);
            _wallVelocity[i] = wallVelocity;
            return;
        }

        var relative = Velocities[i] + Radius * Vector3.Cross(Spins[i], outward) - wallVelocity;
        float vn = Vector3.Dot(relative, outward);
        if (vn <= 0)
            return;
        float jn = (1 + (vn > .25f ? Restitution : 0)) * vn;
        var tangent = relative - vn * outward;
        float speed = tangent.Length();
        var jt = speed > 1e-8f ? tangent * (Math.Min(speed / 3.5f, Friction * jn) / speed) : Vector3.Zero;
        Velocities[i] -= outward * jn + jt;
        Spins[i] -= (2.5f / Radius) * Vector3.Cross(outward, jt);
        DampRolling(i, jn);
    }

    private void DampRolling(int i, float normalImpulse)
    {
        float speed = Spins[i].Length();
        if (speed > 1e-8f)
            Spins[i] *= Math.Max(0, 1 - 2.5f * RollingFriction * normalImpulse / (Radius * speed));
    }
}
