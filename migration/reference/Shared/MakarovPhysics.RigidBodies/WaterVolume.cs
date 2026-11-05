using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>
/// A box of water sitting on the floor. Bodies below the surface get Archimedes buoyancy
/// plus heavy drag. Because we keep each body's density, the buoyant acceleration works
/// out to g * submergedFraction * (waterDensity / bodyDensity): lighter-than-water bodies
/// bob up, denser ones sink - no per-shape volume bookkeeping needed.
/// </summary>
public sealed class WaterVolume
{
    public Vector3 Center;       // center of the XZ footprint
    private float _halfX = 6f, _halfZ = 6f;
    private float _density = 1.65f;
    private float _linearDrag = 2.5f;
    private float _waveAmplitude = 0.22f;

    /// <summary>Half extents of the volume; finite and positive.</summary>
    public float HalfX { get => _halfX; set { if (float.IsFinite(value) && value > 0f) _halfX = value; } }
    public float HalfZ { get => _halfZ; set { if (float.IsFinite(value) && value > 0f) _halfZ = value; } }
    public float SurfaceY = 1.5f;
    /// <summary>Fluid density; finite and positive.</summary>
    public float Density { get => _density; set { if (float.IsFinite(value) && value > 0f) _density = value; } }

    /// <summary>Linear drag coefficient; finite and non-negative.</summary>
    public float LinearDrag { get => _linearDrag; set { if (float.IsFinite(value) && value >= 0f) _linearDrag = value; } }

    /// <summary>Standing-wave amplitude; finite and non-negative.</summary>
    public float WaveAmplitude { get => _waveAmplitude; set { if (float.IsFinite(value) && value >= 0f) _waveAmplitude = value; } }
    /// <summary>Internal ripple clock, advanced by Apply (
    /// external writes could desynchronize ripple ages or inject NaN).</summary>
    public float Time { get; private set; }

    // ---- object-driven ripples (expanding rings spawned when bodies hit/cross the surface) ----
    private struct Ripple { public float X, Z, Start, Strength; }
    private readonly List<Ripple> _ripples = new(32);
    public const int MAX_RIPPLES = 24;
    private const float RippleLife = 1.6f;       // seconds before a ring fades out
    private const float RippleRingSpeed = 3.2f;  // how fast the ring radius expands (units/s)
    private const float RippleWaveK = 6.0f;      // spatial frequency of the ring
    private const float RipplePhaseSpeed = 9.0f;

    /// <summary>Spawn an expanding ripple centered at (x,z). Strength scales its height.</summary>
    public void Disturb(float x, float z, float strength)
    {
        // Math.Clamp(NaN) is NaN - one poisoned ripple made
        // RippleHeightAt and SurfaceAt NaN and broke buoyancy for EVERY body
        // in the volume. Non-finite input is rejected outright.
        if (!float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(strength)) return;

        if (_ripples.Count >= MAX_RIPPLES) _ripples.RemoveAt(0); // drop the oldest

        _ripples.Add(new Ripple
        {
            X = x,
            Z = z,
            Start = Time,
            Strength = Math.Clamp(strength, 0.02f, 0.6f)
        });
    }

    /// <summary>Fills dst with active ripples as (x, z, age, strength) quads; returns the count written.</summary>
    public int FillRipples(float[] dst, int maxQuads)
    {
        // Never trust maxQuads over the actual buffer capacity
        int capacity = Math.Min(maxQuads, dst.Length / 4);
        int n = 0;

        foreach (Ripple rp in _ripples)
        {
            if (n >= capacity) break;
            float age = Time - rp.Start;
            if (age is < 0f or > RippleLife) continue;
            dst[n * 4 + 0] = rp.X;
            dst[n * 4 + 1] = rp.Z;
            dst[n * 4 + 2] = age;
            dst[n * 4 + 3] = rp.Strength;
            n++;
        }
        return n;
    }

    public bool ContainsColumn(Vector3 p)
    {
        return MathF.Abs(p.X - Center.X) <= HalfX && MathF.Abs(p.Z - Center.Z) <= HalfZ;
    }

