#include "promzona/localization.hpp"
#include "promzona/names.hpp"
#include <cctype>
#include <chrono>
#include <fstream>
#include <iostream>
#include <set>
#include <sstream>
#include <stdexcept>

namespace fs = std::filesystem;
namespace
{
void require(bool ok, const std::string &message)
{
    if (!ok)
        throw std::runtime_error(message);
}
// Test sources are UTF-8; the game text is wchar_t on every platform.
std::wstring widen(std::string_view utf8)
{
    std::wstring out;
    for (size_t i = 0; i < utf8.size();)
    {
        const unsigned char c = static_cast<unsigned char>(utf8[i]);
        const int length = c < 0x80 ? 1 : c < 0xe0 ? 2 : c < 0xf0 ? 3 : 4;
        char32_t code = length == 1 ? c : c & (0xff >> (length + 1));
        for (int k = 1; k < length && i + k < utf8.size(); ++k)
            code = (code << 6) | (static_cast<unsigned char>(utf8[i + k]) & 0x3f);
        out.push_back(static_cast<wchar_t>(code));
        i += size_t(length);
    }
    return out;
}
std::string narrow(std::wstring_view text)
{
    std::string out;
    for (wchar_t w : text)
    {
        const auto c = static_cast<char32_t>(w);
        if (c < 0x80)
            out.push_back(char(c));
        else if (c < 0x800)
        {
            out.push_back(char(0xc0 | (c >> 6)));
            out.push_back(char(0x80 | (c & 0x3f)));
        }
        else
        {
            out.push_back(char(0xe0 | (c >> 12)));
            out.push_back(char(0x80 | ((c >> 6) & 0x3f)));
            out.push_back(char(0x80 | (c & 0x3f)));
        }
    }
    return out;
}
// Every wide literal of the game sources that contains Cyrillic text.
std::vector<std::pair<std::string, std::wstring>> gameText(const fs::path &root)
{
    std::vector<std::pair<std::string, std::wstring>> result;
    for (const char *folder : {"src", "include/promzona"})
        for (const auto &entry : fs::directory_iterator(root / folder))
        {
            const auto name = entry.path().filename().string();
            // The catalogue and the name table are the translations themselves.
            if (name == "localization.cpp" || name == "localization_phrases.inc" || name == "names.cpp")
                continue;
            if (entry.path().extension() != ".cpp" && entry.path().extension() != ".hpp")
                continue;
            std::ifstream in(entry.path(), std::ios::binary);
            std::stringstream buffer;
            buffer << in.rdbuf();
            const std::string source = buffer.str();
            for (size_t at = source.find("L\""); at != std::string::npos; at = source.find("L\"", at))
            {
                if (at > 0 &&
                    (std::isalnum(static_cast<unsigned char>(source[at - 1])) || source[at - 1] == '_'))
                {
                    at += 2;
                    continue;
                }
                size_t end = at + 2;
                while (end < source.size() && source[end] != '"')
                    end += source[end] == '\\' ? 2 : 1;
                auto literal = widen(std::string_view(source).substr(at + 2, end - at - 2));
                if (pz::hasCyrillic(literal))
                    result.emplace_back(name, std::move(literal));
                at = end + 1;
            }
        }
    return result;
}
} // namespace

