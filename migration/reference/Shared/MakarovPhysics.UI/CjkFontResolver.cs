using System.Drawing.Text;
using System.Globalization;
using System.Text;

namespace MakarovPhysics.UI;

/// <summary>Explicit regional font preference and real cmap coverage. The bundled
/// Noto Sans SC/JP/KR (Fonts/) come first, so every machine renders the same
/// glyphs; installed Windows fonts are fallbacks for what they lack. A missing
/// glyph is reported instead of silently claiming coverage.</summary>
internal sealed class CjkFontResolver : IDisposable
{
    private sealed record Face(Font Font, FontCoverage? Coverage);
    private readonly HashSet<string> _installed;
    private readonly Dictionary<string, Face> _faces = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedFamilies = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly string _baseFamily, _language;
    private readonly float _pixels;
    private readonly FontStyle _style;
    private readonly Graphics _graphics;
    // The first name of each regional list is the bundled file in Fonts/; the
    // rest are installed Windows or user fonts, tried in order for a glyph the
    // bundled face lacks. Traditional Chinese has no bundled face (no pilot
    // locale): its Han glyphs fall through to the Simplified face.
    internal const string BundledSimplified = "Noto Sans SC", BundledJapanese = "Noto Sans JP", BundledKorean = "Noto Sans KR";
    private static readonly string[] Simplified = [BundledSimplified, "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "SimSun"];
    private static readonly string[] Traditional = ["Noto Sans TC", "Microsoft JhengHei UI", "Microsoft JhengHei", "Noto Sans CJK TC", "Source Han Sans TC", "MingLiU", "PMingLiU"];
    private static readonly string[] Japanese = [BundledJapanese, "Yu Gothic UI", "Yu Gothic", "Meiryo", "Noto Sans CJK JP", "Source Han Sans JP", "MS Gothic"];
    private static readonly string[] Korean = [BundledKorean, "Malgun Gothic", "Noto Sans CJK KR", "Source Han Sans KR", "Gulim", "Batang"];
    private static readonly string[] Symbols = ["Segoe UI Symbol", "Cambria Math", "Arial Unicode MS"];

    internal Font BaseFont { get; }
    internal string? PreferredCjkFamily { get; }
    /// <summary>True when the preferred regional face came from Fonts/, not from the machine.</summary>
    internal bool PreferredIsBundled => PreferredCjkFamily is { } name && BundledFonts.Load().ContainsKey(name);
    internal IEnumerable<string> UsedFamilies => _usedFamilies;

    /// <summary>The bundled file that the regional preference of a language starts with.</summary>
    internal static string? BundledFamilyFor(string code)
    {
        code = code.ToLowerInvariant();
        if (!IsCjkLanguage(code)) return null;
        if (code.StartsWith("ja")) return BundledJapanese;
        if (code.StartsWith("ko")) return BundledKorean;
        return code.Contains("hant") || code.StartsWith("zh-tw") || code.StartsWith("zh-hk") || code.StartsWith("zh-mo") ? null : BundledSimplified;
    }

    internal CjkFontResolver(string baseFamily, float pixels, FontStyle style, string language, Graphics graphics)
    {
        _baseFamily = baseFamily;
        _pixels = pixels;
        _style = style;
        _language = language.ToLowerInvariant();
        _graphics = graphics;

        try
        {
            using var installed = new InstalledFontCollection();
            FontFamily[] families = installed.Families;

            try
            {
                _installed = families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var family in families) family.Dispose();
            }

            BaseFont = GetFace(baseFamily)?.Font ?? GetFace("Arial")?.Font ?? throw new InvalidOperationException("No usable Windows UI font was found.");

            if (IsCjkLanguage(_language))
            {
                PreferredCjkFamily = RegionalFamilies().FirstOrDefault(name => GetFace(name)?.Coverage is not null);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static bool IsCjkLanguage(string code) => 
        code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        || 
        code.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
        ||
        code.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    private string[] RegionalFamilies() => _language.StartsWith("ja") 
        ? Japanese 
        : _language.StartsWith("ko") 
            ? Korean
            : _language.Contains("hant") || _language.StartsWith("zh-tw") || _language.StartsWith("zh-hk") || _language.StartsWith("zh-mo")
                ? Traditional 
                : Simplified;

    internal Font? Resolve(string element)
    {
        bool cjk = element.EnumerateRunes().Any(r => IsCjk(r.Value));
        bool kana = element.EnumerateRunes().Any(r => r.Value is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF);
        bool hangul = element.EnumerateRunes().Any(r => r.Value is >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xAC00 and <= 0xD7AF);
        IEnumerable<string> preferred = cjk ? kana ? Japanese : hangul ? Korean : RegionalFamilies() : [_baseFamily];

        foreach (string name in preferred.Concat([_baseFamily]).Concat(Symbols).Concat(Simplified).Concat(Traditional).Concat(Japanese).Concat(Korean).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Face? face = GetFace(name);
            if (face?.Coverage is not { } coverage) continue;

            if (element.EnumerateRunes().All(r => IsIgnorable(r) || coverage.Contains(r.Value)))
            {
                _usedFamilies.Add(face.Font.Name);
                return face.Font;
            }
        }

        return null;
    }

    private Face? GetFace(string name)
    {
        if (_faces.TryGetValue(name, out var existing)) return existing;
        Font? font = null;

        try
        {
            if (BundledFonts.Load().TryGetValue(name, out var bundled))
            {
                // Shipped file: GDI+ private collection, coverage from the file's
                // own cmap. A style the file lacks falls back to its regular face
                // rather than to a different family.
                try
                {
                    font = new Font(bundled.Family, _pixels, _style, GraphicsUnit.Pixel);
                }
                catch (ArgumentException)
                {
                    font = new Font(bundled.Family, _pixels, FontStyle.Regular, GraphicsUnit.Pixel);
                }

                var shipped = new Face(font, bundled.Coverage);
                _faces.Add(name, shipped);
                return shipped;
            }

            if (!_installed.Contains(name)) return null;
            font = new Font(name, _pixels, _style, GraphicsUnit.Pixel);
            var face = new Face(font, FontCoverage.Read(font, _graphics));
            _faces.Add(name, face);
            return face;
        }
        catch (ArgumentException)
        {
            font?.Dispose();
            return null;
        }
        catch
        {
            font?.Dispose();
            throw;
        }
    }

    private static bool IsIgnorable(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format
        || rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;

    private static bool IsCjk(int c) => c is >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7AF
        or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE4F or >= 0xFF01 and <= 0xFFEF
        or >= 0x1100 and <= 0x11FF or >= 0x20000 and <= 0x323AF;

    public void Dispose()
    {
        foreach (Face face in _faces.Values)
        {
            try
            {
                face.Font.Dispose();
            }
            catch
            {
                // ...
            }
        }

        _faces.Clear();
    }
}