    public void Step(float h)
    {
        Time += h;

        // prune dead ripples
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            if (Time - _ripples[i].Start > RippleLife)
            {
                _ripples.RemoveAt(i);
            }
        }
    }

    private float RippleHeightAt(float x, float z)
    {
        if (_ripples.Count == 0) return 0f;
        float sum = 0f;

        foreach (var rp in _ripples)
        {
            float age = Time - rp.Start;
            if (age < 0f || age > RippleLife) continue;
            float dx = x - rp.X, dz = z - rp.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            float fade = 1f - age / RippleLife;                       // overall decay
            float ringR = age * RippleRingSpeed;                      // current ring radius
            float band = MathF.Exp(-2.5f * MathF.Abs(dist - ringR));  // localize at the ring
            sum += rp.Strength * fade * band * MathF.Sin(dist * RippleWaveK - age * RipplePhaseSpeed);
        }

        return sum;
    }

    public float SurfaceAt(float x, float z)
    {
        float w1 = MathF.Sin(x * 1.15f + Time * 1.35f) * 0.55f;
        float w2 = MathF.Sin(z * 1.75f + Time * 1.85f) * 0.30f;
        float w3 = MathF.Sin((x + z) * 0.80f + Time * 1.10f) * 0.25f;
        return SurfaceY + WaveAmplitude * (w1 + w2 + w3) + RippleHeightAt(x, z);
    }

    /// <summary>Apply buoyancy/drag to a body. Returns true only when the body
    /// is actually submerged (at least partially) - the caller uses this to wake
    /// Sleeping bodies the water is pushing (previously buoyancy could
    /// change a sleeping body's velocity while it stayed frozen).</summary>
    public bool Apply(RigidBody b, Vector3 gravity, float h)
    {
        if (!ContainsColumn(b.Position)) return false;

        float r = b.BoundingRadius;
        float surfaceY = SurfaceAt(b.Position.X, b.Position.Z);

        // spawn a ripple when a body crosses / churns the surface (throttled per body)
        if (b.RippleCooldown <= 0f && MathF.Abs(b.Position.Y - surfaceY) < r + 0.15f)
        {
            float horizontal = MathF.Sqrt(b.Velocity.X * b.Velocity.X + b.Velocity.Z * b.Velocity.Z);
            float speed = MathF.Abs(b.Velocity.Y) + 0.35f * horizontal;

            if (speed > 1.0f)
            {
                Disturb(b.Position.X, b.Position.Z, 0.05f * MathF.Min(speed, 8f) * MathF.Min(2f * r, 1.5f));
                b.RippleCooldown = 0.10f;
            }
        }

        float bottom = b.Position.Y - r;
        if (bottom >= surfaceY) return false; // fully above the surface

        // crude submerged fraction from the bounding sphere - smooth enough for bobbing
        float submerged = Math.Clamp((surfaceY - bottom) / (2f * r), 0f, 1f);

        float g = gravity.Length();
        if (g > 1e-4f)
        {
            float buoyAccel = g * submerged * (Density / MathF.Max(b.Density, 1e-3f));
            b.Velocity += -Vector3.Normalize(gravity) * (buoyAccel * h);
        }

        // drag scales with how deep we are; kills the bounce so things settle at the line
        float k = 1f / (1f + LinearDrag * submerged * h);
        b.Velocity *= k;
        b.AngularVelocity *= k;

        // Gameplay wetness: water volumes are not just visual/buoyancy. They also feed
        // the interaction matrix: wet bodies resist fire and conduct electricity better.
        b.Wetness = Math.Clamp(b.Wetness + submerged * h * 3.5f, 0f, 1f);

        if (submerged > 0.25f && b.Burning)
        {
            // the Wrecksmith gameplay link to RagdollBone is not ported here:
            // fire on ragdoll bones is the job of the layer above the engine (via Tag)
            b.Burning = false;
            b.Temperature = MathF.Min(b.Temperature, 80f);
        }

        return true;
    }
}
