using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// The instrument tray of one module: owns the instruments, keeps their view of
/// the camera current and builds the toggles that stow and unstow them.
///
/// A module wires it up in three lines: create the set, Add the instruments it
/// wants, call Sync once per frame with the matrices it renders with. The
/// instruments are added to the UI root, which the host clears when the screen
/// changes, so there is nothing to unwind on the way out.
/// </summary>
public sealed class InstrumentSet
{
    private readonly UiContext? _ui;
    private readonly List<InstrumentWidget> _items = [];

    /// <param name="ui">
    /// Host UI context, from IModuleContext.GetService. null in SelfTest, where
    /// there is no UI at all: the set then still tracks its instruments so the
    /// module code under test runs unchanged.
    /// </param>
    public InstrumentSet(UiContext? ui) => _ui = ui;

    public IReadOnlyList<InstrumentWidget> Items => _items;

    /// <summary>
    /// Registers an instrument and stows it. Instruments start hidden on
    /// purpose: the scene must open clean, and the user reaches for a ruler
    /// when a question needs one.
    /// </summary>
    public T Add<T>(T instrument, string title) where T : InstrumentWidget
    {
        ArgumentNullException.ThrowIfNull(instrument);

        instrument.Title = title;
        instrument.Visible = false;
        _items.Add(instrument);

        if (_ui is not null)
        {
            _ui.Root.Add(instrument);
            instrument.PlaceDefault(_ui.Width, _ui.Height);
        }

        return instrument;
    }

    /// <summary>
    /// Hands this frame's camera to every instrument. Modules call it from
    /// Update with the same matrices Render is about to use, so a reading can
    /// never lag the picture by a frame while the camera orbits.
    /// </summary>
    public void Sync(SceneProjection projection, ScenePlane plane)
    {
        foreach (InstrumentWidget instrument in _items)
        {
            SceneProjection previous = instrument.Projection;
            instrument.Projection = projection;
            instrument.Plane = plane;
            if (instrument.Visible && (previous.ViewportWidth != projection.ViewportWidth
                || previous.ViewportHeight != projection.ViewportHeight || previous.UiScale != projection.UiScale))
                RestorePosition(instrument);
            (instrument as StopwatchInstrument)?.Tick();
        }
    }

    /// <summary>
    /// The same, for a module that maps the screen to its own coordinates
    /// directly instead of through a camera. A schematic sheet has a pan and a
    /// zoom and no camera at all.
    /// </summary>
    public void Sync(Func<float, float, Vec3?> screenToWorld)
    {
        ArgumentNullException.ThrowIfNull(screenToWorld);

        foreach (InstrumentWidget instrument in _items)
        {
            instrument.ScreenToWorld = screenToWorld;
            (instrument as StopwatchInstrument)?.Tick();
        }
    }

    /// <summary>
    /// Convenience overload for modules that render with a view and a
    /// projection matrix and want the shared plane conventions.
    /// </summary>
    public void Sync(Mat4 view, Mat4 projection, IModuleContext context, ScenePlane plane)
    {
        ArgumentNullException.ThrowIfNull(context);
        float scale = _ui?.Scale ?? 1f;
        Sync(new SceneProjection(view, projection, context.ViewportWidth, context.ViewportHeight, scale), plane);
    }

    /// <summary>Adds a checkbox per instrument to a parameter panel.</summary>
    public void BuildToggles(UiPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        foreach (InstrumentWidget instrument in _items)
        {
            InstrumentWidget captured = instrument;
            Checkbox toggle = panel.Add(new Checkbox(captured.Title, captured.Visible));

            toggle.Changed += visible =>
            {
                captured.Visible = visible;

                // Re-place on the way in: the window may have been resized while
                // the instrument was stowed, and an instrument that came back
                // outside the viewport would be unreachable.
                if (visible) RestorePosition(captured);
            };
        }
    }

    /// <summary>Stows every instrument (used by Reset paths).</summary>
    public void StowAll()
    {
        foreach (InstrumentWidget instrument in _items)
        {
            instrument.Visible = false;
        }
    }

    private void RestorePosition(InstrumentWidget instrument)
    {
        if (_ui is null) return;
        // The camera viewport excludes the parameter panel. Merely touching
        // the full window is not enough: both handles must remain reachable.
        SceneProjection projection = instrument.Projection;
        float width = projection.IsValid ? projection.LogicalWidth : _ui.Width;
        float height = projection.IsValid ? projection.LogicalHeight : _ui.Height;
        UiRect rect = instrument.Rect;
        if (width > 0f && height > 0f && (rect.X < 0f || rect.Y < 0f || rect.Right > width || rect.Bottom > height))
            instrument.PlaceDefault(width, height);
    }
}
