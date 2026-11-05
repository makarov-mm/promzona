namespace MakarovPhysics.Base;

/// <summary>Result codes of SteamAPI initialization (mirror of
/// ESteamAPIInitResult in the Steamworks SDK).</summary>
public enum SteamInitResult
{
    Ok = 0,
    FailedGeneric = 1,
    NoSteamClient = 2,
    VersionMismatch = 3,
}
