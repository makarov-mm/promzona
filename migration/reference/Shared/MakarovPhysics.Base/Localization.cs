using System.Xml;

namespace MakarovPhysics.Base;

/// <summary>
/// Localization: hierarchical XML files, a key is the path through the tree
/// ("Toolbar.Start"). Loading: en.xml as the base, the selected language on top,
/// so an untranslated string falls back to English instead of showing the key.
/// File synchronization is verified by Tools/MakarovPhysics.LangCheck.
/// </summary>
public sealed class Localization
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Current language code ("en", "ru"...).</summary>
    public string Code { get; private set; } = "en";

    /// <summary>Native language name from the root element's name attribute.</summary>
    public string NativeName { get; private set; } = "English";

    public const string DefaultCode = "en";

    /// <summary>No strings loaded: every key comes back as itself. For the
    /// crash card when the fault happened before any language could load.</summary>
    public static Localization Empty { get; } = new();

    /// <summary>String by key; a missing key returns the key itself (visible in the UI).</summary>
    public string this[string key] => _values.GetValueOrDefault(key, key);

    /// <summary>All keys (for LangCheck - the file synchronization checker).</summary>
    public IReadOnlyCollection<string> Keys => _values.Keys;

    public bool Contains(string key) => _values.ContainsKey(key);

    /// <summary>String with {0}, {1}... placeholders.</summary>
    public string Format(string key, params object[] args) => string.Format(this[key], args);

    /// <summary>Set when LoadBest skipped an unreadable/corrupt file - the
    /// message names the file and the parse error, for the caller's log.
    /// null = every present file loaded cleanly.</summary>
    public string? LoadError { get; private set; }

    /// <summary>
    /// Loads en.xml first, then code.xml and an optional platform code.xml overlay.
    /// Tolerant by design: a corrupt or unreadable XML is SKIPPED (recorded in
    /// LoadError), never thrown. Every production caller loads translations for
    /// every module in the catalogue in a loop - one damaged file in Languages/ must degrade
    /// that one module to English/keys, not take down the application (the same
    /// policy the carousel already applies to corrupt preview PNGs). Callers
    /// that want strictness (LangCheck) parse the XML themselves or call
    /// MergeFile directly, which still throws.
    /// </summary>
    public static Localization LoadBest(string directory, string code, string? overlayDirectory = null)
    {
        var loc = new Localization();
        string en = Path.Combine(directory, DefaultCode + ".xml");
        if (File.Exists(en)) loc.TryMergeFile(en);

        // Code must name what was actually LOADED, not what was asked for.
        // Reporting the requested language after falling back to English means
        // the settings file, the dropdown and the strings on screen disagree,
        // and a corrupt or hand-edited code gets written back as if it worked.
        loc.Code = DefaultCode;

        if (!string.Equals(code, DefaultCode, StringComparison.OrdinalIgnoreCase) && IsFileCode(code))
        {
            string wanted = Path.Combine(directory, code + ".xml");

            if (File.Exists(wanted) && loc.TryMergeFile(wanted))
            {
                loc.Code = code;
            }

            // Optional platform-specific translations never change the shared
            // language tree. Missing module files retain the English base.
            if (!string.IsNullOrWhiteSpace(overlayDirectory))
            {
                string overlay = Path.Combine(overlayDirectory, code + ".xml");
                if (File.Exists(overlay) && loc.TryMergeFile(overlay)) loc.Code = code;
            }
        }

        return loc;
    }

    /// <summary>Merges a file, reporting whether it actually went in.</summary>
    private bool TryMergeFile(string path)
    {
        try
        {
            MergeFile(path);
            return true;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            LoadError = LoadError is null
                ? $"{path}: {ex.Message}"
                : $"{LoadError}; {path}: {ex.Message}";

            return false;
        }
    }

    /// <summary>Reads an XML file and adds/overrides keys (hierarchy → "A.B.C").</summary>
    public void MergeFile(string path)
    {
        var doc = new XmlDocument();
        doc.Load(path);
        if (doc.DocumentElement is not { } root) return;
        NativeName = root.GetAttribute("name") is { Length: > 0 } n ? n : NativeName;
        Merge(root, prefix: string.Empty);
    }

    /// <summary>Merges another dictionary under a prefix (module translations → "Module.*").</summary>
    public void MergeUnder(string prefix, Localization other)
    {
        foreach ((string k, string v) in other._values)
        {
            _values[$"{prefix}.{k}"] = v;
        }
    }

    private void Merge(XmlElement element, string prefix)
    {
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is not XmlElement e) continue;
            string key = prefix.Length == 0 ? e.Name : $"{prefix}.{e.Name}";

            if (e.ChildNodes.Count == 1 && e.FirstChild is XmlText t)
            {
                _values[key] = t.Value?.Trim() ?? string.Empty;
            }
            else if (!e.HasChildNodes)
            {
                _values[key] = string.Empty;
            }
            else
            {
                Merge(e, key);
            }
        }
    }

    /// <summary>Lists shared and optional platform languages once: (code, native name).</summary>
    public static List<(string code, string name)> Available(string directory, string? overlayDirectory = null)
    {
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        Read(directory);
        if (!string.IsNullOrWhiteSpace(overlayDirectory)) Read(overlayDirectory);
        return names.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();

        void Read(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string f in Directory.GetFiles(folder, "*.xml").Order())
            {
                var doc = new XmlDocument();
                try { doc.Load(f); } catch { continue; }
                string code = Path.GetFileNameWithoutExtension(f);
                if (!IsFileCode(code)) continue;
                names[code] = doc.DocumentElement?.GetAttribute("name") is { Length: > 0 } n ? n : code;
            }
        }
    }

    private static bool IsFileCode(string code) => code.Length > 0 &&
        code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
