namespace MakarovPhysics.Base;

/// <summary>
/// Ownership gate for a Steam release, see Docs/STEAM-DRM.md. Product-neutral:
/// the AppID arrives from the host, which got it from its product manifest.
///
/// This class is the DRIVER, not just a truth table: the host hands it the
/// native probes as delegates and this method decides both the ORDER and the
/// LAZINESS of the calls. That's the part worth pinning in SelfTest - calling
/// SteamAPI_Init after RestartAppIfNecessary already scheduled a relaunch is
/// exactly the kind of bug that works on the dev machine (where restart never
/// triggers) and misbehaves only on a buyer's first launch. A separate
/// handwritten sequence in the host next to a tested table here would let
/// the two drift; with the driver shape there is one sequence, and it is the
/// tested one.
///
/// Deliberately NOT here: any anti-tamper beyond the gate. A local check can
/// always be patched out (STEAM-DRM.md, the section on the limit in principle); the goal is
/// closing casual copy-the-folder piracy, not an arms race.
/// </summary>
public static class SteamGate
{
    // The AppID used to live here as a constant, and with it this file - the
    // engine - knew the name and the Steam identity of one particular product.
    // Docs/PRODUCTS.md claimed Shared knows nothing about products; the claim
    // was stronger than the check, and the analysis of 30.08 said so.
    //
    // It is gone rather than renamed. The number now has exactly one home,
    // Products/Physics/Product.xml, where the shell reads it from and where
    // docscheck pins the steampipe scripts against it. The gate below is
    // generic: it is told an id, and never knows whose.

    /// <param name="steamBuild">Compile-time flag of the host (STEAM_BUILD).</param>
    /// <param name="apiPresent">Is steam_api64.dll next to the exe.</param>
    /// <param name="restartRequested">SteamAPI_RestartAppIfNecessary(AppId).</param>
    /// <param name="init">SteamAPI init - invoked ONLY when the launch was
    /// not handed over to Steam.</param>
    public static SteamGateAction Run(bool steamBuild, Func<bool> apiPresent, Func<bool> restartRequested, Func<SteamInitResult> init)
    {
        if (!steamBuild) return SteamGateAction.RunWithoutSteam;
        if (!apiPresent()) return SteamGateAction.FailMissingApi;
        if (restartRequested()) return SteamGateAction.ExitForSteamRestart;

        return init() == SteamInitResult.Ok
            ? SteamGateAction.RunUnderSteam
            : SteamGateAction.FailInit;
    }
}
