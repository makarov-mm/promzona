namespace MakarovPhysics.Base;

/// <summary>
/// One reading of a challenge.
/// <paramref name="Detail"/> is the part that matters. A task that answers
/// only yes or no teaches nothing: the reader tries again with no idea which
/// way to move. Reporting the RESIDUAL in physical units, "1.8 m short", turns
/// the attempt into a measurement and the next one into a correction, which is
/// how an experiment is supposed to work.
/// </summary>
/// <param name="State">Met, missed, or still open.</param>
/// <param name="Progress">0..1, for showing how close the attempt came.</param>
/// <param name="Detail">The residual, in the module's own units.</param>
public readonly record struct ChallengeResult(ChallengeState State, double Progress, string Detail)
{
    public static ChallengeResult Pending(double progress, string detail) => new(ChallengeState.Pending, Clamp(progress), detail);
    public static ChallengeResult Achieved(string detail) => new(ChallengeState.Achieved, 1, detail);
    public static ChallengeResult Missed(double progress, string detail) => new(ChallengeState.Missed, Clamp(progress), detail);
    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
