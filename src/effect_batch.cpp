#include "effect_batch.hpp"
#include <algorithm>
#include <cstddef>

namespace Promzona
{
    using namespace gl;

    namespace
    {
        uint32_t mixBits(uint32_t n)
        {
            n ^= n >> 16;
            n *= 0x7feb352du;
            n ^= n >> 15;
            n *= 0x846ca68bu;
            return n ^ (n >> 16);
        }
        float fraction(uint32_t n)
        {
            return static_cast<float>(n & 65535u) / 65536.f;
        }
    }

    void EffectBatch::initialize(GLuint shader)
    {
        matrix_ = GetUniformLocation(shader, "uViewProjection");
        GenVertexArrays(1, &vao_);
        GenBuffers(1, &vbo_);
        BindVertexArray(vao_);
        BindBuffer(ARRAY_BUFFER, vbo_);
        auto attribute = [](GLuint index, GLint size, size_t offset)
        {
            EnableVertexAttribArray(index);
            VertexAttribPointer(index, size, GL_FLOAT, GL_FALSE, sizeof(Vertex),
                                reinterpret_cast<const void *>(offset));
        };
        attribute(0, 3, offsetof(Vertex, p));
        attribute(1, 4, offsetof(Vertex, color));
        attribute(2, 3, offsetof(Vertex, u));
        BindVertexArray(0);
    }
    EffectBatch::~EffectBatch()
    {
        if (vbo_)
            DeleteBuffers(1, &vbo_);
        if (vao_)
            DeleteVertexArrays(1, &vao_);
    }

    void EffectBatch::append(const std::array<Vec3, 4> &corners, Vec3 color, float alpha, float shape)
    {
        auto centre = (corners[0] + corners[2]) * .5f;
        quads_.push_back({corners, color, alpha, shape, dot(centre - eye_, forward_)});
    }

    void EffectBatch::billboard(Vec3 p, Vec3 r, Vec3 u, Vec3 color, float alpha, float shape)
    {
        append({p - r - u, p + r - u, p + r + u, p - r + u}, color, alpha, shape);
    }

    void EffectBatch::rain(const World &w, const Frustum &frustum, float daylight)
    {
        if (w.rain < .015)
            return;
        constexpr float spacing = 2.f;
        int cx = static_cast<int>(std::floor(eye_.x / spacing)), cz = int(std::floor(eye_.z / spacing));
        auto color = Vec3{.65f, .73f, .81f} * (.36f + .64f * daylight);
        for (int z = cz - 12; z <= cz + 12; ++z)
            for (int x = cx - 12; x <= cx + 12; ++x)
            {
                auto key = mixBits(uint32_t(x) * 0x9e3779b9u ^ uint32_t(z) * 0x85ebca6bu ^ w.seed);
                auto phase = mixBits(key);
                if (fraction(phase) > w.rain)
                    continue;
                Vec3 p{(x + fraction(key)) * spacing, float(rainHeight(fraction(phase >> 16), w.time, eye_.y)),
                       (z + fraction(key >> 16)) * spacing};
                if (!frustum.sphere(p, .7f))
                    continue;
                auto &cell = rainCells_[(uint32_t(x) & 31u) + 32 * (uint32_t(z) & 31u)];
                if (!cell.valid || cell.x != x || cell.z != z)
                    cell = {x, z, true, float(w.precipitationSurface({p.x, p.z}))};
                float halfLength = std::min(.36f, (p.y - cell.ground - .02f));
                if (halfLength <= .01f)
                    continue;
                auto delta = p - eye_;
                float fade = std::clamp((24.f - std::hypot(delta.x, delta.z)) * .23f, 0.f, 1.f);
                billboard(p, right_ * .014f, Vec3{.12f, 1.f, .035f} * halfLength, color, .43f * fade, 1);
            }
    }

