#include "promzona/renderer.hpp"
#include "frame_trace.hpp"
#include "promzona/audio.hpp"
#include "promzona/architecture.hpp"
#include "promzona/fleet.hpp"
#include "promzona/frame_blend.hpp"
#include "promzona/sector_stream.hpp"
#include <atomic>
#include <chrono>
#include <fstream>
#include <future>
#include "platform.hpp"
#include "promzona/gl.hpp"
#include "promzona/text.hpp"
#include <SDL3/SDL_main.h>
#include <cstdlib>
#include <deque>
#include <random>
#include <ranges>
#include <thread>
#include <format>
#include <stdexcept>

namespace
{
    using namespace Promzona;

    struct Application
    {
        GameWindow *window{};
        // Synthetic input of the automated scenarios, handled with the next system events.
        std::deque<InputEvent> posted;
        bool running{true}, active{true}, menu{true}, console{}, loading{}, cursorHidden{};
        bool keys[256]{}, pressed[256]{}, firing{};
        int width{1280}, height{720}, wheel{}, cameraMode{};
        double look{}, lookPitch{}, cameraYaw{}, zoom{20};

        // How far from its target the camera stands. It comes in at once and goes back out
        // gently; so far away at first that the first frame only brings it in.
        float camera_reach{1e9f};
        bool jobsOpen{};
        int mouseX{}, mouseY{};
        bool pointerMoved{}, clicked{}, mouseLookReady{};
        GameConsole terminal;
        std::vector<std::wstring> submissions;
        std::wstring message;
        std::filesystem::path saveFile;

        void cursor(bool hidden)
        {
            if (hidden == cursorHidden)
                return;

            cursorHidden = hidden;

            if (window)
                window->capturePointer(hidden);
        }
    };

    void handle(Application *a, const InputEvent &e)
    {
        using T = InputEvent::Type;
        const int w = e.a;
        switch (e.type)
        {
        case T::close:
            a->running = false;
            return;
        case T::resize:
            a->width = e.a;
            a->height = e.b;
            return;
        case T::focus:
            a->active = e.a != 0;
            if (!a->active)
            {
                std::ranges::fill(a->keys, false);
                std::ranges::fill(a->pressed, false);
                a->firing = a->clicked = a->pointerMoved = a->mouseLookReady = false;
                a->wheel = 0;
            }
            a->cursor(a->active && (a->loading || !a->menu) && !a->console);
            return;
        case T::keyDown:
            if (a->console && w != Key::f1 && w != Key::escape && w != Key::grave)
            {
                switch (w)
                {
                case Key::up:
                    a->terminal.history(-1);
                    break;
                case Key::down:
                    a->terminal.history(1);
                    break;
                case Key::left:
                    a->terminal.move(-1);
                    break;
                case Key::right:
                    a->terminal.move(1);
                    break;
                case Key::home:
                    a->terminal.move(-160);
                    break;
                case Key::end:
                    a->terminal.move(160);
                    break;
                case Key::del:
                    a->terminal.erase(false);
                    break;
                case Key::back:
                    a->terminal.erase(true);
                    break;
                case Key::pageUp:
                    a->terminal.scroll(6);
                    break;
                case Key::pageDown:
                    a->terminal.scroll(-6);
                    break;
                case Key::tab:
                    a->terminal.complete();
                    break;
                case Key::enter:
                    a->submissions.push_back(a->terminal.submit());
                    break;
                }
                return;
            }

            if (w > 0 && w < 256)
            {
                if (!a->keys[w])
                    a->pressed[w] = true;
                a->keys[w] = true;
            }
            return;
        case T::keyUp:
            if (w > 0 && w < 256)
                a->keys[w] = false;
            return;
        case T::character:
            if (a->console && w != L'~' && w != L'`')
                a->terminal.insert(static_cast<wchar_t>(w));
            return;
        case T::wheel:
            if (a->console)
                a->terminal.scroll(3 * e.a);
            else
                a->wheel += e.a;
            return;
        case T::mouseMove:
            a->mouseX = e.a;
            a->mouseY = e.b;
            a->pointerMoved = true;
            return;
        case T::buttonDown:
            a->mouseX = e.a;
            a->mouseY = e.b;
            a->clicked = a->menu && !a->loading;
            a->firing = !a->menu && !a->loading && !a->console;
            return;
        case T::buttonUp:
            a->firing = false;
            return;
        }
    }

    View camera(const World &w, Application &a, double dt)
    {
        if (w.dying())
        {
            // Over the body: the camera rises slowly and circles a quarter turn while the
            // caption stands and the picture drains.
            const double t = w.deathSince(), turn = a.cameraYaw + .9 + t * .14;
            const double radius = 3.5 + t * 1.1, rise = 2.6 + t * 2.4;
            View v;
            v.target = {float(w.deathPlace.x), float(w.deathHeight - .55), float(w.deathPlace.z)};
            v.eye = v.target + Vec3{float(std::sin(turn) * radius), float(rise), float(std::cos(turn) * radius)};
            v.heading = turn + pi;
            return v;
        }
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

        Vec2 p = w.position();
        double heading = w.controlledCar >= 0 && !w.transitPassenger() ? w.cars[w.controlledCar].heading() : a.look;
        a.cameraYaw = wrap(a.cameraYaw + wrap(heading - a.cameraYaw) * (1 - std::exp(-dt * 3)));
        float y = static_cast<float>(w.ground());
        View v;
        v.target = {static_cast<float>(p.x), y + 1.3f, static_cast<float>(p.z)};

        // V cycles chase, elevated distant and near top-down views, as in the managed game.
        if (a.cameraMode == 2)
        {
            v.eye = v.target + Vec3{0, 70, -28};
        }
        else if (a.cameraMode == 1)
        {
            const double distance = a.zoom * 2.2, pitch = .72;
            v.eye = v.target + Vec3 {
                static_cast<float>(-std::sin(a.cameraYaw) * std::cos(pitch) * distance),
                static_cast<float>(std::sin(pitch) * distance),
                static_cast<float>(-std::cos(a.cameraYaw) * std::cos(pitch) * distance)
            };
        }
        else
        {
            v.eye = v.target + Vec3 {
                static_cast<float>(-std::sin(a.cameraYaw) * a.zoom),
                static_cast<float>(a.zoom * .43),
                static_cast<float>(-std::cos(a.cameraYaw) * a.zoom)
            };
        }

        const bool on_foot = w.controlledCar < 0 && w.playerScooter < 0 && a.cameraMode == 0;

        // A weapon in the hand brings the camera to the shoulder. The gun of a tank or a carrier
        // leaves it where it is, as in the managed game: taken to the barrel for as long as the
        // button was down, the camera jumped there and back with every shot.
        const bool aim = w.health > 0 && on_foot && w.inventory.active();
        const double pitch = a.lookPitch;

        Vec3 sight {
            static_cast<float>(std::sin(a.look) * std::cos(pitch)),
            static_cast<float>(std::sin(pitch)),
            static_cast<float>(std::cos(a.look) * std::cos(pitch))
        };

        if (on_foot)
        {
            v.eye = v.target - sight * 5.3f + Vec3{static_cast<float>(-std::cos(a.look) * .58), .62f, static_cast<float>(std::sin(a.look) * .58)};
        }

        // Sweep the camera segment; stop before roofs, facades or terrain, including a small clearance.
        auto clear_offset = [&](Vec3 offset, float radius)
        {
            const int steps = std::clamp(static_cast<int>(std::ceil(offset.length() / .22f)), 1, 100);
            for (int i = 1; i <= steps; ++i)
            {
                float t = static_cast<float>(i) / steps;
                auto q = v.target + offset * t;
                if (w.scenerySolid({q.x, q.z}, q.y, radius) || w.inside_building({q.x, q.z}, q.y, radius))
                    return offset * (static_cast<float>(i - 1) / steps);
            }
            return offset;
        };

        Vec3 offset = clear_offset(v.eye - v.target, .6f);

        if (offset.length() < 2.f)
        {
            // A stop can put the passenger on a narrow pavement with a facade behind.
            // Try the sides before falling back to a view from the character's eyes.
            for (double angle : {pi * .5, -pi * .5, pi})
            {
                const auto side = forward(a.cameraYaw + angle);
                auto candidate = clear_offset({float(side.x * 4), 1.65f, float(side.z * 4)}, .22f);
                if (candidate.length() > offset.length())
                    offset = candidate;
                if (offset.length() >= 3.f)
                    break;
            }

            a.camera_reach = offset.length();
        }
        else
        {
            // As in the managed game: the camera comes in at once when something gets in the
            // way and goes back out gently when the view clears. Put back out at once, it
            // jumped with every bridge and every corner of a building that passed between.
            const float clear = offset.length();
            a.camera_reach = clear < a.camera_reach
                                 ? clear
                                 : a.camera_reach + (clear - a.camera_reach) * float(1 - std::exp(-dt * 3));
            offset = offset * (a.camera_reach / clear);
        }

        v.eye = v.target + offset;

        if (aim)
        {
            // Keep the reticle aligned to the firing line, even if a wall shifts the camera sideways.
            v.target = Vec3{float(p.x), float(w.player.height + .47), float(p.z)} + sight * 45.f;
            v.weapon_sight = true;
        }
        else if (on_foot && offset.length() >= .4f)
            v.target = v.target + sight * 3.f;
        else if (offset.length() < .4f)
        {
            const auto ahead = forward(a.cameraYaw);
            v.eye = v.target + Vec3{0, .3f, 0};
            v.target = v.eye + Vec3{float(ahead.x), 0, float(ahead.z)};
        }
        v.distance = std::max(.1f, offset.length());
        v.heading = a.cameraYaw;
        // The sight is drawn over this view where the shot would land: that of a vehicle's gun
        // where the gun is laid, that of a weapon in the hand on its own line of fire.
        v.gun_sight = aim ? w.hand_sight(a.look, pitch) : w.gun_sight(a.look);
        return v;
    }

    bool streetReview(std::wstring_view name)
    {
        return name == L"night-crossing" || name == L"wire-street" || name == L"shadow-avenue";
    }

    Vec2 reviewJunction(const World &world)
    {
        const auto &layout = world.layout;
        double best = 1e30;
        Vec2 result{};
        for (int j = 3; j < layout.nodes() - 3; ++j)
            for (int i = 3; i < layout.nodes() - 3; ++i)
            {
                const int node = i + j * layout.nodes();
                const auto point = layout.point(node);
                if (point.length2() >= best || layout.degree(node) != 4 || world.levelAt(point, 45))
                    continue;
                bool through = true;
                for (int dz = -2; dz <= 2; ++dz)
                    through = through && layout.road(i, j + dz, 2);
                if (through)
                {
                    best = point.length2();
                    result = point;
                }
            }
        if (best == 1e30)
            throw std::runtime_error("No through street for the render review");
        return result;
    }

    // A deterministic lighting fixture. Only rendering sees these five vehicles.
    void renderCrossing(Renderer &renderer, World &world, const View &view, Vec2 junction, int frame,
                        int width, int height)
    {
        std::array<Car, 5> traffic;
        constexpr std::array<unsigned, 5> models{busModel, 0, 3, 1, 2};
        for (size_t i = 0; i < traffic.size(); ++i)
        {
            auto &car = traffic[i];
            const auto &original = world.cars.at(i + 1);
            car.id = original.id;
            car.body.id = original.body.id;
            fit_vehicle(car, models[i]);
            const double sign = i % 2 ? -1. : 1.;
            const double along = sign * (frame * (8. / 60.) - 15. - 12. * i);
            car.body.p = junction + Vec2{-sign * CityLayout::lane, along};
            car.body.angle = sign > 0 ? 0 : pi;
            car.body.v = {0, sign * 8.};
            car.body.height = world.terrain.height(car.body.p) + .8;
            car.terrainPitch = std::atan2(world.terrain.height(car.body.p + Vec2{0, sign}) -
                                              world.terrain.height(car.body.p - Vec2{0, sign}),
                                          2.);
            car.wheelTravel = frame * (8. / 60.);
        }
        std::swap_ranges(traffic.begin(), traffic.end(), world.cars.begin() + 1);
        renderer.render(world, view, width, height);
        std::swap_ranges(traffic.begin(), traffic.end(), world.cars.begin() + 1);
    }

    View visual_route(const World &world, std::wstring_view scenario, View view, float progress)
    {
        if (scenario == L"seafront" || scenario == L"cloud-coast")
        {
            const double x = world.layout.coordinate(22) +
                             std::lerp(-14.f, 26.f, scenario == L"cloud-coast" ? .45f : progress);
            const double z = world.layout.edge() - 14.;
            view.eye = {float(x), float(world.terrain.bed_height(x, z)) + 1.68f, float(z)};
            view.target =
                view.eye + (scenario == L"cloud-coast" ? Vec3{-20.f, -.2f, -.8f} : Vec3{14.f, -.8f, 11.f});
        }
        else if (scenario == L"portal")
        {
            const auto &level = *std::ranges::find(world.roadLevels, true, &RoadLevel::tunnel);
            auto lane = [&](double x)
            {
                const Vec2 point{x, level.centre_z(x) + CityLayout::lane};
                const double ground =
                    level.contains(point) ? level.floor(world.terrain, point) : world.terrain.height(point);
                return Vec3{float(x), float(ground + 1.62), float(point.z)};
            };
            const double x =
                level.x + std::lerp(RoadLevel::covered - 24., RoadLevel::length + 9., double(progress));
            view.eye = lane(x);
            view.target = lane(x + 9.);
        }
        else if (streetReview(scenario))
        {
            const Vec3 motion = scenario == L"shadow-avenue" ? Vec3{0, 0, 32.f}
                                : scenario == L"wire-street" ? Vec3{2.4f, 0, 0}
                                                             : Vec3{};
            const auto offset = motion * (scenario == L"shadow-avenue" ? 3.f * progress : progress);
            if (scenario == L"shadow-avenue")
                view.target.x += 42.f * std::sin(progress * float(pi));
            const float height = float(world.terrain.height(view.eye.x + offset.x, view.eye.z + offset.z) -
                                       world.terrain.height(view.eye.x, view.eye.z));
            view.eye = view.eye + offset + Vec3{0, height, 0};
            view.target = view.target + offset + Vec3{0, height, 0};
        }
        else if (scenario != L"beam-wall" && scenario != L"bus-puddle" && scenario != L"dry-canopy")
        {
            // The facade fixture has already checked clearance in this forecourt.
            view.eye = view.eye + (view.target - view.eye) * (.69f * progress);
        }
        view.heading = std::atan2(view.target.x - view.eye.x, view.target.z - view.eye.z);
        view.distance = (view.target - view.eye).length();
        return view;
    }
}

