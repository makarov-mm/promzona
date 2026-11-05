namespace MakarovPhysics.Numerics;

/// <summary>
/// A counter-based random source: Uniform(seed, k) is a pure function of the
/// seed and the index k, so the k-th draw of a run can be produced without
/// producing the k − 1 before it, two runtimes produce the same bits, and a
/// Python twin can reproduce every sample to the last bit. The mixer is
/// splitmix64's finaliser on seed + (k + 1)·φ, and the double is the top 53
/// bits offset by half a unit, so it never returns 0 or 1 - Log and
/// Box-Muller stay finite.
///
/// Shared by the modules that build a sample from (seed, index) - the
/// statistics family (CentralLimit, LawOfLargeNumbers, ConfidenceInterval,
/// HypothesisTest), the astronomy piles and the mesh cutters. The value for
/// a given (seed, k) is pinned in SelfTest: changing this function changes
/// every anchor those modules carry.
/// </summary>
public static class HashRandom
{
    /// <summary>Uniform on (0, 1), a pure function of (seed, k).</summary>
    public static double Uniform(ulong seed, long k)
    {
        unchecked
        {
            ulong x = seed + (ulong)(k + 1) * 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return ((x >> 11) + 0.5) / 9007199254740992.0;
        }
    }

    /// <summary>A standard normal by Box-Muller from two hashed uniforms: the
    /// second uses the seed with its bits flipped by a constant, so the pair
    /// is independent for every k.</summary>
    public static double Normal(ulong seed, long k)
    {
        double u = Uniform(seed, k), v = Uniform(seed ^ 0x5A5A5A5AUL, k);
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    /// <summary>An exponential of unit mean.</summary>
    public static double Exponential(ulong seed, long k) => -Math.Log(Uniform(seed, k));
}
