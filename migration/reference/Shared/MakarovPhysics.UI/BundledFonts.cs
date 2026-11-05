using System.Diagnostics;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MakarovPhysics.UI;

/// <summary>The fonts shipped in Fonts/ beside the executable (Noto Sans SC/JP/KR,
/// SIL Open Font License). They are loaded into GDI+ private collections, so
/// nothing is installed on the machine and nothing depends on which optional
/// Windows language fonts the user happens to have. Coverage is read from the
/// file's own cmap: GDI's GetFontData cannot see a private collection.</summary>
internal static class BundledFonts
{
    internal sealed record Entry(string File, FontFamily Family, FontCoverage? Coverage);
    private static readonly object Gate = new();
    private static readonly List<PrivateFontCollection> Collections = []; // kept alive: fonts reference them
    private static readonly Dictionary<string, Entry> Families = new(StringComparer.OrdinalIgnoreCase);
    private static string _directory = Path.Combine(AppContext.BaseDirectory, "Fonts");
    private static string? _loaded;

    /// <summary>Where the files are. The host sets it from AppPaths before the
    /// first atlas; tools point it at a repository. Changing it reloads lazily.</summary>
    internal static string Directory
    {
        get
        {
            lock (Gate)
            {
                return _directory;
            }
        }

        set
        {
            lock (Gate)
            {
                _directory = string.IsNullOrWhiteSpace(value)
                    ? _directory
                    : Path.GetFullPath(value);
            }
        }
    }

    /// <summary>Family name → loaded font. Empty when the directory is absent;
    /// an unreadable file is traced and skipped, never fatal. No GL involved.</summary>
    internal static IReadOnlyDictionary<string, Entry> Load()
    {
        lock (Gate)
        {
            if (string.Equals(_loaded, _directory, StringComparison.Ordinal))
            {
                return Families;
            }

            Families.Clear();
            _loaded = _directory;

            if (!System.IO.Directory.Exists(_directory))
            {
                return Families;
            }

            foreach (string file in System.IO.Directory.GetFiles(_directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(file);

                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                    &&
                    !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
                    &&
                    !extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                PrivateFontCollection? collection = null;

                try
                {
                    collection = new PrivateFontCollection();
                    collection.AddFontFile(file);
                    FontCoverage? coverage = FontCoverage.ReadFile(file);

                    foreach (FontFamily family in collection.Families)
                    {
                        Families[family.Name] = new Entry(file, family, coverage);
                    }

                    Collections.Add(collection);
                }
                catch (Exception error) when (error is ArgumentException or IOException or ExternalException or UnauthorizedAccessException)
                {
                    collection?.Dispose();
                    Trace.TraceWarning($"Bundled font skipped: {file}: {error.Message}");
                }
            }

            return Families;
        }
    }
}
