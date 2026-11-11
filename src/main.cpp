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
    bool mounted=w.controlledCar>=0 && w.cars[w.controlledCar].mountedGun();
    bool aim=w.playerScooter<0 && (w.controlledCar<0 || mounted) && a.cameraMode==0 && a.firing;
    if(aim && mounted) v.target.y=y+2.3f;
    Vec3 sight{float(std::sin(a.look)*std::cos(a.lookPitch)),float(std::sin(a.lookPitch)),float(std::cos(a.look)*std::cos(a.lookPitch))};
    if(aim) v.eye=v.target-sight*(mounted?7.f:3.f)+Vec3{float(-std::cos(a.look)*.35),.12f,float(std::sin(a.look)*.35)};
    // Sweep the camera segment; stop before roofs, facades or terrain, including a small clearance.
    auto intended = v.eye;
    for (int i = 1; i <= 80; i++)
    {
        float t = i / 80.f;
        auto q = v.target + (intended - v.target) * t;
        bool blocked = w.scenerySolid({q.x,q.z},q.y,.6) || w.insideBuilding({q.x,q.z},q.y,.6);
        if (blocked)
        {
            v.eye = v.target + (intended - v.target) * std::max(0.f, t - .03f);
            break;
        }
    }
    if(aim) v.target=v.target+sight*45.f;
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
        if (scenario != L"day" && scenario != L"drive" && scenario != L"walk" && scenario != L"storm" && scenario != L"night" &&
            scenario != L"canal" && scenario != L"bridge" && scenario != L"overpass" &&
            scenario != L"underpass" && scenario != L"tunnel" && scenario != L"portal" &&
            scenario != L"facade" && scenario != L"facade-night" && scenario != L"park" &&
            scenario != L"park-unshadowed" && scenario != L"park-evening" && scenario != L"streaming" &&
            scenario != L"courier" && scenario != L"pursuit" && scenario != L"transit" && scenario != L"tank" && scenario != L"drone" &&
            scenario != L"leaflets" && scenario != L"taxi" && scenario != L"wildlife" && scenario != L"scooter" && scenario != L"delivery-rider" && scenario != L"ambulance" && scenario != L"hydrant")
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
                    else if (world && app.pressed['Q'])
                        app.running = false;
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
                            smokeStart = world->position();
                        }
                        renderer.overlay(nullptr, app.width, app.height, fps, false, false, L"", L"", .96f);
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
                        if (app.pressed['K'])
                        {
                            bool active = world->drone.active;
                            world->toggleDrone(app.look);
                            if (world->drone.active) { app.lookPitch = world->drone.pitch; app.jobsOpen = false; }
                            else if (active) { app.look = -world->player.angle; app.lookPitch = 0; }
                        }
                        if (!world->drone.active && app.pressed['F'] && (world->playerScooter >= 0 || !world->buyFood()))
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
                        if (!world->drone.active && app.pressed['E'])
                            world->interactJob();
                        if (!world->drone.active && app.pressed['J'])
                            app.jobsOpen = !app.jobsOpen;
                        if (app.jobsOpen && (app.pressed['1'] || app.pressed['2'] || app.pressed['3'] || app.pressed['4'] || app.pressed['5'] || app.pressed['6'] || app.pressed['7']))
                        {
                            if (app.pressed['3'] && world->job.kind != JobKind::none)
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
                            if (world->controlledCar < 0 || world->cars[world->controlledCar].mountedGun())
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
                        input.shooting = app.firing;
                        input.lookHeading = app.look;
                        input.lookPitch = app.lookPitch;
                        if (smoke && scenario == L"drone") input.collective=1;
                        if (smoke && scenario == L"scooter") input.walking=forward(app.look);
                        if (smoke && scenario == L"wildlife" && frames == 115)
                        {
                            auto centre = world->flocks.at(0).landing;
                            world->scareWildlife(centre,world->terrain.height(centre),0);
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
                        if (smoke && scenario == L"transit") input.car={.8,0,false,false};
                        if (smoke && scenario == L"bridge")
                            input.car = {.55, 0, false, false};
                        accumulator += dt;
                        bool droneWasActive = world->drone.active;
                        int steps = 0;
                        while (accumulator >= 1. / 120 && steps++ < 12)
                        {
                            world->step(input, 1. / 120);
                            accumulator -= 1. / 120;
                        }
                        if (droneWasActive && !world->drone.active)
                        {
                            app.look = -world->player.angle;
                            app.lookPitch = 0;
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
                        for (const auto &b : world->buildings)
                            if (!selected ||
                                (b.p - stops[phase]).length2() < (selected->p - stops[phase]).length2())
                                selected = &b;
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
                    audio.update(*world, view.heading);
                    renderer.render(*world, view, app.width, app.height);
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
                renderer.overlay(world.get(), app.width, app.height, fps, app.menu, app.console, app.command,
                                 app.message, app.loading ? progress.load() * .95f : -1, app.jobsOpen);
                if (smoke)
                    if (auto error = glGetError(); error != GL_NO_ERROR)
                        throw std::runtime_error("OpenGL error: " + std::to_string(error));
                if (smoke && world && !app.loading && ++frames >= smokeFrames)
                {
                    if (scenario == L"streaming")
                    {
                        auto stats = renderer.streamingStats();
                        if (stats.evictions < 10 || stats.uploads <= stats.residentCells ||
                            stats.residentCells >= stats.totalCells)
                            throw std::runtime_error(
                                "Streaming route did not exercise eviction and reloading");
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
                    if (scenario == L"taxi" && (!world->job.acquired || world->people[world->job.actor].riding != world->controlledCar))
                        throw std::runtime_error("Taxi passenger did not board");
                    if (scenario == L"leaflets" && world->job.route.completed != 1)
                        throw std::runtime_error("Leaflet handout did not finish");
                    if (scenario == L"tank" && (world->controlledCar<0 ||
                        world->cars[world->controlledCar].kind!=11 || world->shotCooldown==0))
                        throw std::runtime_error("Tank did not fire");
                    world->save(app.saveFile);
                    World loaded;
                    loaded.load(app.saveFile);
                    if ((loaded.position() - world->position()).length() > 1e-8 ||
                        loaded.inventory.capture() != world->inventory.capture())
                        throw std::runtime_error("Smoke save/load mismatch");
                    if (!screenshot.empty())
                        renderer.screenshot(screenshot, app.width, app.height);
                    log << renderer.sceneryStats() << std::endl;
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
