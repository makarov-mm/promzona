#include "platform.hpp"
#include <SDL3/SDL.h>
#include <cmath>
#include <cstdlib>
#include <stdexcept>
#include <string_view>

namespace Promzona
{
namespace
{
    [[noreturn]] void fail(std::string_view what)
    {
        throw std::runtime_error(std::string(what) + ": " + SDL_GetError());
    }

    // Keys are read by position, as the Windows game read them on a QWERTY or ЙЦУКЕН layout.
    int key_code(SDL_Scancode s)
    {
        if (s >= SDL_SCANCODE_A && s <= SDL_SCANCODE_Z)
            return 'A' + (s - SDL_SCANCODE_A);
        if (s >= SDL_SCANCODE_1 && s <= SDL_SCANCODE_9)
            return '1' + (s - SDL_SCANCODE_1);
        if (s >= SDL_SCANCODE_F1 && s <= SDL_SCANCODE_F12)
            return Key::f1 + (s - SDL_SCANCODE_F1);
        switch (s)
        {
        case SDL_SCANCODE_0:
            return '0';
        case SDL_SCANCODE_RETURN:
        case SDL_SCANCODE_KP_ENTER:
            return Key::enter;
        case SDL_SCANCODE_ESCAPE:
            return Key::escape;
        case SDL_SCANCODE_BACKSPACE:
            return Key::back;
        case SDL_SCANCODE_TAB:
            return Key::tab;
        case SDL_SCANCODE_SPACE:
            return Key::space;
        case SDL_SCANCODE_LEFTBRACKET:
            return Key::leftBracket;
        case SDL_SCANCODE_RIGHTBRACKET:
            return Key::rightBracket;
        case SDL_SCANCODE_GRAVE:
            return Key::grave;
        case SDL_SCANCODE_HOME:
            return Key::home;
        case SDL_SCANCODE_END:
            return Key::end;
        case SDL_SCANCODE_PAGEUP:
            return Key::pageUp;
        case SDL_SCANCODE_PAGEDOWN:
            return Key::pageDown;
        case SDL_SCANCODE_DELETE:
            return Key::del;
        case SDL_SCANCODE_LEFT:
            return Key::left;
        case SDL_SCANCODE_RIGHT:
            return Key::right;
        case SDL_SCANCODE_UP:
            return Key::up;
        case SDL_SCANCODE_DOWN:
            return Key::down;
        case SDL_SCANCODE_LSHIFT:
        case SDL_SCANCODE_RSHIFT:
            return Key::shift;
        case SDL_SCANCODE_LCTRL:
        case SDL_SCANCODE_RCTRL:
            return Key::control;
        default:
            return 0;
        }
    }

    // Decodes the UTF-8 of a text-input event into code points.
    template <class F> void each_code_point(const char *text, F &&f)
    {
        while (*text)
        {
            const Uint32 c = SDL_StepUTF8(&text, nullptr);
            if (c)
                f(int(c));
        }
    }
}

struct GameWindow::Impl
{
    SDL_Window *window{};
    SDL_GLContext context{};
    double motionX{}, motionY{};
    float wheel{};
    bool minimized{}, typing{};

    explicit Impl(const WindowOptions &o)
    {
        SDL_SetHint(SDL_HINT_APP_NAME, "PROMZONA");
        SDL_SetHint(SDL_HINT_WINDOWS_INTRESOURCE_ICON, "101");
        SDL_SetHint(SDL_HINT_WINDOWS_INTRESOURCE_ICON_SMALL, "101");
        if (o.background)
            SDL_SetHint(SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN, "0");
        if (!SDL_InitSubSystem(SDL_INIT_VIDEO))
            fail("Video initialisation failed");

        SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 3);
        SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 3);
        SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK, SDL_GL_CONTEXT_PROFILE_CORE);
#ifdef __APPLE__
        // macOS only gives a core context to a forward-compatible request.
        SDL_GL_SetAttribute(SDL_GL_CONTEXT_FLAGS, SDL_GL_CONTEXT_FORWARD_COMPATIBLE_FLAG);
