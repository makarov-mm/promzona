#pragma once
#include "promzona/gl.hpp"
#include <array>
#include <vector>
#include <string>

namespace Promzona
{
    // What the contact shading has to know about the camera of the frame: the two scale
    // terms of its projection and its clip planes. No samples, no shading.
    struct ContactShading
    {
        float scaleX{}, scaleY{}, nearPlane{}, farPlane{};
        int samples{};
    };

    class PostProcessing
    {
        GLuint sceneCopy_{}, effectsFb_{}, vao_{}, glow_{}, resolve_{}, occlusion_{}, depth_{};
        // The scene, two quarter-size targets of the glow, and the half-size contact shading.
        std::array<GLuint, 4> fb_{}, color_{};
        GLint pass_{}, threshold_{}, conditions_{}, bloom_{}, lens_{}, samples_{}, clip_{}, shaded_{};
        int w_{}, h_{}, glowW_{}, glowH_{};

    public:
        void initialize(GLuint glowProgram, GLuint resolveProgram, GLuint occlusionProgram);
        void resize(int width, int height);
        void releaseTargets();
        void beginEffects(unsigned depthUnit, bool reflectPuddles);
        GLuint sceneTarget() const { return fb_[0]; }
        void present(int width, int height, float day, float rain, float sunHeight, bool bloom, const ContactShading &contact);
        ~PostProcessing();
    };

    // Opt-in elapsed queries. Never wait for an unavailable GPU result on the frame path.
    class SceneTimer
    {
      public:
        enum Stage
        {
            shadows,
            reflection,
            opaque,
            particles,
            post,
            finish
        };

      private:
        std::array<std::array<GLuint, finish + 1>, 8> ids_{};
        std::array<bool, 8> pending_{};
        struct FrameSample
        {
            unsigned frame;
            bool refreshed;
            double elapsed;
        };
        std::array<unsigned, 8> frameIds_{};
        std::array<bool, 8> refreshed_{};
        std::vector<FrameSample> samples_;
        unsigned cursor_{}, frame_{};
        bool active_{};
        std::array<std::vector<double>, finish + 1> milliseconds_;

      public:
        void enable();
        void begin();
        void mark(Stage stage);
        void farRefresh(bool refreshed) { if (active_) refreshed_[cursor_] = refreshed; }
        void end();
        [[nodiscard]] std::string report() const;
        ~SceneTimer();
    };
}
