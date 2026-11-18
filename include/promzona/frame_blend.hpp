#pragma once
#include "world.hpp"

namespace Promzona
{
    // The city moves in steps of its own length, and a frame is seldom a whole number of them:
    // at 144 frames a second every sixth frame showed the city where the fifth had it, and at
    // 100 every fifth showed two steps at once. A frame now shows what lies between the last
    // two steps: every body near the camera is put where it stood before the last step, moved
    // on by as much of that step as the clock has run since, and put back as soon as the frame
    // is drawn. The simulation itself is not changed by this, to the last bit.
    class FrameBlend
    {
        struct Pose
        {
            Vec2 p{};
            double height{}, angle{}, heading{}, roll{}, pitch{}, terrainRoll{}, terrainPitch{};
        };

        struct Kept
        {
            std::uint32_t index{}, id{};
            Pose before{}, now{};
            bool shown{};
        };

        // Further than this in one step is not a movement to show the middle of: a body was
        // put somewhere else.
        static constexpr double leap = 2;

        std::vector<Kept> cars_, people_, scooters_;
        Kept player_{}, drone_{};
        const World *of_{};
        double time_{};
        bool showing_{};

        static Pose read(const Body &b) { return {.p = b.p, .height = b.height, .angle = b.angle}; }

        static void write(Body &b, const Pose &pose)
        {
            b.p = pose.p;
            b.height = pose.height;
            b.angle = pose.angle;
        }

        static Pose read(const Car &c)
        {
            Pose pose = read(c.body);
            pose.roll = c.roll;
            pose.pitch = c.pitch;
            pose.terrainRoll = c.terrainRoll;
            pose.terrainPitch = c.terrainPitch;
            return pose;
        }

        static void write(Car &c, const Pose &pose)
        {
            write(c.body, pose);
            c.roll = pose.roll;
            c.pitch = pose.pitch;
            c.terrainRoll = pose.terrainRoll;
            c.terrainPitch = pose.terrainPitch;
        }

        static Pose read(const Person &p)
        {
            Pose pose = read(p.body);
            pose.heading = p.heading;
            return pose;
        }

        static void write(Person &p, const Pose &pose)
        {
            write(p.body, pose);
            p.heading = pose.heading;
        }

        static Pose read(const Scooter &s) { return read(s.body); }

        static void write(Scooter &s, const Pose &pose) { write(s.body, pose); }

        static Pose read(const ReconDrone &d)
        {
            return {.p = d.position, .height = d.altitude, .heading = d.heading, .pitch = d.pitch};
        }

        static void write(ReconDrone &d, const Pose &pose)
        {
            d.position = pose.p;
            d.altitude = pose.height;
            d.heading = pose.heading;
            d.pitch = pose.pitch;
        }

        static Pose between(const Pose &a, const Pose &b, double part)
        {
            const auto mix = [part](double from, double to) { return from + (to - from) * part; };
            // An angle goes the short way round.
            const auto turn = [part](double from, double to) { return from + wrap(to - from) * part; };

            return {.p = a.p + (b.p - a.p) * part,
                    .height = mix(a.height, b.height),
                    .angle = turn(a.angle, b.angle),
                    .heading = turn(a.heading, b.heading),
                    .roll = turn(a.roll, b.roll),
                    .pitch = turn(a.pitch, b.pitch),
                    .terrainRoll = turn(a.terrainRoll, b.terrainRoll),
                    .terrainPitch = turn(a.terrainPitch, b.terrainPitch)};
        }

        // Shows `actor` between the pose kept for it and the one it has. False, and nothing
        // changed, where the two are not a movement of one step.
        template <class Actor> static bool put(Kept &kept, Actor &actor, double part)
        {
            kept.now = read(actor);
            kept.shown = (kept.now.p - kept.before.p).length2() < leap * leap;

            if (kept.shown)
                write(actor, between(kept.before, kept.now, part));

            return kept.shown;
        }

        template <class Actor>
        static void keep(std::vector<Kept> &kept, const std::vector<Actor> &actors, Vec2 focus, double radius)
        {
            kept.clear();

            for (std::uint32_t i = 0; i < actors.size(); ++i)
                if ((actors[i].body.p - focus).length2() < radius * radius)
                    kept.push_back({.index = i, .id = actors[i].body.id, .before = read(actors[i])});
        }

        template <class Actor>
        static void put(std::vector<Kept> &kept, std::vector<Actor> &actors, double part)
        {
            for (Kept &k : kept)
            {
                // The lists change between steps: the place in the list has to hold the same
                // body still.
                k.shown = k.index < actors.size() && actors[k.index].body.id == k.id;

                if (k.shown)
                    put(k, actors[k.index], part);
            }
        }

        template <class Actor> static void put_back(const std::vector<Kept> &kept, std::vector<Actor> &actors)
        {
            for (const Kept &k : kept)
                if (k.shown && k.index < actors.size())
                    write(actors[k.index], k.now);
        }

      public:
        // Called before the last step of a frame: keeps where the bodies near the camera are.
        void remember(const World &world)
        {
            const Vec2 focus = world.drone.active ? world.drone.position : world.position();

            keep(cars_, world.cars, focus, 260);
            keep(people_, world.people, focus, 150);
            keep(scooters_, world.scooters, focus, 150);
            player_ = {.id = world.player.id, .before = read(world.player)};
            drone_ = {.before = read(world.drone)};
            of_ = &world;
            time_ = world.time;
        }

        // Puts the remembered bodies `part` of the way from where the last step found them to
        // where it left them: 0 is before that step, 1 is the world as it is. Does nothing
        // unless exactly the remembered step has been taken since remember().
        void show(World &world, double part)
        {
            if (showing_ || of_ != &world || !(part >= 0 && part < 1) || !(world.time > time_) ||
                !(world.time - time_ < .05))
                return;

            put(cars_, world.cars, part);
            put(people_, world.people, part);
            put(scooters_, world.scooters, part);
            player_.shown = world.player.id == player_.id && put(player_, world.player, part);
            drone_.shown = world.drone.active && put(drone_, world.drone, part);
            showing_ = true;
        }

        // Puts every body back where the simulation left it.
        void restore(World &world)
        {
            if (!showing_ || of_ != &world)
                return;

            put_back(cars_, world.cars);
            put_back(people_, world.people);
            put_back(scooters_, world.scooters);

            if (player_.shown)
                write(world.player, player_.now);

            if (drone_.shown)
                write(world.drone, drone_.now);

            showing_ = false;
        }

        // The world between its last two steps, for as long as this lives.
        class Shown
        {
            FrameBlend &blend_;
            World &world_;

          public:
            Shown(FrameBlend &blend, World &world, double part) : blend_(blend), world_(world)
            {
                blend_.show(world_, part);
            }

            ~Shown() { blend_.restore(world_); }

            Shown(const Shown &) = delete;
            Shown &operator=(const Shown &) = delete;
        };
    };
} // namespace Promzona
