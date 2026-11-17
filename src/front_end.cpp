#include "promzona/front_end.hpp"
#include "promzona/audio.hpp"
#include "promzona/world.hpp"
#include <algorithm>
#include <chrono>
#include <fstream>
#include <stdexcept>
#include <format>

namespace Promzona
{
    GraphicsPreferences GraphicsPreferences::load(const std::filesystem::path &file)
    {
        GraphicsPreferences settings;
        std::ifstream in(file);
        if (!(in >> settings.distance >> settings.quality >> settings.effects >> settings.bloom))
            return {};
        settings.distance = std::clamp(settings.distance, minimum, maximum);
        settings.quality = std::clamp(settings.quality, 0, 2);
        return settings;
    }

    bool GraphicsPreferences::save(const std::filesystem::path &file) const
    {
        std::ofstream out(file);
        out << distance << ' ' << quality << ' ' << effects << ' ' << bloom << '\n';
        out.close();
        return bool(out);
    }

    const wchar_t *density_title(Density density)
    {
        return density == Density::max      ? L"Максимальная"
               : density == Density::full   ? L"Полная"
               : density == Density::high   ? L"Высокая"
               : density == Density::medium ? L"Средняя"
                                            : L"Низкая";
    }

    const char *density_code(Density density)
    {
        return density == Density::max      ? "max"
               : density == Density::full   ? "full"
               : density == Density::high   ? "high"
               : density == Density::medium ? "medium"
                                            : "low";
    }

    Density density_from_code(std::string_view code, Density fallback)
    {
        return code == "max"      ? Density::max
               : code == "full"   ? Density::full
               : code == "high"   ? Density::high
               : code == "medium" ? Density::medium
               : code == "low"    ? Density::low
                                  : fallback;
    }

    Density cycle_density(Density density, int step)
    {
        constexpr int levels = std::to_underlying(Density::max) + 1;
        return static_cast<Density>((std::to_underlying(density) + (step < 0 ? levels - 1 : 1)) % levels);
    }

    Density load_density(const std::filesystem::path &file, Density fallback)
    {
        std::ifstream in(file);
        std::string code;
        return in >> code ? density_from_code(code, fallback) : fallback;
    }

    bool save_density(const std::filesystem::path &file, Density density)
    {
        std::ofstream out(file);
        out << density_code(density) << '\n';
        out.close();
        return static_cast<bool>(out);
    }

    std::filesystem::path SaveSlots::path(int slot) const
    {
        if (slot < 1 || slot > 3)
            throw std::out_of_range("Save slot");
        if (slot == 1)
            return first_;
        auto name = first_.stem();
        name += std::format("-{}", slot);
        name += first_.extension();
        return first_.parent_path() / name;
    }

    bool SaveSlots::occupied(int slot) const
    {
        std::error_code ec;
        return (writer_.valid() && writing_ == slot) || std::filesystem::is_regular_file(path(slot), ec);
    }

    void SaveSlots::write(const World &world, int slot)
    {
        finish();
        writer_ = std::async(std::launch::async, world.captureSave(path(slot)));
        writing_ = slot;
    }

    void SaveSlots::finish()
    {
        if (writer_.valid())
            writer_.get();
    }

    bool SaveSlots::completed()
    {
        if (!writer_.valid() || writer_.wait_for(std::chrono::seconds(0)) != std::future_status::ready)
            return false;
        finish();
        return true;
    }

    std::vector<MenuRow> FrontEnd::rows(bool playing, const GraphicsPreferences &g, SceneMode mode, const AudioLevels &audio) const
    {
        using enum MenuItem;
        if (page == MenuPage::confirmNew || page == MenuPage::confirmSave)
            return {{back, L"Назад", {}},
                    {confirm, L"Заменить сохранение", L"Слот " + std::to_wstring(selected)}};
        if (page == MenuPage::settings)
        {
            static constexpr const wchar_t *detail[] = {L"Низкая", L"Средняя", L"Высокая"};
            auto toggle = [](bool on) { return on ? L"Вкл." : L"Выкл."; };
            return {{distance, L"Дальность города", std::to_wstring(g.distance) + L" м"},
                    {quality, L"Детализация", detail[g.quality]},
                    {display, L"Изображение",
                     mode == SceneMode::original ? L"Без обработки · 100%"
                     : mode == SceneMode::scaled ? L"Масштаб · 75%"
                                                 : L"Сглаживание · 100%"},
                    {effects, L"Дождь и частицы", toggle(g.effects)},
                    {bloom, L"Свечение", mode == SceneMode::original ? L"Без обработки" : toggle(g.bloom),
                     mode != SceneMode::original},
                    {master, L"Общая громкость", std::to_wstring(audio.master) + L"%"},
                    {radio, L"Громкость радио", std::to_wstring(audio.radio) + L"%"},
                    {nextTrack, L"Сменить композицию", L"Радио «Пустырь»"},
                    {MenuItem::language, L"Язык", std::wstring(languageTitle(this->language))},
                    {MenuItem::population, L"Население новой игры", density_title(this->population)},
                    {back, L"Назад", {}}};
        }
        std::vector<MenuRow> result;
        if (playing)
            result.push_back({resume, L"Продолжить", {}});
        result.push_back({newGame, L"Новая игра", {}});
        result.push_back(
            {slot, L"Слот сохранения",
             std::to_wstring(selected) + L" / 3   ·   " + (occupied[selected - 1] ? L"Занят" : L"Пуст")});
        result.push_back({MenuItem::load, L"Загрузить", {}, occupied[selected - 1]});
        if (playing)
            result.push_back({MenuItem::save, L"Сохранить в этот слот", {}});
        result.push_back({settings, L"Настройки", {}});
        result.push_back({quit, L"Выйти из игры", {}});
        return result;
    }

    MenuLayout::MenuLayout(int w, int h, int count)
    {
        width = std::min(620, std::max(100, w - 32));
        step = std::clamp((h - 132) / std::max(count, 1), 16, 42);
        x = (w - width) / 2;
        y = std::max(52, (h - count * step) / 2 + 12);
    }

    int MenuLayout::hit(int px, int py, int count) const
    {
        if (px < x || px >= x + width || py < y || py >= y + count * step)
            return -1;
        return (py - y) / step;
    }
}