    void EffectBatch::marks(const World &w, const Frustum &frustum, float day)
    {
        for (const auto &m : w.surfaceMarks)
        {
            auto mid = (m.corners[0] + m.corners[2]) * .5f;
            if (std::hypot(mid.x - eye_.x, mid.z - eye_.z) > 110 || !frustum.sphere(mid, 2.f))
                continue;
            float opacity = float(std::clamp((m.duration - w.time + m.born) / (m.duration * .4), 0., 1.));
            if (opacity <= 0)
                continue;
            bool blood = m.kind == MarkKind::blood;
            Vec3 color = blood                      ? Vec3{.26f, .027f, .019f}
                         : m.kind == MarkKind::sand ? Vec3{.39f, .30f, .18f}
                         : m.kind == MarkKind::wet  ? Vec3{.09f, .14f, .15f}
                                                    : Vec3{.04f, .038f, .04f};
            append(m.corners, color * (.42f + .58f * day), opacity * (m.kind == MarkKind::wet ? .36f : .76f),
                   blood ? 2.f : 3.f);
        }
    }

    // Port of the collision marks of CityEffects.Draw: dark strips on both flanks of a car
    // that was hit, and a patch on the wall beside it.
    void EffectBatch::scuffs(const World &w, const Frustum &frustum, float day)
    {
        for (const auto &s : w.scuffs)
        {
            const float opacity = float(std::clamp(1. - (w.time - s.born) / s.duration(), 0., 1.)) * .8f;
            if (s.wall)
            {
                if (!frustum.sphere(s.at, 1.f))
                    continue;
                const Vec3 along = s.facesX ? Vec3{0, 0, -.7f} : Vec3{.7f, 0, 0};
                billboard(s.at, along, Vec3{0, .26f, 0}, Vec3{.08f, .07f, .06f} * (.35f + .65f * day), opacity,
                          1.f);
                continue;
            }
            const auto car = std::ranges::find(w.cars, s.car, &Car::id);
            if (car == w.cars.end() || std::abs(car->roll) > .3)
                continue;
            const Vec3 centre{float(car->body.p.x), float(car->body.height - .13), float(car->body.p.z)};
            if (!frustum.sphere(centre, 3.f))
                continue;
            const auto dir = forward(car->heading());
            const Vec3 f{float(dir.x), 0, float(dir.z)}, side{f.z, 0, -f.x};
            for (float flank : {-1.f, 1.f})
                billboard(centre + side * (flank * (float(car->body.halfW) + .015f)), f * .55f, Vec3{0, .10f, 0},
                          Vec3{.05f, .045f, .04f} * (.35f + .65f * day), opacity, 1.f);
        }
    }

    void EffectBatch::hydrants(const World &w, const Frustum &frustum, float day)
    {
        for (auto index : w.nearbyStaticBodies)
        {
            if (index >= w.fixtureByBody.size() || w.fixtureByBody[index] < 0)
                continue;
            const auto &f = w.fixtures[w.fixtureByBody[index]];
            if (f.sprayUntil <= w.time)
                continue;
            Vec3 base{float(f.home.x), float(w.terrain.height(f.home) + .10), float(f.home.z)};
            if ((base - eye_).length() > 80 || !frustum.sphere(base + Vec3{0, 1.5f, 0}, 3.4f))
                continue;
            float pressure = std::min(1.f, float(f.sprayUntil - w.time) * .5f);
            // The coherent column breaks into separate streaks near its apex.
            Vec3 color = Vec3{.66f, .82f, .88f} * (.48f + .52f * day);
            for (int stream = 0; stream < 5; ++stream)
            {
                Vec3 last = base;
                float angle = float(stream) * 2.40f + float(std::fmod(w.time, 40.)) * .16f;
                for (int segment = 1; segment <= 9; ++segment)
                {
                    float t = float(segment) / 9;
                    float spread = (.13f + .05f * stream) * t * t;
                    float wave = .023f * t * std::sin(float(w.time * 8) + stream + t * 10);
                    Vec3 tip = base + Vec3{std::cos(angle) * spread + wave,
                                           pressure * t * (3.65f - 1.70f * t) * (.83f + stream * .045f),
                                           std::sin(angle) * spread - wave};
                    Vec3 lastEdge = right_ * (.036f + .036f * (t - 1.f / 9)), edge = right_ * (.036f + .036f * t);
                    append({last - lastEdge, last + lastEdge, tip + edge, tip - edge}, color,
                           pressure * (.46f - .40f * t), 3.f);
                    last = tip;
                }
            }
            for (unsigned i = 0; i < 108; ++i)
            {
                uint32_t key = mixBits(uint32_t(index) ^ mixBits(i + 1));
                float spread = fraction(key), angle = float(2 * pi) * fraction(key >> 16);
                float age = float(std::fmod(w.time * (.72 + .17 * spread) + fraction(mixBits(key)), 1.));
                float drift = age * (.35f + spread * 1.55f);
                Vec3 at = base + Vec3{drift * std::cos(angle), pressure * age * (1 - age) * (8 + 4 * spread),
                                      drift * std::sin(angle)};
                float width = .015f + .029f * age;
                billboard(at, right_ * width, up_ * (width + .055f * std::abs(1 - 2 * age)), color,
                          (.72f - .35f * age) * pressure, 1.f);
            }
        }
    }

