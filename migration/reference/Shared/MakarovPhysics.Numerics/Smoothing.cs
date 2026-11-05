namespace MakarovPhysics.Numerics;

/// <summary>
/// Exponential smoothing that means the same thing at any frame rate.
///
/// The shape `x += (target - x) * k` is everywhere in the suite: auto-exposure
/// following a peak, a camera easing toward a new distance, a fling velocity
/// following the pointer. Written with a constant k it is a filter whose time
/// constant is measured in FRAMES, so the picture settles two and a half times
/// faster on a 144 Hz monitor than on a 60 Hz one - which is invisible in a
/// screenshot and obvious the moment two machines are put side by side.
///
/// The fix is one line of algebra. A filter that keeps a fraction (1 - k) of
/// its error per frame keeps exp(-dt/tau) of it per unit time, so the k that
/// belongs to THIS frame is 1 - exp(-dt/tau), with tau read off the rate the
/// value was originally tuned at.
///
/// The tuning stays where it was written - as "the k that looked right at
/// 60 Hz" - because that is the number a human actually chose, and asking
/// every call site to be re-expressed as a time constant would have meant
/// re-tuning nine of them by eye.
/// </summary>
public static class Smoothing
{
    /// <summary>The frame time the existing per-frame constants were tuned at.</summary>
    public const double ReferenceFrameTime = 1.0 / 60.0;

    /// <summary>
    /// The blend factor for a frame of <paramref name="dt"/> seconds that
    /// filters as strongly as <paramref name="blendAt60Hz"/> did per frame at
    /// 60 Hz.
    ///
    /// Degenerate inputs answer sensibly rather than throwing: this is called
    /// from render paths, and a NaN dt from a stalled frame must not take a
    /// module out. A blend of 1 (or more) means "no filtering", and stays 1.
    /// </summary>
    public static double Blend(double blendAt60Hz, double dt)
    {
        if (!(blendAt60Hz > 0)) return 0;
        if (blendAt60Hz >= 1) return 1;
        if (!double.IsFinite(dt) || dt <= 0) return 0;

        // tau from the reference rate, then the exact factor for this frame
        double tau = -ReferenceFrameTime / Math.Log(1 - blendAt60Hz);
        return 1 - Math.Exp(-dt / tau);
    }

    /// <summary>The same, in float, for the render paths that work there.</summary>
    public static float Blend(float blendAt60Hz, double dt) => (float)Blend((double)blendAt60Hz, dt);

    /// <summary>Move <paramref name="current"/> toward <paramref name="target"/>
    /// by the frame-rate-independent equivalent of a per-frame
    /// <paramref name="blendAt60Hz"/>.</summary>
    public static double Toward(double current, double target, double blendAt60Hz, double dt)
        => current + (target - current) * Blend(blendAt60Hz, dt);

    /// <inheritdoc cref="Toward(double,double,double,double)"/>
    public static float Toward(float current, float target, float blendAt60Hz, double dt)
        => current + (target - current) * Blend(blendAt60Hz, dt);

    /// <summary>
    /// A value that FADES rather than follows: `x *= 0.90` per frame is the
    /// same filter as a blend toward zero, and just as tied to the monitor.
    /// <paramref name="keptAt60Hz"/> is the factor the call site was written
    /// with - 0.90 for a flash that dies in a few frames, 0.985 for an
    /// auto-exposure peak that sags slowly.
    ///
    /// Added 28.08 with the two call sites framecheck could not see: it
    /// looked for `+= (target - x) * k` and a decay is spelled `x *= k`, so a
    /// flash lasting 0.16 s at 60 Hz and 0.07 s at 144 Hz passed the gate for
    /// months. The gate now looks for both shapes.
    /// </summary>
    public static double Decay(double value, double keptAt60Hz, double dt)
        => Toward(value, 0, 1 - keptAt60Hz, dt);

    /// <inheritdoc cref="Decay(double,double,double)"/>
    public static float Decay(float value, float keptAt60Hz, double dt)
        => Toward(value, 0f, 1 - keptAt60Hz, dt);

    /// <summary>
    /// The same, for a vector-valued quantity - a pointer velocity, a camera
    /// target, a smoothed direction.
    ///
    /// It exists because the first sweep through the call sites assumed every
    /// one of them was a scalar and broke the build on the one that was not
    /// (Sandbox's fling velocity). A filter whose input happens to be three
    /// numbers is the same filter; the blend is still one number, so it
    /// belongs here rather than being spelled out at the call site.
    /// </summary>
    public static System.Numerics.Vector3 Toward(
        System.Numerics.Vector3 current, System.Numerics.Vector3 target, float blendAt60Hz, double dt)
        => current + (target - current) * Blend(blendAt60Hz, dt);
}
