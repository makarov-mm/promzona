#include "promzona/renderer.hpp"
#include "promzona/audio.hpp"
#include <atomic>
#include <chrono>
#include <fstream>
#include <future>
#include <windows.h>
#include <gl/GL.h>
#include <mmsystem.h>
#include <random>
#include <shellapi.h>
#include <shlobj.h>
#include <thread>

namespace
{
using namespace pz;
struct Application
{
    HWND window{};
    bool running{true}, active{true}, menu{true}, console{}, loading{}, cursorHidden{};
    bool keys[256]{}, pressed[256]{}, firing{};
    int width{1280}, height{720}, wheel{}, cameraMode{};
    double look{}, cameraYaw{}, zoom{20};
    std::wstring command, message;
    std::filesystem::path saveFile;
    void cursor(bool hidden)
    {
        if (hidden == cursorHidden)
            return;
        cursorHidden = hidden;
        if (hidden)
            while (ShowCursor(FALSE) >= 0)
            {
            }
        else
            while (ShowCursor(TRUE) < 0)
            {
            }
    }
};
LRESULT CALLBACK windowProc(HWND window, UINT msg, WPARAM w, LPARAM l)
{
    auto a = reinterpret_cast<Application *>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (msg == WM_NCCREATE)
    {
        a = static_cast<Application *>(reinterpret_cast<CREATESTRUCTW *>(l)->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(a));
    }
    if (!a)
        return DefWindowProcW(window, msg, w, l);
    switch (msg)
    {
    case WM_ERASEBKGND:
    {
        RECT r;
        GetClientRect(window, &r);
        FillRect(reinterpret_cast<HDC>(w), &r, static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
        return 1;
    }
    case WM_CLOSE:
        a->running = false;
        return 0;
    case WM_SIZE:
        a->width = LOWORD(l);
        a->height = HIWORD(l);
        return 0;
    case WM_ACTIVATEAPP:
        a->active = w != 0;
        if (!a->active)
        {
            std::fill(std::begin(a->keys), std::end(a->keys), false);
            a->firing = false;
        }
        a->cursor(a->active && (a->loading || !a->menu) && !a->console);
        return 0;
    case WM_KEYDOWN:
        if (w < 256)
        {
            if (!a->keys[w])
                a->pressed[w] = true;
            a->keys[w] = true;
        }
        return 0;
    case WM_KEYUP:
        if (w < 256)
            a->keys[w] = false;
        return 0;
    case WM_CHAR:
        if (a->console)
        {
            if (w == 8 && !a->command.empty())
                a->command.pop_back();
            else if (w >= 32 && w < 127 && a->command.size() < 160)
                a->command += wchar_t(w);
        }
        return 0;
    case WM_MOUSEWHEEL:
        a->wheel += GET_WHEEL_DELTA_WPARAM(w) / WHEEL_DELTA;
        return 0;
    case WM_LBUTTONDOWN:
        a->firing = true;
        return 0;
    case WM_LBUTTONUP:
        a->firing = false;
        return 0;
    case WM_SETCURSOR:
        if (a->cursorHidden)
        {
            SetCursor(nullptr);
            return TRUE;
        }
        break;
    }
    return DefWindowProcW(window, msg, w, l);
}
struct Context
{
    HWND window{};
    HDC dc{};
    HGLRC gl{};
    explicit Context(HWND w) : window(w), dc(GetDC(w))
    {
        PIXELFORMATDESCRIPTOR p{};
        p.nSize = sizeof p;
        p.nVersion = 1;
        p.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        p.iPixelType = PFD_TYPE_RGBA;
        p.cColorBits = 32;
        p.cDepthBits = 24;
        p.cStencilBits = 8;
        int format = ChoosePixelFormat(dc, &p);
        if (!format || !SetPixelFormat(dc, format, &p))
            throw std::runtime_error("Cannot select OpenGL pixel format");
        auto bootstrap = wglCreateContext(dc);
        if (!bootstrap || !wglMakeCurrent(dc, bootstrap))
            throw std::runtime_error("Cannot create OpenGL context");
        using Create = HGLRC(WINAPI *)(HDC, HGLRC, const int *);
        auto create = reinterpret_cast<Create>(wglGetProcAddress("wglCreateContextAttribsARB"));
        if (create)
        {
            const int attributes[] = {0x2091, 3, 0x2092, 3, 0x9126, 1, 0};
            gl = create(dc, nullptr, attributes);
        }
        wglMakeCurrent(nullptr, nullptr);
        wglDeleteContext(bootstrap);
        if (!gl || !wglMakeCurrent(dc, gl))
            throw std::runtime_error("OpenGL 3.3 is required");
        using Swap = BOOL(WINAPI *)(int);
        if (auto swap = reinterpret_cast<Swap>(wglGetProcAddress("wglSwapIntervalEXT")))
            swap(1);
        glClearColor(0, 0, 0, 1);
        glClear(GL_COLOR_BUFFER_BIT);
        SwapBuffers(dc);
    }
    ~Context()
    {
        wglMakeCurrent(nullptr, nullptr);
        if (gl)
            wglDeleteContext(gl);
        if (dc)
            ReleaseDC(window, dc);
    }
};
std::wstring wide(const std::string &s)
{
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), int(s.size()), nullptr, 0);
    std::wstring r(n, L' ');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), int(s.size()), r.data(), n);
    return r;
}
std::string utf8(const std::wstring &s)
{
    int n = WideCharToMultiByte(CP_UTF8, 0, s.data(), int(s.size()), nullptr, 0, nullptr, nullptr);
    std::string r(n, ' ');
    WideCharToMultiByte(CP_UTF8, 0, s.data(), int(s.size()), r.data(), n, nullptr, nullptr);
    return r;
}
View camera(const World &w, Application &a, double dt)
{
    auto p = w.position();
    double heading = w.controlledCar >= 0 ? w.cars[w.controlledCar].heading() : a.look;
    a.cameraYaw = wrap(a.cameraYaw + wrap(heading - a.cameraYaw) * (1 - std::exp(-dt * 3)));
    float y = float(w.ground());
    View v;
    v.target = {float(p.x), y + 1.3f, float(p.z)};
    if (a.cameraMode == 1)
        v.eye = v.target + Vec3{0, 70, -28};
    else
        v.eye = v.target + Vec3{float(-std::sin(a.cameraYaw) * a.zoom), float(a.zoom * .43),
                                float(-std::cos(a.cameraYaw) * a.zoom)};
    // Sweep the camera segment; stop before roofs, facades or terrain, including a small clearance.
    auto intended = v.eye;
    for (int i = 1; i <= 80; i++)
    {
        float t = i / 80.f;
        auto q = v.target + (intended - v.target) * t;
        bool blocked = w.scenerySolid({q.x, q.z}, q.y, .6);
        for (auto &b : w.buildings)
            if (std::abs(q.x - b.p.x) < b.half.x + .6 && std::abs(q.z - b.p.z) < b.half.z + .6 &&
                q.y > w.terrain.height(b.p) - .6 && q.y < w.terrain.height(b.p) + b.height + .6)
            {
                blocked = true;
                break;
            }
        if (blocked)
        {
            v.eye = v.target + (intended - v.target) * std::max(0.f, t - .03f);
            break;
        }
    }
    v.heading = a.cameraYaw;
    return v;
}
} // namespace
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    Application app;
    int code = 0;
    bool smoke = false;
    int smokeFrames = 180;
    std::wstring scenario = L"day";
    std::filesystem::path screenshot, content, logPath, smokeOutput;
    wchar_t executable[32768]{};
    GetModuleFileNameW(nullptr, executable, 32768);
    auto exeDirectory = std::filesystem::path(executable).parent_path();
    logPath = exeDirectory / "native.log";
    int argc = 0;
    auto argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    bool windowed = false;
    for (int i = 1; i < argc; i++)
    {
        std::wstring arg = argv[i];
        if (arg == L"--smoke")
            smoke = windowed = true;
        else if (arg == L"--windowed")
            windowed = true;
        else if (arg == L"--frames" && i + 1 < argc)
            smokeFrames = std::max(1, _wtoi(argv[++i]));
        else if (arg == L"--screenshot" && i + 1 < argc)
            screenshot = argv[++i];
        else if (arg == L"--content" && i + 1 < argc)
            content = argv[++i];
        else if (arg == L"--scenario" && i + 1 < argc)
            scenario = argv[++i];
        else if (arg == L"--smoke-output" && i + 1 < argc)
            smokeOutput = argv[++i];
    }
    LocalFree(argv);
    std::ofstream log;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    SetProcessDPIAware();
    try
    {
        if (scenario != L"day" && scenario != L"drive" && scenario != L"walk" && scenario != L"night" &&
            scenario != L"canal" && scenario != L"bridge" && scenario != L"overpass" &&
            scenario != L"underpass" && scenario != L"tunnel" && scenario != L"portal")
            throw std::runtime_error("Unknown smoke scenario");
        if (content.empty())
            content = std::filesystem::is_directory(exeDirectory / L"Content")
                          ? exeDirectory / L"Content"
                          : std::filesystem::path(PROMZONA_SOURCE_CONTENT);
        PWSTR local = nullptr;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)))
        {
            app.saveFile = std::filesystem::path(local) / "PromzonaCpp" / "save.json";
            CoTaskMemFree(local);
        }
        else
            app.saveFile = exeDirectory / "save.json";
        if (smoke)
            app.saveFile = (smokeOutput.empty() ? exeDirectory : smokeOutput) / "smoke-save.json";
        std::filesystem::create_directories(app.saveFile.parent_path());
        logPath = app.saveFile.parent_path() / "native.log";
        log.open(logPath);
        if (!log)
            throw std::runtime_error("Cannot open runtime log");
        WNDCLASSW wc{};
        wc.style = CS_OWNDC;
        wc.lpfnWndProc = windowProc;
        wc.hInstance = instance;
        wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(101));
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
        wc.lpszClassName = L"PromzonaCppWindow";
        if (!RegisterClassW(&wc))
            throw std::runtime_error("Window class registration failed");
        int width = windowed ? 1280 : GetSystemMetrics(SM_CXSCREEN),
            height = windowed ? 720 : GetSystemMetrics(SM_CYSCREEN);
        DWORD style = windowed ? WS_OVERLAPPEDWINDOW : WS_POPUP;
        app.window = CreateWindowExW(0, wc.lpszClassName, L"PROMZONA — C++23 migration", style, 0, 0, width,
                                     height, nullptr, nullptr, instance, &app);
        if (!app.window)
            throw std::runtime_error("Window creation failed");
        ShowWindow(app.window, smoke ? SW_SHOWNOACTIVATE : SW_SHOW);
        UpdateWindow(app.window);
        {
            Context context(app.window);
            Renderer renderer(content);
            AudioMixer audio(content / "Audio");
            AudioDevice audioOutput(audio);
            if (!audioOutput.error().empty())
                log << audioOutput.error() << std::endl;
            log << renderer.graphicsInfo() << std::endl;
            std::unique_ptr<World> world;
            std::atomic<float> progress{};
            std::future<std::unique_ptr<World>> generation;
            auto begin = [&](bool load)
            {
                app.loading = true;
                app.menu = false;
                app.console = false;
                app.cursor(app.active);
                progress = 0;
                generation = std::async(std::launch::async,
                                        [&, load]
                                        {
                                            auto next = std::make_unique<World>();
                                            if (load)
                                            {
                                                next->load(app.saveFile);
                                                progress = 1;
                                            }
                                            else
                                                next->generate(smoke ? 20261105u : std::random_device{}(),
                                                               [&](float p) { progress = p; });
                                            return next;
                                        });
            };
            if (smoke)
                begin(false);
            using Timer = std::chrono::steady_clock;
            auto last = Timer::now(), start = last;
            double accumulator = 0, fps = 60, frameAverage = 1. / 60;
            int frames = 0;
            Vec2 smokeStart;
            while (app.running)
            {
                MSG msg;
                while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_QUIT)
                        app.running = false;
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
                if (!app.running)
                    break;
                auto now = Timer::now();
                double elapsed = std::chrono::duration<double>(now - last).count();
                last = now;
                frameAverage += (elapsed - frameAverage) * .05;
                fps = 1 / std::max(.0001, frameAverage);
                double dt = smoke ? 1. / 60 : std::min(.1, elapsed);
                if (app.width < 1 || app.height < 1)
                {
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));
                    continue;
                }
                if (app.pressed[VK_F1] && world && !app.loading)
                {
                    app.console = !app.console;
                    app.firing = false;
                }
                if (app.pressed[VK_ESCAPE] && !app.loading)
                {
                    if (app.console)
                        app.console = false;
                    else if (app.menu)
                    {
                        if (world)
                            app.menu = false;
                        else
                            app.running = false;
                    }
                    else
                        app.menu = true;
                }
                if (!app.loading && app.menu)
                {
                    if (app.pressed[VK_RETURN])
                        begin(false);
                    else if (app.pressed['L'])
                        begin(true);
                }
                if (app.loading && generation.valid() &&
                    generation.wait_for(std::chrono::seconds(0)) == std::future_status::ready)
                {
                    try
                    {
                        world = generation.get();
                        if (smoke)
                        {
                            if (scenario == L"night")
                            {
                                world->clock.setHour(22);
                                world->rain = world->rainTarget = .85;
                                world->weatherClock = 120;
                            }
                            if (scenario == L"walk")
                            {
                                world->enterExit();
                                world->inventory.give(WeaponId::automatic, 1000);
                                app.zoom = 9;
                            }
                            if (scenario == L"canal" || scenario == L"bridge")
                            {
                                auto &c = world->cars[0];
                                auto &l = world->layout;
                                c.body.p = {l.coordinate(World::nodes / 3) + 18 - 21, l.coordinate(24) + 1.9};
                                c.body.angle = -pi * .5;
                                c.body.v = {};
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.setHour(10);
                                app.cameraMode = scenario == L"canal" ? 1 : 0;
                            }
                            if (scenario == L"overpass" || scenario == L"underpass" ||
                                scenario == L"tunnel" || scenario == L"portal")
                            {
                                const auto &level =
                                    world->roadLevels[scenario == L"tunnel" || scenario == L"portal" ? 1 : 0];
                                auto &c = world->cars[0];
                                double x = level.x + (scenario == L"portal"     ? -85
                                                      : scenario == L"overpass" ? -36
                                                      : scenario == L"tunnel"   ? -24
                                                                                : 0);
                                c.body.p = {x, level.centreZ(x) + 1.9};
                                c.body.angle = -pi * .5;
                                c.body.v = {};
                                c.body.roadLevel = scenario == L"underpass" ? 0 : level.id;
                                c.body.height = world->surface(c.body.p, c.body.roadLevel) + .8;
                                c.lift = c.verticalVelocity = 0;
                                world->clock.setHour(10);
                                app.zoom = scenario == L"underpass" ? 12 : scenario == L"tunnel" ? 7 : 24;
                            }
                            smokeStart = world->position();
                        }
                        renderer.overlay(nullptr, app.width, app.height, fps, false, false, L"", L"", .96f);
                        SwapBuffers(context.dc);
                        audio.clear();
                        renderer.prepare(*world);
                        app.look = app.cameraYaw =
                            world->controlledCar >= 0 ? world->cars[world->controlledCar].heading() : 0;
                        app.loading = false;
                        accumulator = 0;
                        last = Timer::now();
                    }
                    catch (const std::exception &e)
                    {
                        app.message = wide(e.what());
                        log << e.what() << std::endl;
                        app.loading = false;
                        app.menu = true;
                        if (smoke)
                            throw;
                    }
                }
                app.cursor(app.active && (app.loading || !app.menu) && !app.console);
                if (world && !app.loading && !app.menu)
                {
                    if (app.console)
                    {
                        if (app.pressed[VK_RETURN])
                        {
                            try
                            {
                                world->command(utf8(app.command));
                                app.message = L"OK";
                            }
                            catch (const std::exception &e)
                            {
                                app.message = wide(e.what());
                            }
                            app.command.clear();
                        }
                    }
                    else if (app.active || smoke)
                    {
                        if (app.pressed['F'])
                            world->enterExit();
                        if (app.pressed['R'])
                            world->recover();
                        if (app.pressed['V'])
                            app.cameraMode = 1 - app.cameraMode;
                        if (app.wheel)
                            world->inventory.cycle(app.wheel > 0 ? 1 : -1);
                        if (app.pressed[VK_F5])
                        {
                            try
                            {
                                world->save(app.saveFile);
                                app.message = L"Сохранено (F9 — загрузить)";
                            }
                            catch (const std::exception &e)
                            {
                                app.message = wide(e.what());
                            }
                        }
                        if (app.pressed[VK_F9])
                            begin(true);
                        if (!smoke && app.active)
                        {
                            POINT centre{app.width / 2, app.height / 2}, mouse;
                            ClientToScreen(app.window, &centre);
                            GetCursorPos(&mouse);
                            if (world->controlledCar < 0)
                                app.look = wrap(app.look - (mouse.x - centre.x) * .003);
                            SetCursorPos(centre.x, centre.y);
                        }
                        PlayerInput input;
                        input.car = {double(app.keys['W']) - double(app.keys['S']),
                                     double(app.keys['A']) - double(app.keys['D']), false,
                                     app.keys[VK_SPACE]};
                        auto f = forward(app.look);
                        input.walking = f * (double(app.keys['W']) - double(app.keys['S'])) +
                                        perpendicular(f) * (double(app.keys['D']) - double(app.keys['A']));
                        input.running = app.keys[VK_SHIFT];
                        input.shooting = app.firing;
                        input.lookHeading = app.look;
                        if (smoke && scenario == L"drive")
                            input.car = {frames < smokeFrames * 2 / 3 ? 1. : 0., .12, false,
                                         frames >= smokeFrames * 2 / 3};
                        if (smoke && scenario == L"walk")
                        {
                            input.walking = {0, 1};
                            input.running = input.shooting = true;
                        }
                        if (smoke && scenario == L"bridge")
                            input.car = {.55, 0, false, false};
                        accumulator += dt;
                        int steps = 0;
                        while (accumulator >= 1. / 120 && steps++ < 12)
                        {
                            world->step(input, 1. / 120);
                            accumulator -= 1. / 120;
                        }
                        if (steps >= 12)
                            accumulator = 0;
                    }
                }
                else
                    accumulator = 0;
                audio.pause(!world || app.loading || app.menu || app.console || (!app.active && !smoke));
                if (world && !app.loading)
                {
                    auto view = camera(*world, app, dt);
                    audio.update(*world, view.heading);
                    renderer.render(*world, view, app.width, app.height);
                }
                else
                {
                    glClearColor(0, 0, 0, 1);
                    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
                }
                renderer.overlay(world.get(), app.width, app.height, fps, app.menu, app.console, app.command,
                                 app.message, app.loading ? progress.load() * .95f : -1);
                if (smoke)
                    if (auto error = glGetError(); error != GL_NO_ERROR)
                        throw std::runtime_error("OpenGL error: " + std::to_string(error));
                if (smoke && world && !app.loading && ++frames >= smokeFrames)
                {
                    double distance = (world->position() - smokeStart).length();
                    if ((scenario == L"drive" || scenario == L"walk" || scenario == L"bridge") &&
                        distance < 1)
                        throw std::runtime_error("Smoke movement did not advance");
                    if (scenario == L"bridge")
                    {
                        const auto &car = world->cars[0];
                        const double riverX = world->layout.coordinate(World::nodes / 3) + 18;
                        if (car.body.p.x <= riverX + world->layout.bankHalf() || car.destroyed ||
                            std::abs(car.body.height - world->terrain.height(car.body.p) - .8) > .5)
                            throw std::runtime_error(
                                "Smoke bridge traversal did not reach the opposite bank on its road surface");
                    }
                    if (scenario == L"walk" &&
                        (world->controlledCar >= 0 || world->inventory.ammo(WeaponId::automatic) >= 1000))
                        throw std::runtime_error("Smoke running/fire input failed");
                    if (scenario == L"overpass" || scenario == L"underpass" || scenario == L"tunnel" ||
                        scenario == L"portal")
                    {
                        auto &c = world->cars[0];
                        int expected = scenario == L"underpass" ? 0 : scenario == L"overpass" ? 1 : 2;
                        if (c.body.roadLevel != expected ||
                            std::abs(world->ground() - world->surface(c.body.p, expected)) > .1)
                            throw std::runtime_error("Smoke support level mismatch");
                    }
                    world->save(app.saveFile);
                    World loaded;
                    loaded.load(app.saveFile);
                    if ((loaded.position() - world->position()).length() > 1e-8 ||
                        loaded.inventory.capture() != world->inventory.capture())
                        throw std::runtime_error("Smoke save/load mismatch");
                    if (!screenshot.empty())
                        renderer.screenshot(screenshot, app.width, app.height);
                    log << "SMOKE PASS frames=" << frames << " cars=" << world->cars.size()
                        << " people=" << world->people.size() << " moved=" << distance
                        << " simulation=" << world->time << " scenario=" << utf8(scenario)
                        << " elapsed=" << std::chrono::duration<double>(now - start).count() << std::endl;
                    app.running = false;
                }
                SwapBuffers(context.dc);
                std::fill(std::begin(app.pressed), std::end(app.pressed), false);
                app.wheel = 0;
            }
        }
    }
    catch (const std::exception &e)
    {
        log << "FATAL: " << e.what() << std::endl;
        if (!smoke)
            MessageBoxW(app.window, wide(e.what()).c_str(), L"PROMZONA", MB_OK | MB_ICONERROR);
        code = 1;
    }
    app.cursor(false);
    if (app.window)
        DestroyWindow(app.window);
    CoUninitialize();
    return code;
}
