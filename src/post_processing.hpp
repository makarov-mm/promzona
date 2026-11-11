#pragma once
#include "promzona/gl.hpp"
#include <array>
#include <vector>
#include <string>

namespace pz
{
class PostProcessing
{
    GLuint vao_{}, glow_{}, resolve_{}, depth_{};
    std::array<GLuint, 3> fb_{}, color_{};
    GLint pass_{}, threshold_{}, conditions_{}, bloom_{};
    int w_{}, h_{}, glowW_{}, glowH_{};

  public:
    void initialize(GLuint glowProgram, GLuint resolveProgram);
    void resize(int width, int height);
    void releaseTargets();
    GLuint sceneTarget() const { return fb_[0]; }
    void present(int width, int height, float day, float rain, float sunHeight, bool bloom);
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
    std::string report() const;
    ~SceneTimer();
};
} // namespace pz