int main(int argc, char **argv)
{
    try
    {
        using pz::Language;
        require(pz::languageFromCode("ru-RU") == Language::russian &&
                    pz::languageFromCode("UK_ua") == Language::ukrainian &&
                    pz::languageFromCode("de") == Language::english &&
                    pz::languageFromCode("") == Language::english,
                "language codes");
        require(pz::cycleLanguage(Language::english, 1) == Language::russian &&
                    pz::cycleLanguage(Language::english, -1) == Language::ukrainian &&
                    pz::cycleLanguage(Language::ukrainian, 5) == Language::english,
                "language cycle wraps");
        const auto file =
            fs::temp_directory_path() /
            ("promzona-language-" +
             std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".txt");
        require(pz::loadLanguage(file, Language::ukrainian) == Language::ukrainian,
                "missing preference uses fallback");
        require(pz::saveLanguage(file, Language::russian) &&
                    pz::loadLanguage(file, Language::english) == Language::russian,
                "language preference round trip");
        fs::remove(file);

        pz::PhraseTranslator words;
        words.add(L"кот", L"cat");
        words.add(L"кот учёный", L"learned cat");
        words.add(L"cat", L"dog");
        words.add(L"м", L"m");
        require(words.translate(L"кот учёный, кот") == L"learned cat, cat", "longest phrase wins");
        require(words.translate(L"котик и скот") == L"котик и скот", "no match inside a word");
        require(words.translate(L"120 м, 5м") == L"120 m, 5m", "units after digits");
        require(words.translate(L"кот") == L"cat", "replacement is not translated again");
        words.add(L"кот", L"tomcat");
        require(words.translate(L"кот") == L"tomcat", "later entry replaces the phrase");

        std::set<std::wstring_view> keys;
        for (const auto &phrase : pz::phrases())
        {
            const auto key = narrow(phrase.russian);
            require(keys.insert(phrase.russian).second, "duplicate catalogue key: " + key);
            require(!phrase.english.empty() && !phrase.ukrainian.empty(), "empty translation: " + key);
            require(!pz::hasCyrillic(phrase.english), "Cyrillic in English text: " + key);
            require(phrase.russian.front() != L' ' && phrase.russian.back() != L' ', "untrimmed key: " + key);
        }
        require(pz::phrases().size() > 200, "catalogue is populated");
        require(pz::localize(L"Новая игра", Language::russian) == L"Новая игра",
                "Russian is the source text");
        require(pz::localize(L"Новая игра", Language::english) == L"New game" &&
                    pz::localize(L"Новая игра", Language::ukrainian) == L"Нова гра",
                "menu row");
        require(pz::localize(L"Задержание · штраф $160 · прошло 4 часа", Language::english) ==
                    L"Arrested · fine $160 · elapsed 4 h",
                "composed notice");
        require(pz::localize(L"Рынок — на ул. Ленина, 120 м на запад.", Language::english) ==
                        L"The market is on Lenina St, 120 m west." &&
                    pz::localize(L"Поликлиника — на ул. Мира, 40 м на юг, милок.", Language::ukrainian) ==
                        L"Поліклініка — на вул. Миру, 40 м на південь, любчику.",
                "directions name the street");
        require(pz::localize(L"Слот 2", Language::english) == L"Slot 2" &&
                    pz::localize(L"Перекус $15", Language::ukrainian) == L"Перекус $15",
                "fragments with numbers");
        require(pz::localize(L"PROMZONA", Language::ukrainian) == L"PROMZONA", "Latin title is untouched");

        require(pz::streetNames().size() == 49 && pz::avenueNames().size() == 49, "name tables");
        require(pz::streetLabel(0) == L"ул. Ленина" && pz::streetLabel(49) == pz::streetLabel(0) &&
                    pz::streetLabel(-1) == pz::streetLabel(48),
                "street index wraps");
        require(pz::localize(pz::streetLabel(0), Language::english) == L"Lenina St" &&
                    pz::localize(pz::avenueLabel(2), Language::ukrainian) == L"вул. Паркова",
                "street names follow the language");
        // Node (24, 24) is the origin of a 49-node city; a point just east of it lies on avenue 24.
        require(pz::streetAt({0, 5}) == pz::streetLabel(24) && pz::streetAt({5, 0}) == pz::avenueLabel(24) &&
                    pz::streetAt({-900, 0}) == pz::avenueLabel(24) &&
                    pz::streetAt({-860, -900}) == pz::streetLabel(0),
                "nearest road of a point");
        for (const auto &name : pz::streetNames())
            require(!pz::hasCyrillic(pz::localize(L"ул. " + std::wstring(name.russian), Language::english)),
                    "street transliteration");

        if (argc == 2)
        {
            const auto text = gameText(argv[1]);
            require(text.size() > 200, "game text was found in the sources");
            for (const auto &[file, literal] : text)
            {
                const auto english = pz::localize(literal, Language::english);
                require(!pz::hasCyrillic(english), "untranslated text in " + file + ": \"" + narrow(literal) +
                                                       "\" -> \"" + narrow(english) + "\"");
            }
            std::cout << text.size() << " game strings have English text\n";
        }
        std::cout << "Localization tests passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << "Localization test failed: " << e.what() << '\n';
        return 1;
    }
}
