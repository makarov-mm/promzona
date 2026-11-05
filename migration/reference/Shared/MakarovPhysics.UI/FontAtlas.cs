using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI;

/// <summary>Demand-loaded, bounded glyph pages. Metrics never touch OpenGL;
/// dirty regions are uploaded only when Draw2D submits the corresponding page.</summary>
public sealed class FontAtlas : IDisposable
{
    private const int PageSize = 1024;
    private const int MaxPages = 8; // At most 32 MiB each of CPU bitmap and GPU storage.
    private const int MaxMetricEntries = 8192;
    private readonly float _scale;
    private readonly List<Page> _pages = [];
    private readonly Dictionary<string, GlyphInfo> _glyphs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Metrics> _metrics = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly Bitmap _measureBitmap;
    private readonly Graphics _measureGraphics;
    private readonly CjkFontResolver _fonts;
    private readonly StringFormat _format;
    private readonly GlyphInfo _fallback;
    private long _frame;
    private bool _disposed;

    private sealed record Metrics(Font Font, string Text, float Advance, int Width, int Height, float OffsetY);

    private sealed class Page : IDisposable
    {
        internal readonly Bitmap Bitmap;
        internal readonly Graphics Graphics;
        internal Texture? Texture;
        internal Rectangle Dirty;
        internal int X, Y, RowHeight;
        internal long LastUsed = -1;
        internal readonly HashSet<string> Keys = new(StringComparer.Ordinal);
        internal Page()
        {
            Bitmap = new Bitmap(PageSize, PageSize, PixelFormat.Format32bppArgb);
            try
            {
                Graphics = System.Drawing.Graphics.FromImage(Bitmap);
                Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Graphics.Clear(Color.Transparent);
            }
            catch { Graphics?.Dispose(); Bitmap.Dispose(); throw; }
        }
        internal bool TryAllocate(int width, int height, out Rectangle cell)
        {
            int x = X, y = Y, rowHeight = RowHeight;
            if (x + width > PageSize) { x = 0; y += rowHeight; rowHeight = 0; }
            if (width > PageSize || y + height > PageSize) { cell = default; return false; }
            cell = new Rectangle(x, y, width, height);
            X = x + width; Y = y; RowHeight = Math.Max(rowHeight, height);
            return true;
        }
        internal void MarkDirty(Rectangle rect) => Dirty = Dirty.IsEmpty ? rect : Rectangle.Union(Dirty, rect);
        internal void Clear()
        {
            Graphics.Clear(Color.Transparent);
            X = Y = RowHeight = 0; LastUsed = -1; Keys.Clear();
            Dirty = new Rectangle(0, 0, PageSize, PageSize);
        }
        public void Dispose() { Texture?.Dispose(); Graphics.Dispose(); Bitmap.Dispose(); }
    }

    /// <summary>The first page remains available for existing callers and solid fills.</summary>
    public Texture Texture => TextureForPage(0);
    public bool Overflowed { get; private set; }
    public float LineHeight { get; }
    public string LanguageCode { get; }
    public string? PreferredCjkFontFamily => _fonts.PreferredCjkFamily;
    /// <summary>The regional face in use is the shipped one (Fonts/), not a machine font.</summary>
    public bool PreferredCjkFontIsBundled => _fonts.PreferredIsBundled;
    public IReadOnlyCollection<string> MissingGlyphs => _missing;
    public int PageCount => _pages.Count;
    public int PageLimit => MaxPages;
    public int RecycledPageCount { get; private set; }
    public IReadOnlyList<string> UsedFontFamilies => _fonts.UsedFamilies.ToArray();

    /// <summary>Directory of the fonts shipped beside the executable (AppPaths.FontsDirectory).
    /// The host sets it before InitGL; tools point it at a repository checkout.</summary>
    public static string BundledFontsDirectory { get => BundledFonts.Directory; set => BundledFonts.Directory = value; }
    /// <summary>Family names actually loaded from BundledFontsDirectory, sorted. Needs no GL context.</summary>
    public static IReadOnlyList<string> BundledFontFamilies => BundledFonts.Load().Keys.Order(StringComparer.Ordinal).ToArray();
    /// <summary>The bundled family a language's regional preference starts with
    /// ("Noto Sans SC" for zh-Hans, "Noto Sans JP" for ja, "Noto Sans KR" for ko); null otherwise.</summary>
    public static string? BundledFontFamilyFor(string languageCode) => CjkFontResolver.BundledFamilyFor(languageCode);
    /// <summary>The cmap of a font FILE, for coverage questions without GDI+ or GL
    /// (FontCoverage.ReadFile); null when the file is not a parsable sfnt font.</summary>
    public static FontCoverage? ReadFontFile(string path) => FontCoverage.ReadFile(path);
    public (float u, float v) WhiteUv { get; } = (1.5f / PageSize, 1f - 1.5f / PageSize);

