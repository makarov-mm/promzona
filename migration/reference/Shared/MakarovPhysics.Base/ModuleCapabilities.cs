namespace MakarovPhysics.Base;

/// <summary>
/// What the host toolbar should offer for a module. Static visualizations
/// (field-line pictures, geometric constructions) drop the time controls.
/// </summary>
[Flags]
public enum ModuleCapabilities
{
    None = 0,
    /// <summary>Start / Pause / Stop buttons and the Space hotkey.</summary>
    RunPause = 1 << 0,
    /// <summary>The single-step button and the S hotkey.</summary>
    SingleStep = 1 << 1,
    /// <summary>The simulation-time / timescale readout line.</summary>
    SimulationTime = 1 << 2,
    /// <summary>Run the moment the screen opens. Modules deliberately open
    /// paused so the user reads the initial scene and presses Start; the rare
    /// exceptions are pure renderers whose whole point is the live picture
    /// (the ray-traced black hole) - they carry this flag, usually WITHOUT
    /// RunPause, so they can never be left frozen.</summary>
    AutoStart = 1 << 3,

    /// <summary>
    /// Drop the toolbar's Reset button. A module that rebuilds its scene from
    /// its own controls - a picker that loads a different subject - has
    /// nothing left for Reset to do, and offering it anyway gives the same
    /// action two names in one panel.
    ///
    /// An opt-OUT rather than a positive Reset flag on purpose: fifty-six
    /// modules spell their capabilities out by hand rather than taking All,
    /// and a flag they would have to add would silently take the button away
    /// from every one of them the moment it was introduced.
    /// </summary>
    NoReset = 1 << 4,

    /// <summary>The module supplies its own launch/run controls instead of the generic transport.</summary>
    CustomRunControls = 1 << 5,

    All = RunPause | SingleStep | SimulationTime,
}
