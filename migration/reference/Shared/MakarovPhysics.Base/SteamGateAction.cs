namespace MakarovPhysics.Base;

/// <summary>What the host must do after the ownership gate.</summary>
public enum SteamGateAction
{
    /// <summary>Dev build: no DRM, start normally.</summary>
    RunWithoutSteam,

    /// <summary>RestartAppIfNecessary handed the launch to Steam: exit
    /// SILENTLY (code 1) - Steam relaunches the app itself, and any dialog
    /// here would flash over that relaunch.</summary>
    ExitForSteamRestart,

    /// <summary>Init succeeded: run, pump callbacks, shut Steam down on exit.</summary>
    RunUnderSteam,

    /// <summary>Steam build without steam_api64.dll next to the exe. The
    /// classic three-second crack is deleting the DLL, so its absence gets
    /// the same closed door as a failed license check, not a free pass.</summary>
    FailMissingApi,

    /// <summary>SteamAPI init failed: no running client, no license on the
    /// account, or an SDK version mismatch.</summary>
    FailInit,
}