    /// <summary>Construction requires a current GL context. Language may be zh-Hans,
    /// zh-Hant, ja or ko; the original four constructor arguments remain compatible.</summary>
    public FontAtlas(string family = "Segoe UI", float sizePx = 14f, float scale = 1f,
        FontStyle style = FontStyle.Regular, string languageCode = "en")
    {
        _scale = Math.Clamp(scale, 0.5f, 4f);
        LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode;
        try
        {
            _measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            _measureGraphics = Graphics.FromImage(_measureBitmap);
            _measureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _format = (StringFormat)StringFormat.GenericTypographic.Clone();
            _format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            _fonts = new CjkFontResolver(family, sizePx * _scale, style, LanguageCode, _measureGraphics);
            LineHeight = _fonts.BaseFont.GetHeight(_measureGraphics) / _scale;
            var first = new Page();
            _pages.Add(first);
            first.Graphics.FillRectangle(Brushes.White, 0, 0, 4, 4);
            first.X = 8; first.RowHeight = 4;
            first.MarkDirty(new Rectangle(0, 0, 4, 4));
            _fallback = Bake("?", GetMetrics("?"));
            // Preserve the existing European/mathematical repertoire, while CJK is
            // loaded as needed. Additional pages are bounded and can be recycled.
            foreach (char ch in EnumerateCharset().Distinct()) Glyph(ch);
            for (int page = 0; page < _pages.Count; page++) TextureForPage(page);
            if (CjkFontResolver.IsCjkLanguage(LanguageCode) && PreferredCjkFontFamily is null)
                System.Diagnostics.Trace.TraceWarning($"No CJK font for {LanguageCode}: nothing loaded from {BundledFontsDirectory} and no regional font installed; inspect FontAtlas.MissingGlyphs.");
            else if (CjkFontResolver.IsCjkLanguage(LanguageCode) && !PreferredCjkFontIsBundled)
                System.Diagnostics.Trace.TraceInformation($"Bundled font for {LanguageCode} not loaded from {BundledFontsDirectory}; using installed {PreferredCjkFontFamily}.");
        }
        catch
        {
            foreach (var page in _pages) page.Dispose();
            _fonts?.Dispose(); _format?.Dispose(); _measureGraphics?.Dispose(); _measureBitmap?.Dispose();
            throw;
        }
    }