#endif
        SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
        SDL_GL_SetAttribute(SDL_GL_RED_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GL_GREEN_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GL_BLUE_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GL_DEPTH_SIZE, 24);
        SDL_GL_SetAttribute(SDL_GL_STENCIL_SIZE, 8);

        SDL_WindowFlags flags = SDL_WINDOW_OPENGL | SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WINDOW_HIDDEN;
        int width = o.width, height = o.height;
        if (o.borderless)
            flags |= SDL_WINDOW_BORDERLESS;
        else if (o.fullscreen)
        {
            flags |= SDL_WINDOW_FULLSCREEN;
            if (const SDL_DisplayMode *mode = SDL_GetDesktopDisplayMode(SDL_GetPrimaryDisplay()))
            {
                width = mode->w;
                height = mode->h;
            }
        }
        else
            flags |= SDL_WINDOW_RESIZABLE;

        window = SDL_CreateWindow(o.title, width, height, flags);
        if (!window)
            fail("Window creation failed");
        if (o.borderless)
            SDL_SetWindowPosition(window, 0, 0);

        context = SDL_GL_CreateContext(window);
        if (!context)
        {
            SDL_DestroyWindow(window);
            throw std::runtime_error(std::string("OpenGL 3.3 is required: ") + SDL_GetError());
        }
        SDL_GL_MakeCurrent(window, context);
        SDL_GL_SetSwapInterval(o.vsync ? 1 : 0);
        SDL_ShowWindow(window);
        // Text input stays off until the console opens.
        SDL_StopTextInput(window);
    }

    ~Impl()
    {
        if (context)
            SDL_GL_DestroyContext(context);
        if (window)
            SDL_DestroyWindow(window);
        SDL_QuitSubSystem(SDL_INIT_VIDEO);
    }

    // Pointer coordinates arrive in window points; the game works in pixels.
    std::pair<int, int> pixels(float x, float y) const
    {
        const float density = SDL_GetWindowPixelDensity(window);
        return {int(std::lround(x * density)), int(std::lround(y * density))};
    }
};

GameWindow::GameWindow(const WindowOptions &options) : impl_(std::make_unique<Impl>(options)) {}
GameWindow::~GameWindow() = default;

std::vector<InputEvent> GameWindow::poll()
{
    using T = InputEvent::Type;
    std::vector<InputEvent> events;
    SDL_Event e;
    while (SDL_PollEvent(&e))
    {
        switch (e.type)
        {
        case SDL_EVENT_QUIT:
        case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
            events.push_back({T::close});
            break;
        case SDL_EVENT_WINDOW_MINIMIZED:
            impl_->minimized = true;
            events.push_back({T::resize, 0, 0});
            break;
        case SDL_EVENT_WINDOW_RESTORED:
        case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
            impl_->minimized = false;
            events.push_back({T::resize, width(), height()});
            break;
        case SDL_EVENT_WINDOW_FOCUS_GAINED:
            events.push_back({T::focus, 1});
            break;
        case SDL_EVENT_WINDOW_FOCUS_LOST:
            events.push_back({T::focus, 0});
            break;
        case SDL_EVENT_KEY_DOWN:
        case SDL_EVENT_KEY_UP:
            if (const int code = key_code(e.key.scancode))
                events.push_back({e.type == SDL_EVENT_KEY_DOWN ? T::keyDown : T::keyUp, code});
            break;
        case SDL_EVENT_TEXT_INPUT:
            each_code_point(e.text.text, [&](int c) { events.push_back({T::character, c}); });
            break;
        case SDL_EVENT_MOUSE_WHEEL:
        {
            float y = e.wheel.y;
            if (e.wheel.direction == SDL_MOUSEWHEEL_FLIPPED)
                y = -y;
            // Touchpads scroll in fractions; the game counts whole notches.
            impl_->wheel += y;
            const int notches = int(impl_->wheel);
            if (notches)
            {
                impl_->wheel -= float(notches);
                events.push_back({T::wheel, notches});
            }
            break;
        }
        case SDL_EVENT_MOUSE_MOTION:
        {
            const float density = SDL_GetWindowPixelDensity(impl_->window);
            impl_->motionX += e.motion.xrel * density;
            impl_->motionY += e.motion.yrel * density;
            const auto [x, y] = impl_->pixels(e.motion.x, e.motion.y);
            events.push_back({T::mouseMove, x, y});
            break;
        }
        case SDL_EVENT_MOUSE_BUTTON_DOWN:
        case SDL_EVENT_MOUSE_BUTTON_UP:
            if (e.button.button == SDL_BUTTON_LEFT)
            {
                const auto [x, y] = impl_->pixels(e.button.x, e.button.y);
                events.push_back({e.type == SDL_EVENT_MOUSE_BUTTON_DOWN ? T::buttonDown : T::buttonUp, x, y});
            }
            break;
        default:
            break;
        }
    }
    return events;
}

