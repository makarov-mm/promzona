namespace MakarovPhysics.Base;

/// <summary>How a challenge stands right now.</summary>
public enum ChallengeState
{
    /// <summary>Not yet met, and still reachable.</summary>
    Pending,

    /// <summary>Met, and held long enough if the challenge asked for that.</summary>
    Achieved,

    /// <summary>This attempt cannot succeed anymore; the run has to be repeated.</summary>
    Missed
}
