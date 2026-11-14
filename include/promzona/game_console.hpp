#pragma once
#include <span>
#include <string>
#include <vector>

namespace Promzona
{
    struct World;

    class GameConsole
    {
        std::wstring input_, draft_;
        std::vector<std::wstring> recall_, output_;
        size_t caret_{}, historyPos_{}, scroll_{};
        void write(std::wstring line);

      public:
        const std::wstring &input() const { return input_; }
        size_t caret() const { return caret_; }
        std::span<const std::wstring> visibleLines(size_t count) const;
        void insert(wchar_t c);
        void move(int delta);
        void erase(bool backwards);
        void history(int direction);
        void scroll(int lines);
        void complete();
        std::wstring submit();
        void execute(World &world, const std::wstring &line);
    };
}