int GameWindow::width() const
{
    int w = 0, h = 0;
    if (!impl_->minimized)
        SDL_GetWindowSizeInPixels(impl_->window, &w, &h);
    return w;
}

int GameWindow::height() const
{
    int w = 0, h = 0;
    if (!impl_->minimized)
        SDL_GetWindowSizeInPixels(impl_->window, &w, &h);
    return h;
}

void GameWindow::swap()
{
    SDL_GL_SwapWindow(impl_->window);
}

void GameWindow::capturePointer(bool captured)
{
    SDL_SetWindowRelativeMouseMode(impl_->window, captured);
    if (captured)
        SDL_HideCursor();
    else
        SDL_ShowCursor();
    impl_->motionX = impl_->motionY = 0;
}

void GameWindow::textInput(bool on)
{
    if (on == impl_->typing)
        return;
    impl_->typing = on;
    if (on)
        SDL_StartTextInput(impl_->window);
    else
        SDL_StopTextInput(impl_->window);
}

std::pair<double, double> GameWindow::takeMotion()
{
    const std::pair motion{impl_->motionX, impl_->motionY};
    impl_->motionX = impl_->motionY = 0;
    return motion;
}

void show_error(const std::string &text)
{
    SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, "PROMZONA", text.c_str(), nullptr);
}

std::filesystem::path executable_directory()
{
    if (const char *base = SDL_GetBasePath())
    {
        const std::string_view path(base);
        return std::filesystem::path(std::u8string(path.begin(), path.end()));
    }
    return std::filesystem::current_path();
}

std::filesystem::path user_data_directory()
{
#ifdef _WIN32
    // The folder of the earlier Windows builds, so their saves stay where they were.
    wchar_t *local = nullptr;
    size_t length = 0;
    if (_wdupenv_s(&local, &length, L"LOCALAPPDATA") == 0 && local)
    {
        std::filesystem::path folder(local);
        std::free(local);
        if (!folder.empty())
            return folder / "PromzonaCpp";
    }
#endif
    // ~/Library/Application Support/PromzonaCpp on macOS, ~/.local/share/PromzonaCpp on Linux.
    if (char *pref = SDL_GetPrefPath(nullptr, "PromzonaCpp"))
    {
        const std::string_view path(pref);
        std::filesystem::path result(std::u8string(path.begin(), path.end()));
        SDL_free(pref);
        return result;
    }
    return executable_directory();
}

Language system_language()
{
    Language result = Language::english;
    int count = 0;
    if (SDL_Locale **locales = SDL_GetPreferredLocales(&count))
    {
        if (count > 0 && locales[0]->language)
        {
            const std::string_view code(locales[0]->language);
            if (code == "ru")
                result = Language::russian;
            else if (code == "uk")
                result = Language::ukrainian;
        }
        SDL_free(locales);
    }
    return result;
}
} // namespace Promzona
