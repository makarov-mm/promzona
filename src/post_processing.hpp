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
        GLuint vao_{}, glow_{}, resolve_{}, occlusion_{}, depth_{};
        // The scene, two quarter-size targets of the glow, and the half-size contact shading.
        std::array<GLuint, 4> fb_{}, color_{};
        GLint pass_{}, threshold_{}, conditions_{}, bloom_{}, lens_{}, samples_{}, clip_{}, shaded_{};
        int w_{}, h_{}, glowW_{}, glowH_{};

    public:
        void initialize(GLuint glowProgram, GLuint resolveProgram, GLuint occlusionProgram);
        void resize(int width, int height);
        void releaseTargets();
        GLuint sceneTarget() const { return fb_[0]; }
        void present(int width, int height, float day, float rain, float sunHeight, bool bloom, const ContactShading &contact);
        ~PostProcessing();
    };

    // Opt-in elapsed queries. Never wait for an unavailable GPU result on the frame path.
    class SceneTimer
    {
        std::array<GLuint, 8> ids_{};
        std::array<bool, 8> pending_{};
        unsigned cursor_{}, frame_{};
        bool active_{};
        std::vector<double> milliseconds_;

    public:
        void enable();
        void begin();
        void end();
        [[nodiscard]] std::string report() const;
        ~SceneTimer();
    };
}
