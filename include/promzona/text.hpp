#pragma once
#include <filesystem>
#include <string>
#include <string_view>

namespace Promzona
{
    // Game text is held as std::wstring: UTF-16 on Windows, UTF-32 elsewhere.
    // These helpers convert it the same way on every system.
    std::wstring wide(std::string_view utf8Text);
    std::string utf8(std::wstring_view text);

    // A path given as UTF-8 (command line, compile-time constant) on any system.
    std::filesystem::path utf8_path(std::string_view utf8Text);

    // The code point at `at`, advancing past it. UTF-16 surrogate pairs are joined;
    // an unpaired surrogate reads as U+FFFD.
    char32_t next_code_point(std::wstring_view text, size_t &at);
}
