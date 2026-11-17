#include "promzona/game_console.hpp"
#include "promzona/world.hpp"
#include <algorithm>
#include <exception>

namespace Promzona
{
    namespace
    {
        constexpr std::wstring_view usages[] = {
            L"help",
            L"clear",
            L"money 0..999999999",
            L"health",
            L"repair",
            L"god on|off",
            L"weapons",
            L"weapon pistol|automatic|bazooka|flamethrower [1..9999]",
            L"spawn car|van|truck|ambulance|police|rosguard|fsb|btr|tank|helicopter|pills|energy|pistol|automatic|bazooka|flamethrower",
            L"wanted 0..6",
            L"hour 0..24",
            L"rain on|off",
            L"cycle on|off",
            L"speed 0.1..3",
            L"recover",
            L"jobs"};

        std::wstring lowercase(std::wstring text)
        {
            for (wchar_t &c : text)
                if (c >= L'A' && c <= L'Z')
                    c += L'a' - L'A';

            return text;
        }
    }

    void GameConsole::write(std::wstring line)
    {
        if (output_.size() == 100)
            output_.erase(output_.begin());
        output_.push_back(std::move(line));
        scroll_ = 0;
    }

    std::span<const std::wstring> GameConsole::visibleLines(size_t count) const
    {
        size_t end = output_.size() - scroll_, start = end > count ? end - count : 0;
        return std::span(output_).subspan(start, end - start);
    }

    void GameConsole::insert(wchar_t c)
    {
        if (c < 32 || c > 126 || input_.size() >= 160)
            return;
        input_.insert(caret_, 1, c);
        ++caret_;
    }

    void GameConsole::move(int delta)
    {
        caret_ = static_cast<size_t>(std::clamp(static_cast<int>(caret_) + delta, 0, static_cast<int>(input_.size())));
    }

    void GameConsole::erase(bool backwards)
    {
        if (backwards && caret_)
            input_.erase(--caret_, 1);
        else if (!backwards && caret_ < input_.size())
            input_.erase(caret_, 1);
    }

    void GameConsole::history(int direction)
    {
        if (recall_.empty())
            return;
        if (historyPos_ == recall_.size())
            draft_ = input_;
        historyPos_ = static_cast<size_t>(std::clamp(static_cast<int>(historyPos_) + direction, 0, static_cast<int>(recall_.size())));
        input_ = historyPos_ == recall_.size() ? draft_ : recall_[historyPos_];
        caret_ = input_.size();
    }

    void GameConsole::scroll(int lines)
    {
        scroll_ = static_cast<size_t>(std::clamp(static_cast<int>(scroll_) + lines, 0, std::max(0, static_cast<int>(output_.size()) - 1)));
    }

    void GameConsole::complete()
    {
        auto prefix = lowercase(input_);
        std::wstring choices, unique;
        int matches = 0;
        for (auto usage : usages)
        {
            auto name = usage.substr(0, usage.find(L' '));
            if (!name.starts_with(prefix))
                continue;
            if (!choices.empty())
                choices += L"  ";
            choices += name;
            unique = name;
            ++matches;
        }
        if (matches == 1)
        {
            input_ = unique + L" ";
            caret_ = input_.size();
        }
        else if (matches > 1)
        {
            // "wea" offers both weapon and weapons: type on as far as they agree.
            std::wstring common;
            for (auto usage : usages)
            {
                auto name = usage.substr(0, usage.find(L' '));
                if (!name.starts_with(prefix))
                    continue;
                if (common.empty())
                    common = name;
                else
                    common.resize(std::ranges::mismatch(common, name).in1 - common.begin());
            }
            if (common.size() > prefix.size())
            {
                input_ = common;
                caret_ = input_.size();
            }
            write(std::move(choices));
        }
    }

    std::wstring GameConsole::submit()
    {
        auto start = input_.find_first_not_of(L' ');
        std::wstring line;
        if (start != std::wstring::npos)
            line = input_.substr(start, input_.find_last_not_of(L' ') - start + 1);
        if (!line.empty() && (recall_.empty() || recall_.back() != line))
        {
            if (recall_.size() == 50)
                recall_.erase(recall_.begin());
            recall_.push_back(line);
        }
        historyPos_ = recall_.size();
        draft_.clear();
        input_.clear();
        caret_ = 0;
        return line;
    }

    void GameConsole::execute(World &world, const std::wstring &line)
    {
        if (line.empty())
            return;
        std::wstring text = lowercase(line);

        if (text == L"clear")
        {
            output_.clear();
            scroll_ = 0;
            return;
        }

        write(L"> " + line);

        if (text == L"help")
        {
            for (std::wstring_view usage : usages)
                write(std::wstring(usage));
            return;
        }

        std::string command;
        command.reserve(text.size());

        for (wchar_t c : text)
            command.push_back(static_cast<char>(c)); // Input is limited to printable ASCII.

        try
        {
            write(world.command(command));
        }
        catch (const std::exception &e)
        {
            std::string error = e.what();
            write(std::wstring(error.begin(), error.end()));
        }
    }
}
