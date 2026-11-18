#include "promzona/frame_blend.hpp"
#include <cmath>
#include <filesystem>
#include <format>
#include <fstream>
#include <iterator>
#include <print>
#include <source_location>
#include <stdexcept>
#include <string>
#include <vector>

using namespace Promzona;
namespace
{
void require(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}

constexpr double step = 1. / 120;

std::string saved(const World &w, const std::filesystem::path &file)
{
    std::filesystem::remove(file);
    w.save(file);
    std::ifstream in(file, std::ios::binary);
    return {std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>()};
}

// One car that covers `speed` metres a second in steps of the simulation's length, drawn
// `rate` times a second the way the game draws: how far the picture of it moves from one
// frame to the next, the least and the most, with the frames between steps and without.
struct Pace
{
    double least{1e9}, most{};
};

Pace pace(double rate, bool blended)
{
    const double speed = 20;
    World w;
    w.cars.resize(1);
    w.cars[0].body.id = 5;
    FrameBlend blend;
    Pace result;
    double accumulator = 0, last = 0;

    for (int frame = 0; frame < 600; ++frame)
    {
        accumulator += 1 / rate;

        while (accumulator >= step)
        {
            if (accumulator < 2 * step)
                blend.remember(w);

            w.time += step;
            w.cars[0].body.p.x += speed * step;
            accumulator -= step;
        }

        if (blended)
            blend.show(w, accumulator / step);

        const double shown = w.cars[0].body.p.x;
        blend.restore(w);

        // The first frames have no step behind them yet.
        if (frame > 3)
        {
            result.least = std::min(result.least, shown - last);
            result.most = std::max(result.most, shown - last);
        }

        last = shown;
    }

    return result;
}
} // namespace

