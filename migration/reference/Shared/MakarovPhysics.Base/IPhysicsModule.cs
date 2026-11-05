namespace MakarovPhysics.Base;

/// <summary>
/// The experiment contract. A module owns neither the window nor the GL context:
/// the host calls these methods in the frame loop with the GL context already current.
/// </summary>
public interface IPhysicsModule : IDisposable
{
    ModuleInfo Info { get; }
    SimulationState State { get; }

    /// <summary>Which toolbar controls make sense for this module.</summary>
    ModuleCapabilities Capabilities { get; }

    /// <summary>Backdrop the host draws behind the scene.</summary>
    SceneBackground Background { get; }

    /// <summary>
    /// Logical pixels of the screen's LEFT edge this module occupies with its
    /// own UI (0 for almost everyone). The host shifts the bottom-left
    /// description card right by this much, so the card and the module's own
    /// strip never stack - the Sandbox tools column was the case that
    /// motivated it (REVIEW-PRERELEASE, P3).
    /// </summary>
    float OverlayLeftInset { get; }

    /// <summary>Create GL resources and the initial state (context is current).</summary>
    void Initialize(IModuleContext context);

    void Resize(int width, int height);

    /// <summary>Advance the physics by the real frame time (seconds).</summary>
    void Update(double realDt, InputState input);

    /// <summary>Render the experiment scene (no UI - the host draws it on top).</summary>
    void Render();

    /// <summary>
    /// Build the parameter panel. uiRoot is a MakarovPhysics.UI.UiPanel
    /// (typed as object so Base does not depend on UI).
    /// </summary>
    void BuildUi(object uiRoot);

    void Start();
    void Pause();
    void Stop();

    /// <summary>Rebuild the initial scene and run - exactly one ResetCore
    /// (what parameter callbacks and hosts use instead of Stop+Start).</summary>
    void Restart();
    void StepOnce();
    void Reset();
}
