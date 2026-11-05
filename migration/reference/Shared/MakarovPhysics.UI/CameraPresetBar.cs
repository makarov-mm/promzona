using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI;

/// <summary>
/// A row of buttons for a module's author cameras (visual audit, 02.09):
/// "Overview", "Close", "Instrument" - the labels come from the shell
/// strings under Camera.*, or from the module's own strings when a preset
/// key is not one of the three. The first preset is applied on build, so a
/// module that declares presets opens on its author's view; every button
/// re-fits the camera to the viewport's aspect through the callback.
/// </summary>
public static class CameraPresetBar
{
    /// <param name="loc">A lookup for the button labels (the module's Tr).</param>
    /// <param name="aspect">The module's current viewport aspect, asked at click time.</param>
    public static UiPanel Add(UiPanel panel, OrbitCamera camera, IReadOnlyList<CameraPreset> presets, Func<string, string> loc, Func<float> aspect, bool applyFirst = true)
        => AddCore(panel, camera, presets, loc, aspect, applyFirst, responsive: false);

    /// <summary>Opt-in vertical camera choices in narrow/high-DPI panels.</summary>
    public static UiPanel AddResponsive(UiPanel panel, OrbitCamera camera, IReadOnlyList<CameraPreset> presets, Func<string, string> loc, Func<float> aspect, bool applyFirst = true)
        => AddCore(panel, camera, presets, loc, aspect, applyFirst, responsive: true);

    private sealed class ResponsiveRow : UiPanel
    {
        public override float PreferredHeight(float width)
        {
            Horizontal = width >= Theme.FontSize * 24f;
            return base.PreferredHeight(width);
        }

        public override void Layout()
        {
            Horizontal = Rect.W >= Theme.FontSize * 24f;
            base.Layout();
        }
    }

    private static UiPanel AddCore(UiPanel panel, OrbitCamera camera, IReadOnlyList<CameraPreset> presets, Func<string, string> loc, Func<float> aspect, bool applyFirst, bool responsive)
    {
        UiPanel row = responsive ? new ResponsiveRow() : new UiPanel();
        row.DrawBackground = false;
        row.Horizontal = true;
        panel.Add(row);
        List<UiButton> buttons = [with(presets.Count)];

        foreach (CameraPreset preset in presets)
        {
            UiButton button = row.Add(new UiButton(loc(preset.Key)));
            CameraPreset p1 = preset;

            button.Click += () =>
            {
                camera.Apply(p1, aspect());

                foreach (UiButton other in buttons)
                {
                    other.Selected = ReferenceEquals(other, button);
                }
            };

            buttons.Add(button);
        }

        if (applyFirst && presets.Count > 0)
        {
            camera.Apply(presets[0], aspect());
            buttons[0].Selected = true;
        }

        return row;
    }
}
