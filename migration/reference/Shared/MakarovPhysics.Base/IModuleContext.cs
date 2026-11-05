namespace MakarovPhysics.Base;

/// <summary>
/// Everything the host hands to a module. Base has no references, so upper-layer
/// services (UI, charts) are reached via GetService.
/// </summary>
public interface IModuleContext
{
    AppPaths Shared { get; }

    /// <summary>
    /// Translations: shared keys ("Toolbar.Start") plus module keys under the
    /// "Module." prefix ("Module.Params.Length"). See Languages/.
    /// </summary>
    Localization Loc { get; }

    /// <summary>Current size of the module render area, in pixels.</summary>
    int ViewportWidth { get; }
    int ViewportHeight { get; }

    /// <summary>
    /// Host services: UiContext, chart factory, etc.
    /// Returns null when the service is unavailable (e.g. in SelfTest).
    /// </summary>
    T? GetService<T>() where T : class;
}
