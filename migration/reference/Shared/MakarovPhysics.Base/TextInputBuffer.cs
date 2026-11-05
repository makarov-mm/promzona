using System.Globalization;
using System.Text;

namespace MakarovPhysics.Base;

/// <summary>Single-line editing at the end of a string. Limits and deletion use
/// text elements; a UTF-16 keyboard stream never exposes half a surrogate pair.</summary>
public sealed class TextInputBuffer
{
    private char? _pendingHighSurrogate;
    public string Text { get; private set; } = string.Empty;

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // External strings can contain malformed UTF-16. EnumerateRunes replaces
        // invalid sequences without splitting valid supplementary ideographs.
        Text = string.Concat(text.EnumerateRunes().Select(r => r.ToString()));
        _pendingHighSurrogate = null;
    }

    public void ResetPending() => _pendingHighSurrogate = null;

    public void Backspace()
    {
        if (_pendingHighSurrogate is not null)
        {
            _pendingHighSurrogate = null;
            return;
        }
        int[] starts = StringInfo.ParseCombiningCharacters(Text);
        if (starts.Length > 0) Text = Text[..starts[^1]];
    }

    public void TypeChar(char value, int maxLength)
    {
        if (char.IsControl(value)) return;
        if (char.IsHighSurrogate(value))
        {
            _pendingHighSurrogate = value;
            return;
        }
        string next;
        if (char.IsLowSurrogate(value))
        {
            if (_pendingHighSurrogate is not char high) return;
            next = new string([high, value]);
        }
        else next = value.ToString();
        _pendingHighSurrogate = null;

        string candidate = Text + next;
        // A combining mark can finish the final character even at the limit.
        if (StringInfo.ParseCombiningCharacters(candidate).Length <= Math.Max(0, maxLength))
            Text = candidate;
    }
}
