#pragma once
#include "promzona/localization.hpp"
#include <filesystem>
#include <memory>
#include <optional>
#include <string>
#include <vector>

// The window, input, OpenGL context and system queries of the game, implemented with SDL3
// on Windows, Linux and macOS alike.
namespace Promzona
{
    // Key codes of the game's input tables. The values are the Windows virtual-key codes the
    // game was written with: letters and digits are their ASCII capitals.
    namespace Key
    {
        constexpr int back = 0x08, tab = 0x09, enter = 0x0D, shift = 0x10, control = 0x11, escape = 0x1B,
                      space = 0x20, pageUp = 0x21, pageDown = 0x22, end = 0x23, home = 0x24, left = 0x25,
                      up = 0x26, right = 0x27, down = 0x28, del = 0x2E, f1 = 0x70, f2 = 0x71, f3 = 0x72,
                      f4 = 0x73, f5 = 0x74, f6 = 0x75, f7 = 0x76, f8 = 0x77, f9 = 0x78, f10 = 0x79,
                      f11 = 0x7A, f12 = 0x7B, grave = 0xC0, leftBracket = 0xDB, rightBracket = 0xDD;
    }

    struct InputEvent
    {
        enum class Type
        {
            close,
            resize,     // a = width, b = height in pixels; zero while minimised
            focus,      // a = 1 when the game gained focus
            keyDown,    // a = key code; repeats arrive as further key-downs
            keyUp,      // a = key code
            character,  // a = typed code point
            wheel,      // a = notches, positive away from the user
            mouseMove,  // a, b = pointer in pixels
            buttonDown, // a, b = pointer in pixels
            buttonUp,
        } type;
        int a{}, b{};
    };

    struct WindowOptions
    {
        const char *title = "PROMZONA";
        int width = 1280, height = 720;
        bool fullscreen{}, borderless{}, background{}, vsync{true};
    };

    class GameWindow
    {
        struct Impl;
        std::unique_ptr<Impl> impl_;

    public:
        explicit GameWindow(const WindowOptions &options);
        ~GameWindow();
        GameWindow(const GameWindow &) = delete;
        GameWindow &operator=(const GameWindow &) = delete;

        // Translates what the system reported since the last call.
        std::vector<InputEvent> poll();
        // Size of the drawable surface in pixels.
        int width() const;
        int height() const;
        void swap();
        // Hides the pointer and reports raw motion for mouse look while hidden.
        void capturePointer(bool captured);
        // Pointer motion in pixels since the last call.
        std::pair<double, double> takeMotion();
        // Typed characters are reported only while on. Off, a held key is just a key: macOS
        // would otherwise offer its accent picker ("Ŵ 1 2 3") over the game for a held W.
        void textInput(bool on);
    };

    // Shows an error to the player; usable with or without a window.
    void show_error(const std::string &utf8Text);
    // Directory of the executable.
    std::filesystem::path executable_directory();
    // Per-user directory for saves and settings, created if missing.
    std::filesystem::path user_data_directory();
    // The interface language of the system, used before the player picks one.
    Language system_language();
}