int main()
{
    try
    {
        // Whole steps at 144 and at 100 frames a second: a frame that repeats the one before,
        // a frame that shows two steps. Between the steps every frame moves the car as far as
        // a frame lasts.
        for (double rate : {144., 100., 60., 75., 165., 240.})
        {
            const Pace even = pace(rate, true);
            require(std::abs(even.least - 20 / rate) < 1e-9 && std::abs(even.most - 20 / rate) < 1e-9,
                    "a frame between two steps does not move the picture by a frame's worth");
        }

        require(pace(144, false).least == 0 && pace(100, false).most > 20 * step * 1.9,
                "whole steps are even at 144 and at 100 frames a second: nothing is tested");

        // A city that has been walking and driving: what is shown lies between the last two
        // steps, and putting it back leaves the city as the simulation had it, to the byte of
        // its save.
        World w;
        w.generate(20261105, {}, CityLayout::districtRevision, Density(0));
        PlayerInput input{};

        for (int i = 0; i < 240; ++i)
            w.step(input, step);

        const auto file = std::filesystem::temp_directory_path() / "promzona-frame-blend-test.json";
        FrameBlend blend;
        blend.remember(w);
        std::vector<Vec2> carsBefore, peopleBefore;

        for (const Car &c : w.cars)
            carsBefore.push_back(c.body.p);

        for (const Person &p : w.people)
            peopleBefore.push_back(p.body.p);

        // Nothing is shown before the step has been taken.
        const std::string start = saved(w, file);
        blend.show(w, .5);
        blend.restore(w);
        require(saved(w, file) == start, "a frame was drawn between steps before the step was taken");

        w.step(input, step);
        const std::string whole = saved(w, file);
        std::vector<Vec2> carsAfter, peopleAfter;

        for (const Car &c : w.cars)
            carsAfter.push_back(c.body.p);

        for (const Person &p : w.people)
            peopleAfter.push_back(p.body.p);

        require(carsAfter.size() == carsBefore.size() && peopleAfter.size() == peopleBefore.size(),
                "the city changed its lists in one quiet step");
        blend.show(w, .25);
        int movedCars = 0, movedPeople = 0, distant = 0;
        const Vec2 focus = w.position();
        // A body the step put somewhere else is shown where it is.
        const auto between = [](Vec2 shown, Vec2 before, Vec2 after)
        {
            const Vec2 expected = (after - before).length() < 2 ? before + (after - before) * .25 : after;
            return (shown - expected).length() < 1e-12;
        };

        for (size_t i = 0; i < w.cars.size(); ++i)
        {
            const bool moved = (carsAfter[i] - carsBefore[i]).length() > 1e-6;
            const double away = (carsBefore[i] - focus).length();

            if (away < 259)
            {
                require(between(w.cars[i].body.p, carsBefore[i], carsAfter[i]),
                        "a car near the camera is not shown between the steps");
                movedCars += moved;
            }
            else if (away > 261)
            {
                require(w.cars[i].body.p.x == carsAfter[i].x && w.cars[i].body.p.z == carsAfter[i].z,
                        "a car far from the camera was moved");
                ++distant;
            }
        }

        for (size_t i = 0; i < w.people.size(); ++i)
        {
            const bool moved = (peopleAfter[i] - peopleBefore[i]).length() > 1e-6;

            if ((peopleBefore[i] - focus).length() < 149)
            {
                require(between(w.people[i].body.p, peopleBefore[i], peopleAfter[i]),
                        "a passer-by near the camera is not shown between the steps");
                movedPeople += moved;
            }
        }

        require(movedCars > 3 && movedPeople > 10 && distant > 20,
                "too little moves in the city to test with");
        require(saved(w, file) != whole, "the city between two steps is the city after them");
        blend.restore(w);
        require(saved(w, file) == whole, "the city was not put back as the simulation left it");

        // Shown twice, the second time changes nothing; so does a city that is not the one
        // remembered.
        blend.show(w, .25);
        const std::string once = saved(w, file);
        blend.show(w, .75);
        require(saved(w, file) == once, "a city already between two steps was moved again");
        blend.restore(w);
        blend.restore(w);
        require(saved(w, file) == whole, "the city was not put back after being shown twice");
        World other;
        other.cars.resize(1);
        other.cars[0].body.p = {3, 4};
        other.time = w.time;
        blend.show(other, .5);
        require(other.cars[0].body.p.x == 3 && other.cars[0].body.p.z == 4, "another city was moved");
        blend.restore(other);

        // A body put somewhere else by the step is shown where it is; a list that has changed
        // moves nobody into another's place; an angle goes the short way round.
        {
            World small;
            small.cars.resize(3);

            for (std::uint32_t i = 0; i < 3; ++i)
            {
                small.cars[i].body.id = 10 + i;
                small.cars[i].body.p = {i * 5., 0};
            }

            small.cars[0].body.angle = 3.1;
            small.people.resize(1);
            small.people[0].body.id = 40;
            small.people[0].heading = -3.1;
            small.player.id = 1;
            FrameBlend b;
            b.remember(small);
            small.time += step;
            small.cars[0].body.p.x += .2;
            small.cars[0].body.angle = -3.1;
            small.cars[0].roll = .2;
            small.cars[1].body.p = {400, 80};
            small.people[0].heading = 3.1;
            small.people[0].body.height = 1;
            small.player.p = {.5, -.5};
            small.cars.erase(small.cars.begin() + 2);
            small.cars.insert(small.cars.begin() + 2, Car{});
            small.cars[2].body.id = 99;
            small.cars[2].body.p = {10.5, 0};
            b.show(small, .5);
            require(std::abs(small.cars[0].body.p.x - .1) < 1e-12 &&
                        std::abs(small.cars[0].roll - .1) < 1e-12 &&
                        std::abs(std::abs(small.cars[0].body.angle) - pi) < 1e-9,
                    "a car is not shown half way, or turned the long way round");
            require(small.cars[1].body.p.x == 400 && small.cars[1].body.p.z == 80,
                    "a car put somewhere else is shown on its way there");
            require(small.cars[2].body.p.x == 10.5, "a new car was shown in the place of the one before it");
            require(std::abs(std::abs(small.people[0].heading) - pi) < 1e-9 &&
                        std::abs(small.people[0].body.height - .5) < 1e-12,
                    "a passer-by is not shown half way round");
            require(std::abs(small.player.p.x - .25) < 1e-12 && std::abs(small.player.p.z + .25) < 1e-12,
                    "the player is not shown half way");
            b.restore(small);
            require(small.cars[0].body.p.x == .2 && small.cars[0].body.angle == -3.1 &&
                        small.cars[0].roll == .2 && small.people[0].heading == 3.1 &&
                        small.people[0].body.height == 1 && small.player.p.x == .5 && small.player.p.z == -.5,
                    "the small city was not put back");

            // A part that is not within a step, or not a number, shows the city as it is.
            for (double part : {-0.1, 1., 7., std::nan("")})
            {
                b.show(small, part);
                require(small.cars[0].body.p.x == .2, "a part outside the step moved the city");
                b.restore(small);
            }
        }

        // The drone, when it flies, is shown between the steps as well.
        {
            World small;
            small.drone.active = true;
            small.drone.position = {10, 10};
            small.drone.altitude = 30;
            FrameBlend b;
            b.remember(small);
            small.time += step;
            small.drone.position = {10.4, 10};
            small.drone.altitude = 31;
            small.drone.heading = .2;
            b.show(small, .5);
            require(std::abs(small.drone.position.x - 10.2) < 1e-12 &&
                        std::abs(small.drone.altitude - 30.5) < 1e-12 &&
                        std::abs(small.drone.heading - .1) < 1e-12,
                    "the drone is not shown half way");
            b.restore(small);
            require(small.drone.position.x == 10.4 && small.drone.altitude == 31 && small.drone.heading == .2,
                    "the drone was not put back");
        }

        std::filesystem::remove(file);
        auto backup = file;
        backup += ".bak";
        std::filesystem::remove(backup);
        std::println("frame blend tests passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "FAILED: {}", e.what());
        return 1;
    }
}
