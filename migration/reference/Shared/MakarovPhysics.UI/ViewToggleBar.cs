using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>One layer a module can hide: its key, its state, and where the
/// state goes when the reader clicks.</summary>
/// <param name="Key">A <see cref="ViewLayers"/> constant, or the module's own
/// key for something the vocabulary does not name.</param>
public readonly record struct ViewToggle(string Key, bool Value, Action<bool> Changed);

/// <summary>
/// The switches for what the scene draws (visual audit, 02.09), built the same
/// way in every module: same words, same order, same place in the panel.
///
/// Two per row rather than four: at panel width four labels either wrap or
/// shrink the checkbox out of reach, and two rows of two cost one line more
/// than a single row of four while staying clickable. A module with a single
/// layer gets a single checkbox - the bar is not a frame, it is an ordering.
///
/// Labels come through the module's own lookup, so a module may override any
/// of the four words with a string of its own; the shell strings under View.*
/// are the default and the reason the words match between modules.
/// </summary>
public static class ViewToggleBar
{
    public const int DefaultPerRow = 2;

    /// <param name="loc">The module's Tr - shell strings first, module strings on top.</param>
    /// <returns>The panel the switches were added to, for callers that lay out by hand.</returns>
    public static UiPanel Add(UiPanel panel, Func<string, string> loc, IReadOnlyList<ViewToggle> toggles, int perRow = DefaultPerRow)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(toggles);
        if (perRow < 1) perRow = DefaultPerRow;

        var keys = new string[toggles.Count];

        for (int i = 0; i < toggles.Count; i++)
        {
            keys[i] = toggles[i].Key;
        }

        string[] order = ViewLayers.Order(keys);

        var taken = new bool[toggles.Count];
        UiPanel? row = null;
        int inRow = 0;

        foreach (string key in order)
        {
            int index = -1;

            for (int i = 0; i < toggles.Count; i++)
            {
                if (!taken[i] && toggles[i].Key == key)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0) continue;
            taken[index] = true;
            ViewToggle toggle = toggles[index];

            if (row is null || inRow == perRow)
            {
                row = panel.Add(new UiPanel { DrawBackground = false, Horizontal = true });
                inRow = 0;
            }

            Checkbox box = row.Add(new Checkbox(loc(toggle.Key), toggle.Value));
            Action<bool> sink = toggle.Changed;
            box.Changed += sink;
            inRow++;
        }

        return panel;
    }
}
