using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI;

/// <summary>
/// Loads tool icons from the Icons folder and keeps them, one GL texture per
/// name, for as long as the UI lives.
///
/// A missing file is not an error: the name is remembered as absent, nothing
/// is retried every frame, and the button that asked for it stays text-only.
/// That is how the original sandbox behaves and it is the right behaviour for
/// decoration - an icon that failed to load must never take a panel with it.
/// </summary>
public sealed class UiIconCache : IDisposable
{
    private readonly Dictionary<string, Texture?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private string _directory = string.Empty;

    /// <summary>Points the cache at a folder. Changing it drops what was loaded.</summary>
    public void SetDirectory(string? directory)
    {
        if (string.Equals(_directory, directory, StringComparison.OrdinalIgnoreCase)) return;

        Clear();
        _directory = directory ?? string.Empty;
    }

    /// <summary>The icon for a name (without extension), or null when there is none.</summary>
    public Texture? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || _directory.Length == 0) return null;
        if (_icons.TryGetValue(name, out Texture? cached)) return cached;

        Texture? texture = null;

        try
        {
            string path = Path.Combine(_directory, name + ".png");

            if (File.Exists(path))
            {
                using var bitmap = new Bitmap(path);
                texture = new Texture(bitmap);
            }
        }
        catch
        {
            // A corrupt or locked file is one missing icon, not a broken panel.
            texture = null;
        }

        _icons[name] = texture;
        return texture;
    }

    public void Clear()
    {
        foreach (Texture? texture in _icons.Values)
        {
            try
            {
                texture?.Dispose();
            }
            catch
            {
                 /* teardown is best effort, as everywhere else */
            }
        }

        _icons.Clear();
    }

    public void Dispose() => Clear();
}
