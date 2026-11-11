#include "promzona/renderer.hpp"
#include "promzona/audio.hpp"
#include "promzona/architecture.hpp"
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
    double look{}, lookPitch{}, cameraYaw{}, zoom{20};
    bool jobsOpen{};
    int mouseX{}, mouseY{};
    bool pointerMoved{}, clicked{};
    GameConsole terminal;
    std::vector<std::wstring> submissions;
    std::wstring message;
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
        if (a->console && w != VK_F1 && w != VK_ESCAPE && w != VK_OEM_3)
        {
            switch (w)
            {
            case VK_UP: a->terminal.history(-1); break;
            case VK_DOWN: a->terminal.history(1); break;
            case VK_LEFT: a->terminal.move(-1); break;
            case VK_RIGHT: a->terminal.move(1); break;
            case VK_HOME: a->terminal.move(-160); break;
            case VK_END: a->terminal.move(160); break;
            case VK_DELETE: a->terminal.erase(false); break;
            case VK_BACK: a->terminal.erase(true); break;
            case VK_PRIOR: a->terminal.scroll(6); break;
            case VK_NEXT: a->terminal.scroll(-6); break;
            case VK_TAB: a->terminal.complete(); break;
            case VK_RETURN: a->submissions.push_back(a->terminal.submit()); break;
            }
            return 0;
        }
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
        if (a->console && w != L'~' && w != L'`') a->terminal.insert(wchar_t(w));
        return 0;
    case WM_MOUSEWHEEL:
        if (a->console) a->terminal.scroll(3 * GET_WHEEL_DELTA_WPARAM(w) / WHEEL_DELTA);
        else a->wheel += GET_WHEEL_DELTA_WPARAM(w) / WHEEL_DELTA;
        return 0;
    case WM_MOUSEMOVE:
        a->mouseX = static_cast<short>(LOWORD(l));
        a->mouseY = static_cast<short>(HIWORD(l));
        a->pointerMoved = true;
        return 0;
    case WM_LBUTTONDOWN:
        a->mouseX = static_cast<short>(LOWORD(l));
        a->mouseY = static_cast<short>(HIWORD(l));
        a->clicked = a->menu && !a->loading;
        a->firing = !a->menu && !a->loading && !a->console;
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
    explicit Context(HWND w, bool vsync) : window(w), dc(GetDC(w))
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
            swap(vsync ? 1 : 0);
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
    if (w.drone.active)
    {
        const auto &d = w.drone;
        View camera;
        camera.eye = {float(d.position.x), float(d.altitude), float(d.position.z)};
        const auto f = forward(d.heading) * std::cos(d.pitch);
        camera.target = camera.eye + Vec3{float(f.x), float(std::sin(d.pitch)), float(f.z)};
        camera.heading = d.heading;
        return camera;
    }
    auto p = w.position();
    double heading = w.controlledCar >= 0 && !w.transitPassenger() ? w.cars[w.controlledCar].heading() : a.look;
    a.cameraYaw = wrap(a.cameraYaw + wrap(heading - a.cameraYaw) * (1 - std::exp(-dt * 3)));
    float y = float(w.ground());
    View v;
    v.target = {float(p.x), y + 1.3f, float(p.z)};
    if (a.cameraMode == 1)
        v.eye = v.target + Vec3{0, 70, -28};
    else
        v.eye = v.target + Vec3{float(-std::sin(a.cameraYaw) * a.zoom), float(a.zoom * .43),
                                float(-std::cos(a.cameraYaw) * a.zoom)};
    bool mounted=w.controlledCar>=0 && w.cars[w.controlledCar].mountedGun();
    bool aim=w.playerScooter<0 && (w.controlledCar<0 || mounted) && a.cameraMode==0 && a.firing;
    if(aim && mounted) v.target.y=y+2.3f;
    Vec3 sight{float(std::sin(a.look)*std::cos(a.lookPitch)),float(std::sin(a.lookPitch)),float(std::cos(a.look)*std::cos(a.lookPitch))};
    if(aim) v.eye=v.target-sight*(mounted?7.f:3.f)+Vec3{float(-std::cos(a.look)*.35),.12f,float(std::sin(a.look)*.35)};
    // Sweep the camera segment; stop before roofs, facades or terrain, including a small clearance.
    auto clearOffset = [&](Vec3 offset, float radius) {
        for (int i = 1; i <= 80; ++i)
        {
            float t = i / 80.f;
            auto q = v.target + offset * t;
            if (w.scenerySolid({q.x,q.z},q.y,radius) || w.insideBuilding({q.x,q.z},q.y,radius))
                return offset * std::max(0.f, t - .03f);
        }
        return offset;
    };
    auto offset = clearOffset(v.eye - v.target, .6f);
    if (!aim && offset.length() < 2.f)
    {
        // A stop can put the passenger on a narrow pavement with a facade behind.
        // Try the sides before falling back to a view from the character's eyes.
        for (double angle : {pi * .5, -pi * .5, pi})
        {
            const auto side = forward(a.cameraYaw + angle);
            auto candidate = clearOffset({float(side.x * 4), 1.65f, float(side.z * 4)}, .22f);
            if (candidate.length() > offset.length()) offset = candidate;
            if (offset.length() >= 3.f) break;
        }
    }
    v.eye = v.target + offset;
    if(aim) v.target=v.target+sight*45.f;
    else if (offset.length() < .4f)
    {
        const auto ahead = forward(a.cameraYaw);
        v.eye = v.target + Vec3{0,.3f,0};
        v.target = v.eye + Vec3{float(ahead.x),0,float(ahead.z)};
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
    int smokeFrames = 180, captureWidth=0, captureHeight=0;
    std::optional<SceneMode> modeOverride;
    bool profile=false, invalidDisplay=false;
    std::wstring scenario = L"day";
    std::optional<View> reviewView;
    int reviewRider = -1;
    double riderStartDistance{};
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
        else if (arg == L"--gpu-profile") profile=true;
        else if (arg == L"--scene-mode" && i+1 < argc) {
            std::wstring mode=argv[++i];
            if (mode==L"native") modeOverride=SceneMode::original;
            else if (mode==L"quality") modeOverride=SceneMode::enhanced;
            else if (mode==L"balanced") modeOverride=SceneMode::scaled;
            else invalidDisplay=true;
        }
        else if (arg == L"--smoke-size" && i+2 < argc) {
            captureWidth=_wtoi(argv[++i]); captureHeight=_wtoi(argv[++i]);
            if (captureWidth<320 || captureHeight<200 || captureWidth>8192 || captureHeight>8192) invalidDisplay=true;
        }
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
    const bool arrestScenario = scenario == L"arrest" || scenario == L"arrest-car";
    const bool passengerScenario = scenario == L"tram-ride" || scenario == L"trolley-ride";
    std::ofstream log;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    SetProcessDPIAware();
    try
    {
        if (scenario != L"day" && scenario != L"drive" && scenario != L"walk" && scenario != L"storm" && scenario != L"night" &&
            scenario != L"canal" && scenario != L"bridge" && scenario != L"overpass" &&
            scenario != L"underpass" && scenario != L"tunnel" && scenario != L"portal" &&
            scenario != L"facade" && scenario != L"facade-night" && scenario != L"park" &&
            scenario != L"park-unshadowed" && scenario != L"park-evening" && scenario != L"streaming" &&
            !arrestScenario && !passengerScenario && scenario != L"traffic-lanes" && scenario != L"radio-menu" && scenario != L"save-slots" && scenario != L"settings" && scenario != L"market" && scenario != L"workshop" && scenario != L"courier" && scenario != L"pursuit" && scenario != L"transit" && scenario != L"tank" && scenario != L"drone" &&
            scenario != L"leaflets" && scenario != L"taxi" && scenario != L"wildlife" && scenario != L"scooter" && scenario != L"delivery-rider" && scenario != L"ambulance" && scenario != L"hydrant" && scenario != L"phone-booth" && scenario != L"soda-machine" && scenario != L"puddle" && scenario != L"canal-impact" &&
            scenario != L"job-board" && scenario != L"contract-series" && scenario != L"console" && scenario != L"street-hail" &&
            scenario != L"estate" && scenario != L"courtyard" && scenario != L"cottages" &&
            scenario != L"centre" && scenario != L"garages" && scenario != L"rail-yard" &&
            scenario != L"seafront" && scenario != L"canal-mouth")
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
        if (invalidDisplay || (captureWidth && !smoke) || (profile && !smoke))
            throw std::runtime_error("Invalid mode, benchmark dimensions, or --smoke missing");
        SceneMode displayMode=SceneMode::enhanced;
        const auto displayFile=app.saveFile.parent_path() / "display-mode.txt";
        if (!smoke) {
            std::ifstream prefs(displayFile);
            std::string saved;
            if (prefs>>saved) {
                if (saved=="native") displayMode=SceneMode::original;
                if (saved=="balanced") displayMode=SceneMode::scaled;
            }
        }
        if (modeOverride) displayMode=*modeOverride;
        auto modeName=[&]() {
            return displayMode==SceneMode::original ? "native" : displayMode==SceneMode::scaled ? "balanced" : "quality";
        };
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
        if (captureWidth) { width=captureWidth; height=captureHeight; style=WS_POPUP; }
        app.window = CreateWindowExW(0, wc.lpszClassName, L"PROMZONA — C++23 migration", style, 0, 0, width,
                                     height, nullptr, nullptr, instance, &app);
        if (!app.window)
            throw std::runtime_error("Window creation failed");
        ShowWindow(app.window, smoke ? SW_SHOWNOACTIVATE : SW_SHOW);
        UpdateWindow(app.window);
        {
            Context context(app.window, !profile);
            Renderer renderer(content);
            renderer.sceneMode(displayMode);
            const auto graphicsFile = app.saveFile.parent_path() / "graphics.txt";
            auto graphics = smoke ? GraphicsPreferences{} : GraphicsPreferences::load(graphicsFile);
            renderer.graphics(graphics);
            FrontEnd menus;
            SaveSlots slots(app.saveFile);
            auto refreshSlots = [&] {
                for (int i=1;i<=3;++i) menus.occupied[i-1] = slots.occupied(i);
            };
            refreshSlots();
            if (profile) renderer.profileGpu();
            AudioMixer audio(content / "Audio");
            const auto audioFile = app.saveFile.parent_path() / "audio-levels.txt";
            AudioLevels levels = smoke ? AudioLevels{} : AudioLevels::load(audioFile);
            audio.setLevels(levels);
            AudioDevice audioOutput(audio);
            if (!audioOutput.error().empty())
                log << audioOutput.error() << std::endl;
            log << renderer.graphicsInfo() << std::endl;
            std::unique_ptr<World> world;
            std::atomic<float> progress{};
            std::future<std::unique_ptr<World>> generation;
            int pendingSlot = 1;
            double sinceSave = 0;
            bool dirty = false;
            int feedbackSlot = 0;
            auto reportSaveError = [&](const std::exception &e) {
                app.message = L"Ошибка сохранения: " + wide(e.what());
                log << "Save failed: " << e.what() << std::endl;
            };
            auto saveGame = [&](int slot, bool manual) {
                try {
                    slots.write(*world,slot);
                    feedbackSlot = manual ? slot : 0;
                    if (slot == menus.active) { sinceSave = 0; dirty = false; }
                    refreshSlots();
                    return true;
                } catch (const std::exception &e) { reportSaveError(e); return false; }
            };
            auto begin = [&](bool load)
            {
                try { slots.finish(); }
                catch (const std::exception &e) { dirty = true; refreshSlots(); reportSaveError(e); return; }
                if (load && !slots.occupied(menus.selected))
                {
                    app.message = L"Этот слот пуст.";
                    return;
                }
                app.firing = false;
                app.message.clear();
                pendingSlot = menus.selected;
                const auto path = slots.path(pendingSlot);
                app.loading = true;
                app.menu = false;
                app.console = false;
                app.cursor(app.active);
                progress = 0;
                generation = std::async(std::launch::async,
                                        [&, load, path]
                                        {
                                            auto next = std::make_unique<World>();
                                            if (load)
                                            {
                                                next->load(path);
                                                progress = 1;
                                            }
                                            else
                                                next->generate(smoke ? 20261105u : std::random_device{}(),
                                                               [&](float p) { progress = p; });
                                            return next;
                                        });
            };
            if (smoke && scenario != L"save-slots")
                begin(false);
            using Timer = std::chrono::steady_clock;
            auto last = Timer::now(), start = last;
            Timer::time_point clearNoticeAt{};
            std::wstring savedNotice;
            double accumulator = 0, fps = 60, frameAverage = 1. / 60;
            int frames = 0;
            std::vector<double> frameTimes;
            Vec2 smokeStart;
            std::optional<Vec2> requestedPlatform;
            bool trafficPassed = false, officerCaptured = false, consoleReviewed = false;
            bool capturedHome = false;
            Vec2 passingWreckPosition;
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
                if (profile && frames>=60) frameTimes.push_back(elapsed*1000.);
                frameAverage += (elapsed - frameAverage) * .05;
                fps = 1 / std::max(.0001, frameAverage);
                double dt = smoke ? 1. / 60 : std::min(.1, elapsed);
                if (app.width < 1 || app.height < 1)
                {
                    audio.pause(true);
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));
                    continue;
                }
                if (!savedNotice.empty() && now >= clearNoticeAt)
                {
                    if (app.message == savedNotice) app.message.clear();
                    savedNotice.clear();
                }
                try {
                    if (slots.completed())
                    {
                        refreshSlots();
                        if (feedbackSlot)
                        {
                            savedNotice = L"Сохранено в слот " + std::to_wstring(feedbackSlot);
                            app.message = savedNotice;
                            clearNoticeAt = now + std::chrono::seconds(3);
                        }
                        feedbackSlot = 0;
                    }
                } catch (const std::exception &e) {
                    dirty = true;
                    feedbackSlot = 0;
                    refreshSlots();
                    reportSaveError(e);
                }
                if (smoke && world && !app.loading && (scenario==L"settings" || scenario==L"save-slots"))
                {
                    auto key = [&](int vk) {
                        SendMessageW(app.window,WM_KEYDOWN,vk,0);
                        SendMessageW(app.window,WM_KEYUP,vk,0);
                    };
                    auto click = [&](MenuItem target) {
                        auto rows = menus.rows(true,graphics,displayMode,levels);
                        auto it = std::ranges::find(rows,target,&MenuRow::item);
                        if (it == rows.end() || !app.menu) throw std::runtime_error("Smoke menu item unavailable");
                        MenuLayout layout(app.width,app.height,int(rows.size()));
                        int x = layout.x+layout.width*3/4, y=layout.y+(int(it-rows.begin())*2+1)*layout.step/2;
                        SendMessageW(app.window,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(x,y));
                        SendMessageW(app.window,WM_LBUTTONUP,0,MAKELPARAM(x,y));
                    };
                    if (scenario==L"settings")
                    {
                        switch (frames)
                        {
                        case 2: key(VK_ESCAPE); break;
                        case 3: click(MenuItem::settings); break;
                        case 4: key(VK_RIGHT); break;
                        case 5: click(MenuItem::quality); break;
                        case 6: click(MenuItem::effects); break;
                        case 7: click(MenuItem::bloom); break;
                        case 8: click(MenuItem::display); break;
                        case 9: click(MenuItem::master); break;
                        case 10: click(MenuItem::radio); break;
                        case 12: key(VK_ESCAPE); break;
                        case 13: key(VK_ESCAPE); break;
                        }
                    }
                    else
                    {
                        switch (frames)
                        {
                        case 2: world->money=111; key(VK_ESCAPE); break;
                        case 3: key('2'); break;
                        case 4: world->money=222; click(MenuItem::save); break;
                        case 5: if (menus.page==MenuPage::confirmSave) click(MenuItem::confirm); break;
                        case 6: world->money=333; key('L'); break;
                        case 8:
                            if (menus.active!=2 || world->money!=222 || app.menu)
                                throw std::runtime_error("Selected slot failed to restore its snapshot");
                            world->money=444;
                            sinceSave=119.999;
                            break;
                        case 10: world->money=555; key(VK_F9); break;
                        case 12:
                            if (world->money!=444) throw std::runtime_error("Periodic autosave was not loaded");
                            world->money=666;
                            key(VK_F5);
                            break;
                        case 14: key(VK_ESCAPE); break;
                        case 15: key('3'); break;
                        case 16: click(MenuItem::save); break;
                        case 17: if (menus.page==MenuPage::confirmSave) click(MenuItem::confirm); break;
                        }
                    }
                }
                if ((app.pressed[VK_F1] || app.pressed[VK_OEM_3]) && world && !app.loading && !app.menu)
                {
                    app.console = !app.console;
                    app.jobsOpen = false;
                    app.firing = false;
                }
                if (smoke && scenario == L"console" && app.console && !consoleReviewed)
                {
                    for (std::wstring_view cmd : {L"money 35", L"weapon pistol 12", L"jobs"})
                    {
                        for (wchar_t c : cmd) SendMessageW(app.window, WM_CHAR, c, 0);
                        SendMessageW(app.window, WM_KEYDOWN, VK_RETURN, 0);
                    }
                    for (wchar_t c : std::wstring_view(L"wan")) SendMessageW(app.window, WM_CHAR, c, 0);
                    SendMessageW(app.window, WM_KEYDOWN, VK_TAB, 0);
                    consoleReviewed = true;
                }
                if (world && !app.submissions.empty())
                {
                    for (auto &line : app.submissions) app.terminal.execute(*world, line);
                    app.submissions.clear();
                }
                if (app.pressed[VK_ESCAPE] && !app.loading)
                {
                    if (app.console)
                        app.console = false;
                    else if (world && world->openMarket >= 0 && !app.menu)
                        world->openMarket = -1;
                    else if (app.menu)
                    {
                        if (menus.page != MenuPage::home) menus.open(MenuPage::home);
                        else if (world) app.menu = false;
                        else app.running = false;
                    }
                    else
                    {
                        app.menu = true;
                        app.firing = false;
                        menus.open(MenuPage::home);
                        refreshSlots();
                        if (world && dirty) saveGame(menus.active,false);
                    }
                }
                if (!app.console && app.pressed['M']) audio.nextTrack();
                if (!app.loading && app.menu)
                {
                    auto rows = menus.rows(world != nullptr, graphics, displayMode, levels);
                    const int count = int(rows.size());
                    MenuLayout layout(app.width,app.height,count);
                    int hit = layout.hit(app.mouseX,app.mouseY,count);
                    if ((app.clicked || app.pointerMoved) && hit >= 0) menus.focus = hit;
                    const int travel = int(app.pressed[VK_DOWN])-int(app.pressed[VK_UP]);
                    if (travel) menus.focus = (menus.focus + travel + count) % count;
                    if (menus.page == MenuPage::home)
                        for (int i=1;i<=3;++i) if (app.pressed['0'+i]) menus.selected=i;
                    auto item = rows.at(menus.focus).item;
                    int dir = int(app.pressed[VK_RIGHT])-int(app.pressed[VK_LEFT]);
                    bool activate = app.pressed[VK_RETURN] || (app.clicked && hit>=0);
                    if (app.clicked && hit>=0 && !rows[hit].value.empty())
                        dir = app.mouseX < layout.x+layout.width/2 ? -1 : 1;
                    if (menus.page != MenuPage::confirmNew && menus.page != MenuPage::confirmSave)
                    {
                        if (app.pressed[VK_F2]) { item=MenuItem::display; dir=1; }
                        if (menus.page == MenuPage::home && app.pressed['L']) { item=MenuItem::load; activate=true; }
                        if (menus.page == MenuPage::home && app.pressed['Q']) { item=MenuItem::quit; activate=true; }
                    }
                    const int step = dir ? dir : 1;
                    if (activate || dir)
                    {
                        using enum MenuItem;
                        switch (item)
                        {
                        case resume: if (activate) app.menu=false; break;
                        case quit: if (activate) app.running=false; break;
                        case back: if (activate) menus.open(MenuPage::home); break;
                        case settings: if (activate) menus.open(MenuPage::settings); break;
                        case slot: menus.selected=(menus.selected-1+step+3)%3+1; break;
                        case newGame:
                            if (activate && slots.occupied(menus.selected)) menus.open(MenuPage::confirmNew);
                            else if (activate) begin(false);
                            break;
                        case load: if (activate) begin(true); break;
                        case MenuItem::save:
                            if (!activate) break;
                            if (slots.occupied(menus.selected) && menus.selected!=menus.active)
                                menus.open(MenuPage::confirmSave);
                            else saveGame(menus.selected,true);
                            break;
                        case confirm:
                            if (!activate) break;
                            if (menus.page == MenuPage::confirmNew) begin(false);
                            else saveGame(menus.selected,true);
                            menus.open(MenuPage::home);
                            break;
                        case display:
                            displayMode=SceneMode((int(displayMode)+step+3)%3);
                            renderer.sceneMode(displayMode);
                            if ((!smoke || scenario==L"settings") && !(std::ofstream(displayFile) << modeName()))
                                app.message=L"Не удалось сохранить режим изображения.";
                            break;
                        case distance: case quality: case effects: case bloom:
                            if (item==distance) graphics.distance=std::clamp(graphics.distance+50*step,
                                GraphicsPreferences::minimum,GraphicsPreferences::maximum);
                            if (item==quality) graphics.quality=(graphics.quality+step+3)%3;
                            if (item==effects) graphics.effects=!graphics.effects;
                            if (item==bloom && displayMode!=SceneMode::original) graphics.bloom=!graphics.bloom;
                            renderer.graphics(graphics);
                            if ((!smoke || scenario==L"settings") && !graphics.save(graphicsFile))
                                app.message=L"Не удалось сохранить настройки графики.";
                            break;
                        case master: case radio:
                            if (item==master) levels.master=std::clamp(levels.master+5*step,0,100);
                            else levels.radio=std::clamp(levels.radio+5*step,0,50);
                            audio.setLevels(levels);
                            if ((!smoke || scenario==L"settings") && !levels.save(audioFile)) app.message=L"Не удалось сохранить громкость.";
                            break;
                        case nextTrack: if (activate) audio.nextTrack(); break;
                        }
                    }
                }
                if (app.loading && generation.valid() &&
                    generation.wait_for(std::chrono::seconds(0)) == std::future_status::ready)
                {
                    try
                    {
                        world = generation.get();
                        if (smoke)
                        {
                            if (scenario == L"storm")
                            {
                                world->clock.setHour(16);
                                world->rain = world->rainTarget = 1;
                                world->weatherClock = 120;
                                world->time = 95;
                            }
                            if (scenario == L"night")
                            {
                                world->clock.setHour(22);
                                world->rain = world->rainTarget = .85;
                                world->weatherClock = 120;
                            }
                            if (scenario == L"drone")
                            {
                                world->enterExit();
                                world->toggleDrone(0);
                                if (!world->drone.active) throw std::runtime_error("Drone launch failed");
                                const auto &b = world->buildings[world->drone.targets[0]];
                                auto delta = b.p - world->drone.position;
                                app.look = std::atan2(delta.x, delta.z);
                                app.lookPitch = .1;
                            }
                            if (scenario == L"wildlife" || scenario == L"scooter" || scenario == L"delivery-rider")
                            {
                                world->clock.setHour(10);
                                world->rain = world->rainTarget = 0;
                                world->controlledCar = -1;
                                world->player.roadLevel = 0;
                                if (scenario == L"wildlife")
                                {
                                    const auto centre = world->flocks.at(0).landing;
                                    world->player.p = centre + Vec2{0,8};
                                    world->player.height = world->terrain.height(world->player.p)+.85;
                                    View view;
                                    view.target = {float(centre.x),float(world->terrain.height(centre)+.45),float(centre.z)};
                                    view.eye = view.target + Vec3{3,1.9f,4};
                                    reviewView = view;
                                }
                                else
                                {
                                    auto it = std::ranges::find_if(world->scooters,[&](const Scooter &s) {
                                        return world->people[s.owner].courier;
                                    });
                                    if(it==world->scooters.end()) throw std::runtime_error("Missing courier rider");
                                    auto &rider = world->people[it->owner];
                                    if (scenario == L"delivery-rider")
                                    {
                                        reviewRider = it->owner;
                                        riderStartDistance = rider.distance;
                                        world->player.p = rider.body.p + Vec2{0, 8};
                                        world->player.height = world->terrain.height(world->player.p) + .85;
                                    }
                                    else
                                    {
                                        world->releaseScooter(int(it - world->scooters.begin()), {});
                                        world->player.p = it->body.p;
                                        world->player.height = it->body.height + .7;
                                        if (!world->useScooter()) throw std::runtime_error("Could not mount dropped scooter");
                                        auto dir = (rider.target - rider.body.p).unit();
                                        app.look = std::atan2(dir.x, dir.z);
                                    }
                                }
                            }
                            if (scenario == L"hydrant")
                            {
                                auto at = world->position();
                                const World::StreetFixture *chosen = nullptr;
                                for (const auto &f : world->fixtures)
                                    if (f.kind==World::FixtureKind::hydrant && !world->levelAt(f.home,8) &&
                                        (!chosen || (f.home-at).length2()<(chosen->home-at).length2()))
                                        chosen=&f;
                                if (!chosen) throw std::runtime_error("No hydrant for review");
                                auto &car=world->cars.at(world->controlledCar);
                                Vec2 target=chosen->home;
                                auto offset=target-World::nodePoint(0);
                                double dx=std::remainder(offset.x,World::spacing);
                                double dz=std::remainder(offset.z,World::spacing);
                                Vec2 incoming=std::abs(dx)<std::abs(dz)
                                    ? Vec2{std::copysign(1.,dx),0} : Vec2{0,std::copysign(1.,dz)};
                                car.body.p=target-incoming*(car.body.halfH+1.1);
                                car.body.angle=-std::atan2(incoming.x,incoming.z);
                                car.body.height=world->terrain.height(car.body.p)+.8;
                                car.body.v=incoming*11;
                                car.body.wake();
                                world->clock.setHour(11);
                                world->rain=world->rainTarget=0;
                                View view;
                                auto camera=target+perpendicular(incoming)*3.6-incoming*4.5;
                                view.eye={float(camera.x),float(world->terrain.height(camera)+2.35),float(camera.z)};
                                view.target={float(target.x),float(world->terrain.height(target)+1.25),float(target.z)};
                                reviewView=view;
                            }
                            if (scenario == L"canal-impact")
                            {
                                // Fire from the quay between two intersections, through the normal weapon input.
                                const auto &l = world->layout;
                                Vec2 target{l.coordinate(l.nodes()/3) + 18., l.coordinate(23) + 18.};
                                Vec2 bank = target + Vec2{l.bankHalf() + .5, 0};
                                world->controlledCar = -1;
                                world->player.p = bank;
                                world->player.roadLevel = 0;
                                world->player.height = world->terrain.height(bank) + .85;
                                world->inventory.give(WeaponId::pistol, 30);
                                world->rain = world->rainTarget = 0;
                                world->clock.setHour(11);
                                View view;
                                view.eye = {float(target.x - 1.), 1.6f, float(target.z - 6.)};
                                view.target = {float(target.x), float(CityLayout::waterLevel), float(target.z)};
                                view.heading = std::atan2(view.target.x - view.eye.x, view.target.z - view.eye.z);
                                reviewView = view;
                            }
                            if (scenario == L"puddle")
                            {
                                if (world->puddles.empty()) throw std::runtime_error("No road puddles in city");
                                auto closest = std::ranges::min_element(world->puddles,{},[](const auto &p){
                                    return p.centre.length2();
                                });
                                auto target = closest->centre;
                                auto &car = world->cars[0];
                                car.body.p = target + Vec2{.83*car.body.halfW,-5.};
                                car.body.height = world->terrain.height(car.body.p) + .8;
                                car.body.angle = 0;
                                car.body.v = {0,5};
                                world->rain = world->rainTarget = 0;
                                world->clock.setHour(10);
                                View v;
                                auto eye = target + Vec2{-3.8,-4.0};
                                v.eye = {float(eye.x),float(world->terrain.height(eye)+1.95),float(eye.z)};
                                v.target = {float(target.x),float(world->terrain.height(target)+.25),float(target.z+1.8)};
                                reviewView = v;
                            }
                            if (scenario == L"phone-booth" || scenario == L"soda-machine")
                            {
                                const auto kind = scenario == L"soda-machine" ? World::FixtureKind::drinkMachine
                                                                              : World::FixtureKind::phoneBooth;
                                const World::StreetFixture *fixture = nullptr;
                                for (const auto &f : world->fixtures)
                                    if (f.kind == kind &&
                                        (!fixture || f.home.length2()<fixture->home.length2())) fixture=&f;
                                if (!fixture) throw std::runtime_error("Requested street fixture missing in generated city");
                                Vec2 eye=fixture->home+Vec2{-2.6,-4.25};
                                double ground=world->terrain.height(fixture->home);
                                View v;
                                v.eye={float(eye.x),float(ground+1.55),float(eye.z)};
                                v.target={float(fixture->home.x),float(ground+1.20),float(fixture->home.z)};
                                reviewView=v;
                                auto &c=world->cars[0];
                                c.body.p=eye+Vec2{-4,-4};
                                c.body.v={};
                                c.body.height=world->terrain.height(c.body.p)+.8;
                                world->clock.setHour(10);
                            }
                            if (scenario == L"traffic-lanes" || arrestScenario)
                            {
                                // Only place the vehicles. Steering, braking and lane choice are normal game AI.
                                int from = -1;
                                for (int row = 17; row < 32 && from < 0; ++row)
                                    for (int col = 18; col < 30; ++col)
                                    {
                                        int n = row * World::nodes + col;
                                        auto links = world->neighbours(n);
                                        auto mid = World::nodePoint(n) + Vec2{18, 0};
                                        if (std::ranges::find(links, n + 1) == links.end() ||
                                            world->levelAt(mid, 27) || !world->layout.wide() ||
                                            std::ranges::any_of(world->cars, [&](const Car &c) {
                                                return (c.body.p - mid).length2() < 28 * 28;
                                            })) continue;
                                        from = n;
                                        break;
                                    }
                                if (from < 0) throw std::runtime_error("Missing clear four-lane street");
                                auto origin = World::nodePoint(from);
                                if (arrestScenario)
                                {
                                    world->controlledCar = -1;
                                    world->player.p = origin + Vec2{18,3.5};
                                    world->player.v = {};
                                    world->player.height = world->terrain.height(world->player.p) + .85;
                                    if (scenario == L"arrest-car")
                                    {
                                        world->controlledCar = 0;
                                        auto &car = world->cars.front().body;
                                        car.p = world->player.p;
                                        car.v = {};
                                        car.angle = -pi / 2;
                                        car.roadLevel = 0;
                                        car.height = world->surface(car.p, 0) + .8;
                                    }
                                    world->wanted = 1;
                                    world->wantedCooldown = 100;
                                    world->money = 300;
                                    world->inventory.give(WeaponId::pistol);
                                    world->updateDispatch();
                                    auto unit = std::ranges::find(world->cars, true, &Car::responding);
                                    if (unit == world->cars.end()) throw std::runtime_error("No arrest unit");
                                    unit->body.p = origin + Vec2{14,-1.2};
                                    unit->body.angle = -pi/2;
                                    unit->body.height = world->terrain.height(unit->body.p) + .8;
                                    unit->body.v = {};
                                    world->rain = world->rainTarget = 0;
                                    world->clock.setHour(11);
                                    View view;
                                    auto eye = origin + Vec2{24,-5};
                                    view.eye = {float(eye.x),float(world->terrain.height(eye)+4),float(eye.z)};
                                    view.target = {float(origin.x+16),float(unit->body.height+.45),float(origin.z+1)};
                                    reviewView = view;
                                }
                                else
                                {
                                    auto &driver = world->cars.at(1);
                                    driver.routeFrom = from;
                                    driver.routeTo = from + 1;
                                    driver.routePhase = 1;
                                    driver.laneIndex = 0;
                                    world->newRoute(driver);
                                    auto place = [&](Car &c, double x, double z) {
                                        c.body.p = origin + Vec2{x,z};
                                        c.body.angle = -pi / 2;
                                        c.body.roadLevel = 0;
                                        c.body.height = world->surface(c.body.p, 0) + .8;
                                        c.body.v = {};
                                    };
                                    place(driver, 9, CityLayout::lane);
                                    auto &wreck = world->cars.at(2);
                                    place(wreck, 25, CityLayout::lane);
                                    wreck.destroy();
                                    passingWreckPosition = wreck.body.p;
                                    place(world->cars.at(0), 2, CityLayout::lane);
                                    world->clock.setHour(11);
                                    world->rain = world->rainTarget = 0;
                                    world->weatherClock = 120;
                                    auto eye = origin + Vec2{6, -6};
                                    View view;
                                    view.eye = {float(eye.x),float(world->terrain.height(eye)+9),float(eye.z)};
                                    view.target = {float(wreck.body.p.x-3),float(wreck.body.height),float(wreck.body.p.z+1)};
                                    reviewView = view;
                                }
                            }
                            if (scenario == L"ambulance")
                            {
                                auto unit = std::ranges::find_if(world->cars,[&](const Car &c) {
                                    return c.kind==4 && !c.body.roadLevel && !world->levelAt(c.body.p,15);
                                });
                                if(unit==world->cars.end()) throw std::runtime_error("Missing ambulance");
                                auto side=rotate(Vec2{1,0},unit->body.angle);
                                auto at=unit->body.p+side*3.4;
                                world->clock.setHour(10);
                                world->rain=world->rainTarget=0;
                                world->controlledCar=-1;
                                world->player.p=at+side*8;
                                world->player.height=world->terrain.height(world->player.p)+.85;
                                auto &patient=world->people.at(15);
                                world->resumeWalk(patient,at,0);
                                world->killPerson(patient);
                                world->time+=8;
                                world->updateRescues();
                                world->updateRescues();
                                View view;
                                view.target={float(at.x),float(patient.body.height+.25),float(at.z)};
                                auto camera=at+side*4.6+forward(unit->heading())*5.5;
                                view.eye={float(camera.x),float(world->terrain.height(camera)+2.6),float(camera.z)};
                                reviewView=view;
                            }
                            if (scenario == L"tank")
                            {
                                world->command("spawn tank");
                                world->controlledCar=int(world->cars.size()-1);
                                app.look=world->cars.back().heading()+.46;
                                app.zoom=12;
                                world->clock.setHour(14);
                            }
                            if (passengerScenario)
                            {
                                auto service = std::ranges::find(world->cars, scenario == L"tram-ride" ? 9 : 10, &Car::kind);
                                if (service == world->cars.end()) throw std::runtime_error("No vehicle on smoke route");
                                auto &c = *service;
                                const auto &line = world->lines.at(c.line);
                                bool found = false;
                                world->controlledCar = -1;
                                for (int stop=0; stop<int(line.stops.size()); ++stop)
                                {
                                    auto [p,heading] = line.pose(line.stops[stop]);
                                    if (std::ranges::any_of(world->cars,[&](const Car &other){
                                        return other.id != c.id && (other.body.p-p).length2()<32*32;})) continue;
                                    c.along = line.stops[stop];
                                    c.body.p = p;
                                    c.body.height = world->terrain.height(p)+.8;
                                    c.body.angle = -heading;
                                    c.body.v = {};
                                    c.nextStop = (stop+1)%int(line.stops.size());
                                    c.dwell = 3;
                                    world->player.p = line.curb(stop);
                                    world->player.height = world->terrain.height(world->player.p)+.85;
                                    if (world->boardableTransit() == int(c.id)) {found = true; break;}
                                }
                                if (!found) throw std::runtime_error("No accessible stop in smoke city");
                                world->clock.setHour(13);
                                app.zoom = 17;
                            }
                            if (scenario == L"transit")
                            {
                                auto it=std::ranges::find(world->cars,9,&Car::kind);
                                if(it==world->cars.end()) throw std::runtime_error("Missing tram in generated city");
                                world->controlledCar=int(it-world->cars.begin());
                                app.zoom=18;
                                world->clock.setHour(13);
                            }
                            if (scenario == L"pursuit")
                            {
                                world->command("wanted 6");
                                for(int n=0;n<24;++n) world->updateDispatch();
                                world->clock.setHour(12);
                            }
                            if (scenario == L"leaflets")
                            {
                                world->enterExit();
                                if (!world->startContract(JobKind::leaflets)) throw std::runtime_error("No leaflet route");
                                world->player.p=world->jobTarget();
                                world->player.height=world->terrain.height(world->player.p)+.85;
                                world->interactJob();
                            }
                            if (scenario == L"street-hail")
                            {
                                world->updateHailers();
                                int actor = world->hailers.front().actor;
                                if (actor < 0) throw std::runtime_error("No pedestrian hailed a taxi");
                                auto &c = world->cars.at(world->controlledCar);
                                c.body.angle = 0;
                                c.body.p = world->people[actor].body.p - Vec2{c.body.halfW + 1.25, 0};
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                c.body.v = {};
                            }
                            if (scenario == L"taxi")
                            {
                                if (!world->startContract(JobKind::taxi)) throw std::runtime_error("Taxi unavailable");
                                auto &c = world->cars[world->controlledCar];
                                const auto &person = world->people[world->job.actor];
                                // Park with the passenger on the kerb side of this street.
                                c.body.p = person.body.p + rotate({c.body.halfW + .68,0},c.body.angle);
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                c.body.v = {};
                                world->interactJob();
                            }
                            if (scenario == L"market" || scenario == L"workshop")
                            {
                                const bool shopping = scenario == L"market";
                                const int index = world->nearestSite(shopping ? ServiceKind::market : ServiceKind::garage, {});
                                if (index < 0) throw std::runtime_error("No trading site in generated city");
                                const auto &site = world->sites[index];
                                const Vec2 front = (site.entrance - site.centre).unit();
                                if (shopping)
                                {
                                    world->controlledCar = -1;
                                    world->player.p = site.entrance;
                                    world->player.height = world->terrain.height(site.entrance) + .85;
                                    world->money = 220;
                                    world->interactJob();
                                    if (!world->buyAtMarket(ShopItem::pistol) || world->money != 100)
                                        throw std::runtime_error("Purchase at street market failed");
                                }
                                else
                                {
                                    auto &car = world->cars[world->controlledCar];
                                    car.body.p = site.entrance + front*3.8;
                                    car.body.angle = pi*.43;
                                    car.body.height = world->terrain.height(car.body.p) + .8;
                                    car.body.v = {};
                                    car.health = .45;
                                    world->wanted = 2;
                                    world->money = 600;
                                    world->useOwnedGarage();
                                    world->interactJob();
                                    if (world->ownedGarage != index || car.paint <= 0 || world->money != 100)
                                        throw std::runtime_error("Workshop purchase or repaint failed");
                                }
                                auto centre = site.centre + front*(shopping ? 1.5 : site.halfDepth+1.7);
                                auto eye = centre + front*(shopping ? 6.3 : 8.) + Vec2{shopping ? 3.3 : 6.,0};
                                View view;
                                view.eye = {float(eye.x),float(world->terrain.height(eye) + (shopping ? 2.5 : 3.5)),float(eye.z)};
                                view.target = {float(centre.x),float(world->terrain.height(centre) + 1.2),float(centre.z)};
                                reviewView = view;
                                world->clock.setHour(11);
                            }
                            if (scenario == L"courier")
                            {
                                int source = world->nearestSite(ServiceKind::kiosk,world->position());
                                if (source < 0) throw std::runtime_error("Missing kiosk in smoke city");
                                const auto &s = world->sites[source];
                                world->controlledCar = -1;
                                world->player.p = s.entrance;
                                world->player.height = world->terrain.height(s.entrance)+.85;
                                world->health = .4;
                                world->money = 60;
                                world->buyFood();
                                world->interactJob();
                                if (world->job.kind != JobKind::courier || world->money != 45)
                                    throw std::runtime_error("Courier or shop unavailable in gameplay");
                                Vec3 at{float(s.centre.x),float(world->terrain.height(s.centre)+1.15),float(s.centre.z)};
                                View v;
                                v.eye = at + Vec3{-3.5f,1.95f,-6.5f};
                                v.target = at;
                                reviewView = v;
                                world->clock.setHour(11);
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
                            if (scenario == L"facade" || scenario == L"facade-night")
                            {
                                const Building *selected = nullptr;
                                for (const auto &b : world->buildings)
                                    if (b.height > 16 && (!selected || b.p.length2() < selected->p.length2()))
                                        selected = &b;
                                if (!selected)
                                    throw std::runtime_error("No facade review building");
                                auto plan = planBuilding(*selected, world->terrain.height(selected->p));
                                Vec3 n = plan.windowsOnZ ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
                                auto r = cross({0, 1, 0}, n);
                                float depth = plan.windowsOnZ ? plan.halfDepth : plan.halfWidth;
                                auto front = plan.origin + n * depth;
                                View v;
                                v.eye = front + n * 17 + r * 7 + Vec3{0, 6, 0};
                                v.target = front + Vec3{0, 7.5f, 0};
                                v.heading = std::atan2(v.target.x - v.eye.x, v.target.z - v.eye.z);
                                reviewView = v;
                                auto &c = world->cars[0];
                                auto parking = front + n * 8;
                                c.body.p = {parking.x, parking.z};
                                c.body.v = {};
                                c.body.roadLevel = 0;
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.setHour(scenario == L"facade-night" ? 22 : 10);
                                world->rain = world->rainTarget = 0;
                                world->weatherClock = 120;
                            }
                            if (scenario == L"park" || scenario == L"park-unshadowed" ||
                                scenario == L"park-evening")
                            {
                                const TreePlant *selected = nullptr;
                                for (const auto &t : world->trees)
                                    if (t.kind == TreeKind::birch &&
                                        (!selected || Vec2{t.foot.x, t.foot.z}.length2() <
                                                          Vec2{selected->foot.x, selected->foot.z}.length2()))
                                        selected = &t;
                                if (!selected)
                                    throw std::runtime_error("No tree review location");
                                View v;
                                v.target = selected->foot + Vec3{0, 4, 0};
                                v.eye = selected->foot + Vec3{13, 7, 17};
                                v.shadows = scenario != L"park-unshadowed";
                                reviewView = v;
                                auto &c = world->cars[0];
                                c.body.p = {v.eye.x, v.eye.z};
                                c.body.v = {};
                                c.body.roadLevel = 0;
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.setHour(scenario == L"park-evening" ? 17.5 : 10);
                                world->rain = world->rainTarget = 0;
                                world->weatherClock = 120;
                            }
                            const std::array<std::wstring_view, 8> locations{
                                L"estate",  L"courtyard", L"cottages", L"centre",
                                L"garages", L"rail-yard", L"seafront", L"canal-mouth"};
                            auto location = std::ranges::find(locations, scenario);
                            if (location != locations.end())
                            {
                                const auto &l = world->layout;
                                const int place = int(location - locations.begin());
                                Vec2 eye{}, target{};
                                float eyeHeight = 8, targetHeight = 3;
                                if (place < 5)
                                {
                                    static constexpr std::array<Vec2, 5> centres{
                                        {{24, 40}, {36, 11}, {12, 12}, {22, 25}, {9, 34}}};
                                    double best = 1e30;
                                    for (const auto &b : world->buildings)
                                    {
                                        const int i = int(std::floor((b.p.x - l.coordinate(0)) / l.spacing));
                                        const int j = int(std::floor((b.p.z - l.coordinate(0)) / l.spacing));
                                        if (int(l.district(i, j)) != place || CityLayout::clinic(i, j) ||
                                            CityLayout::station(i, j) || !l.road(i, j, 1) ||
                                            world->levelAt(b.p, 22))
                                            continue;
                                        double distance =
                                            (Vec2{double(i), double(j)} - centres[place]).length2();
                                        if (distance >= best)
                                            continue;
                                        best = distance;
                                        auto centre = l.point(i + j * l.nodes()) + Vec2{18, 18};
                                        target = place == 4 ? centre + Vec2{-3, 4.1} : centre;
                                        eye = place == 4 ? centre + Vec2{16, .5} : centre + Vec2{-22, -24};
                                    }
                                    if (best == 1e30)
                                        throw std::runtime_error("District review street missing");
                                    eyeHeight = place == 4   ? 3.4f
                                                : place == 2 ? 8.f
                                                : place == 1 ? 13.f
                                                             : 18.f;
                                    targetHeight = place == 0 || place == 3 ? 8.f : place == 4 ? 1.4f : 3.f;
                                }
                                else if (place == 5)
                                {
                                    target = {l.railEnd() - 38, l.railZ()};
                                    eye = target + Vec2{24, -12};
                                    eyeHeight = 8;
                                    targetHeight = 2.8f;
                                }
                                else if (place == 6)
                                {
                                    target = {l.coordinate(22) + 18, l.edge() - 38};
                                    eye = target + Vec2{-15, -10};
                                    eyeHeight = 4.6f;
                                    targetHeight = 1.2f;
                                }
                                else
                                {
                                    target = {l.coordinate(l.nodes() / 3) + 18, l.edge() - 19};
                                    eye = target + Vec2{-18, -25};
                                    eyeHeight = 8;
                                    targetHeight = .5f;
                                }
                                auto elevated = [&](Vec2 p, float h)
                                {
                                    return Vec3{
                                        float(p.x),
                                        float(std::max(world->terrain.height(p), CityLayout::waterLevel)) + h,
                                        float(p.z)};
                                };
                                View view;
                                view.eye = elevated(eye, eyeHeight);
                                view.target = elevated(target, targetHeight);
                                view.heading =
                                    std::atan2(view.target.x - view.eye.x, view.target.z - view.eye.z);
                                reviewView = view;
                                // Keep simulation and streamed detail local to the camera. Its eye may be
                                // inside a building footprint or directly above the estuary.
                                const auto observerOrigin = eye + (eye - target).unit() * 5;
                                std::optional<Vec2> observer;
                                for (int radius = 0; radius <= 48 && !observer; radius += 2)
                                    for (int k = 0; k < 12; ++k)
                                    {
                                        const double a = k * pi / 6;
                                        const auto p = observerOrigin + Vec2{std::cos(a), std::sin(a)} * radius;
                                        const double floor = world->terrain.height(p);
                                        if (floor <= CityLayout::waterLevel + 1 || world->openCut(p, 1) ||
                                            world->scenerySolid(p, floor + .85, .55) ||
                                            world->insideBuilding(p, floor + .85, .55))
                                            continue;
                                        observer = p;
                                        break;
                                    }
                                if (!observer)
                                    throw std::runtime_error("No dry vantage point near review camera");
                                world->controlledCar = -1;
                                world->player.p = *observer;
                                world->player.v = {};
                                world->player.roadLevel = 0;
                                world->player.verticalSpeed = 0;
                                world->player.height = world->terrain.height(*observer) + .85;
                                world->clock.setHour(15);
                                world->rain = world->rainTarget = 0;
                                world->weatherClock = 120;
                            }
                            smokeStart = world->position();
                        }
                        renderer.overlay(nullptr, app.width, app.height, fps, false, nullptr, L"", levels, .96f);
                        SwapBuffers(context.dc);
                        audio.clear();
                        renderer.prepare(*world);
                        app.jobsOpen = false;
                        app.lookPitch = world->drone.active ? world->drone.pitch : 0;
                        app.look = world->controlledCar >= 0 ? world->cars[world->controlledCar].heading()
                                                             : -world->player.angle;
                        if (world->drone.active)
                            app.look = world->drone.heading;
                        app.cameraYaw = app.look;
                        app.loading = false;
                        menus.active = pendingSlot;
                        menus.open(MenuPage::home);
                        sinceSave = 0;
                        dirty = true;
                        if (smoke && passengerScenario) app.pressed['B'] = true;
                        if (smoke && scenario == L"radio-menu") { app.menu = true; menus.open(MenuPage::settings); }
                        if (smoke && (scenario == L"job-board" || scenario == L"contract-series"))
                        {
                            app.pressed['J'] = true;
                            app.pressed['8'] = scenario == L"contract-series";
                        }
                        if (smoke && scenario == L"console")
                        {
                            PostMessageW(app.window, WM_KEYDOWN, VK_F1, 0);
                            PostMessageW(app.window, WM_KEYUP, VK_F1, 0);
                        }
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
                    if (!app.console && (app.active || smoke))
                    {
                        const int passengerVehicle = world->transitPassenger() ? world->controlledCar : -1;
                        if (smoke && passengerScenario && frames == 570)
                            app.pressed['F'] = true;
                        if (!app.jobsOpen && world->openMarket < 0 && !world->drone.active && app.pressed['B'])
                        {
                            if (world->rideTransit() && world->transitPassenger())
                                app.look = world->cars[world->controlledCar].heading();
                        }
                        if (world->openMarket < 0 && app.pressed['K'])
                        {
                            bool active = world->drone.active;
                            world->toggleDrone(app.look);
                            if (world->drone.active) { app.lookPitch = world->drone.pitch; app.jobsOpen = false; }
                            else if (active) { app.look = -world->player.angle; app.lookPitch = 0; }
                        }
                        if (world->openMarket < 0 && !world->drone.active && app.pressed['F'] && (world->playerScooter >= 0 || !world->buyFood()))
                        {
                            int previous = world->controlledCar;
                            world->enterExit();
                            if(previous!=world->controlledCar)
                            {
                                int vehicle=world->controlledCar>=0 ? world->controlledCar : previous;
                                app.look=world->cars[vehicle].heading();
                                app.lookPitch=0;
                            }
                        }
                        if (!world->drone.active && app.pressed['E'] && !app.jobsOpen)
                            world->interactJob();
                        if (world->openMarket >= 0)
                        {
                            for (int i = 0; i < 4; ++i)
                                if (app.pressed['1' + i]) { world->buyAtMarket(static_cast<ShopItem>(i)); break; }
                        }
                        else if (!app.jobsOpen && app.pressed['G'])
                            world->useOwnedGarage();
                        if (world->openMarket < 0 && !world->drone.active && app.pressed['J'])
                            app.jobsOpen = !app.jobsOpen;
                        if (app.jobsOpen && (app.pressed['1'] || app.pressed['2'] || app.pressed['3'] || app.pressed['4'] || app.pressed['5'] || app.pressed['6'] || app.pressed['7'] || app.pressed['8']))
                        {
                            if (app.pressed['8']) world->startSeries();
                            else if (app.pressed['3'] && world->job.kind != JobKind::none)
                                world->finishJob(false);
                            else if (app.pressed['1'] || app.pressed['2'])
                                world->startContract(app.pressed['1'] ? JobKind::vehicle : JobKind::elimination);
                            else if (app.pressed['4'] || app.pressed['5'])
                                world->startContract(app.pressed['4'] ? JobKind::leaflets : JobKind::broadcast);
                            else if (app.pressed['6'] || app.pressed['7'])
                                world->startContract(app.pressed['6'] ? JobKind::taxi : JobKind::patient);
                            app.jobsOpen = false;
                        }
                        if (!world->drone.active && app.pressed['R'])
                            world->recover();
                        if (!world->drone.active && app.pressed['V'])
                            app.cameraMode = 1 - app.cameraMode;
                        if (app.wheel)
                            world->inventory.cycle(app.wheel > 0 ? 1 : -1);
                        if (app.pressed[VK_F5]) saveGame(menus.active,true);
                        if (app.pressed[VK_F9])
                            begin(true);
                        if (!smoke && app.active)
                        {
                            POINT centre{app.width / 2, app.height / 2}, mouse;
                            ClientToScreen(app.window, &centre);
                            GetCursorPos(&mouse);
                            if (world->controlledCar < 0 || world->transitPassenger() || world->cars[world->controlledCar].mountedGun())
                            {
                                app.look = wrap(app.look - (mouse.x - centre.x) * .003);
                                app.lookPitch=clamp(app.lookPitch-(mouse.y-centre.y)*.003,-1.1,1.1);
                            }
                            SetCursorPos(centre.x, centre.y);
                        }
                        PlayerInput input;
                        input.car = {double(app.keys['W']) - double(app.keys['S']),
                                     double(app.keys['A']) - double(app.keys['D']), false,
                                     app.keys[VK_SPACE]};
                        auto f = forward(app.look);
                        input.walking = f * (double(app.keys['W']) - double(app.keys['S'])) +
                                        perpendicular(f) * (double(app.keys['D']) - double(app.keys['A']));
                        input.collective = double(app.keys[VK_SPACE]) - double(app.keys[VK_CONTROL]);
                        input.running = app.keys[VK_SHIFT];
                        input.shooting = app.firing && world->openMarket < 0;
                        input.lookHeading = app.look;
                        input.lookPitch = app.lookPitch;
                        if (smoke && scenario == L"drone") input.collective=1;
                        if (smoke && scenario == L"scooter") input.walking=forward(app.look);
                        if (smoke && scenario == L"wildlife" && frames == 115)
                        {
                            auto centre = world->flocks.at(0).landing;
                            world->scareWildlife(centre,world->terrain.height(centre),0);
                        }
                        if (smoke && scenario == L"canal-impact")
                        {
                            const auto target = reviewView->target;
                            Vec2 delta = Vec2{target.x, target.z} - world->player.p;
                            input.lookHeading = std::atan2(delta.x, delta.z);
                            input.lookPitch = std::atan2(target.y - world->player.height - .47, delta.length());
                            input.shooting = frames % 80 == 8;
                        }
                        if (smoke && scenario == L"tank")
                            input.shooting=frames>=150;
                        if (smoke && (scenario == L"drive" || scenario == L"storm"))
                            input.car = {frames < smokeFrames * 2 / 3 ? 1. : 0., .12, false,
                                         frames >= smokeFrames * 2 / 3};
                        if (smoke && scenario == L"walk")
                        {
                            input.walking = {0, 1};
                            input.running = input.shooting = true;
                        }
                        if (smoke && passengerScenario)
                        {
                            input.car = {-1.,1.,true,true};
                            app.look = world->controlledCar >= 0 ? world->cars[world->controlledCar].heading()+.95 : app.look;
                        }
                        if (smoke && scenario == L"puddle") input.car={.18,0,false,false};
                        if (smoke && scenario == L"transit") input.car={.8,0,false,false};
                        if (smoke && (scenario == L"traffic-lanes" || scenario == L"arrest-car")) input.car.handbrake = true;
                        if (smoke && scenario == L"bridge")
                            input.car = {.55, 0, false, false};
                        dirty = true;
                        sinceSave += dt;
                        if (sinceSave >= 120)
                        {
                            sinceSave = 0;
                            saveGame(menus.active,false);
                        }
                        accumulator += dt * world->simulationSpeed;
                        const int arrestsBefore = world->arrests;
                        bool droneWasActive = world->drone.active;
                        const int maxSteps = int(std::ceil(12 * std::max(1., world->simulationSpeed)));
                        int steps = 0;
                        while (accumulator >= 1. / 120 && steps < maxSteps)
                        {
                            ++steps;
                            world->step(input, 1. / 120);
                            if (world->arrests != arrestsBefore) input.lookHeading = -world->player.angle;
                            accumulator -= 1. / 120;
                        }
                        if (passengerVehicle >= 0 && world->controlledCar < 0)
                        {
                            // Face down the pavement on arrival. The passenger may have been
                            // looking through the side window straight at the facade.
                            app.cameraYaw = app.look = world->cars[passengerVehicle].heading();
                            app.lookPitch = 0;
                        }
                        if (world->arrests != arrestsBefore) app.cameraYaw = -world->player.angle;
                        if (world->arrests != arrestsBefore || (droneWasActive && !world->drone.active))
                        {
                            app.look = -world->player.angle;
                            app.lookPitch = 0;
                        }
                        if (steps == maxSteps && accumulator >= 1. / 120)
                            accumulator = std::fmod(accumulator, 1. / 120);
                    }
                }
                else
                    accumulator = 0;
                audio.pause(!app.active && !smoke, !world || app.loading || app.menu || app.console);
                if (world && !app.loading && !app.menu)
                {
                    auto view = camera(*world, app, dt);
                    if (smoke && passengerScenario && (view.eye-view.target).length() < .39f)
                        throw std::runtime_error("Passenger camera lost its look direction");
                    if (smoke && arrestScenario && world->arrests > 0) reviewView.reset();
                    if (smoke && reviewView)
                        view = *reviewView;
                    if (reviewRider >= 0)
                    {
                        const auto &rider = world->people.at(reviewRider);
                        Vec2 f = forward(rider.heading);
                        view.target = {float(rider.body.p.x), float(rider.body.height + .1), float(rider.body.p.z)};
                        // Film from the carriageway side of the sidewalk. At a junction either side
                        // can face a wall, a tree or a parked car, so test the actual sightline.
                        double best = -1e9;
                        for (double angle : {1.1, -1.1, 1.8, -1.8, .5, -.5, 2.6, -2.6})
                        {
                            Vec2 offset = rotate(-f, angle) * 4.6, eye = rider.body.p + offset;
                            double eyeHeight = std::max(world->surface(eye, rider.body.roadLevel) + 1.8,
                                                     rider.body.height + 1.4);
                            double score = -world->layout.roadDistance(eye) - .15 * std::abs(angle);
                            double length = offset.length();
                            auto hit = world->traceAttack(rider.body.p, offset / length, view.target.y,
                                                          (eyeHeight - view.target.y) / length, length, -1, true, &rider);
                            if (hit.kind != HitKind::none)
                                score -= 50. + length - hit.distance;
                            if (score > best)
                            {
                                view.eye = {float(eye.x), float(eyeHeight), float(eye.z)};
                                best = score;
                            }
                        }
                    }
                    if (smoke && scenario == L"wildlife" && frames >= 115)
                    {
                        auto it = std::ranges::find(world->animals,0,&Animal::flock);
                        Vec3 centre{float(it->p.x),float(it->altitude+.25),float(it->p.z)};
                        view.target=centre; view.eye=centre+Vec3{3,1.9f,4};
                    }
                    if (smoke && scenario == L"streaming")
                    {
                        // Far jumps stress cancellation/eviction; the last stop revisits the first.
                        constexpr Vec2 stops[] = {
                            {-600, -600}, {600, 600}, {-600, 600}, {600, -600}, {-600, -600}};
                        const int phase = std::min(4, frames / 180);
                        const Building *selected = nullptr;
                        // Only apartments have a separate streamed detail mesh. A cottage or
                        // warehouse is already complete in its district's base mesh.
                        for (const auto &b : world->buildings)
                            if (b.kind == BuildingKind::apartment && (!selected ||
                                (b.p - stops[phase]).length2() < (selected->p - stops[phase]).length2()))
                                selected = &b;
                        if (!selected) throw std::runtime_error("No apartments on streaming test route");
                        if (frames % 180 == 0)
                            log << "Streaming camera " << phase << ": apartment at " << selected->p.x << ", "
                                << selected->p.z << " (distance to district corner " << (selected->p-stops[phase]).length()
                                << " m)" << std::endl;
                        auto plan = planBuilding(*selected, world->terrain.height(selected->p));
                        Vec3 normal = plan.windowsOnZ ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
                        auto front =
                            plan.origin + normal * (plan.windowsOnZ ? plan.halfDepth : plan.halfWidth);
                        view.target = front + Vec3{0, 5, 0};
                        view.eye = front + normal * 23 + cross({0, 1, 0}, normal) * 12 + Vec3{0, 8, 0};
                        world->clock.setHour(10);
                        world->rain = world->rainTarget = 0;
                    }
                    if(smoke && scenario==L"pursuit")
                    {
                        auto unit=std::ranges::find_if(world->cars,[](const Car &c){return c.responding && c.kind==12;});
                        if(unit==world->cars.end() || unit->lift<5) throw std::runtime_error("Missing airborne pursuit unit");
                        view.target={float(unit->body.p.x),float(unit->body.height+.4),float(unit->body.p.z)};
                        view.eye=view.target+Vec3{10,4,-12};
                    }
                    Vec2 facing{view.target.x - view.eye.x, view.target.z - view.eye.z};
                    audio.update(*world, facing.length2() > .0001 ? std::atan2(facing.x, facing.z) : view.heading);
                    renderer.render(*world, view, app.width, app.height);
                    if (smoke && scenario == L"traffic-lanes")
                    {
                        const auto &driver = world->cars.at(1);
                        auto relative = driver.body.p - passingWreckPosition;
                        if (!trafficPassed && driver.laneIndex == 1 && relative.x >= .5 && relative.z > 2.5)
                        {
                            trafficPassed = true;
                            renderer.screenshot(smokeOutput / "pass.bmp", app.width, app.height);
                        }
                    }
                    if (smoke && scenario == L"streaming")
                    {
                        auto stats = renderer.streamingStats();
                        if (stats.peakGpuBytes > 64 * 1024 * 1024 || stats.queued > 8 || stats.ready > 2)
                            throw std::runtime_error("Streaming exceeded its memory/queue budget");
                        if (frames % 180 == 179)
                        {
                            if (stats.residentCells == 0)
                                throw std::runtime_error(
                                    "Streaming left a visited neighbourhood without details");
                            log << "Stop " << frames / 180 << ": " << renderer.sceneryStats() << std::endl;
                            renderer.screenshot(smokeOutput /
                                                    ("stop-" + std::to_string(frames / 180) + ".bmp"),
                                                app.width, app.height);
                        }
                    }
                }
                else
                {
                    glClearColor(0, 0, 0, 1);
                    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
                }
                renderer.overlay(world.get(), app.width, app.height, fps, app.menu, app.console ? &app.terminal : nullptr,
                                 app.message, levels, app.loading ? progress.load() * .95f : -1, app.jobsOpen, &menus);
                if (smoke && scenario == L"save-slots" && !world && app.menu && !app.loading)
                {
                    if (!capturedHome)
                    {
                        renderer.screenshot(smokeOutput / "main-menu.bmp",app.width,app.height);
                        capturedHome = true;
                    }
                    const auto rows = menus.rows(false,graphics,displayMode,levels);
                    const auto item = menus.page == MenuPage::home ? MenuItem::newGame : MenuItem::confirm;
                    const auto row = std::ranges::find(rows,item,&MenuRow::item);
                    if (row==rows.end()) throw std::runtime_error("New game action missing");
                    const MenuLayout layout(app.width,app.height,int(rows.size()));
                    const int x=layout.x+layout.width/2, y=layout.y+(int(row-rows.begin())*2+1)*layout.step/2;
                    PostMessageW(app.window,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(x,y));
                    PostMessageW(app.window,WM_LBUTTONUP,0,MAKELPARAM(x,y));
                }
                if (smoke && scenario==L"settings" && app.menu && frames==11)
                    renderer.screenshot(smokeOutput / "settings.bmp",app.width,app.height);
                if (smoke && arrestScenario && world && !officerCaptured && world->patrol.hold > .15)
                {
                    renderer.screenshot(smokeOutput / "officer.bmp", app.width, app.height);
                    officerCaptured = true;
                }
                if (smoke)
                    if (auto error = glGetError(); error != GL_NO_ERROR)
                        throw std::runtime_error("OpenGL error: " + std::to_string(error));
                if (smoke && world && !app.loading)
                    ++frames;
                if (smoke && passengerScenario && world && !app.loading && frames == 610)
                {
                    if (!world->transitPassenger() || !world->transitExitRequested)
                        throw std::runtime_error("Passenger input did not board and request the next stop");
                    const auto &car = world->cars[world->controlledCar];
                    const auto &line = world->lines[car.line];
                    int departure = (car.nextStop + int(line.stops.size()) - 1) % int(line.stops.size());
                    double travelled = std::remainder(car.along-line.stops[departure],line.track.length());
                    if (travelled < 1 || car.dwell > 0) throw std::runtime_error("AI service failed to leave platform");
                    requestedPlatform = line.curb(car.nextStop);
                    renderer.screenshot(smokeOutput / "onboard.bmp",app.width,app.height);
                    log << "Passenger seat=" << world->playerTransitSeat << " AI distance=" << travelled
                        << " requested stop=" << car.nextStop+1 << std::endl;
                    if (smokeFrames > 610) world->simulationSpeed = 3;
                }
                bool reachedPlatform = requestedPlatform && world && world->controlledCar < 0;
                if (smoke && world && !app.loading && (frames >= smokeFrames || reachedPlatform))
                {
                    if (scenario == L"streaming")
                    {
                        auto stats = renderer.streamingStats();
                        if (stats.evictions < 10 || stats.uploads <= stats.residentCells ||
                            stats.residentCells >= stats.totalCells)
                            throw std::runtime_error(
                                "Streaming route did not exercise eviction and reloading");
                    }
                    if (arrestScenario)
                    {
                        log << "Officer reached suspect=" << officerCaptured << " arrests=" << world->arrests
                            << " fine=" << world->lastFine << " money=" << world->money << std::endl;
                        if (!officerCaptured || world->arrests != 1 || world->lastFine != 100 ||
                            world->money != 200 || world->wantedLevel() || world->controlledCar >= 0 ||
                            (world->position() - world->stationEntrance).length() > 3 || world->inventory.active())
                            throw std::runtime_error("Patrol failed to book and release suspect at station");
                    }
                    if (scenario == L"market" && (world->openMarket < 0 || world->inventory.ammo(WeaponId::pistol) != 30))
                        throw std::runtime_error("Market did not stay open at the counter");
                    if (scenario == L"workshop" && (world->ownedGarage < 0 || !world->cars.at(0).paint || world->wantedLevel()))
                        throw std::runtime_error("Workshop service did not last through gameplay");
                    if (scenario == L"traffic-lanes")
                    {
                        const auto &driver = world->cars.at(1);
                        const auto &wreck = world->cars.at(2);
                        log << "Overtake: passed=" << trafficPassed << " lane=" << driver.laneIndex
                            << " travelX=" << driver.body.p.x-passingWreckPosition.x
                            << " lateral=" << driver.body.p.z-passingWreckPosition.z
                            << " health=" << driver.health << " wreck drift=" << (wreck.body.p-passingWreckPosition).length() << std::endl;
                        if (!trafficPassed || driver.health < .999 || (wreck.body.p-passingWreckPosition).length() > .3)
                            throw std::runtime_error("NPC failed to pass stationary wreck without contact");
                    }
                    if (scenario == L"canal-impact" && world->ripples.empty())
                        throw std::runtime_error("Shots from the quay did not reach the canal surface");
                    if (scenario == L"puddle" && (world->tireContacts.empty() ||
                        world->tireContacts.front().wetUntil <= world->time ||
                        std::ranges::none_of(world->surfaceMarks, [](const auto &m){return m.kind == MarkKind::wet;})))
                        throw std::runtime_error("Drive through standing water left no wet tracks");
                    if (passengerScenario && smokeFrames > 610)
                    {
                        if (!reachedPlatform || (world->player.p-*requestedPlatform).length() > 3.5)
                            throw std::runtime_error("AI service did not deliver passenger to requested curb");
                        log << "Alighted at requested stop, distance to curb centre="
                            << (world->player.p-*requestedPlatform).length() << std::endl;
                    }
                    double distance = (world->position() - smokeStart).length();
                    if ((scenario == L"drive" || scenario == L"walk" || scenario == L"transit" || scenario == L"bridge") &&
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
                    if (scenario == L"drone" && (!world->drone.active || world->drone.altitude < world->ground()+12))
                        throw std::runtime_error("Recon flight or camera did not advance");
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
                    if (scenario == L"storm" && (distance < 1 || world->surfaceMarks.empty()))
                        throw std::runtime_error("No moving traffic left tyre marks during the rain");
                    if (scenario == L"scooter" && (world->playerScooter < 0 || (world->player.p-smokeStart).length()<2))
                        throw std::runtime_error("Scooter ride failed");
                    if (reviewRider >= 0)
                    {
                        const auto &rider = world->people.at(reviewRider);
                        if (world->scooters.at(rider.ownedScooter).rider != reviewRider ||
                            rider.distance - riderStartDistance < 1.5)
                            throw std::runtime_error("Delivery rider did not travel with their scooter");
                    }
                    if (scenario == L"hydrant" && std::ranges::none_of(world->fixtures,[&](const auto &f) {
                        return f.sprayUntil>world->time && (f.home-world->position()).length2()<65*65;
                    })) throw std::runtime_error("Review car failed to dislodge hydrant");
                    if (scenario == L"wildlife" && !world->flocks.at(0).flying)
                        throw std::runtime_error("Flock did not take off");
                    if (scenario == L"ambulance" &&
                        (world->rescues[0].stage!=World::RescueStage::care || world->people[world->rescues[0].medic].riding>=0))
                        {
                        const auto &r = world->rescues[0];
                        throw std::runtime_error("Ambulance review: stage " + std::to_string(int(r.stage)) +
                            ", car " + std::to_string(r.car) + ", patient " + std::to_string(r.patient) +
                            ", simulation time " + std::to_string(world->time) +
                            (r.car >= 0 && r.patient >= 0 ? ", distance " + std::to_string(
                               (world->cars[r.car].body.p - world->people[r.patient].body.p).length()) : ""));
                    }
                    if (scenario == L"console" && (!consoleReviewed || world->money != 35 ||
                        app.terminal.input() != L"wanted " || app.terminal.visibleLines(6).size() != 6))
                        throw std::runtime_error("Console commands or keyboard editing failed");
                    if (scenario == L"job-board" && !app.jobsOpen)
                        throw std::runtime_error("Journal did not open");
                    if (scenario == L"contract-series" && (!world->job.series || world->expectedPay() != 300))
                        throw std::runtime_error("First series contract was not offered");
                    if (scenario == L"street-hail" && world->hailerInReach() < 0)
                        throw std::runtime_error("Hailing pedestrian is unavailable at the kerb");
                    if (scenario == L"taxi" && (!world->job.acquired || world->people[world->job.actor].riding != world->controlledCar))
                        throw std::runtime_error("Taxi passenger did not board");
                    if (scenario == L"leaflets" && world->job.route.completed != 1)
                        throw std::runtime_error("Leaflet handout did not finish");
                    if (scenario == L"tank" && (world->controlledCar<0 ||
                        world->cars[world->controlledCar].kind!=11 || world->shotCooldown==0))
                        throw std::runtime_error("Tank did not fire");
                    if (scenario == L"settings")
                    {
                        const auto disk=GraphicsPreferences::load(graphicsFile);
                        const auto savedAudio=AudioLevels::load(audioFile);
                        std::string savedDisplay;
                        std::ifstream(displayFile) >> savedDisplay;
                        if (app.menu || disk.distance!=1050 || disk.quality!=0 || disk.effects || disk.bloom ||
                            displayMode!=SceneMode::scaled || savedDisplay!="balanced" || savedAudio.master!=70 || savedAudio.radio!=25)
                            throw std::runtime_error("Settings failed to apply or persist");
                        log << "Settings applied during play: range=" << disk.distance << " quality=" << disk.quality
                            << " particles=" << disk.effects << " bloom=" << disk.bloom << " display=" << modeName() << std::endl;
                    }
                    slots.finish();
                    if (scenario == L"save-slots")
                    {
                        World check;
                        for (int i=1;i<=3;++i)
                        {
                            check.load(slots.path(i));
                            if (check.money!=(i==1 ? 111 : 666)) throw std::runtime_error("Save slots leaked state");
                        }
                        if (menus.active!=2 || menus.selected!=3) throw std::runtime_error("Save-as changed active slot");
                        log << "Three independent saves; active=2 selected=3, pause/periodic/F5/F9 verified" << std::endl;
                    }
                    const auto checkPath = scenario == L"save-slots" ? smokeOutput / "roundtrip.json" : app.saveFile;
                    world->save(checkPath);
                    World loaded;
                    loaded.load(checkPath);
                    if ((loaded.position() - world->position()).length() > 1e-8 ||
                        loaded.inventory.capture() != world->inventory.capture())
                        throw std::runtime_error("Smoke save/load mismatch");
                    if (!screenshot.empty())
                        renderer.screenshot(screenshot, app.width, app.height);
                    log << renderer.sceneryStats() << std::endl;
                    if (profile && !frameTimes.empty()) {
                        std::sort(frameTimes.begin(),frameTimes.end());
                        auto at=[&](double p) { return frameTimes[size_t(std::ceil(p*frameTimes.size()))-1]; };
                        log << "frame: samples=" << frameTimes.size() << " median_ms=" << at(.5)
                            << " p95_ms=" << at(.95) << " vsync_requested=0" << std::endl;
                    }
                    if (profile)
                        log << renderer.gpuReport() << " output=" << app.width << "x" << app.height
                            << " mode=" << modeName() << std::endl;
                    log << "SMOKE PASS frames=" << frames << " cars=" << world->cars.size()
                        << " people=" << world->people.size() << " moved=" << distance
                        << " simulation=" << world->time << " scenario=" << utf8(scenario)
                        << " elapsed=" << std::chrono::duration<double>(now - start).count() << std::endl;
                    app.running = false;
                }
                SwapBuffers(context.dc);
                std::fill(std::begin(app.pressed), std::end(app.pressed), false);
                app.wheel = 0;
                app.clicked = app.pointerMoved = false;
            }
            if (!smoke && world && dirty)
                slots.write(*world,menus.active);
            slots.finish();
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
