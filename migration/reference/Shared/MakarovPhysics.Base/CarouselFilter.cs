using System.Globalization;
using System.Text;

namespace MakarovPhysics.Base;

/// <summary>Culture-independent catalogue matching, shared by host and CPU tests.</summary>
public static class CarouselFilter
{
    /// <summary>All query words must occur in the title, ID or theme. Initials
    /// also match word boundaries and the capital letters of a module ID.</summary>
    public static bool Match(string query, string id, string title, string theme)
    {
        string q = Normalize(query).Trim();
        if (q.Length == 0) return true;
        if (id.Length == 0) return false;
        string text = Normalize(id + " " + title + " " + theme);
        if (q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.Ordinal))) return true;
        if (q.Any(char.IsWhiteSpace)) return false;
        return Initials(id).StartsWith(q, StringComparison.Ordinal)
            || Initials(title).StartsWith(q, StringComparison.Ordinal)
            || Initials(theme).StartsWith(q, StringComparison.Ordinal);
    }

    private static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);

        // A Windows character message can contain half a surrogate pair.
        // Replace incomplete pairs for matching until the next message arrives.
        var valid = new StringBuilder(text.Length);

        foreach (Rune rune in text.EnumerateRunes())
        {
            valid.Append(rune.ToString());
        }

        bool foldMarks = false;
        foreach (Rune rune in valid.ToString().Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                // Dakuten changes a Japanese syllable: カ and ガ must not
                // collapse into the same search term. Keep marks on scripts
                // outside the accent-insensitive Latin/Greek/Cyrillic search.
                if (foldMarks && category == UnicodeCategory.NonSpacingMark) continue;
            }
            else
            {
                foldMarks = rune.Value is >= 0x0041 and <= 0x024F
                    or >= 0x0370 and <= 0x052F or >= 0x1E00 and <= 0x1FFF;
            }
            result.Append(Rune.ToUpperInvariant(rune).ToString());
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Initials(string text)
    {
        var result = new StringBuilder();

        bool previousLetter = false, previousLower = false;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            Rune rune = element.EnumerateRunes().First();
            bool letter = Rune.IsLetterOrDigit(rune);
            if (letter && (!previousLetter || (Rune.IsUpper(rune) && previousLower)))
            {
                result.Append(element);
            }
            previousLetter = letter;
            previousLower = Rune.IsLower(rune);
        }

        return Normalize(result.ToString());
    }
}
