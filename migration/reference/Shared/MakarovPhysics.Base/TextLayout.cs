using System.Globalization;
using System.Text;

namespace MakarovPhysics.Base;

/// <summary>Horizontal UI text layout shared by the Windows controls. Keeps
/// grapheme clusters intact, Latin words together when they fit, and allows
/// CJK text to wrap without spaces. This is a practical punctuation policy,
/// not a complete implementation of Unicode line breaking.</summary>
public static class TextLayout
{
    // Unicode code-point classes, not display text or a font coverage list.
    private const string Opening = "([{\u3008\u300A\u300C\u300E\u3010\u3014\u3016\u3018\u301A\u2018\u201C\u00AB\u2039\uFF08\uFF3B\uFF5B\uFF5F\uFF62";
    private const string Closing = ")]},.!?:;%\u3009\u300B\u300D\u300F\u3011\u3015\u3017\u3019\u301B\u2019\u201D\u00BB\u203A\u3001\u3002\uFF0C\uFF0E\uFF01\uFF1F\uFF1A\uFF1B\uFF05\u2030\u00B0\u2103\uFF09\uFF3D\uFF5D\uFF60\uFF63\u30FB\uFF65\u309D\u309E\u30FD\u30FE\u3005\u30FC\u3041\u3043\u3045\u3047\u3049\u3063\u3083\u3085\u3087\u308E\u3095\u3096\u30A1\u30A3\u30A5\u30A7\u30A9\u30C3\u30E3\u30E5\u30E7\u30EE\u30F5\u30F6\u31F0\u31F1\u31F2\u31F3\u31F4\u31F5\u31F6\u31F7\u31F8\u31F9\u31FA\u31FB\u31FC\u31FD\u31FE\u31FF";

    /// <summary>Preserves explicit line breaks, including empty lines. Width
    /// at or below zero (or NaN) means no wrapping. A grapheme wider than the
    /// box remains whole; at extremely narrow widths a punctuation pair may
    /// exceed it by one grapheme instead of leaving a closing mark alone.</summary>
    public static List<string> Wrap(string text, float width, Func<string, float> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        List<string> lines = [];
        foreach (string paragraph in HardLines(text))
        {
            if (!(width > 0) || paragraph.Length == 0 || measure(paragraph) <= width)
            {
                lines.Add(paragraph);
                continue;
            }

            int[] starts = StringInfo.ParseCombiningCharacters(paragraph);
            string Part(int from, int to) => paragraph[starts[from]..(to < starts.Length ? starts[to] : paragraph.Length)];
            Rune First(int index) => Rune.TryGetRuneAt(paragraph, starts[index], out Rune rune) ? rune : Rune.ReplacementChar;
            bool Space(int index) => IsBreakableSpace(First(index));
            bool PunctuationAllows(int end)
            {
                if (end == starts.Length) return true;
                // Look through boundary whitespace so a bracket cannot be
                // stranded simply because the author put spaces around it.
                int left = end - 1, right = end;
                while (left >= 0 && Space(left)) left--;
                while (right < starts.Length && Space(right)) right++;
                return (left < 0 || !IsOpening(First(left)))
                    && (right == starts.Length || !IsClosing(First(right)));
            }
            bool Opportunity(int end) => PunctuationAllows(end)
                && (end == starts.Length || Space(end - 1) || Space(end)
                    || ((IsCjk(First(end - 1)) || IsCjk(First(end)))
                        && !IsNonBreaking(First(end - 1)) && !IsNonBreaking(First(end))));

            int start = 0, before = lines.Count;
            while (start < starts.Length)
            {
                while (start < starts.Length && Space(start)) start++;
                if (start == starts.Length) break;

                int fit = start;
                while (fit < starts.Length && measure(Part(start, fit + 1)) <= width) fit++;
                if (fit == starts.Length)
                {
                    lines.Add(TrimBreakableEnd(Part(start, fit)));
                    break;
                }

                int end = fit;
                while (end > start && !Opportunity(end)) end--;
                if (end == start)
                {
                    // A long Latin token has no normal break opportunity.
                    // Split at a whole grapheme while still respecting nearby
                    // punctuation where the width permits it.
                    end = fit;
                    while (end > start && !PunctuationAllows(end)) end--;
                    if (end == start)
                    {
                        end = Math.Max(start + 1, fit);
                        if (end < starts.Length && !PunctuationAllows(end)) end++;
                    }
                }

                string line = TrimBreakableEnd(Part(start, end));
                if (line.Length > 0) lines.Add(line);
                start = end;
            }
            if (lines.Count == before) lines.Add(string.Empty);
        }
        return lines;
    }

    /// <summary>Fits a single-line caption using whole graphemes. Returns an
    /// empty string when even the ellipsis cannot fit; never slices UTF-16.</summary>
    public static string Ellipsize(string text, float width, Func<string, float> measure, string ellipsis = "…")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(ellipsis);
        if (!(width > 0)) return string.Empty;
        if (measure(text) <= width) return text;
        if (measure(ellipsis) > width) return string.Empty;

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int end = 0;
        foreach (int next in starts.Skip(1).Append(text.Length))
        {
            if (measure(text[..next] + ellipsis) > width) break;
            end = next;
        }
        return TrimBreakableEnd(text[..end]) + ellipsis;
    }

    private static string[] HardLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n').Split('\n');

    private static bool IsBreakableSpace(Rune rune) => Rune.IsWhiteSpace(rune)
        && rune.Value is not (0x00A0 or 0x202F);
    private static bool IsNonBreaking(Rune rune) => rune.Value is 0x00A0 or 0x202F or 0x2060 or 0xFEFF;
    private static string TrimBreakableEnd(string text)
    {
        int end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]) && text[end - 1] is not ('\u00A0' or '\u202F')) end--;
        return text[..end];
    }
    private static bool IsOpening(Rune rune) => Opening.Contains(rune.ToString(), StringComparison.Ordinal)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.OpenPunctuation or UnicodeCategory.InitialQuotePunctuation;
    private static bool IsClosing(Rune rune) => Closing.Contains(rune.ToString(), StringComparison.Ordinal)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.ClosePunctuation or UnicodeCategory.FinalQuotePunctuation;
    private static bool IsCjk(Rune rune) => rune.Value is >= 0x2E80 and <= 0xA4CF
        or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF
        or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFFEF
        or >= 0x20000 and <= 0x323AF;
}
