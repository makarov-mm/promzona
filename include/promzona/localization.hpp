#pragma once
#include <array>
#include <filesystem>
#include <span>
#include <string>
#include <string_view>
#include <vector>
#include <utility>

namespace Promzona
{
    // The game world speaks Russian: notices, hints and menu rows are authored in it.
    // English and Ukrainian are produced from that text when it is drawn, so saved
    // notices and composed strings follow a language change immediately.
    enum class Language
    {
        english,
        russian,
        ukrainian
    };

    inline constexpr std::array languages{Language::english, Language::russian, Language::ukrainian};

    std::string_view languageCode(Language language);
    std::wstring_view languageTitle(Language language);
    // Accepts "ru", "ru-RU", "uk_UA", "en-GB"...; anything else is English, as in the managed game.
    Language languageFromCode(std::string_view code);
    Language cycleLanguage(Language language, int step);
    Language loadLanguage(const std::filesystem::path &file, Language fallback);
    [[nodiscard]] bool save_language(const std::filesystem::path &file, Language language);

    // Port of Ukrainian.Translate: the longest catalogue phrase starting at a word
    // boundary is replaced, and replaced text is never translated a second time.
    class PhraseTranslator
    {
        struct Node
        {
            std::vector<std::pair<wchar_t, int>> next; // sorted by character
            int translation{-1};
        };

        std::vector<Node> nodes_{1};
        std::vector<std::wstring> translations_;
        int child(int node, wchar_t c) const;

    public:
        void add(std::wstring_view phrase, std::wstring_view translation);
        std::wstring translate(std::wstring_view text) const;
        size_t size() const { return translations_.size(); }
    };

    struct Phrase
    {
        std::wstring_view russian, english, ukrainian;
    };

    std::span<const Phrase> phrases();
    const PhraseTranslator &translator(Language language);
    std::wstring localize(std::wstring_view russian, Language language);
    bool has_cyrillic(std::wstring_view text);
}
