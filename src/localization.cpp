#include "promzona/localization.hpp"
#include "promzona/names.hpp"
#include <algorithm>
#include <fstream>

namespace Promzona
{
    namespace
    {
        // Game catalogue: Russian source text, English, Ukrainian. Keys are the trimmed
        // text of the literals used by the game; composed strings are matched piecewise.
        constexpr Phrase catalogue[] = {
        #include "localization_phrases.inc"
        };

        // Locale-independent replacement for char.IsLetter over the scripts the game draws.
        bool letter(wchar_t c)
        {
            if (c < 0x80)
                return (c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z');
            if (c >= 0xc0 && c <= 0x24f)
                return c != 0xd7 && c != 0xf7;
            return c >= 0x400 && c <= 0x52f;
        }

        PhraseTranslator build(Language language)
        {
            PhraseTranslator result;

            if (language == Language::russian)
            {
                return result;
            }

            const bool english = language == Language::english;

            for (const auto& phrase : catalogue)
            {
                result.add(phrase.russian, english ? phrase.english : phrase.ukrainian);
            }

            auto addNames = [&](std::span<const StreetName> names)
            {
                for (const auto &name : names)
                {
                    const std::wstring russian(name.russian);

                    if (english)
                    {
                        // "ул. Ленина" reads "Lenina St"; a bare name is transliterated.
                        result.add(L"ул. " + russian, std::wstring(name.english) + L" St");
                        result.add(russian, name.english);
                    }
                    else
                    {
                        result.add(L"ул. " + russian, L"вул. " + std::wstring(name.ukrainian));
                        result.add(russian, name.ukrainian);
                    }
                }
            };

            addNames(streetNames());
            addNames(avenueNames());
            return result;
        }
    }

    std::string_view languageCode(Language language)
    {
        return language == Language::russian ? "ru" : language == Language::ukrainian ? "uk" : "en";
    }

    std::wstring_view languageTitle(Language language)
    {
        return language == Language::russian     ? L"Русский"
               : language == Language::ukrainian ? L"Українська"
                                                 : L"English";
    }

    Language languageFromCode(std::string_view code)
    {
        auto starts = [&](std::string_view prefix)
        { return code.size() >= 2 && (code[0] | 0x20) == prefix[0] && (code[1] | 0x20) == prefix[1]; };
        return starts("uk") ? Language::ukrainian : starts("ru") ? Language::russian : Language::english;
    }

    Language cycleLanguage(Language language, int step)
    {
        const int count = int(languages.size());
        const int direction = step > 0 ? 1 : step < 0 ? -1 : 0;
        return languages[size_t((std::to_underlying(language) + direction + count) % count)];
    }

    Language loadLanguage(const std::filesystem::path &file, Language fallback)
    {
        std::ifstream in(file);
        std::string code;
        return in >> code ? languageFromCode(code) : fallback;
    }

    bool saveLanguage(const std::filesystem::path &file, Language language)
    {
        std::ofstream out(file);
        out << languageCode(language) << '\n';
        out.close();
        return bool(out);
    }

    int PhraseTranslator::child(int node, wchar_t c) const
    {
        const auto &next = nodes_[size_t(node)].next;
        auto it = std::ranges::lower_bound(next, c, {}, &std::pair<wchar_t, int>::first);
        return it != next.end() && it->first == c ? it->second : -1;
    }

    void PhraseTranslator::add(std::wstring_view phrase, std::wstring_view translation)
    {
        if (phrase.empty())
            return;

        int node = 0;

        for (wchar_t c : phrase)
        {
            int next = child(node, c);

            if (next < 0)
            {
                next = static_cast<int>(nodes_.size());
                nodes_.emplace_back();
                auto &links = nodes_[size_t(node)].next;
                links.insert(std::ranges::lower_bound(links, c, {}, &std::pair<wchar_t, int>::first), {c, next});
            }

            node = next;
        }

        // A later entry for the same phrase replaces the earlier one.
        if (nodes_[size_t(node)].translation < 0)
        {
            nodes_[size_t(node)].translation = int(translations_.size());
            translations_.emplace_back(translation);
        }
        else
        {
            translations_[size_t(nodes_[size_t(node)].translation)] = translation;
        }
    }

    std::wstring PhraseTranslator::translate(std::wstring_view text) const
    {
        if (translations_.empty())
            return std::wstring(text);
        std::wstring result;
        size_t copied = 0;
        for (size_t start = 0; start < text.size(); ++start)
        {
            if (start > 0 && letter(text[start - 1]))
                continue;
            int node = 0, found = -1;
            size_t matchEnd = start;
            for (size_t end = start; end < text.size(); ++end)
            {
                node = child(node, text[end]);
                if (node < 0)
                    break;
                const int translation = nodes_[size_t(node)].translation;
                if (translation >= 0 && (end + 1 == text.size() || !letter(text[end + 1])))
                {
                    found = translation;
                    matchEnd = end + 1;
                }
            }
            if (found < 0)
                continue;
            result.append(text, copied, start - copied).append(translations_[size_t(found)]);
            copied = matchEnd;
            start = matchEnd - 1;
        }

        result.append(text, copied, text.size() - copied);
        return result;
    }

    std::span<const Phrase> phrases()
    {
        return catalogue;
    }

    const PhraseTranslator &translator(Language language)
    {
        // Built on first use; function-local statics are initialised thread-safely.
        static const std::array<PhraseTranslator, 3> translators{
            build(Language::english), build(Language::russian), build(Language::ukrainian)};
        return translators[std::to_underlying(language)];
    }

    std::wstring localize(std::wstring_view russian, Language language)
    {
        return language == Language::russian ? std::wstring(russian) : translator(language).translate(russian);
    }

    bool has_cyrillic(std::wstring_view text)
    {
        return std::ranges::any_of(text, [](wchar_t c) { return c >= 0x400 && c <= 0x52f; });
    }
}
