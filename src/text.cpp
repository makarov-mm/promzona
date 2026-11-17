#include "promzona/text.hpp"

namespace Promzona
{
namespace
{
    constexpr char32_t replacement = 0xFFFD;

    void append(std::wstring &out, char32_t c)
    {
        if constexpr (sizeof(wchar_t) == 2)
        {
            if (c >= 0x10000)
            {
                c -= 0x10000;
                out.push_back(wchar_t(0xD800 + (c >> 10)));
                out.push_back(wchar_t(0xDC00 + (c & 0x3FF)));
                return;
            }
        }
        out.push_back(wchar_t(c));
    }

    void append(std::string &out, char32_t c)
    {
        if (c < 0x80)
            out.push_back(char(c));
        else if (c < 0x800)
        {
            out.push_back(char(0xC0 | (c >> 6)));
            out.push_back(char(0x80 | (c & 0x3F)));
        }
        else if (c < 0x10000)
        {
            out.push_back(char(0xE0 | (c >> 12)));
            out.push_back(char(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(char(0x80 | (c & 0x3F)));
        }
        else
        {
            out.push_back(char(0xF0 | (c >> 18)));
            out.push_back(char(0x80 | ((c >> 12) & 0x3F)));
            out.push_back(char(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(char(0x80 | (c & 0x3F)));
        }
    }

    bool valid(char32_t c)
    {
        return c <= 0x10FFFF && (c < 0xD800 || c > 0xDFFF);
    }
}

std::wstring wide(std::string_view s)
{
    std::wstring out;
    out.reserve(s.size());
    for (size_t i = 0; i < s.size();)
    {
        const auto lead = static_cast<unsigned char>(s[i]);
        const int length = lead < 0x80 ? 1 : (lead >> 5) == 6 ? 2 : (lead >> 4) == 14 ? 3 : (lead >> 3) == 30 ? 4 : 0;
        if (!length || i + length > s.size())
        {
            append(out, replacement);
            ++i;
            continue;
        }
        char32_t c = length == 1 ? lead : lead & (0x7F >> length);
        bool ok = true;
        for (int k = 1; k < length; ++k)
        {
            const auto next = static_cast<unsigned char>(s[i + k]);
            ok = ok && (next & 0xC0) == 0x80;
            c = (c << 6) | (next & 0x3F);
        }
        // Overlong forms are rejected as well as truncated ones.
        constexpr char32_t least[] = {0, 0, 0x80, 0x800, 0x10000};
        if (!ok || c < least[length] || !valid(c))
        {
            append(out, replacement);
            ++i;
            continue;
        }
        append(out, c);
        i += length;
    }
    return out;
}

std::string utf8(std::wstring_view s)
{
    std::string out;
    out.reserve(s.size());
    for (size_t i = 0; i < s.size();)
        append(out, next_code_point(s, i));
    return out;
}

std::filesystem::path utf8_path(std::string_view s)
{
    return std::filesystem::path(std::u8string(s.begin(), s.end()));
}

char32_t next_code_point(std::wstring_view s, size_t &at)
{
    char32_t c = char32_t(s[at++]);
    if constexpr (sizeof(wchar_t) == 2)
    {
        if (c >= 0xD800 && c <= 0xDBFF && at < s.size() && s[at] >= 0xDC00 && s[at] <= 0xDFFF)
            return 0x10000 + ((c - 0xD800) << 10) + (char32_t(s[at++]) - 0xDC00);
    }
    return valid(c) ? c : replacement;
}
} // namespace Promzona