    /// <summary>The legacy shared repertoire stays stable for Mac and glyph checks.
    /// CJK coverage is checked against installed fonts, not this compatibility list.</summary>
    public static IEnumerable<char> EnumerateCharset()
    {
        for (char c = ' '; c <= '~'; c++) yield return c; // ASCII
        // Latin-1 Supplement + Latin Extended-A: ö ü ß é è ç ñ á í ó ú ą ć ę ł
        // ń ś ź ż œ Š ž ... - everything de/fr/es/it/pl need
        for (char c = '¡'; c <= 'ÿ'; c++) yield return c;
        for (char c = 'Ā'; c <= 'ſ'; c++) yield return c;
        // Latin Extended-B, the four Romanian comma-below letters. They are NOT
        // the cedilla forms in Latin Extended-A: Romanian uses U+0218..U+021B,
        // and substituting ş/ţ is a spelling error, not a font fallback.
        foreach (char c in "\u0218\u0219\u021A\u021B") yield return c; // Ș ș Ț ț
        // Latin Extended Additional: Vietnamese tone marks. This is the single
        // biggest block in the atlas - 96 glyphs - so it goes in as a range
        // rather than a hand-listed set.
        for (char c = '\u1EA0'; c <= '\u1EF9'; c++) yield return c;
        foreach (char c in "\u01A0\u01A1\u01AF\u01B0") yield return c; // Ơ ơ Ư ư
        for (char c = 'А'; c <= 'я'; c++) yield return c; // Cyrillic (Russian)
        yield return 'Ё'; yield return 'ё';
        foreach (char c in "ЄєІіЇїҐґЎў") yield return c; // Ukrainian (+Belarusian ў)
        // Bulgarian и-grave: a SEPARATE letter, not an accent. Bulgarian uses ѝ
        // for the possessive pronoun to tell it apart from the conjunction и,
        // so a Bulgarian UI without it prints a box in the middle of a word.
        foreach (char c in "\u040D\u045D") yield return c; // Ѝ ѝ
        // Greek, the WHOLE printable block and not just Α-Ω / α-ω: written
        // Greek needs the accented vowels (ά έ ή ί ό ύ ώ, Ά Έ Ή Ί Ό Ύ Ώ, ϊ ϋ
        // ΐ ΰ). The bare alphabet is enough for physics symbols and nothing
        // else - a Greek UI built on it would show holes in half the words.
        for (char c = '\u0386'; c <= '\u03CE'; c++)
        {
            if (c is '\u0387' or '\u038B' or '\u038D' or '\u03A2') continue; // unassigned / punctuation
            yield return c;
        }
        foreach (char c in "°·×÷±√∞≈≠≤≥→←↑↓↔−–—-…″′⋅∂∇∫Δ∝‖⟨⟩") yield return c;
        // The en dash, em dash and ellipsis are DATA here, not prose: the shipped Languages XML use them and TestGlyphCoverage fails if they are missing from the atlas.
        // ⟨⟩ (expectation values ⟨x⟩, ⟨E⟩), ↔ (EnergyPark), ∝ (Foucault) and
        // ‖ψ‖ are already used by 48 shipped translations and chart labels -
        // SelfTest cross-checks every Languages XML against this charset
        foreach (char c in "⁰¹²³⁴⁵⁶⁷⁸⁹₀₁₂₃₄₅₆₇₈₉ᵀᵢ∑∏∈∉∪∩⊂⊆∅∀∃∥∠⊥↦⇒⇔ℝℂℤℕℚ") yield return c;
        foreach (char c in "ʲʳᴺᵐᵣᶜⁱ⁻ⁿ₊₋₌ₖₙ⅓⅔↖∬∮│✓ⱼ") yield return c; // Experiment notation
        foreach (char c in "ᵃᶻₐ⊕☉") yield return c; // Astronomy notation
        foreach (char c in "•«»„“”‘’‹›€№ⓘ▸▾") yield return c; // UI punctuation (▸▾ - the InfoOverlay chevrons)
    }

    /// <summary>Begin a frame only after the preceding Draw2D batch was flushed.</summary>
    internal void BeginFrame() => _frame++;

    public GlyphInfo Glyph(char ch) => Glyph(char.IsSurrogate(ch) ? "\uFFFD" : ch.ToString());
    public GlyphInfo Glyph(Rune rune) => Glyph(rune.ToString());

    /// <summary>One Unicode text element (base plus combining marks / surrogate pair).
    /// Rasterizes on the CPU; GPU uploads are deferred until the page is submitted.</summary>
    public GlyphInfo Glyph(string element)
    {
        if (element.Length == 0 || element.All(char.IsControl)) return default;
        if (_glyphs.TryGetValue(element, out var cached))
        {
            _pages[cached.Page].LastUsed = _frame;
            return cached;
        }
        return Bake(element, GetMetrics(element));
    }

    /// <summary>Checks real font cmap coverage, independent of the static repertoire.
    /// This method does not allocate a texture or require a current GL context.</summary>
    public bool SupportsText(string text) => Elements(text).All(e => e.All(char.IsControl) || _fonts.Resolve(e) is not null);

    /// <summary>Measurement changes neither atlas placement nor GL state.</summary>
    public float MeasureText(string text)
    {
        float width = 0;
        foreach (string element in Elements(text))
        {
            if (element.All(char.IsControl)) continue;
            width += _glyphs.TryGetValue(element, out var glyph) ? glyph.Advance : GetMetrics(element).Advance;
        }
        return width;
    }

