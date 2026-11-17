#pragma once
#include "localization.hpp"
#include <array>
#include <filesystem>
#include <future>
#include <string>
#include <string_view>
#include <vector>
#include <utility>

namespace Promzona
{
    struct World;
    struct AudioLevels;
    enum class Density;

    // Population of the next new game; a running game keeps the density it was made with.
    const wchar_t *density_title(Density density);
    const char *density_code(Density density);
    Density density_from_code(std::string_view code, Density fallback);
    Density cycle_density(Density density, int step);
    Density load_density(const std::filesystem::path &file, Density fallback);
    [[nodiscard]] bool save_density(const std::filesystem::path &file, Density density);

    enum class SceneMode
    {
        original,
        enhanced,
        scaled
    };

    struct GraphicsPreferences
    {
        static constexpr int minimum = 130, maximum = 3000;
        int distance = 1000, quality = 2;
        bool effects = true, bloom = true;
        static GraphicsPreferences load(const std::filesystem::path &file);
        bool save(const std::filesystem::path &file) const;
    };

    class SaveSlots
    {
        std::filesystem::path first_;
        std::future<void> writer_;
        int writing_{};

    public:
        explicit SaveSlots(std::filesystem::path first) : first_(std::move(first)) {}
        std::filesystem::path path(int slot) const;
        bool occupied(int slot) const;
        void write(const World &world, int slot);
        void finish();
        [[nodiscard]] bool completed();
    };

    enum class MenuPage
    {
        home,
        settings,
        confirmNew,
        confirmSave
    };

    enum class MenuItem
    {
        resume,
        newGame,
        slot,
        load,
        save,
        settings,
        quit,
        distance,
        quality,
        effects,
        bloom,
        display,
        master,
        radio,
        language,
        population,
        nextTrack,
        back,
        confirm
    };

    struct MenuRow
    {
        MenuItem item;
        std::wstring label, value;
        bool enabled = true;
    };

    struct FrontEnd
    {
        MenuPage page = MenuPage::home;
        int focus{}, selected = 1, active = 1;
        std::array<bool, 3> occupied{};
        Language language{Language::russian};
        Density population{};

        void open(const MenuPage next)
        {
            page = next;
            focus = 0;
        }

        std::vector<MenuRow> rows(bool playing, const GraphicsPreferences &, SceneMode, const AudioLevels &) const;
    };

    struct MenuLayout
    {
        int x, y, width, step;
        MenuLayout(int width_px, int height_px, int count);
        int hit(int mouse_x, int mouse_y, int count) const;
    };
}