int main(int argc, char *argv[])
{
    Application app;
    int code = 0;
    bool smoke = false;
    int smokeFrames = 180, captureWidth = 0, captureHeight = 0;
    std::optional<SceneMode> modeOverride;
    std::optional<bool> visualRouteRain;
    bool profile = false, frameProfile = false, invalidDisplay = false;
    std::wstring scenario = L"day";
    // Smoke scenario "people": who stands where in the line-up.
    std::filesystem::path screenshot, content, logPath, smokeOutput, importFile;
    std::optional<Language> languageOverride;
    std::optional<Density> densityOverride;
    std::optional<int> qualityOverride;
    const auto exeDirectory = executable_directory();
    logPath = exeDirectory / "native.log";
    bool windowed = false;

    // Show whole steps of the simulation only, as the game did before frames were drawn
    // between them: for comparing the two.
    bool raw_frames = false;
    // SDL hands over the command line as UTF-8 on every system.
    for (int i = 1; i < argc; i++)
    {
        const std::wstring arg = wide(argv[i]);
        if (arg == L"--smoke")
            smoke = windowed = true;
        else if (arg == L"--visual-route")
        {
            const std::wstring weather = i + 1 < argc ? wide(argv[++i]) : L"";
            if (weather == L"clear" || weather == L"rain")
                visualRouteRain = weather == L"rain";
            else
                invalidDisplay = true;
        }
        else if (arg == L"--gpu-profile")
            profile = frameProfile = true;
        else if (arg == L"--frame-profile")
            frameProfile = true;
        else if (arg == L"--scene-mode" && i + 1 < argc)
        {
            const std::wstring mode = wide(argv[++i]);
            if (mode == L"native")
                modeOverride = SceneMode::original;
            else if (mode == L"quality")
                modeOverride = SceneMode::enhanced;
            else if (mode == L"balanced")
                modeOverride = SceneMode::scaled;
            else
                invalidDisplay = true;
        }
        else if (arg == L"--smoke-size" && i + 2 < argc)
        {
            captureWidth = std::atoi(argv[++i]);
            captureHeight = std::atoi(argv[++i]);
            if (captureWidth < 320 || captureHeight < 200 || captureWidth > 8192 || captureHeight > 8192)
                invalidDisplay = true;
        }
        else if (arg == L"--windowed")
            windowed = true;
        else if (arg == L"--raw-frames")
            raw_frames = true;
        else if (arg == L"--frames" && i + 1 < argc)
            smokeFrames = std::max(1, std::atoi(argv[++i]));
        else if (arg == L"--screenshot" && i + 1 < argc)
            screenshot = utf8_path(argv[++i]);
        else if (arg == L"--language" && i + 1 < argc)
            languageOverride = languageFromCode(argv[++i]);
        else if (arg == L"--density" && i + 1 < argc)
            densityOverride = density_from_code(argv[++i], Density::full);
        else if (arg == L"--quality" && i + 1 < argc)
            qualityOverride = std::clamp(std::atoi(argv[++i]), 0, 2);
        else if (arg == L"--content" && i + 1 < argc)
            content = utf8_path(argv[++i]);
        else if (arg == L"--import-save" && i + 1 < argc)
            importFile = std::filesystem::absolute(utf8_path(argv[++i]));
        else if (arg == L"--scenario" && i + 1 < argc)
            scenario = wide(argv[++i]);
        else if (arg == L"--smoke-output" && i + 1 < argc)
            smokeOutput = utf8_path(argv[++i]);
    }
    const bool arrestScenario = scenario == L"arrest" || scenario == L"arrest-car";
    const bool recoveryScenario =
        scenario == L"recovery" || scenario == L"rollover" || scenario == L"tram-recovery";
    const bool passengerScenario = scenario == L"tram-ride" || scenario == L"trolley-ride";
    std::ofstream log;
    std::optional<GameWindow> window;
    try
    {
        const bool yardCar = scenario == L"yard-car-day" || scenario == L"yard-car-evening";
        const bool yardReview = yardCar || scenario == L"yard-foot-day" ||
                                scenario == L"yard-foot-evening" || scenario == L"yard-estate";
        const bool wallReview = scenario == L"beam-wall" || scenario == L"bus-puddle";
        const bool entranceReview = wallReview || scenario == L"dry-canopy" || scenario == L"entrance" ||
                                    scenario == L"entrance-night";
        if (!streetReview(scenario) && !entranceReview && !yardReview && scenario != L"day" &&
            scenario != L"drive" && scenario != L"walk" && scenario != L"storm" && scenario != L"night" &&
            scenario != L"canal" && scenario != L"bridge" && scenario != L"overpass" &&
            scenario != L"underpass" && scenario != L"tunnel" && scenario != L"portal" &&
            scenario != L"facade" && scenario != L"facade-night" && scenario != L"far-vehicle" &&
            scenario != L"lawn" && scenario != L"park" && scenario != L"park-unshadowed" &&
            scenario != L"park-evening" && scenario != L"streaming" && !arrestScenario &&
            !passengerScenario && !recoveryScenario && scenario != L"tunnel-exit" && scenario != L"carjack" &&
            scenario != L"traffic-lanes" && scenario != L"radio-menu" && scenario != L"save-slots" &&
            scenario != L"settings" && scenario != L"market" && scenario != L"workshop" &&
            scenario != L"courier" && scenario != L"pursuit" && scenario != L"transit" &&
            scenario != L"tank" && scenario != L"drone" && scenario != L"leaflets" && scenario != L"taxi" &&
            scenario != L"wildlife" && scenario != L"scooter" && scenario != L"delivery-rider" &&
            scenario != L"ambulance" && scenario != L"hydrant" && scenario != L"phone-booth" &&
            scenario != L"soda-machine" && scenario != L"wreckage" && scenario != L"puddle" &&
            scenario != L"pothole" && scenario != L"canal-impact" && scenario != L"people" &&
            scenario != L"bus" && scenario != L"job-board" && scenario != L"contract-series" &&
            scenario != L"console" && scenario != L"street-hail" && scenario != L"estate" &&
            scenario != L"courtyard" && scenario != L"cottages" && scenario != L"centre" &&
            scenario != L"garages" && scenario != L"rail-yard" && scenario != L"cloud-coast" && scenario != L"seafront" &&
            scenario != L"canal-mouth" && scenario != L"crowd" && scenario != L"aerial" && scenario != L"death")
            throw std::runtime_error("Unknown smoke scenario");

        if (content.empty())
            content = std::filesystem::is_directory(exeDirectory / "Content")
                ? exeDirectory / "Content"
                : utf8_path(PROMZONA_SOURCE_CONTENT);

        app.saveFile = user_data_directory() / "save.json";

        if (smoke)
            app.saveFile = (smokeOutput.empty() ? exeDirectory : smokeOutput) / "smoke-save.json";

        std::filesystem::create_directories(app.saveFile.parent_path());
        logPath = app.saveFile.parent_path() / "native.log";
        log.open(logPath);

        if (!log)
            throw std::runtime_error("Cannot open runtime log");

        if (invalidDisplay || (captureWidth && !smoke) || (profile && !smoke))
            throw std::runtime_error("Invalid mode, benchmark dimensions, or --smoke missing");

        if (visualRouteRain && (!smoke || smokeOutput.empty() || smokeFrames < 62 ||
                                (!streetReview(scenario) && !entranceReview && scenario != L"cloud-coast" && scenario != L"seafront" &&
                                 scenario != L"portal" && scenario != L"facade")))
            throw std::runtime_error(
                "Visual route requires smoke output, at least 62 frames and a supported review scenario");

        SceneMode displayMode = SceneMode::enhanced;
        const auto displayFile = app.saveFile.parent_path() / "display-mode.txt";

        if (!smoke)
        {
            std::ifstream prefs(displayFile);
            std::string saved;
            if (prefs >> saved)
            {
                if (saved == "native")
                    displayMode = SceneMode::original;
                if (saved == "balanced")
                    displayMode = SceneMode::scaled;
            }
        }

        if (modeOverride)
            displayMode = *modeOverride;

        auto modeName = [&]()
        {
            return displayMode == SceneMode::original ? "native"
                   : displayMode == SceneMode::scaled ? "balanced"
                                                      : "quality";
        };

        WindowOptions options;
        options.title = "PROMZONA";
        options.fullscreen = !windowed;
        options.background = smoke;
        options.vsync = !profile;
        if (captureWidth)
        {
            options.width = captureWidth;
            options.height = captureHeight;
            options.borderless = true;
        }
        window.emplace(options);
        app.window = &*window;
        app.width = window->width();
        app.height = window->height();
        {
            std::vector<std::pair<int, Vec2>> lineup;
            std::optional<View> reviewView;
            Vec2 reviewCrossing{}, reviewNormal{};
            double riderStartDistance{};
            int reviewRider = -1;
            Renderer renderer(content);
            renderer.scene_mode(displayMode);
            const auto graphicsFile = app.saveFile.parent_path() / "graphics.txt";
            auto graphics = smoke ? GraphicsPreferences{} : GraphicsPreferences::load(graphicsFile);
            if (qualityOverride)
                graphics.quality = *qualityOverride;
            renderer.graphics(graphics);
            FrontEnd menus;
            // The first run follows the system interface language, as the managed game did.
            const auto languageFile = app.saveFile.parent_path() / "language.txt";
            menus.language = languageOverride ? *languageOverride
                             : smoke          ? Language::russian
                                              : loadLanguage(languageFile, system_language());
            renderer.language(menus.language);
            // New games start with the population the managed game keeps. Automated runs
            // keep the low one their scenarios were written for unless told otherwise;
            // `crowd` is the day scenario in the full city.
            const auto densityFile = app.saveFile.parent_path() / "density.txt";
            menus.population = densityOverride        ? *densityOverride
                               : scenario == L"crowd" ? Density::full
                               : scenario == L"aerial" ? Density::max
                               : smoke                ? Density::low
                                                      : load_density(densityFile, Density::max);
            SaveSlots slots(app.saveFile);
            auto refreshSlots = [&]
            {
                for (int i = 1; i <= 3; ++i)
                    menus.occupied[i - 1] = slots.occupied(i);
            };
            refreshSlots();
            if (!importFile.empty())
            {
                // A save of the managed (C#) game becomes a native save in the first free
                // slot; nothing is overwritten and the source file is only read.
                int target = 0;
                for (int i = 3; i >= 1; --i)
                    if (!slots.occupied(i))
                        target = i;
                if (!target)
                    throw std::runtime_error(
                        "All three save slots are occupied. Free one, then import again.");
                World imported;
                const auto carried = imported.importManaged(importFile, menus.population);
                imported.save(slots.path(target));
                refreshSlots();
                menus.selected = target;
                app.message = L"Сохранение C# версии перенесено в слот " + std::to_wstring(target);
                log << "Imported managed save format " << carried.format << ", road revision "
                    << carried.roadRevision << ", seed " << carried.seed << ", weapons " << carried.weapons
                    << (carried.positionKept ? ", position kept" : ", default start")
                    << (carried.carKept ? ", vehicle kept" : "") << ", cars " << carried.cars << ", people "
                    << carried.people << ", slot " << target << std::endl;
            }
            if (profile)
                renderer.profile_gpu();
            AudioMixer audio(content / "Audio");
            const auto audioFile = app.saveFile.parent_path() / "audio-levels.txt";
            AudioLevels levels = smoke ? AudioLevels{} : AudioLevels::load(audioFile);
            audio.set_levels(levels);
            AudioDevice audioOutput(audio);
            if (!audioOutput.error().empty())
                log << audioOutput.error() << std::endl;
            log << renderer.graphics_info() << std::endl;
            std::unique_ptr<World> world;
            std::atomic<float> progress{};
            std::future<std::unique_ptr<World>> generation;
            int pendingSlot = 1;
            double sinceSave = 0;
            bool dirty = false;
            int feedbackSlot = 0;
            auto reportSaveError = [&](const std::exception &e)
            {
                app.message = L"Ошибка сохранения: " + wide(e.what());
                log << "Save failed: " << e.what() << std::endl;
            };
            auto saveGame = [&](int slot, bool manual)
            {
                try
                {
                    slots.write(*world, slot);
                    feedbackSlot = manual ? slot : 0;
                    if (slot == menus.active)
                    {
                        sinceSave = 0;
                        dirty = false;
                    }
                    refreshSlots();
                    return true;
                }
                catch (const std::exception &e)
                {
                    reportSaveError(e);
                    return false;
                }
            };
            auto begin = [&](bool load, int slot)
            {
                try
                {
                    slots.finish();
                }
                catch (const std::exception &e)
                {
                    dirty = true;
                    refreshSlots();
                    reportSaveError(e);
                    return;
                }
                if (load && !slots.occupied(slot))
                {
                    app.message = L"Этот слот пуст.";
                    return;
                }
                app.firing = false;
                app.message.clear();
                pendingSlot = slot;
                const auto path = slots.path(pendingSlot);
                app.loading = true;
                app.menu = false;
                app.console = false;
                app.cursor(app.active);
                progress = 0;
                const Density population = menus.population;
                // New games get the varied city. The automated scenarios were laid out on the
                // revision-4 city of their fixed seed and keep it; "aerial" shows the new one.
                const int revision = smoke && scenario != L"aerial" ? CityLayout::districtRevision
                                                                    : CityLayout::latestRevision;
                generation =
                    std::async(std::launch::async,
                               [&, load, path, population, revision]
                               {
                                   auto next = std::make_unique<World>();
                                   if (load)
                                   {
                                       next->load(path);
                                       progress = 1;
                                   }
                                   else
                                       next->generate(
                                           smoke ? 20261105u : std::random_device{}(), [&](float p)
                                           { progress = p; }, revision, population);
                                   return next;
                               });
            };
            if (smoke && scenario != L"save-slots")
                begin(false, menus.selected);
            using Timer = std::chrono::steady_clock;
            auto last = Timer::now(), start = last;
            Timer::time_point clearNoticeAt{};
            std::wstring savedNotice;
            double accumulator = 0, fps = 60, frameAverage = 1. / 60;

            // What a frame shows between two steps of the simulation, and how far from the
            // one before the last to the last. A smoke run draws whole steps: its frames are
            // then the same from run to run.
            FrameBlend blend;
            const bool blend_frames = !smoke && !raw_frames;
            double shown_part = 0;
            int frames = 0;
            std::vector<double> frameTimes;
            std::vector<FrameTrace::Sample> phases;
            frame_trace.active = smoke && frameProfile && scenario == L"streaming";
            if (frame_trace.active)
            {
                phases.reserve(smokeFrames);
                frameTimes.reserve(smokeFrames);
                frame_trace.last = last;
            }
            Vec2 smokeStart;
            std::optional<Vec2> requestedPlatform;
            int hijackedCar = -1;
            Vec2 recoveryStart{}, rightedAt{};
            int recoveryLine = -1, exitLevel = 0;
            bool rightingCaptured = false, backOnWheels = false;
            bool doorCaptured = false, crewCaptured = false, attemptedEntry = false;
            bool trafficPassed = false, officerCaptured = false, consoleReviewed = false;
            bool capturedHome = false;
            Vec2 passingWreckPosition;
            // The wreckage review: the most particles of each kind seen in one frame, and what was heard.
            std::array<size_t, 8> particlePeak{};
            std::array<int, 3> wreckageCars{-1, -1, -1};
            int blastsHeard = 0, firesHeard = 0;
            while (app.running)
            {
                frame_trace.enter(FrameTrace::messages);
                for (const auto &event : window->poll())
                    handle(&app, event);
                while (!app.posted.empty())
                {
                    const InputEvent event = app.posted.front();
                    app.posted.pop_front();
                    handle(&app, event);
                }
                window->textInput(app.console);
                if (!app.running)
                    break;
                frame_trace.enter(FrameTrace::other);
                auto now = Timer::now();
                double elapsed = std::chrono::duration<double>(now - last).count();
                last = now;
                if (smoke && frameProfile && frames > 0)
                {
                    frameTimes.push_back(elapsed * 1000.);
                    if (frame_trace.active)
                        phases.push_back(frame_trace.ms);
                }
                if (frame_trace.active)
                    frame_trace.ms = {};
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
                    if (app.message == savedNotice)
                        app.message.clear();
                    savedNotice.clear();
                }
                try
                {
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
                }
                catch (const std::exception &e)
                {
                    dirty = true;
                    feedbackSlot = 0;
                    refreshSlots();
                    reportSaveError(e);
                }
                if (smoke && world && !app.loading && (scenario == L"settings" || scenario == L"save-slots"))
                {
                    auto key = [&](int vk)
                    {
                        handle(&app, {InputEvent::Type::keyDown, vk});
                        handle(&app, {InputEvent::Type::keyUp, vk});
                    };
                    auto click = [&](MenuItem target)
                    {
                        auto rows = menus.rows(true, graphics, displayMode, levels);
                        auto it = std::ranges::find(rows, target, &MenuRow::item);
                        if (it == rows.end() || !app.menu)
                            throw std::runtime_error("Smoke menu item unavailable");
                        MenuLayout layout(app.width, app.height, int(rows.size()));
                        int x = layout.x + layout.width * 3 / 4,
                            y = layout.y + (int(it - rows.begin()) * 2 + 1) * layout.step / 2;
                        handle(&app, {InputEvent::Type::buttonDown, x, y});
                        handle(&app, {InputEvent::Type::buttonUp, x, y});
                    };
                    if (scenario == L"settings")
                    {
                        switch (frames)
                        {
                        case 2:
                            key(Key::escape);
                            break;
                        case 3:
                            click(MenuItem::settings);
                            break;
                        case 4:
                            key(Key::right);
                            break;
                        case 5:
                            click(MenuItem::quality);
                            break;
                        case 6:
                            click(MenuItem::effects);
                            break;
                        case 7:
                            click(MenuItem::bloom);
                            break;
                        case 8:
                            click(MenuItem::display);
                            break;
                        case 9:
                            click(MenuItem::master);
                            break;
                        case 10:
                            click(MenuItem::radio);
                            break;
                        case 12:
                            key(Key::escape);
                            break;
                        case 13:
                            key(Key::escape);
                            break;
                        }
                    }
                    else
                    {
                        switch (frames)
                        {
                        case 2:
                            world->money = 111;
                            key(Key::escape);
                            break;
                        case 3:
                            key('2');
                            break;
                        case 4:
                            world->money = 222;
                            click(MenuItem::save);
                            break;
                        case 5:
                            if (menus.page == MenuPage::confirmSave)
                                click(MenuItem::confirm);
                            break;
                        case 6:
                            world->money = 333;
                            key('L');
                            break;
                        case 8:
                            if (menus.active != 2 || world->money != 222 || app.menu)
                                throw std::runtime_error("Selected slot failed to restore its snapshot");
                            world->money = 444;
                            sinceSave = 119.999;
                            break;
                        case 10:
                            world->money = 555;
                            menus.selected = 1; // Browsing slots must not change the quick-load target.
                            key(Key::f9);
                            break;
                        case 12:
                            if (world->money != 444)
                                throw std::runtime_error("Periodic autosave was not loaded");
                            world->money = 666;
                            key(Key::f5);
                            break;
                        case 14:
                            key(Key::escape);
                            break;
                        case 15:
                            key('3');
                            break;
                        case 16:
                            click(MenuItem::save);
                            break;
                        case 17:
                            if (menus.page == MenuPage::confirmSave)
                                click(MenuItem::confirm);
                            break;
                        }
                    }
                }
                if ((app.pressed[Key::f1] || app.pressed[Key::grave]) && world && !app.loading && !app.menu)
                {
                    app.console = !app.console;
                    app.jobsOpen = false;
                    app.firing = false;
                }
                if (smoke && scenario == L"console" && app.console && !consoleReviewed)
                {
                    for (std::wstring_view cmd : {L"money 35", L"weapon pistol 12", L"jobs"})
                    {
                        for (wchar_t c : cmd)
                            handle(&app, {InputEvent::Type::character, int(c)});
                        handle(&app, {InputEvent::Type::keyDown, Key::enter});
                    }
                    for (wchar_t c : std::wstring_view(L"wan"))
                        handle(&app, {InputEvent::Type::character, int(c)});
                    handle(&app, {InputEvent::Type::keyDown, Key::tab});
                    consoleReviewed = true;
                }
                if (world && !app.submissions.empty())
                {
                    for (auto &line : app.submissions)
                        app.terminal.execute(*world, line);
                    app.submissions.clear();
                }
                if (smoke && scenario == L"job-board" && world && frames == 13)
                    app.pressed[Key::escape] = true;
                const bool closingOverlay = app.pressed[Key::escape] && !app.loading;
                if (closingOverlay)
                {
                    if (app.console)
                        app.console = false;
                    else if (app.jobsOpen && !app.menu)
                        app.jobsOpen = false;
                    else if (world && world->openMarket >= 0 && !app.menu)
                        world->openMarket = -1;
                    else if (app.menu)
                    {
                        if (menus.page != MenuPage::home)
                            menus.open(MenuPage::home);
                        else if (world)
                            app.menu = false;
                        else
                            app.running = false;
                    }
                    else
                    {
                        app.menu = true;
                        app.firing = false;
                        menus.open(MenuPage::home);
                        refreshSlots();
                        if (world && dirty)
                            saveGame(menus.active, false);
                    }
                }
                if (!app.console && app.pressed['M'])
                    audio.next_track();
                if (!app.loading && app.menu)
                {
                    auto rows = menus.rows(world != nullptr, graphics, displayMode, levels);
                    const int count = int(rows.size());
                    MenuLayout layout(app.width, app.height, count);
                    int hit = layout.hit(app.mouseX, app.mouseY, count);
                    if ((app.clicked || app.pointerMoved) && hit >= 0)
                        menus.focus = hit;
                    const int travel = int(app.pressed[Key::down]) - int(app.pressed[Key::up]);
                    if (travel)
                        menus.focus = (menus.focus + travel + count) % count;
                    if (menus.page == MenuPage::home)
                        for (int i = 1; i <= 3; ++i)
                            if (app.pressed['0' + i])
                                menus.selected = i;
                    auto item = rows.at(menus.focus).item;
                    int dir = int(app.pressed[Key::right]) - int(app.pressed[Key::left]);
                    bool activate = app.pressed[Key::enter] || (app.clicked && hit >= 0);
                    if (app.clicked && hit >= 0 && !rows[hit].value.empty())
                        dir = app.mouseX < layout.x + layout.width / 2 ? -1 : 1;
                    if (menus.page != MenuPage::confirmNew && menus.page != MenuPage::confirmSave)
                    {
                        if (app.pressed[Key::f2])
                        {
                            item = MenuItem::display;
                            dir = 1;
                        }
                        if (menus.page == MenuPage::home && app.pressed['L'])
                        {
                            item = MenuItem::load;
                            activate = true;
                        }
                        if (menus.page == MenuPage::home && app.pressed['Q'])
                        {
                            item = MenuItem::quit;
                            activate = true;
                        }
                    }
                    const int step = dir ? dir : 1;
                    if (activate || dir)
                    {
                        using enum MenuItem;
                        switch (item)
                        {
                        case resume:
                            if (activate)
                                app.menu = false;
                            break;
                        case quit:
                            if (activate)
                                app.running = false;
                            break;
                        case back:
                            if (activate)
                                menus.open(MenuPage::home);
                            break;
                        case settings:
                            if (activate)
                                menus.open(MenuPage::settings);
                            break;
                        case slot:
                            menus.selected = (menus.selected - 1 + step + 3) % 3 + 1;
                            break;
                        case newGame:
                            if (activate && slots.occupied(menus.selected))
                                menus.open(MenuPage::confirmNew);
                            else if (activate)
                                begin(false, menus.selected);
                            break;
                        case load:
                            if (activate)
                                begin(true, menus.selected);
                            break;
                        case MenuItem::save:
                            if (!activate)
                                break;
                            if (slots.occupied(menus.selected) && menus.selected != menus.active)
                                menus.open(MenuPage::confirmSave);
                            else
                                saveGame(menus.selected, true);
                            break;
                        case confirm:
                            if (!activate)
                                break;
                            if (menus.page == MenuPage::confirmNew)
                                begin(false, menus.selected);
                            else
                                saveGame(menus.selected, true);
                            menus.open(MenuPage::home);
                            break;
                        case display:
                            displayMode = SceneMode((std::to_underlying(displayMode) + step + 3) % 3);
                            renderer.scene_mode(displayMode);
                            if ((!smoke || scenario == L"settings") &&
                                !(std::ofstream(displayFile) << modeName()))
                                app.message = L"Не удалось сохранить режим изображения.";
                            break;
                        case distance:
                        case quality:
                        case effects:
                        case bloom:
                            if (item == distance)
                                graphics.distance =
                                    std::clamp(graphics.distance + 50 * step, GraphicsPreferences::minimum,
                                               GraphicsPreferences::maximum);
                            if (item == quality)
                                graphics.quality = (graphics.quality + step + 3) % 3;
                            if (item == effects)
                                graphics.effects = !graphics.effects;
                            if (item == bloom && displayMode != SceneMode::original)
                                graphics.bloom = !graphics.bloom;
                            renderer.graphics(graphics);
                            if ((!smoke || scenario == L"settings") && !graphics.save(graphicsFile))
                                app.message = L"Не удалось сохранить настройки графики.";
                            break;
                        case master:
                        case radio:
                            if (item == master)
                                levels.master = std::clamp(levels.master + 5 * step, 0, 100);
                            else
                                levels.radio = std::clamp(levels.radio + 5 * step, 0, 50);
                            audio.set_levels(levels);
                            if ((!smoke || scenario == L"settings") && !levels.save(audioFile))
                                app.message = L"Не удалось сохранить громкость.";
                            break;
                        case MenuItem::language:
                            menus.language = cycleLanguage(menus.language, step);
                            renderer.language(menus.language);
                            if (!smoke && !save_language(languageFile, menus.language))
                                app.message = L"Не удалось сохранить язык.";
                            break;
                        case MenuItem::population:
                            menus.population = cycle_density(menus.population, step);
                            if (!smoke && !save_density(densityFile, menus.population))
                                app.message = L"Не удалось сохранить плотность населения.";
                            break;
                        case nextTrack:
                            if (activate)
                                audio.next_track();
                            break;
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
                                world->clock.set_hour(16);
                                world->setWeather(1);
                                world->weatherClock = 120;
                                world->time = 95;
                            }
                            if (scenario == L"night")
                            {
                                world->clock.set_hour(22);
                                world->setWeather(.85);
                                world->weatherClock = 120;
                            }
                            if (scenario == L"drone")
                            {
                                world->enter_exit();
                                world->toggle_drone(0);
                                if (!world->drone.active)
                                    throw std::runtime_error("Drone launch failed");
                                const auto &b = world->buildings[world->drone.targets[0]];
                                auto delta = b.p - world->drone.position;
                                app.look = std::atan2(delta.x, delta.z);
                                app.lookPitch = .1;
                            }
                            if (scenario == L"wildlife" || scenario == L"scooter" ||
                                scenario == L"delivery-rider")
                            {
                                world->clock.set_hour(10);
                                world->setWeather(0);
                                world->controlledCar = -1;
                                world->player.roadLevel = 0;
                                if (scenario == L"wildlife")
                                {
                                    const auto centre = world->flocks.at(0).landing;
                                    world->player.p = centre + Vec2{0, 8};
                                    world->player.height = world->terrain.height(world->player.p) + .85;
                                    View view;
                                    view.target = {float(centre.x),
                                                   float(world->terrain.height(centre) + .45),
                                                   float(centre.z)};
                                    view.eye = view.target + Vec3{3, 1.9f, 4};
                                    reviewView = view;
                                }
                                else
                                {
                                    auto it =
                                        std::ranges::find_if(world->scooters, [&](const Scooter &s)
                                                             { return world->people[s.owner].courier; });
                                    if (it == world->scooters.end())
                                        throw std::runtime_error("Missing courier rider");
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
                                        if (!world->use_scooter())
                                            throw std::runtime_error("Could not mount dropped scooter");
                                        auto dir = (rider.target - rider.body.p).unit();
                                        app.look = std::atan2(dir.x, dir.z);
                                    }
                                }
                            }
                            if (scenario == L"wreckage")
                            {
                                // Three parked cars up the street: one with a damaged engine, one on
                                // fire and one that is blown up while the player watches.
                                auto &own = world->cars.at(world->controlledCar);
                                const Vec2 origin = own.body.p, ahead = forward(own.heading());
                                own.body.v = {};
                                int staged = 0;
                                for (auto &c : world->cars)
                                {
                                    if (&c == &own || c.kind > 2 || c.line >= 0 || staged == 3)
                                        continue;
                                    // The last one stands clear of the others: its blast reaches ten metres.
                                    c.body.p = origin + ahead * (staged == 2 ? 32. : 9. + 9 * staged);
                                    c.body.angle = own.body.angle;
                                    c.body.roadLevel = own.body.roadLevel;
                                    c.body.height = world->surface(c.body.p, c.body.roadLevel) + .8;
                                    c.body.v = {};
                                    c.driverless = true;
                                    c.health = staged == 0 ? .45 : staged == 1 ? .3 : 1;
                                    if (staged == 1)
                                        c.fireTime = 2;
                                    wreckageCars[staged++] = int(&c - world->cars.data());
                                }
                                if (staged != 3)
                                    throw std::runtime_error("No cars to stage the wreckage with");
                                world->clock.set_hour(13);
                                world->setWeather(0);
                                world->weatherClock = 120;
                                const Vec2 side{ahead.z, -ahead.x}, eye = origin + ahead * 8 + side * 11;
                                View view;
                                view.eye = {float(eye.x), float(world->terrain.height(eye) + 4.5),
                                            float(eye.z)};
                                const Vec2 centre = origin + ahead * 19;
                                view.target = {float(centre.x), float(world->terrain.height(centre) + 1.2),
                                               float(centre.z)};
                                reviewView = view;
                            }
                            if (scenario == L"hydrant")
                            {
                                auto at = world->position();
                                const World::StreetFixture *chosen = nullptr;
                                for (const auto &f : world->fixtures)
                                    if (f.kind == World::FixtureKind::hydrant && !world->levelAt(f.home, 8) &&
                                        (!chosen || (f.home - at).length2() < (chosen->home - at).length2()))
                                        chosen = &f;
                                if (!chosen)
                                    throw std::runtime_error("No hydrant for review");
                                auto &car = world->cars.at(world->controlledCar);
                                Vec2 target = chosen->home;
                                auto offset = target - World::nodePoint(0);
                                double dx = std::remainder(offset.x, World::spacing);
                                double dz = std::remainder(offset.z, World::spacing);
                                Vec2 incoming = std::abs(dx) < std::abs(dz) ? Vec2{std::copysign(1., dx), 0}
                                                                            : Vec2{0, std::copysign(1., dz)};
                                car.body.p = target - incoming * (car.body.halfH + 1.1);
                                car.body.angle = -std::atan2(incoming.x, incoming.z);
                                car.body.height = world->terrain.height(car.body.p) + .8;
                                car.body.v = incoming * 11;
                                car.body.wake();
                                world->clock.set_hour(11);
                                world->setWeather(0);
                                View view;
                                auto camera = target + perpendicular(incoming) * 3.6 - incoming * 4.5;
                                view.eye = {float(camera.x), float(world->terrain.height(camera) + 2.35),
                                            float(camera.z)};
                                view.target = {float(target.x), float(world->terrain.height(target) + 1.25),
                                               float(target.z)};
                                reviewView = view;
                            }
                            if (scenario == L"canal-impact")
                            {
                                // Fire from the quay between two intersections, through the normal weapon
                                // input.
                                const auto &l = world->layout;
                                Vec2 target{l.coordinate(l.nodes() / 3) + 18., l.coordinate(23) + 18.};
                                Vec2 bank = target + Vec2{l.bankHalf() + .5, 0};
                                world->controlledCar = -1;
                                world->player.p = bank;
                                world->player.roadLevel = 0;
                                world->player.height = world->terrain.height(bank) + .85;
                                world->inventory.give(WeaponId::pistol, 30);
                                world->setWeather(0);
                                world->clock.set_hour(11);
                                View view;
                                view.eye = {float(target.x - 1.), 1.6f, float(target.z - 6.)};
                                view.target = {float(target.x), float(CityLayout::waterLevel),
                                               float(target.z)};
                                view.heading =
                                    std::atan2(view.target.x - view.eye.x, view.target.z - view.eye.z);
                                reviewView = view;
                            }
                            if (scenario == L"puddle")
                            {
                                if (world->puddles.empty())
                                    throw std::runtime_error("No road puddles in city");
                                auto closest = std::ranges::min_element(world->puddles, {}, [](const auto &p)
                                                                        { return p.centre.length2(); });
                                auto target = closest->centre;
                                auto &car = world->cars[0];
                                car.body.p = target + Vec2{.83 * car.body.halfW, -5.};
                                car.body.height = world->terrain.height(car.body.p) + .8;
                                car.body.angle = 0;
                                car.body.v = {0, 5};
                                world->setWeather(0);
                                world->clock.set_hour(10);
                                View v;
                                auto eye = target + Vec2{-3.8, -4.0};
                                v.eye = {float(eye.x), float(world->terrain.height(eye) + 1.95),
                                         float(eye.z)};
                                v.target = {float(target.x), float(world->terrain.height(target) + .25),
                                            float(target.z + 1.8)};
                                reviewView = v;
                            }
                            if (scenario == L"pothole")
                            {
                                // The hole nearest to the centre on a north-south street, met at speed.
                                const Pothole *hole = nullptr;
                                for (const auto &candidate : world->potholes)
                                {
                                    const double column = std::round(
                                        (candidate.p.x - world->layout.coordinate(0)) / World::spacing);
                                    const double street =
                                        world->layout.coordinate(0) + column * World::spacing;
                                    if (std::abs(candidate.p.x - street) < 3.6 &&
                                        (!hole || candidate.p.length2() < hole->p.length2()))
                                        hole = &candidate;
                                }
                                if (!hole)
                                    throw std::runtime_error("No potholes in city");
                                auto &car = world->cars[0];
                                car.body.p = hole->p + Vec2{0, -9.};
                                car.body.height = world->terrain.height(car.body.p) + .8;
                                car.body.angle = 0;
                                car.body.v = {0, 7};
                                world->setWeather(0);
                                world->clock.set_hour(10);
                                View v;
                                auto eye = hole->p + Vec2{-2.6, -3.4};
                                v.eye = {float(eye.x), float(world->terrain.height(eye) + 1.7), float(eye.z)};
                                v.target = {float(hole->p.x), float(world->terrain.height(hole->p)),
                                            float(hole->p.z)};
                                reviewView = v;
                            }
                            if (scenario == L"phone-booth" || scenario == L"soda-machine")
                            {
                                const auto kind = scenario == L"soda-machine"
                                                      ? World::FixtureKind::drinkMachine
                                                      : World::FixtureKind::phoneBooth;
                                const World::StreetFixture *fixture = nullptr;
                                for (const auto &f : world->fixtures)
                                    if (f.kind == kind &&
                                        (!fixture || f.home.length2() < fixture->home.length2()))
                                        fixture = &f;
                                if (!fixture)
                                    throw std::runtime_error(
                                        "Requested street fixture missing in generated city");
                                Vec2 eye = fixture->home + Vec2{-2.6, -4.25};
                                double ground = world->terrain.height(fixture->home);
                                View v;
                                v.eye = {float(eye.x), float(ground + 1.55), float(eye.z)};
                                v.target = {float(fixture->home.x), float(ground + 1.20),
                                            float(fixture->home.z)};
                                reviewView = v;
                                auto &c = world->cars[0];
                                c.body.p = eye + Vec2{-4, -4};
                                c.body.v = {};
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.set_hour(10);
                            }
                            if (scenario == L"traffic-lanes" || arrestScenario)
                            {
                                // Only place the vehicles. Steering, braking and lane choice are normal game
                                // AI.
                                int from = -1;
                                for (int row = 17; row < 32 && from < 0; ++row)
                                    for (int col = 18; col < 30; ++col)
                                    {
                                        int n = row * World::nodes + col;
                                        auto links = world->neighbours(n);
                                        auto mid = World::nodePoint(n) + Vec2{18, 0};
                                        if (std::ranges::find(links, n + 1) == links.end() ||
                                            world->levelAt(mid, 27) || !world->layout.wide() ||
                                            std::ranges::any_of(
                                                world->cars, [&](const Car &c)
                                                { return (c.body.p - mid).length2() < 28 * 28; }))
                                            continue;
                                        from = n;
                                        break;
                                    }
                                if (from < 0)
                                    throw std::runtime_error("Missing clear four-lane street");
                                auto origin = World::nodePoint(from);
                                if (arrestScenario)
                                {
                                    world->controlledCar = -1;
                                    world->player.p = origin + Vec2{18, 3.5};
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
                                    if (unit == world->cars.end())
                                        throw std::runtime_error("No arrest unit");
                                    unit->body.p = origin + Vec2{14, -1.2};
                                    unit->body.angle = -pi / 2;
                                    unit->body.height = world->terrain.height(unit->body.p) + .8;
                                    unit->body.v = {};
                                    world->setWeather(0);
                                    world->clock.set_hour(11);
                                    View view;
                                    auto eye = origin + Vec2{24, -5};
                                    view.eye = {float(eye.x), float(world->terrain.height(eye) + 4),
                                                float(eye.z)};
                                    view.target = {float(origin.x + 16), float(unit->body.height + .45),
                                                   float(origin.z + 1)};
                                    reviewView = view;
                                }
                                else
                                {
                                    auto &driver = world->cars.at(1);
                                    driver.routeFrom = from;
                                    driver.routeTo = from + 1;
                                    driver.routePhase = 1;
                                    driver.laneIndex = 0;
                                    world->new_route(driver);
                                    auto place = [&](Car &c, double x, double z)
                                    {
                                        c.body.p = origin + Vec2{x, z};
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
                                    world->clock.set_hour(11);
                                    world->setWeather(0);
                                    world->weatherClock = 120;
                                    auto eye = origin + Vec2{6, -6};
                                    View view;
                                    view.eye = {float(eye.x), float(world->terrain.height(eye) + 9),
                                                float(eye.z)};
                                    view.target = {float(wreck.body.p.x - 3), float(wreck.body.height),
                                                   float(wreck.body.p.z + 1)};
                                    reviewView = view;
                                }
                            }
                            if (scenario == L"tunnel-exit")
                            {
                                const auto &level =
                                    *std::ranges::find(world->roadLevels, true, &RoadLevel::tunnel);
                                exitLevel = level.id;
                                auto &hero = world->cars.at(0);
                                hero.body.p = {level.x, level.centre_z(level.x)};
                                hero.body.angle = -pi / 2;
                                hero.body.roadLevel = level.id;
                                hero.body.height = world->surface(hero.body.p, level.id) + .8;
                                hero.body.v = {};
                                hero.body.w = 0;
                                recoveryStart = hero.body.p;
                                int parkedCount = 0;
                                for (auto &parked : world->cars)
                                {
                                    if (&parked == &hero || parked.line >= 0 || parked.destroyed ||
                                        parked.body.halfH < hero.body.halfH * 1.4)
                                        continue;
                                    const double side = parkedCount == 0 ? -1. : 1.;
                                    parked.body.p =
                                        hero.body.p +
                                        rotate({side * (hero.body.halfW + parked.body.halfW + .2), 0},
                                               hero.body.angle);
                                    parked.body.angle = hero.body.angle;
                                    parked.body.roadLevel = level.id;
                                    parked.body.height = world->surface(parked.body.p, level.id) + .8;
                                    parked.body.v = {};
                                    parked.body.w = 0;
                                    parked.driverless = true;
                                    if (++parkedCount == 2)
                                        break;
                                }
                                if (parkedCount != 2 || world->vehicleExit(hero))
                                    throw std::runtime_error("Emergency exit fixture has an open door");
                            }
                            if (recoveryScenario)
                            {
                                if (scenario == L"tram-recovery")
                                {
                                    auto tram = std::ranges::find(world->cars, 9, &Car::kind);
                                    if (tram == world->cars.end())
                                        throw std::runtime_error("Missing recovery tram");
                                    world->controlledCar = int(tram - world->cars.begin());
                                    recoveryLine = tram->line;
                                    tram->body.p += perpendicular(forward(tram->heading())) * 8.;
                                }
                                auto &car = world->cars.at(world->controlledCar);
                                if (scenario == L"recovery")
                                {
                                    const auto &level =
                                        *std::ranges::find(world->roadLevels, true, &RoadLevel::tunnel);
                                    car.body.p = {level.x + 100, level.centre_z(level.x + 100)};
                                    car.body.roadLevel = level.id;
                                }
                                car.body.height = world->surface(car.body.p, car.body.roadLevel) + .8;
                                car.roll = pi;
                                car.body.v = {};
                                car.body.w = 0;
                                car.health = .62;
                                recoveryStart = car.body.p;
                                world->clock.set_hour(11);
                                world->setWeather(0);
                                world->weatherClock = 120;
                            }
                            if (scenario == L"carjack")
                            {
                                world->controlledCar = -1;
                                for (auto &car : world->cars)
                                {
                                    if (!car.id || car.model != 3 || car.body.roadLevel ||
                                        world->levelAt(car.body.p, 14))
                                        continue;
                                    Vec2 side = rotate({-1, 0}, car.body.angle);
                                    Vec2 door = World::driver_door(car, -1);
                                    Vec2 approachStart = door + side * 1.35;
                                    world->player.p = approachStart;
                                    world->player.height = world->surface(approachStart, 0) + .85;
                                    if (!world->entry_path(car, door) ||
                                        !world->vehicleSpace(approachStart, world->player.height, nullptr,
                                                             false))
                                        continue;
                                    hijackedCar = int(car.id);
                                    car.body.v = {};
                                    car.body.w = 0;
                                    View view;
                                    Vec2 eye = door + side * 5. + forward(car.heading()) * 4.;
                                    view.eye = {float(eye.x), float(car.body.height + 2.35), float(eye.z)};
                                    view.target = {float(door.x), float(car.body.height + .15),
                                                   float(door.z)};
                                    reviewView = view;
                                    break;
                                }
                                if (hijackedCar < 0)
                                    throw std::runtime_error("Missing accessible patrol car");
                                world->clock.set_hour(13);
                                world->setWeather(0);
                            }
                            if (scenario == L"ambulance")
                            {
                                auto unit = std::ranges::find_if(world->cars,
                                                                 [&](const Car &c) {
                                                                     return c.kind == 4 &&
                                                                            !c.body.roadLevel &&
                                                                            !world->levelAt(c.body.p, 15);
                                                                 });
                                if (unit == world->cars.end())
                                    throw std::runtime_error("Missing ambulance");
                                auto side = rotate(Vec2{1, 0}, unit->body.angle);
                                auto at = unit->body.p + side * 3.4;
                                world->clock.set_hour(10);
                                world->setWeather(0);
                                world->controlledCar = -1;
                                world->player.p = at + side * 8;
                                world->player.height = world->terrain.height(world->player.p) + .85;
                                auto &patient = world->people.at(15);
                                world->resumeWalk(patient, at, 0);
                                world->killPerson(patient);
                                world->time += 8;
                                world->update_rescues();
                                world->update_rescues();
                                View view;
                                view.target = {float(at.x), float(patient.body.height + .25), float(at.z)};
                                auto camera = at + side * 4.6 + forward(unit->heading()) * 5.5;
                                view.eye = {float(camera.x), float(world->terrain.height(camera) + 2.6),
                                            float(camera.z)};
                                reviewView = view;
                            }
                            if (scenario == L"aerial")
                            {
                                // The city from a helicopter: the view that shows how much of it
                                // is lived in and how alike its blocks are.
                                world->command("spawn helicopter");
                                world->controlledCar = int(world->cars.size() - 1);
                                app.look = world->cars.back().heading() + .6;
                                world->clock.set_hour(16);
                            }
                            if (scenario == L"tank")
                            {
                                world->command("spawn tank");
                                world->controlledCar = int(world->cars.size() - 1);
                                app.look = world->cars.back().heading() + .46;
                                app.zoom = 12;
                                world->clock.set_hour(14);
                            }
                            if (passengerScenario)
                            {
                                auto service = std::ranges::find(
                                    world->cars, scenario == L"tram-ride" ? 9 : 10, &Car::kind);
                                if (service == world->cars.end())
                                    throw std::runtime_error("No vehicle on smoke route");
                                auto &c = *service;
                                const auto &line = world->lines.at(c.line);
                                bool found = false;
                                world->controlledCar = -1;
                                for (int stop = 0; stop < int(line.stops.size()); ++stop)
                                {
                                    auto [p, heading] = line.pose(line.stops[stop]);
                                    if (std::ranges::any_of(world->cars,
                                                            [&](const Car &other) {
                                                                return other.id != c.id &&
                                                                       (other.body.p - p).length2() < 32 * 32;
                                                            }))
                                        continue;
                                    c.along = line.stops[stop];
                                    c.body.p = p;
                                    c.body.height = world->terrain.height(p) + .8;
                                    c.body.angle = -heading;
                                    c.body.v = {};
                                    c.next_stop = (stop + 1) % int(line.stops.size());
                                    c.dwell = 3;
                                    world->player.p = line.curb(stop);
                                    world->player.height = world->terrain.height(world->player.p) + .85;
                                    if (world->boardableTransit() == int(c.id))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                    throw std::runtime_error("No accessible stop in smoke city");
                                world->clock.set_hour(13);
                                app.zoom = 17;
                            }
                            if (scenario == L"transit")
                            {
                                auto it = std::ranges::find(world->cars, 9, &Car::kind);
                                if (it == world->cars.end())
                                    throw std::runtime_error("Missing tram in generated city");
                                world->controlledCar = int(it - world->cars.begin());
                                app.zoom = 18;
                                world->clock.set_hour(13);
                            }
                            if (scenario == L"pursuit")
                            {
                                world->command("wanted 6");
                                for (int n = 0; n < 24; ++n)
                                    world->updateDispatch();
                                world->clock.set_hour(12);
                            }
                            if (scenario == L"leaflets")
                            {
                                world->enter_exit();
                                if (!world->startContract(JobKind::leaflets))
                                    throw std::runtime_error("No leaflet route");
                                world->player.p = world->jobTarget();
                                world->player.height = world->terrain.height(world->player.p) + .85;
                                world->interactJob();
                            }
                            if (scenario == L"street-hail")
                            {
                                world->updateHailers();
                                int actor = world->hailers.front().actor;
                                if (actor < 0)
                                    throw std::runtime_error("No pedestrian hailed a taxi");
                                auto &c = world->cars.at(world->controlledCar);
                                const Vec2 hailer = world->people[actor].body.p,
                                           road = world->roadward(hailer), along = perpendicular(road);
                                c.body.angle = -std::atan2(along.x, along.z);
                                c.body.p = hailer + road * (c.body.halfW + 1.25);
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                c.body.v = {};
                            }
                            if (scenario == L"taxi")
                            {
                                if (!world->startContract(JobKind::taxi))
                                    throw std::runtime_error("Taxi unavailable");
                                auto &c = world->cars[world->controlledCar];
                                const auto &person = world->people[world->job.actor];
                                // Park along the kerb beside the passenger, whichever pavement
                                // of whichever street that is.
                                const Vec2 road = world->roadward(person.body.p), along = perpendicular(road);
                                c.body.angle = -std::atan2(along.x, along.z);
                                c.body.p = person.body.p + road * (c.body.halfW + .68);
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                c.body.v = {};
                                world->interactJob();
                            }
                            if (scenario == L"market" || scenario == L"workshop")
                            {
                                const bool shopping = scenario == L"market";
                                const int index = world->nearestSite(
                                    shopping ? ServiceKind::market : ServiceKind::garage, {});
                                if (index < 0)
                                    throw std::runtime_error("No trading site in generated city");
                                const auto &site = world->sites[index];
                                const Vec2 front = (site.entrance - site.centre).unit();
                                if (shopping)
                                {
                                    world->controlledCar = -1;
                                    world->player.p = site.entrance;
                                    world->player.height = world->terrain.height(site.entrance) + .85;
                                    world->money = 220;
                                    world->interactJob();
                                    if (!world->buy_at_market(ShopItem::pistol) || world->money != 100)
                                        throw std::runtime_error("Purchase at street market failed");
                                }
                                else
                                {
                                    auto &car = world->cars[world->controlledCar];
                                    car.body.p = site.entrance + front * 3.8;
                                    car.body.angle = pi * .43;
                                    car.body.height = world->terrain.height(car.body.p) + .8;
                                    car.body.v = {};
                                    car.health = .45;
                                    world->wanted = 2;
                                    world->money = 600;
                                    world->use_owned_garage();
                                    world->interactJob();
                                    if (world->ownedGarage != index || car.paint <= 0 || world->money != 100)
                                        throw std::runtime_error("Workshop purchase or repaint failed");
                                }
                                auto centre = site.centre + front * (shopping ? 1.5 : site.half_depth + 1.7);
                                auto eye =
                                    centre + front * (shopping ? 6.3 : 8.) + Vec2{shopping ? 3.3 : 6., 0};
                                View view;
                                view.eye = {float(eye.x),
                                            float(world->terrain.height(eye) + (shopping ? 2.5 : 3.5)),
                                            float(eye.z)};
                                view.target = {float(centre.x), float(world->terrain.height(centre) + 1.2),
                                               float(centre.z)};
                                reviewView = view;
                                world->clock.set_hour(11);
                            }
                            if (scenario == L"courier")
                            {
                                int source = world->nearestSite(ServiceKind::kiosk, world->position());
                                if (source < 0)
                                    throw std::runtime_error("Missing kiosk in smoke city");
                                const auto &s = world->sites[source];
                                world->controlledCar = -1;
                                world->player.p = s.entrance;
                                world->player.height = world->terrain.height(s.entrance) + .85;
                                world->health = .4;
                                world->money = 60;
                                world->buyFood();
                                world->interactJob();
                                if (world->job.kind != JobKind::courier || world->money != 45)
                                    throw std::runtime_error("Courier or shop unavailable in gameplay");
                                Vec3 at{float(s.centre.x), float(world->terrain.height(s.centre) + 1.15),
                                        float(s.centre.z)};
                                View v;
                                v.eye = at + Vec3{-3.5f, 1.95f, -6.5f};
                                v.target = at;
                                reviewView = v;
                                world->clock.set_hour(11);
                            }
                            if (scenario == L"walk")
                            {
                                world->enter_exit();
                                world->inventory.give(WeaponId::automatic, 1000);
                                app.zoom = 9;
                            }
                            if (scenario == L"people")
                            {
                                // Every outfit of the city side by side on the eastern beach:
                                // nine everyday choices, the personas, the services, the habits
                                // and the player, in two rows up the slope.
                                using U = Person::Uniform;
                                using R = Person::Role;
                                struct Cast
                                {
                                    std::uint32_t style;
                                    U uniform;
                                    R role;
                                };
                                std::vector<Cast> cast;
                                for (std::uint32_t k = 0; k < 9; ++k)
                                    cast.push_back({k % 2, U::none, R::none});
                                for (std::uint32_t habit : {2u, 8u, 9u, 10u})
                                    cast.push_back({habit, U::none, R::none});
                                for (U uniform : {U::medical, U::police, U::rosguard, U::fsb, U::army})
                                    cast.push_back({0, uniform, R::none});
                                cast.push_back({1, U::none, R::beach});
                                cast.push_back({0, U::none, R::beach});
                                cast.push_back({1, U::none, R::night});
                                const double shore = World::landEdge - 37, along = 120;
                                auto spot = [&](size_t n)
                                {
                                    const size_t row = n % 2, column = n / 2;
                                    return Vec2{shore - 2.3 * row, along + (column + .5 * row) * .92};
                                };
                                const Vec2 middle = spot(cast.size() / 2);
                                View v;
                                v.target = {float(middle.x - .9), float(world->terrain.height(middle) + 1.05),
                                            float(middle.z)};
                                v.eye = v.target + Vec3{10.2f, 1.9f, 0};
                                reviewView = v;
                                // The first nine show the nine everyday choices, which follow the id.
                                size_t next = 0;
                                for (size_t n = 0; n < cast.size(); ++n)
                                {
                                    for (;; ++next)
                                    {
                                        const auto &candidate = world->people.at(next);
                                        if (candidate.riding < 0 && !candidate.courier &&
                                            !candidate.inCompany && candidate.ownedScooter < 0 &&
                                            candidate.waitingLine < 0 && candidate.role == R::none &&
                                            !world->personEngaged(int(next)) &&
                                            (n >= 9 || candidate.body.id % 9 == n))
                                            break;
                                    }
                                    auto &p = world->people[next];
                                    p.style = cast[n].style;
                                    p.uniform = cast[n].uniform;
                                    p.role = cast[n].role;
                                    p.roleHome = spot(n);
                                    lineup.push_back({int(next++), spot(n)});
                                }
                                world->controlledCar = -1;
                                world->player.p = spot(cast.size());
                                world->player.height = world->terrain.height(world->player.p) + .85;
                                world->player.angle = -pi * .5;
                                app.look = pi * .5;
                                world->clock.set_hour(11);
                                world->dayNightCycle = false;
                                world->dispatchAt = world->patrolScanAt = world->nextRescueDispatch = 1e9;
                            }
                            if (scenario == L"bus")
                            {
                                // The three things the managed game builds for its buses: a
                                // shelter, the bus short of it and a van of the gas service on
                                // the other side of the street.
                                const Vec2 home = world->position();
                                int node = -1;
                                for (int n = 0; n < World::nodes * World::nodes; ++n)
                                    if (world->busStopOfNode[size_t(n)] >= 0 &&
                                        (node < 0 ||
                                         (world->busStops[size_t(world->busStopOfNode[size_t(n)])] - home)
                                                 .length2() <
                                             (world->busStops[size_t(world->busStopOfNode[size_t(node)])] -
                                              home)
                                                 .length2()))
                                        node = n;
                                if (node < 0)
                                    throw std::runtime_error("The city has no bus stop");
                                const Vec2 sign = world->busStops[size_t(world->busStopOfNode[size_t(node)])];
                                auto stand = [&](int kind, int from, int to, double along)
                                {
                                    auto found = std::ranges::find_if(
                                        world->cars,
                                        [&](const Car &c) {
                                            return c.kind == kind && c.id &&
                                                   int(c.id) < int(world->trafficPopulation);
                                        });
                                    if (found == world->cars.end())
                                        throw std::runtime_error("The city has no such vehicle");
                                    auto &c = *found;
                                    c.routeFrom = from;
                                    c.routeTo = to;
                                    c.routePhase = 1;
                                    c.laneIndex = 1;
                                    c.routeLevel = c.body.roadLevel = 0;
                                    world->new_route(c);
                                    c.routeDistance = along;
                                    const auto [at, heading] = c.route->sample(along);
                                    c.body.p = at;
                                    c.body.angle = -heading;
                                    c.body.v = {};
                                    c.body.height = world->surface(at, 0) + .8;
                                    c.driverless = true;
                                };
                                stand(5, node, node + World::nodes, .8);
                                stand(8, node + World::nodes, node, 12);
                                const float ground = float(world->terrain.height(sign));
                                View v;
                                v.eye = {float(sign.x + 24), ground + 4.2f, float(sign.z - 6)};
                                v.target = {float(sign.x + 2), ground + 1.2f, float(sign.z - 2)};
                                reviewView = v;
                                world->controlledCar = -1;
                                world->player.p = sign + Vec2{-3, 7};
                                world->player.height = world->terrain.height(world->player.p) + .85;
                                world->clock.set_hour(11);
                                world->dayNightCycle = false;
                                world->dispatchAt = world->patrolScanAt = world->nextRescueDispatch = 1e9;
                            }
                            if (scenario == L"canal" || scenario == L"bridge")
                            {
                                auto &c = world->cars[0];
                                auto &l = world->layout;
                                c.body.p = {l.coordinate(World::nodes / 3) + 18 - 21, l.coordinate(24) + 1.9};
                                c.body.angle = -pi * .5;
                                c.body.v = {};
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.set_hour(10);
                                app.cameraMode = scenario == L"canal" ? 2 : 0;
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
                                c.body.p = {x, level.centre_z(x) + 1.9};
                                c.body.angle = -pi * .5;
                                c.body.v = {};
                                c.body.roadLevel = scenario == L"underpass" ? 0 : level.id;
                                c.body.height = world->surface(c.body.p, c.body.roadLevel) + .8;
                                c.lift = c.verticalVelocity = 0;
                                world->clock.set_hour(10);
                                if (scenario == L"tunnel")
                                    world->setWeather(1);
                                app.zoom = scenario == L"underpass" ? 12 : scenario == L"tunnel" ? 7 : 24;
                            }
                            if (streetReview(scenario))
                            {
                                reviewCrossing = reviewJunction(*world);
                                auto point = [&](Vec2 offset, float height)
                                {
                                    const Vec2 at = reviewCrossing + offset;
                                    return Vec3{float(at.x), float(world->terrain.height(at)) + height,
                                                float(at.z)};
                                };
                                View v;
                                if (scenario == L"wire-street" && !world->lines.empty())
                                {
                                    const auto &line = world->lines.front();
                                    const auto [at, heading] = line.pose(line.stops.front());
                                    const auto direction = forward(heading);
                                    reviewCrossing = at;
                                    v.eye = point(perpendicular(direction) * 5. - direction * 12., 1.8f);
                                    v.target = point(direction * 20., 5.8f);
                                }
                                else if (scenario == L"night-crossing")
                                {
                                    v.eye = point({8., 12.}, 2.1f);
                                    v.target = point({-2., -9.}, 1.1f);
                                }
                                else
                                {
                                    v.eye = point({1.9, -40.}, 3.4f);
                                    v.target = point({1.9, 66.}, 3.2f);
                                }
                                v.distance = 6;
                                reviewView = v;
                                world->clock.set_hour(scenario == L"night-crossing" ? 22. : 16.5);
                                world->dayNightCycle = false;
                                world->setWeather(scenario == L"night-crossing" ? 1. : 0.);
                                world->weatherClock = 120;
                            }
                            if (entranceReview || scenario == L"facade" || scenario == L"facade-night")
                            {
                                // The nearest tall building with an open forecourt: the parked car and
                                // the camera must not end up inside the next block of the row.
                                const Building *selected = nullptr;
                                auto forecourt = [&](const Building &b)
                                {
                                    const auto plan = plan_building(b, world->terrain.height(b.p));
                                    const Vec3 n = plan.windows_on_z ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
                                    const Vec3 side = cross({0, 1, 0}, n);
                                    const Vec3 front =
                                        plan.origin + n * (plan.windows_on_z ? plan.half_depth : plan.half_width);
                                    for (float along = 3; along <= 19; along += 1.5f)
                                        for (float across : {0.f, 7.f})
                                        {
                                            const Vec3 at = front + n * along + side * across;
                                            const Vec2 p{at.x, at.z};
                                            if (world->inside_building(p, world->terrain.height(p) + 3, 2.4) ||
                                                world->levelAt(p, 3))
                                                return false;
                                        }
                                    const Vec3 parking = front + n * 8;
                                    const Vec2 p{parking.x, parking.z};
                                    return world->vehicleSpace(p, world->terrain.height(p) + .8,
                                                               &world->cars[0], false);
                                };
                                for (const auto &b : world->buildings)
                                    if (b.height > 16 &&
                                        (!entranceReview || b.kind == BuildingKind::apartment) &&
                                        (!selected || b.p.length2() < selected->p.length2()) && forecourt(b))
                                        selected = &b;
                                if (!selected)
                                    throw std::runtime_error("No facade review building");
                                auto plan = plan_building(*selected, world->terrain.height(selected->p));
                                Vec3 n = plan.windows_on_z ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
                                auto r = cross({0, 1, 0}, n);
                                float depth = plan.windows_on_z ? plan.half_depth : plan.half_width;
                                auto front = plan.origin + n * depth;
                                View v;
                                v.eye = front + n * 17 + r * 7 + Vec3{0, 6, 0};
                                v.target = front + Vec3{0, 7.5f, 0};
                                if (entranceReview)
                                {
                                    v.eye = front + n * 6.4f + r * 2.8f + Vec3{0, 1.68f, 0};
                                    v.target = front + Vec3{0, 1.42f, 0};
                                }
                                if (wallReview)
                                {
                                    reviewCrossing = {front.x, front.z};
                                    reviewNormal = {n.x, n.z};
                                    v.eye = front + n * 10.5f - r * 5.5f + Vec3{0, 1.75f, 0};
                                    v.target = front - r * 1.2f + Vec3{0, 1.05f, 0};
                                    if (scenario == L"bus-puddle")
                                    {
                                        const auto centre = front + n * 7.5f + r * 1.8f;
                                        RoadPuddle puddle{};
                                        puddle.centre = {centre.x, centre.z};
                                        size_t index = 0;
                                        for (auto offset :
                                             {r * -2.f - n, r * 2.f - n, r * 2.f + n, r * -2.f + n})
                                        {
                                            const auto p = centre + offset;
                                            puddle.corners[index++] = {
                                                p.x, float(world->terrain.height(p.x, p.z)) + .035f, p.z};
                                        }
                                        world->puddles.push_back(puddle);
                                        v.eye = centre + n * 5.f + r * 1.f + Vec3{0, 1.1f, 0};
                                        v.target = centre - n * 3.f + Vec3{0, .7f, 0};
                                    }
                                }
                                v.heading = std::atan2(v.target.x - v.eye.x, v.target.z - v.eye.z);
                                reviewView = v;
                                auto &c = world->cars[0];
                                auto parking = front + n * 8 + (entranceReview ? r * 7 : Vec3{});
                                c.body.p = {parking.x, parking.z};
                                c.body.v = {};
                                c.body.roadLevel = 0;
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.set_hour(
                                    scenario == L"facade-night" || scenario == L"entrance-night" || scenario == L"beam-wall" ? 22 : 10);
                                world->setWeather(scenario == L"dry-canopy" || scenario == L"beam-wall" ? 1. : 0.);
                                world->weatherClock = 120;
                            }
                            if (scenario == L"far-vehicle")
                            {
                                // From the sea the silhouette and both axles of the distant
                                // truck remain visible without intervening city blocks.
                                auto &car = world->cars.at(world->controlledCar);
                                fit_vehicle(car, 16);
                                const Vec2 at{world->layout.coordinate(21) + 18,
                                              world->layout.coordinate(0) + 1.9};
                                car.body.p = at;
                                car.body.v = {};
                                car.body.roadLevel = 0;
                                car.body.height = world->terrain.height(at) + .8;
                                car.body.angle = pi * .5;
                                View v;
                                v.target = {float(at.x), float(car.body.height + .7), float(at.z)};
                                v.eye = v.target + Vec3{18, 8, -160};
                                reviewView = v;
                                world->clock.set_hour(11);
                                world->setWeather(0);
                                world->weatherClock = 120;
                            }
                            if (scenario == L"lawn" || scenario == L"park" || scenario == L"park-unshadowed" ||
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
                                if (scenario == L"lawn")
                                {
                                    // Eye height at the margin of the park, looking across the turf.
                                    v.eye = selected->foot + Vec3{2, 0, 3};
                                    v.eye.y = float(world->terrain.height(v.eye.x, v.eye.z)) + 1.55f;
                                    v.target = selected->foot + Vec3{-2, .45f, -1};
                                }
                                v.shadows = scenario != L"park-unshadowed";
                                reviewView = v;
                                auto &c = world->cars[0];
                                c.body.p = scenario == L"lawn" ? Vec2{v.eye.x + 9, v.eye.z + 9} : Vec2{v.eye.x, v.eye.z};
                                c.body.v = {};
                                c.body.roadLevel = 0;
                                c.body.height = world->terrain.height(c.body.p) + .8;
                                world->clock.set_hour(scenario == L"park-evening" ? 17.5 : 10);
                                world->setWeather(0);
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
                                        if (std::to_underlying(l.district(i, j)) != place ||
                                            CityLayout::clinic(i, j) || CityLayout::station(i, j) ||
                                            !l.road(i, j, 1) || world->levelAt(b.p, 22))
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
                                        const auto p =
                                            observerOrigin + Vec2{std::cos(a), std::sin(a)} * radius;
                                        const double floor = world->terrain.height(p);
                                        if (floor <= CityLayout::waterLevel + 1 || world->openCut(p, 1) ||
                                            world->scenerySolid(p, floor + .85, .55) ||
                                            world->inside_building(p, floor + .85, .55))
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
                                world->clock.set_hour(15);
                                world->setWeather(0);
                                world->weatherClock = 120;
                            }
                            if (yardReview)
                            {
                                const auto &l = world->layout;
                                const bool estate = scenario == L"yard-estate";
                                const Vec2 preferred = estate ? Vec2{24, 40} : Vec2{36, 11};
                                Vec2 centre{};
                                double best = 1e20;
                                for (int j = 0; j < l.nodes() - 1; ++j)
                                    for (int i = 0; i < l.nodes() - 1; ++i)
                                    {
                                        const auto *yard = l.courtyard(i, j);
                                        if (!yard ||
                                            l.district(i, j) !=
                                                (estate ? District::estate : District::courtyard) ||
                                            (!estate && yard->paths.size() < 2) || !l.road(i, j + 1, 1))
                                            continue;
                                        const auto d = (Vec2{double(i), double(j)} - preferred).length2();
                                        if (d >= best)
                                            continue;
                                        best = d;
                                        centre = l.point(i + j * l.nodes()) + Vec2{18, 18};
                                    }
                                if (best == 1e20)
                                    throw std::runtime_error("No residential court for review");
                                const auto &yard = *l.courtyard(centre);
                                Vec2 approach = centre + (yardCar ? Vec2{-7.2, 16.4} : Vec2{1.9, 9.4});
                                Vec2 dir = (centre + Vec2{-.8, -2} - approach).unit();
                                if (estate)
                                {
                                    const auto &path = yard.paths.front().points;
                                    dir = (path[2] - path[3]).unit();
                                    approach = path.back() - dir * 2.1;
                                }
                                const double heading = std::atan2(dir.x, dir.z);
                                if (yardCar)
                                {
                                    auto &car = world->cars.at(world->controlledCar);
                                    fit_vehicle(car, 0);
                                    app.zoom = 8.5;
                                    car.body.p = approach;
                                    car.body.v = {};
                                    car.body.roadLevel = 0;
                                    car.body.angle = -heading;
                                    car.body.height = world->terrain.height(approach) + .8;
                                }
                                else
                                {
                                    world->controlledCar = -1;
                                    world->player.p = approach;
                                    world->player.v = {};
                                    world->player.roadLevel = 0;
                                    world->player.angle = -heading;
                                    world->player.height = world->terrain.height(approach) + .85;
                                    world->player.verticalSpeed = 0;
                                }
                                world->clock.set_hour(scenario.ends_with(L"evening") ? 17.65 : 11.0);
                                world->setWeather(0);
                                world->weatherClock = 120;
                                log << "Courtyard centre " << centre.x << "," << centre.z << " paths "
                                    << yard.paths.size() << " curb stones " << yard.curbs.size() << " trees ";
                                for (const auto &t : world->trees)
                                    if ((Vec2{t.foot.x, t.foot.z} - centre).length() < 13.)
                                        log << std::to_underlying(t.kind) << " ";
                                log << "\n";
                            }
                            smokeStart = world->position();
                        }
                        renderer.overlay(nullptr, app.width, app.height, fps, false, nullptr, L"", levels,
                                         .96f);
                        window->swap();
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
                        if (visualRouteRain)
                        {
                            world->setWeather(*visualRouteRain ? 1.0 : 0.0);
                            world->weatherClock = 120;
                        }
                        menus.active = pendingSlot;
                        menus.open(MenuPage::home);
                        sinceSave = 0;
                        dirty = true;
                        if (smoke && passengerScenario)
                            app.pressed['B'] = true;
                        if (smoke && scenario == L"radio-menu")
                        {
                            app.menu = true;
                            menus.open(MenuPage::settings);
                        }
                        if (smoke && (scenario == L"job-board" || scenario == L"contract-series"))
                        {
                            app.pressed['J'] = true;
                            app.pressed['8'] = scenario == L"contract-series";
                        }
                        if (smoke && scenario == L"console")
                        {
                            app.posted.push_back({InputEvent::Type::keyDown, Key::f1});
                            app.posted.push_back({InputEvent::Type::keyUp, Key::f1});
                        }
                        accumulator = 0;
                        last = Timer::now();
                        if (frame_trace.active)
                        {
                            frame_trace.last = last;
                            frame_trace.phase = FrameTrace::other;
                            frame_trace.ms = {};
                        }
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
                if (app.menu || app.console || app.loading || !app.active)
                    app.mouseLookReady = false;
                if (world && !app.loading && !app.menu && !app.console && !closingOverlay &&
                    !app.jobsOpen && world->openMarket < 0 && app.pressed[Key::f9])
                    begin(true, menus.active);
                if (smoke && scenario == L"job-board" && world && frames == 14)
                {
                    if (app.menu || app.jobsOpen)
                        throw std::runtime_error("Escape did not dismiss just the journal");
                    app.pressed['J'] = true;
                }
                if (world && !app.loading && !app.menu)
                {
                    if (!app.console && (app.active || smoke))
                    {
                        const int passengerVehicle = world->transitPassenger() ? world->controlledCar : -1;
                        if (smoke && scenario == L"tunnel-exit" && frames == 0)
                            app.pressed['F'] = true;
                        if (smoke && ((scenario == L"recovery" && frames == 4) ||
                                      (scenario == L"tram-recovery" && frames == 0)))
                            app.pressed['R'] = true;
                        if (smoke && scenario == L"carjack" && !attemptedEntry)
                        {
                            attemptedEntry = true;
                            app.pressed['F'] = true;
                        }
                        if (smoke && passengerScenario && frames == 570)
                            app.pressed['F'] = true;
                        if (smoke && scenario == L"job-board" && frames == 5)
                            app.pressed['F'] = app.pressed['V'] = app.keys['W'] = app.firing = true;
                        if (smoke && scenario == L"job-board" && frames == 6)
                        {
                            if (!app.jobsOpen || world->controlledCar < 0 || app.cameraMode != 0 || app.firing)
                                throw std::runtime_error("Journal passed input through to the game");
                            app.keys['W'] = false;
                        }
                        const bool toggleJournal = world->openMarket < 0 && !world->drone.active &&
                                                   !closingOverlay && app.pressed['J'];
                        if (toggleJournal)
                            app.jobsOpen = !app.jobsOpen;
                        bool gameplayInput = !toggleJournal && !closingOverlay && !app.jobsOpen &&
                                             world->openMarket < 0;
                        if (gameplayInput && !world->drone.active && app.pressed['B'])
                        {
                            if (world->rideTransit() && world->transitPassenger())
                                app.look = world->cars[world->controlledCar].heading();
                        }
                        if (gameplayInput && app.pressed['K'])
                        {
                            bool active = world->drone.active;
                            world->toggle_drone(app.look);
                            if (world->drone.active)
                            {
                                app.lookPitch = world->drone.pitch;
                                app.jobsOpen = false;
                            }
                            else if (active)
                            {
                                app.look = -world->player.angle;
                                app.lookPitch = 0;
                            }
                        }
                        if (gameplayInput && !world->drone.active && app.pressed['F'] &&
                            (world->vehicleEntry.car >= 0 || world->playerScooter >= 0 || !world->buyFood()))
                        {
                            int previous = world->controlledCar;
                            world->enter_exit();
                            if (previous != world->controlledCar)
                            {
                                int vehicle = world->controlledCar >= 0 ? world->controlledCar : previous;
                                app.look = world->cars[vehicle].heading();
                                app.lookPitch = 0;
                            }
                        }
                        if (!closingOverlay && !toggleJournal && !world->drone.active && app.pressed['E'] && !app.jobsOpen)
                        {
                            world->interactJob();
                            gameplayInput = gameplayInput && world->openMarket < 0;
                        }
                        if (world->openMarket >= 0)
                        {
                            for (int i = 0; i < 4; ++i)
                                if (app.pressed['1' + i])
                                {
                                    world->buy_at_market(static_cast<ShopItem>(i));
                                    break;
                                }
                        }
                        else if (gameplayInput && app.pressed['G'])
                            world->use_owned_garage();
                        if (app.jobsOpen &&
                            (app.pressed['1'] || app.pressed['2'] || app.pressed['3'] || app.pressed['4'] ||
                             app.pressed['5'] || app.pressed['6'] || app.pressed['7'] || app.pressed['8']))
                        {
                            if (app.pressed['8'])
                                world->startSeries();
                            else if (app.pressed['3'] && world->job.kind != JobKind::none)
                                world->finishJob(false);
                            else if (app.pressed['1'] || app.pressed['2'])
                                world->startContract(app.pressed['1'] ? JobKind::vehicle
                                                                      : JobKind::elimination);
                            else if (app.pressed['4'] || app.pressed['5'])
                                world->startContract(app.pressed['4'] ? JobKind::leaflets
                                                                      : JobKind::broadcast);
                            else if (app.pressed['6'] || app.pressed['7'])
                                world->startContract(app.pressed['6'] ? JobKind::taxi : JobKind::patient);
                            app.jobsOpen = false;
                        }
                        if (gameplayInput && !world->drone.active && app.pressed['R'])
                            world->recover();
                        if (gameplayInput && !world->drone.active && app.pressed['V'])
                            app.cameraMode = (app.cameraMode + 1) % 3;
                        if (gameplayInput && app.pressed['L'])
                            world->toggleHelicopterLanding();
                        // The wheel and the bracket keys of the managed game both switch weapons.
                        if (const int turn = (app.wheel > 0) - (app.wheel < 0) + int(app.pressed[Key::rightBracket]) -
                                             int(app.pressed[Key::leftBracket]); turn && gameplayInput)
                            world->inventory.cycle(turn > 0 ? 1 : -1);
                        // Developer preview keys kept from the managed game.
                        if (gameplayInput && app.pressed['N'])
                            world->clock.set_hour(world->clock.hour + 6);
                        if (gameplayInput && app.pressed['P'])
                            world->setRain(world->rainTarget <= .5);
                        if (app.pressed[Key::f5])
                            saveGame(menus.active, true);
                        if (!gameplayInput)
                            app.firing = app.mouseLookReady = false;
                        if (!smoke && app.active && gameplayInput)
                        {
                            // The pointer is captured while hidden; its raw motion turns the view.
                            const auto [dx, dy] = window->takeMotion();
                            if (app.mouseLookReady && (world->controlledCar < 0 || world->transitPassenger() ||
                                                       world->cars[world->controlledCar].mounted_gun()))
                            {
                                app.look = wrap(app.look - dx * .003);
                                app.lookPitch = clamp(app.lookPitch - dy * .003, -1.1, 1.1);
                            }
                            app.mouseLookReady = true;
                        }
                        PlayerInput input{};
                        if (gameplayInput)
                        {
                            input.car = {double(app.keys['W']) - double(app.keys['S']),
                                         double(app.keys['A']) - double(app.keys['D']), false, app.keys[Key::space]};
                            auto f = forward(app.look);
                            input.walking = f * (double(app.keys['W']) - double(app.keys['S'])) +
                                            perpendicular(f) * (double(app.keys['D']) - double(app.keys['A']));
                            input.collective = double(app.keys[Key::space] || app.keys['Q']) -
                                               double(app.keys[Key::control] || app.keys['Z']);
                            input.running = app.keys[Key::shift];
                            input.shooting = app.firing;
                        }
                        input.lookHeading = app.look;
                        input.lookPitch = app.lookPitch;
                        if (smoke && scenario == L"drone")
                            input.collective = 1;
                        if (smoke && scenario == L"scooter")
                            input.walking = forward(app.look);
                        if (smoke && scenario == L"wildlife" && frames == 115)
                        {
                            auto centre = world->flocks.at(0).landing;
                            world->scare_wildlife(centre, world->terrain.height(centre), 0);
                        }
                        if (smoke && scenario == L"canal-impact")
                        {
                            const auto target = reviewView->target;
                            Vec2 delta = Vec2{target.x, target.z} - world->player.p;
                            input.lookHeading = std::atan2(delta.x, delta.z);
                            input.lookPitch =
                                std::atan2(target.y - world->player.height - .47, delta.length());
                            input.shooting = frames % 80 == 8;
                        }
                        if (smoke && recoveryScenario && backOnWheels)
                            input.car = {.85, 0, false, false};
                        if (smoke && scenario == L"tank")
                            input.shooting = app.firing = frames >= 150;
                        // The hero dies at frame 30: the caption and the hovering camera, then
                        // the clinic out of the black six seconds later.
                        if (smoke && scenario == L"death" && frames == 30)
                            world->health = 0;
                        if (smoke && scenario == L"aerial" && world->controlledCar >= 0)
                        {
                            // Held at 110 m over the street, so that every frame is the same view.
                            auto &heli = world->cars[world->controlledCar];
                            heli.lift = 110;
                            heli.body.height = world->surface(heli.body.p, 0) + .8 + heli.lift;
                            heli.verticalVelocity = 0;
                            heli.body.v = {};
                        }
                        if (smoke && scenario == L"wreckage")
                        {
                            input.car = {0, 0, true, true};
                            // The damaged car has just been struck: sparks, scrap and scuffed flanks.
                            if (frames == 20)
                                world->vehicleImpact(world->cars.at(wreckageCars[0]), 4, {4, 0});
                            if (frames == 40)
                                world->destroyCar(world->cars.at(wreckageCars[2]));
                        }
                        if (smoke && (scenario == L"drive" || scenario == L"storm"))
                            input.car = {frames < smokeFrames * 2 / 3 ? 1. : 0., .12, false,
                                         frames >= smokeFrames * 2 / 3};
                        if (smoke && yardReview && frames < smokeFrames / 2)
                        {
                            if (yardCar)
                                input.car = {.34, 0, false, false};
                            else
                                input.walking = forward(app.look);
                        }
                        if (smoke && yardCar && frames >= smokeFrames / 2)
                            input.car.handbrake = true;
                        if (smoke && scenario == L"walk")
                        {
                            input.walking = {0, 1};
                            input.running = input.shooting = true;
                        }
                        // The line-up keeps its places.
                        for (const auto &[actor, place] : lineup)
                        {
                            auto &p = world->people[actor];
                            p.body.p = place;
                            p.body.v = {};
                            p.body.height = world->terrain.height(place) + .85;
                            p.fear = 0;
                        }
                        if (smoke && passengerScenario)
                        {
                            input.car = {-1., 1., true, true};
                            app.look = world->controlledCar >= 0
                                           ? world->cars[world->controlledCar].heading() + .95
                                           : app.look;
                        }
                        if (smoke && scenario == L"puddle")
                            input.car = {.18, 0, false, false};
                        if (smoke && scenario == L"pothole")
                            input.car = {.3, 0, false, false};
                        if (smoke && scenario == L"transit")
                            input.car = {.8, 0, false, false};
                        if (smoke && (scenario == L"traffic-lanes" || scenario == L"arrest-car"))
                            input.car.handbrake = true;
                        if (smoke && scenario == L"bridge")
                            input.car = {.55, 0, false, false};
                        dirty = true;
                        sinceSave += dt;
                        if (sinceSave >= 120)
                        {
                            sinceSave = 0;
                            saveGame(menus.active, false);
                        }
                        accumulator += dt * world->simulationSpeed;
                        const int vehicleBefore = world->controlledCar;
                        const int arrestsBefore = world->arrests;
                        bool droneWasActive = world->drone.active;
                        const int maxSteps = int(std::ceil(12 * std::max(1., world->simulationSpeed)));
                        frame_trace.enter(FrameTrace::simulation);
                        int steps = 0;
                        while (accumulator >= 1. / 120 && steps < maxSteps)
                        {
                            ++steps;

                            // The frame is drawn between its last two steps: keep what the
                            // last one starts from.
                            if (blend_frames && (accumulator < 2. / 120 || steps == maxSteps))
                                blend.remember(*world);

                            world->step(input, 1. / 120);
                            if (world->arrests != arrestsBefore)
                                input.lookHeading = -world->player.angle;
                            accumulator -= 1. / 120;
                        }
                        frame_trace.enter(FrameTrace::other);
                        // The line-up looks at the camera out at sea.
                        for (const auto &member : lineup)
                            world->people[member.first].heading = pi * .5;
                        if (smoke && scenario == L"wreckage")
                        {
                            std::array<size_t, 8> seen{};
                            for (const auto &p : world->particles)
                                ++seen[std::to_underlying(p.kind)];
                            for (size_t kind = 0; kind < seen.size(); ++kind)
                                particlePeak[kind] = std::max(particlePeak[kind], seen[kind]);
                            for (const auto &sound : world->sounds)
                            {
                                blastsHeard += sound.name == "explosion";
                                firesHeard += sound.name == "fire";
                            }
                        }
                        if (vehicleBefore < 0 && world->controlledCar >= 0)
                        {
                            app.look = world->cars[world->controlledCar].heading();
                            app.lookPitch = 0;
                        }
                        if (passengerVehicle >= 0 && world->controlledCar < 0)
                        {
                            // Face down the pavement on arrival. The passenger may have been
                            // looking through the side window straight at the facade.
                            app.cameraYaw = app.look = world->cars[passengerVehicle].heading();
                            app.lookPitch = 0;
                        }
                        if (world->arrests != arrestsBefore)
                            app.cameraYaw = -world->player.angle;
                        if (world->arrests != arrestsBefore || (droneWasActive && !world->drone.active))
                        {
                            app.look = -world->player.angle;
                            app.lookPitch = 0;
                        }
                        if (steps == maxSteps && accumulator >= 1. / 120)
                            accumulator = std::fmod(accumulator, 1. / 120);

                        shown_part = accumulator * 120;
                    }
                }
                else
                    accumulator = 0;
                audio.pause(!app.active && !smoke, !world || app.loading || app.menu || app.console);

                // From here until the picture is complete the world stands between its last two
                // steps; it is put back before anything else looks at it.
                std::optional<FrameBlend::Shown> between_steps;

                if (blend_frames && world && !app.loading && !app.menu)
                    between_steps.emplace(blend, *world, shown_part);

                if (world && !app.loading && !app.menu)
                {
                    frame_trace.enter(FrameTrace::camera);
                    auto view = camera(*world, app, dt);
                    if (smoke && passengerScenario && (view.eye - view.target).length() < .39f)
                        throw std::runtime_error("Passenger camera lost its look direction");
                    // The tank fires with the button held: the camera stays behind the hull, and
                    // the sight is the point the gun is laid on.
                    if (smoke && scenario == L"tank" && frames >= 150 &&
                        (view.weapon_sight || !view.gun_sight))
                        throw std::runtime_error("The tank's gun moved the camera or has no sight");
                    if (smoke && arrestScenario && world->arrests > 0)
                        reviewView.reset();
                    if (smoke && reviewView)
                        view = *reviewView;
                    if (reviewRider >= 0)
                    {
                        const auto &rider = world->people.at(reviewRider);
                        Vec2 f = forward(rider.heading);
                        view.target = {float(rider.body.p.x), float(rider.body.height + .1),
                                       float(rider.body.p.z)};
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
                                                          (eyeHeight - view.target.y) / length, length, -1,
                                                          true, &rider);
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
                        auto it = std::ranges::find(world->animals, 0, &Animal::flock);
                        Vec3 centre{float(it->p.x), float(it->altitude + .25), float(it->p.z)};
                        view.target = centre;
                        view.eye = centre + Vec3{3, 1.9f, 4};
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
                            if (b.kind == BuildingKind::apartment &&
                                (!selected ||
                                 (b.p - stops[phase]).length2() < (selected->p - stops[phase]).length2()))
                                selected = &b;
                        if (!selected)
                            throw std::runtime_error("No apartments on streaming test route");
                        if (frames % 180 == 0)
                            log << "Streaming camera " << phase << ": apartment at " << selected->p.x << ", "
                                << selected->p.z << " (distance to district corner "
                                << (selected->p - stops[phase]).length() << " m)" << std::endl;
                        auto plan = plan_building(*selected, world->terrain.height(selected->p));
                        Vec3 normal = plan.windows_on_z ? Vec3{0, 0, 1} : Vec3{1, 0, 0};
                        auto front =
                            plan.origin + normal * (plan.windows_on_z ? plan.half_depth : plan.half_width);
                        view.target = front + Vec3{0, 5, 0};
                        view.eye = front + normal * 23 + cross({0, 1, 0}, normal) * 12 + Vec3{0, 8, 0};
                        world->clock.set_hour(10);
                        world->setWeather(0);
                    }
                    if (smoke && scenario == L"pursuit")
                    {
                        auto unit = std::ranges::find_if(world->cars, [](const Car &c)
                                                         { return c.responding && c.kind == 12; });
                        if (unit == world->cars.end() || unit->lift < 5)
                            throw std::runtime_error("Missing airborne pursuit unit");
                        view.target = {float(unit->body.p.x), float(unit->body.height + .4),
                                       float(unit->body.p.z)};
                        view.eye = view.target + Vec3{10, 4, -12};
                    }
                    if (smoke && scenario == L"cloud-coast")
                    {
                        world->dayNightCycle = false;
                        world->clock.set_hour(16.5);
                    }
                    if (smoke && scenario == L"dry-canopy" && visualRouteRain.value_or(true) &&
                        frames == smokeFrames / 2)
                        world->rain = world->rainTarget = 0.;
                    if (visualRouteRain || (smoke && scenario == L"cloud-coast") || (profile && scenario == L"shadow-avenue"))
                    {
                        const float routeProgress =
                            std::clamp(float(frames - 60) / float(std::max(1, smokeFrames - 61)), 0.f, 1.f);
                        view = visual_route(*world, scenario, view, routeProgress);
                    }
                    Vec2 facing{view.target.x - view.eye.x, view.target.z - view.eye.z};
                    frame_trace.enter(FrameTrace::audio);
                    audio.update(*world,
                                 facing.length2() > .0001 ? std::atan2(facing.x, facing.z) : view.heading);
                    frame_trace.enter(FrameTrace::prepare);
                    if (smoke && scenario == L"night-crossing")
                        renderCrossing(renderer, *world, view, reviewCrossing, frames, app.width, app.height);
                    else if (smoke && wallReview)
                    {
                        const Vec2 across{reviewNormal.z, -reviewNormal.x};
                        auto drawTraffic = [&](int index, unsigned model, Vec2 offset, Vec2 direction)
                        {
                            auto &car = world->cars.at(index);
                            fit_vehicle(car, model);
                            car.body.p = reviewCrossing + offset;
                            car.body.height = world->terrain.height(car.body.p) + .8;
                            car.body.angle = -std::atan2(direction.x, direction.z);
                            car.driverless = false;
                        };
                        const auto savedBus = world->cars.at(1), savedCar = world->cars.at(2);
                        drawTraffic(1, busModel, reviewNormal * 4. + across * (double(frames) / 12. - 16.),
                                    across);
                        drawTraffic(2, 0, reviewNormal * 8.7 - across * 2., reviewNormal * -1.);
                        renderer.render(*world, view, app.width, app.height);
                        world->cars[1] = savedBus;
                        world->cars[2] = savedCar;
                    }
                    else
                        renderer.render(*world, view, app.width, app.height);
                    frame_trace.enter(FrameTrace::other);
                    if (visualRouteRain && frames >= 60 &&
                        ((frames - 60) % 15 < 2 || frames == smokeFrames - 1))
                        renderer.screenshot(smokeOutput / std::format("review-{:04}.bmp", frames), app.width,
                                            app.height);
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
                        auto stats = renderer.streaming_stats();
                        if (stats.peak_gpu_bytes > 64 * 1024 * 1024 || stats.queued > 8 || stats.ready > 2 ||
                            stats.sectors_queued > SectorStream::maxQueued ||
                            stats.sectors_ready > SectorStream::maxReady)
                            throw std::runtime_error("Streaming exceeded its memory/queue budget");
                        // A jump of the camera must never show a hole under it, and the
                        // standing city takes less memory than all of it would.
                        if (stats.nearestMissingSector < sector_urgent)
                            throw std::runtime_error("Streaming left the ground at the camera unbuilt");
                        if (frames % 180 == 179)
                        {
                            if (stats.resident_cells == 0)
                                throw std::runtime_error(
                                    "Streaming left a visited neighbourhood without details");
                            if (stats.nearestMissingSector <= float(graphics.distance) + sector_reach ||
                                stats.resident_sectors >= stats.total_sectors)
                                throw std::runtime_error(
                                    "Streaming did not bring the city within the draw distance into memory");
                            log << "Stop " << frames / 180 << ": " << renderer.scenery_stats() << std::endl;
                            if (!frameProfile) // Readback and encoding are outside normal gameplay.
                                renderer.screenshot(smokeOutput / std::format("stop-{}.bmp", frames / 180),
                                                    app.width, app.height);
                        }
                    }
                }
                else
                {
                    glClearColor(0, 0, 0, 1);
                    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
                }
                frame_trace.enter(FrameTrace::hudPaint);
                renderer.overlay(world.get(), app.width, app.height, fps, app.menu,
                                 app.console ? &app.terminal : nullptr, app.message, levels,
                                 app.loading ? progress.load() * .95f : -1, app.jobsOpen, &menus);
                between_steps.reset();
                frame_trace.enter(FrameTrace::other);
                if (smoke && scenario == L"save-slots" && !world && app.menu && !app.loading)
                {
                    if (!capturedHome)
                    {
                        renderer.screenshot(smokeOutput / "main-menu.bmp", app.width, app.height);
                        capturedHome = true;
                    }
                    const auto rows = menus.rows(false, graphics, displayMode, levels);
                    const auto item = menus.page == MenuPage::home ? MenuItem::newGame : MenuItem::confirm;
                    const auto row = std::ranges::find(rows, item, &MenuRow::item);
                    if (row == rows.end())
                        throw std::runtime_error("New game action missing");
                    const MenuLayout layout(app.width, app.height, int(rows.size()));
                    const int x = layout.x + layout.width / 2,
                              y = layout.y + (int(row - rows.begin()) * 2 + 1) * layout.step / 2;
                    app.posted.push_back({InputEvent::Type::buttonDown, x, y});
                    app.posted.push_back({InputEvent::Type::buttonUp, x, y});
                }
                if (smoke && scenario == L"settings" && app.menu && frames == 11)
                    renderer.screenshot(smokeOutput / "settings.bmp", app.width, app.height);
                if (smoke && recoveryScenario && world && !app.loading)
                {
                    auto &car = world->cars.at(world->controlledCar);
                    if (!rightingCaptured && car.recovering && car.righting_time > 1.15)
                    {
                        renderer.screenshot(smokeOutput / "righting.bmp", app.width, app.height);
                        rightingCaptured = true;
                    }
                    if (rightingCaptured && !car.recovering && !backOnWheels && car.grounded())
                    {
                        rightedAt = car.body.p;
                        backOnWheels = true;
                    }
                }
                if (smoke && scenario == L"carjack" && world && !app.loading)
                {
                    if (!doorCaptured && world->vehicleEntry.progress > .58)
                    {
                        renderer.screenshot(smokeOutput / "door.bmp", app.width, app.height);
                        doorCaptured = true;
                    }
                    if (!crewCaptured && world->controlledCar == hijackedCar && hijackedCar >= 0)
                    {
                        renderer.screenshot(smokeOutput / "crew.bmp", app.width, app.height);
                        crewCaptured = true;
                    }
                }
                // The death in pictures: the caption over the body, the black, the clinic fading in.
                if (smoke && scenario == L"death" && world && !app.loading &&
                    (frames == 150 || frames == 380 || frames == 420 || frames == 470))
                    renderer.screenshot(smokeOutput / std::format("death-{:04}.bmp", frames), app.width, app.height);
                if (smoke && arrestScenario && world && !officerCaptured && world->patrol.hold > .15)
                {
                    renderer.screenshot(smokeOutput / "officer.bmp", app.width, app.height);
                    officerCaptured = true;
                }
                if (smoke)
                    if (auto error = glGetError(); error != GL_NO_ERROR)
                        throw std::runtime_error(std::format("OpenGL error: {:#06x}", error));
                if (smoke && world && !app.loading)
                    ++frames;
                if (smoke && passengerScenario && world && !app.loading && frames == 610)
                {
                    if (!world->transitPassenger() || !world->transitExitRequested)
                        throw std::runtime_error("Passenger input did not board and request the next stop");
                    const auto &car = world->cars[world->controlledCar];
                    const auto &line = world->lines[car.line];
                    int departure = (car.next_stop + int(line.stops.size()) - 1) % int(line.stops.size());
                    double travelled = std::remainder(car.along - line.stops[departure], line.track.length());
                    if (travelled < 1 || car.dwell > 0)
                        throw std::runtime_error("AI service failed to leave platform");
                    requestedPlatform = line.curb(car.next_stop);
                    renderer.screenshot(smokeOutput / "onboard.bmp", app.width, app.height);
                    log << "Passenger seat=" << world->playerTransitSeat << " AI distance=" << travelled
                        << " requested stop=" << car.next_stop + 1 << std::endl;
                    if (smokeFrames > 610)
                        world->simulationSpeed = 3;
                }
                bool reachedPlatform = requestedPlatform && world && world->controlledCar < 0;
                if (smoke && world && !app.loading && (frames >= smokeFrames || reachedPlatform))
                {
                    if (scenario == L"streaming")
                    {
                        auto stats = renderer.streaming_stats();
                        if (stats.evictions < 10 || stats.uploads <= stats.resident_cells ||
                            stats.resident_cells >= stats.total_cells || stats.sector_evictions < 20 ||
                            stats.sector_uploads < 20)
                            throw std::runtime_error(
                                "Streaming route did not exercise eviction and reloading");
                    }
                    if (arrestScenario)
                    {
                        log << "Officer reached suspect=" << officerCaptured << " arrests=" << world->arrests
                            << " fine=" << world->lastFine << " money=" << world->money << std::endl;
                        if (!officerCaptured || world->arrests != 1 || world->lastFine != 100 ||
                            world->money != 200 || world->wantedLevel() || world->controlledCar >= 0 ||
                            (world->position() - world->stationEntrance).length() > 3 ||
                            world->inventory.active())
                            throw std::runtime_error("Patrol failed to book and release suspect at station");
                    }
                    if (scenario == L"tunnel-exit")
                    {
                        const auto &player = world->player;
                        const double distance = (player.p - recoveryStart).length();
                        log << "Emergency exit distance=" << distance
                            << " m, support layer=" << player.roadLevel << std::endl;
                        if (world->controlledCar >= 0 || player.roadLevel != exitLevel || distance > 10.1 ||
                            std::abs(player.height - world->surface(player.p, exitLevel) - .85) > .05)
                            throw std::runtime_error("Emergency exit left the player on the wrong surface");
                    }
                    if (recoveryScenario)
                    {
                        const auto &car = world->cars.at(world->controlledCar);
                        const double offset = (rightedAt - recoveryStart).length();
                        const double driven = (car.body.p - rightedAt).length();
                        log << "Recovery displacement=" << offset << " m, driven after landing=" << driven
                            << " m, health=" << car.health << " level=" << car.body.roadLevel << std::endl;
                        if (scenario == L"tram-recovery" &&
                            (car.line != recoveryLine ||
                             (car.body.p - world->lines[recoveryLine].pose(car.along).first).length() > .15))
                            throw std::runtime_error("Rescued tram did not stay on its line");
                        if (!backOnWheels || !rightingCaptured || driven < .5 ||
                            std::abs(car.health - .62) > .001 ||
                            (scenario != L"rollover" && world->recoveryReadyAt <= 0) || offset > 190 ||
                            (scenario == L"recovery" && (offset < 1 || car.body.roadLevel != 0)) ||
                            (scenario == L"rollover" && offset > 3))
                            throw std::runtime_error("Vehicle failed to recover and drive away");
                    }
                    if (scenario == L"carjack")
                    {
                        int officers =
                            int(std::ranges::count(world->people, Person::Uniform::police, &Person::uniform));
                        log << "Vehicle=" << hijackedCar << " controlled=" << world->controlledCar
                            << " door=" << doorCaptured << " ejected officers=" << officers << std::endl;
                        if (!doorCaptured || !crewCaptured || world->controlledCar != hijackedCar ||
                            officers != 2)
                            throw std::runtime_error("Vehicle takeover did not complete as expected");
                    }
                    if (scenario == L"market" &&
                        (world->openMarket < 0 || world->inventory.ammo(WeaponId::pistol) != 30))
                        throw std::runtime_error("Market did not stay open at the counter");
                    if (scenario == L"workshop" &&
                        (world->ownedGarage < 0 || !world->cars.at(0).paint || world->wantedLevel()))
                        throw std::runtime_error("Workshop service did not last through gameplay");
                    if (scenario == L"traffic-lanes")
                    {
                        const auto &driver = world->cars.at(1);
                        const auto &wreck = world->cars.at(2);
                        log << "Overtake: passed=" << trafficPassed << " lane=" << driver.laneIndex
                            << " travelX=" << driver.body.p.x - passingWreckPosition.x
                            << " lateral=" << driver.body.p.z - passingWreckPosition.z
                            << " health=" << driver.health
                            << " wreck drift=" << (wreck.body.p - passingWreckPosition).length() << std::endl;
                        if (!trafficPassed || driver.health < .999 ||
                            (wreck.body.p - passingWreckPosition).length() > .3)
                            throw std::runtime_error("NPC failed to pass stationary wreck without contact");
                    }
                    if (scenario == L"canal-impact" && world->ripples.empty())
                        throw std::runtime_error("Shots from the quay did not reach the canal surface");
                    if (scenario == L"pothole" && world->potholeJolts == 0)
                        throw std::runtime_error("Driving over a pothole did not jolt the car");
                    if (scenario == L"puddle" &&
                        (world->tireContacts.empty() || world->tireContacts.front().wetUntil <= world->time ||
                         std::ranges::none_of(world->surfaceMarks,
                                              [](const auto &m) { return m.kind == MarkKind::wet; })))
                        throw std::runtime_error("Drive through standing water left no wet tracks");
                    if (passengerScenario && smokeFrames > 610)
                    {
                        if (!reachedPlatform || (world->player.p - *requestedPlatform).length() > 3.5)
                            throw std::runtime_error(
                                "AI service did not deliver passenger to requested curb");
                        log << "Alighted at requested stop, distance to curb centre="
                            << (world->player.p - *requestedPlatform).length() << std::endl;
                    }
                    double distance = (world->position() - smokeStart).length();
                    if (yardReview)
                    {
                        log << "Courtyard travel " << distance << " m, vehicle " << world->controlledCar
                            << "\n";
                        if (distance < .4 || (world->controlledCar >= 0) != yardCar)
                            throw std::runtime_error("Courtyard approach failed to advance in selected mode");
                    }
                    if ((scenario == L"drive" || scenario == L"walk" || scenario == L"transit" ||
                         scenario == L"bridge") &&
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
                    if (scenario == L"drone" &&
                        (!world->drone.active || world->drone.altitude < world->ground() + 12))
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
                    if (scenario == L"scooter" &&
                        (world->playerScooter < 0 || (world->player.p - smokeStart).length() < 2))
                        throw std::runtime_error("Scooter ride failed");
                    if (reviewRider >= 0)
                    {
                        const auto &rider = world->people.at(reviewRider);
                        if (world->scooters.at(rider.ownedScooter).rider != reviewRider ||
                            rider.distance - riderStartDistance < 1.5)
                            throw std::runtime_error("Delivery rider did not travel with their scooter");
                    }
                    if (scenario == L"wreckage")
                    {
                        const auto &smoking = world->cars.at(wreckageCars[0]);
                        const auto &burning = world->cars.at(wreckageCars[1]);
                        const auto &blown = world->cars.at(wreckageCars[2]);
                        log << "wreckage peaks: spark="
                            << particlePeak[std::to_underlying(ParticleKind::spark)]
                            << " smoke=" << particlePeak[std::to_underlying(ParticleKind::smoke)]
                            << " debris=" << particlePeak[std::to_underlying(ParticleKind::debris)]
                            << " fire=" << particlePeak[std::to_underlying(ParticleKind::fire)]
                            << " blasts=" << blastsHeard << " fires=" << firesHeard
                            << " burning=" << burning.fireTime << " scuffs=" << world->scuffs.size()
                            << std::endl;
                        if (!blown.destroyed || blastsHeard != 1)
                            throw std::runtime_error("The staged car did not blow up once");
                        if (burning.destroyed || burning.fireTime <= 0 || firesHeard < 1)
                            throw std::runtime_error("The staged fire went out or was not heard");
                        if (smoking.destroyed || smoking.fireTime > 0)
                            throw std::runtime_error("The damaged car caught fire");
                        if (particlePeak[std::to_underlying(ParticleKind::spark)] < 18 ||
                            world->scuffs.empty())
                            throw std::runtime_error("The struck car shows no sparks or scuffs");
                        if (particlePeak[std::to_underlying(ParticleKind::fire)] < 55 ||
                            particlePeak[std::to_underlying(ParticleKind::smoke)] < 60 ||
                            particlePeak[std::to_underlying(ParticleKind::debris)] < 30)
                            throw std::runtime_error("The wreckage threw too few particles");
                    }
                    if (scenario == L"hydrant" &&
                        std::ranges::none_of(world->fixtures,
                                             [&](const auto &f) {
                                                 return f.sprayUntil > world->time &&
                                                        (f.home - world->position()).length2() < 65 * 65;
                                             }))
                        throw std::runtime_error("Review car failed to dislodge hydrant");
                    if (scenario == L"wildlife" && !world->flocks.at(0).flying)
                        throw std::runtime_error("Flock did not take off");
                    if (scenario == L"ambulance" && (world->rescues[0].stage != World::RescueStage::care ||
                                                     world->people[world->rescues[0].medic].riding >= 0))
                    {
                        const auto &r = world->rescues[0];
                        const std::string apart =
                            r.car >= 0 && r.patient >= 0
                                ? std::format(
                                      ", distance {:f}",
                                      (world->cars[r.car].body.p - world->people[r.patient].body.p).length())
                                : std::string{};
                        throw std::runtime_error(std::format(
                            "Ambulance review: stage {}, car {}, patient {}, simulation time {:f}{}",
                            std::to_underlying(r.stage), r.car, r.patient, world->time, apart));
                    }
                    if (scenario == L"console" &&
                        (!consoleReviewed || world->money != 35 || app.terminal.input() != L"wanted " ||
                         app.terminal.visible_lines(6).size() != 6))
                        throw std::runtime_error("Console commands or keyboard editing failed");
                    if (scenario == L"job-board" && !app.jobsOpen)
                        throw std::runtime_error("Journal did not open");
                    if (scenario == L"contract-series" && (!world->job.series || world->expectedPay() != 300))
                        throw std::runtime_error("First series contract was not offered");
                    if (scenario == L"street-hail" && world->hailerInReach() < 0)
                        throw std::runtime_error("Hailing pedestrian is unavailable at the kerb");
                    if (scenario == L"taxi" &&
                        (!world->job.acquired ||
                         world->people[world->job.actor].riding != world->controlledCar))
                        throw std::runtime_error("Taxi passenger did not board");
                    if (scenario == L"leaflets" && world->job.route.completed != 1)
                        throw std::runtime_error("Leaflet handout did not finish");
                    if (scenario == L"tank" &&
                        (world->controlledCar < 0 || world->cars[world->controlledCar].kind != 11 ||
                         world->shotCooldown == 0))
                        throw std::runtime_error("Tank did not fire");
                    if (scenario == L"crowd" &&
                        (world->density != Density::full ||
                         world->cars.size() < populationSize(Density::full).cars ||
                         world->people.size() != populationSize(Density::full).people ||
                         world->animals.size() < 1000 || std::ranges::any_of(world->people, &Person::dead)))
                        throw std::runtime_error("The full city is not populated");
                    if (scenario == L"settings")
                    {
                        const auto disk = GraphicsPreferences::load(graphicsFile);
                        const auto savedAudio = AudioLevels::load(audioFile);
                        std::string savedDisplay;
                        std::ifstream(displayFile) >> savedDisplay;
                        if (app.menu || disk.distance != 1050 || disk.quality != 0 || disk.effects ||
                            disk.bloom || displayMode != SceneMode::scaled || savedDisplay != "balanced" ||
                            savedAudio.master != 70 || savedAudio.radio != 25)
                            throw std::runtime_error("Settings failed to apply or persist");
                        log << "Settings applied during play: range=" << disk.distance
                            << " quality=" << disk.quality << " particles=" << disk.effects
                            << " bloom=" << disk.bloom << " display=" << modeName() << std::endl;
                    }
                    slots.finish();
                    if (scenario == L"save-slots")
                    {
                        World check;
                        for (int i = 1; i <= 3; ++i)
                        {
                            check.load(slots.path(i));
                            if (check.money != (i == 1 ? 111 : 666))
                                throw std::runtime_error("Save slots leaked state");
                        }
                        if (menus.active != 2 || menus.selected != 3)
                            throw std::runtime_error("Save-as changed active slot");
                        log << "Three independent saves; active=2 selected=3, pause/periodic/F5/F9 verified"
                            << std::endl;
                    }
                    const auto checkPath =
                        scenario == L"save-slots" ? smokeOutput / "roundtrip.json" : app.saveFile;
                    world->save(checkPath);
                    World loaded;
                    loaded.load(checkPath);
                    if ((loaded.position() - world->position()).length() > 1e-8 ||
                        loaded.inventory.capture() != world->inventory.capture())
                        throw std::runtime_error("Smoke save/load mismatch");
                    if (!screenshot.empty())
                        renderer.screenshot(screenshot, app.width, app.height);
                    log << renderer.scenery_stats() << std::endl;
                    if (frameProfile && !frameTimes.empty())
                    {
                        if (scenario == L"streaming")
                        {
                            std::ofstream trace(smokeOutput / "frame-times.csv");
                            trace << "frame,ms," << FrameTrace::columns << '\n';
                            for (size_t i = 0; i < frameTimes.size(); ++i)
                            {
                                trace << i << ',' << frameTimes[i];
                                for (const double ms : phases[i])
                                    trace << ',' << ms;
                                trace << '\n';
                            }
                        }
                        if (frameTimes.size() > 60)
                            frameTimes.erase(frameTimes.begin(), frameTimes.begin() + 60);
                        std::ranges::sort(frameTimes);
                        auto at = [&](double p)
                        { return frameTimes[size_t(std::ceil(p * frameTimes.size())) - 1]; };
                        log << "frame: samples=" << frameTimes.size() << " median_ms=" << at(.5)
                            << " p95_ms=" << at(.95) << " p99_ms=" << at(.99) << " max_ms=" << frameTimes.back()
                            << " vsync_requested=" << !profile << std::endl;
                    }
                    if (profile)
                        log << renderer.gpu_report() << " output=" << app.width << "x" << app.height
                            << " mode=" << modeName() << std::endl;
                    log << "SMOKE PASS frames=" << frames << " cars=" << world->cars.size()
                        << " people=" << world->people.size() << " moved=" << distance
                        << " simulation=" << world->time << " scenario=" << utf8(scenario)
                        << " elapsed=" << std::chrono::duration<double>(now - start).count() << std::endl;
                    app.running = false;
                }
                frame_trace.enter(FrameTrace::swap);
                window->swap();
                frame_trace.enter(FrameTrace::other);
                std::ranges::fill(app.pressed, false);
                app.wheel = 0;
                app.clicked = app.pointerMoved = false;
            }
            if (!smoke && world && dirty && !app.loading)
                slots.write(*world, menus.active);
            slots.finish();
        }
    }
    catch (const std::exception &e)
    {
        log << "FATAL: " << e.what() << std::endl;
        if (!smoke)
            show_error(e.what());
        code = 1;
    }
    app.cursor(false);
    app.window = nullptr;
    window.reset();
    SDL_Quit();
    return code;
}