    void EffectBatch::draw(GLuint shader, const World &w, const View &view, const Mat4 &vp, bool particles)
    {
        quads_.clear();
        vertices_.clear();
        eye_ = view.eye;
        forward_ = (view.target - view.eye).unit();
        right_ = cross(forward_, std::abs(forward_.y) > .999f ? Vec3{0, 0, 1} : Vec3{0, 1, 0}).unit();
        up_ = cross(right_, forward_);
        Frustum frustum(vp);
        float day = float(w.clock.daylight());

        // The shader mirrors the city and the sky in a puddle; this is the colour of its bed.
        // The cached corners follow the road grade.
        for (const auto &puddle : w.puddles)
        {
            Vec3 centre = (puddle.corners[0] + puddle.corners[2]) * .5f;
            Vec3 delta = eye_ - centre;
            if (dot(delta, delta) > 100 * 100 || !frustum.sphere(centre, 2.f))
                continue;
            append(puddle.corners, Vec3{.07f, .10f, .105f} * (.35f + .65f * day), .78f, 4.f);
        }

        for (const auto &wave : w.ripples)
        {
            float age = float(w.time - wave.born);
            float radius = float(std::sqrt(wave.strength)) * (.18f + 1.25f * age);
            Vec3 p{float(wave.centre.x), float(CityLayout::waterLevel + .023), float(wave.centre.z)};
            if (!frustum.sphere(p, radius))
                continue;
            float opacity = float(std::clamp(1. - age / WaterRipple::lifetime, 0., 1.));
            billboard(p, {radius, 0, 0}, {0, 0, -radius}, Vec3{.58f, .81f, .84f} * (.25f + .75f * day),
                      opacity * .78f, 5.f);
        }

        marks(w, frustum, day);

        if (particles)
        {
            rain(w, frustum, day);
            hydrants(w, frustum, day);
            for (const auto &p : w.particles)
            {
                auto delta = p.p - eye_;
                if (dot(delta, delta) > 150 * 150 || !frustum.sphere(p.p, p.size * 2.f))
                    continue;
                if (p.kind == ParticleKind::plain)
                {
                    float fade = std::min(1.f, p.life * 9.f);
                    billboard(p.p, right_ * p.size, up_ * p.size, p.color, fade * .94f, 1.f);
                    continue;
                }
                // The colours and the fade of the managed game (CityEffects.Draw).
                using enum ParticleKind;
                const float age = p.span - p.life;
                const Vec3 colour = p.kind == flame
                                        ? (age < .12f ? Vec3{2.2f, 1.5f, .35f} : Vec3{1.8f, .42f, .035f})
                                    : p.kind == sand   ? Vec3{.63f, .53f, .34f}
                                    : p.kind == spark  ? Vec3{1.7f, .68f, .14f}
                                    : p.kind == smoke  ? Vec3{.24f, .25f, .24f}
                                    : p.kind == debris ? Vec3{.32f, .29f, .24f}
                                    : p.kind == fire   ? Vec3{3.f, .62f, .06f}
                                                       : Vec3{.50f, .72f, .85f};
                const float fade =
                    std::clamp(p.life / std::max(p.span, 1e-4f) * 3.f, 0.f, 1.f) * (p.kind == smoke   ? .38f
                                                                                    : p.kind == flame ? .46f
                                                                                                      : .85f);
                // Smoke, scrap and dust are lit by the day; what glows is not.
                const bool lit = p.kind == smoke || p.kind == debris || p.kind == sand || p.kind == water;
                billboard(p.p, right_ * p.size, up_ * p.size, lit ? colour * (.35f + .65f * day) : colour, fade,
                          1.f);
            }

            scuffs(w, frustum, day);

            // Render exhaust and wet-tyre spray locally. Their visual phase never changes the simulation RNG.
            for (const auto &car : w.cars)
            {
                if (car.destroyed || car.falling || car.kind == 9 || car.kind == 10 || car.kind == 12 ||
                    !car.grounded())
                    continue;
                Vec3 p{float(car.body.p.x), float(car.body.height - .48), float(car.body.p.z)};
                auto delta = p - eye_;
                if (dot(delta, delta) > 55 * 55 || !frustum.sphere(p, 5.f))
                    continue;
                auto dir = forward(car.heading());
                Vec3 f{float(dir.x), 0, float(dir.z)}, side{f.z, 0, -f.x};
                float speed = float(car.body.v.length());
                Vec3 rear = p - f * float(car.body.halfH);
                bool spray =
                    w.wetness > .2 && speed > 3 && w.precipitationSurface(car.body.p) < car.body.height + .25;
                for (int i = 0; i < 5; ++i)
                {
                    double cycles = w.time * 1.35 + car.id * .373 + i * .2;
                    float age = float(cycles - std::floor(cycles));
                    float radius = .07f + age * .27f;
                    Vec3 at = rear - f * (age * (.65f + .17f * speed)) + Vec3{age * .14f, age * .65f, 0};
                    billboard(at + side * float(.6 * car.body.halfW), right_ * radius, up_ * radius,
                              Vec3{.58f, .59f, .58f} * (.45f + .55f * day), .19f * (1 - age), 1.f);
                    if (spray)
                        for (int s : {-1, 1})
                            billboard(at + side * float(s * car.body.halfW) - Vec3{0, .24f, 0},
                                      right_ * (radius * 1.4f), up_ * radius,
                                      Vec3{.60f, .70f, .76f} * (.45f + .55f * day),
                                      float(w.wetness) * .22f * (1 - age), 1.f);
                }
            }
        }

        if (quads_.empty())
            return;

        std::ranges::sort(quads_, [](const Quad &a, const Quad &b) { return a.depth > b.depth; });
        constexpr float uv[][2] = {{-1, -1}, {1, -1}, {1, 1}, {-1, 1}};
        vertices_.reserve(quads_.size() * 6);

        for (const Quad &q : quads_)
            for (int i : {0, 1, 2, 0, 2, 3})
                vertices_.push_back({
                    .p = q.corners[i], 
                    .color = q.color, 
                    .alpha = q.alpha, 
                    .u = uv[i][0], 
                    .v = uv[i][1], 
                    .shape = q.shape
                });

        UseProgram(shader);
        UniformMatrix4fv(matrix_, 1, GL_FALSE, vp.m.data());
        BindVertexArray(vao_);
        BindBuffer(ARRAY_BUFFER, vbo_);
        BufferData(ARRAY_BUFFER, static_cast<std::ptrdiff_t>(vertices_.size() * sizeof(Vertex)), vertices_.data(), DYNAMIC_DRAW);
        glEnable(GL_BLEND);

        // The shader hands over colour already multiplied by its opacity (see effects.frag).
        glBlendFunc(GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
        glDepthMask(GL_FALSE);
        glDrawArrays(GL_TRIANGLES, 0, GLsizei(vertices_.size()));
        glDepthMask(GL_TRUE);
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        glDisable(GL_BLEND);
        BindVertexArray(0);
    }
}