    internal static IEnumerable<string> Elements(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext()) yield return elements.GetTextElement();
    }

    private Metrics GetMetrics(string element)
    {
        if (_metrics.TryGetValue(element, out var metrics)) return metrics;
        Font? font = _fonts.Resolve(element);
        string rendered = element.Any(char.IsSurrogate)
            ? string.Concat(element.EnumerateRunes().Select(r => r.ToString())) : element;
        if (font is null)
        {
            // A cmap miss is a real failure, not success through a tofu glyph.
            if (_missing.Count < 256 && _missing.Add(element))
                System.Diagnostics.Trace.TraceWarning($"No installed font covers {string.Join(" ", element.EnumerateRunes().Select(r => $"U+{r.Value:X4}"))} ({LanguageCode}).");
            font = _fonts.BaseFont; rendered = "?";
        }
        SizeF size = _measureGraphics.MeasureString(rendered, font, PointF.Empty, _format);
        int pad = Math.Max(1, (int)MathF.Ceiling(_scale));
        float height = Math.Max(size.Height, font.GetHeight(_measureGraphics));
        metrics = new Metrics(font, rendered, size.Width / _scale,
            Math.Max(1, (int)MathF.Ceiling(size.Width)) + 2 * pad,
            Math.Max(1, (int)MathF.Ceiling(height)) + 2 * pad,
            (LineHeight - height / _scale) * 0.5f);
        if (_metrics.Count >= MaxMetricEntries) _metrics.Clear();
        _metrics[element] = metrics;
        return metrics;
    }

    private GlyphInfo Bake(string element, Metrics metrics)
    {
        int index = -1;
        Rectangle cell = default;
        for (int i = 0; i < _pages.Count; i++)
            if (_pages[i].TryAllocate(metrics.Width, metrics.Height, out cell)) { index = i; break; }
        if (index < 0 && _pages.Count < MaxPages && metrics.Width <= PageSize && metrics.Height <= PageSize)
        {
            index = _pages.Count; _pages.Add(new Page());
            _pages[index].TryAllocate(metrics.Width, metrics.Height, out cell);
        }
        if (index < 0 && metrics.Width <= PageSize && metrics.Height <= PageSize)
        {
            // Never recycle a page referenced by this frame's unsubmitted geometry.
            // Page zero contains the permanent white pixel and fallback glyph.
            index = Enumerable.Range(1, _pages.Count - 1).Where(i => _pages[i].LastUsed < _frame)
                .OrderBy(i => _pages[i].LastUsed).DefaultIfEmpty(-1).First();
            if (index >= 0)
            {
                foreach (string key in _pages[index].Keys) _glyphs.Remove(key);
                _pages[index].Clear();
                RecycledPageCount++;
                _pages[index].TryAllocate(metrics.Width, metrics.Height, out cell);
            }
        }
        if (index < 0)
        {
            if (!Overflowed) System.Diagnostics.Trace.TraceWarning("Font atlas reached its per-frame memory limit; some glyphs use the fallback.");
            Overflowed = true;
            // Keep the measured advance even when a bounded atlas substitutes
            // the visible fallback. Layout and the drawing pen must agree.
            return new GlyphInfo(_fallback.U0, _fallback.V0, _fallback.U1, _fallback.V1,
                _fallback.W, _fallback.H, _fallback.OffsetX, _fallback.OffsetY, metrics.Advance, _fallback.Page);
        }
        Page page = _pages[index];
        int pad = Math.Max(1, (int)MathF.Ceiling(_scale));
        page.Graphics.DrawString(metrics.Text, metrics.Font, Brushes.White, cell.X + pad, cell.Y + pad, _format);
        page.MarkDirty(cell); page.LastUsed = _frame; page.Keys.Add(element);
        var glyph = new GlyphInfo((float)cell.Left / PageSize, 1f - (float)cell.Top / PageSize,
            (float)cell.Right / PageSize, 1f - (float)cell.Bottom / PageSize,
            (float)cell.Width / _scale, (float)cell.Height / _scale,
            -(float)pad / _scale, metrics.OffsetY - (float)pad / _scale, metrics.Advance, index);
        _glyphs[element] = glyph;
        return glyph;
    }

    internal Texture TextureForPage(int index)
    {
        Page page = _pages[index];
        if (page.Texture is null || !page.Dirty.IsEmpty) page.Graphics.Flush(FlushIntention.Sync);
        if (page.Texture is null) page.Texture = new Texture(page.Bitmap, mipmapped: false);
        else if (!page.Dirty.IsEmpty) page.Texture.UpdateRegion(page.Bitmap, page.Dirty);
        page.Dirty = default;
        return page.Texture;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var page in _pages) page.Dispose();
        _fonts.Dispose(); _format.Dispose(); _measureGraphics.Dispose(); _measureBitmap.Dispose();
    }
}
