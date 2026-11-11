#include "post_processing.hpp"
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <sstream>

namespace pz
{
using namespace gl;
void PostProcessing::initialize(GLuint glowProgram, GLuint resolveProgram)
{
    glow_ = glowProgram;
    resolve_ = resolveProgram;
    GenVertexArrays(1, &vao_);
    UseProgram(glow_);
    Uniform1i(GetUniformLocation(glow_, "source"), 0);
    pass_ = GetUniformLocation(glow_, "pass");
    threshold_ = GetUniformLocation(glow_, "threshold");
    UseProgram(resolve_);
    Uniform1i(GetUniformLocation(resolve_, "sceneImage"), 0);
    Uniform1i(GetUniformLocation(resolve_, "glowImage"), 1);
    conditions_ = GetUniformLocation(resolve_, "conditions");
    UseProgram(0);
}
void PostProcessing::resize(int w, int h)
{
    if (w == w_ && h == h_)
        return;
    if (!depth_)
    {
        GenFramebuffers(3, fb_.data());
        glGenTextures(3, color_.data());
        glGenTextures(1, &depth_);
    }
    w_ = w;
    h_ = h;
    glowW_ = (w + 3) / 4;
    glowH_ = (h + 3) / 4;
    ActiveTexture(TEXTURE0);
    auto allocate = [](GLuint tex, int width, int height, bool depth)
    {
        glBindTexture(GL_TEXTURE_2D, tex);
        glTexImage2D(GL_TEXTURE_2D, 0, depth ? DEPTH_COMPONENT24 : GL_RGBA8, width, height, 0,
                     depth ? GL_DEPTH_COMPONENT : GL_RGBA, depth ? GL_UNSIGNED_INT : GL_UNSIGNED_BYTE,
                     nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, depth ? GL_NEAREST : GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, depth ? GL_NEAREST : GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
    };
    allocate(depth_, w, h, true);
    for (unsigned i = 0; i < fb_.size(); ++i)
    {
        allocate(color_[i], i ? glowW_ : w, i ? glowH_ : h, false);
        BindFramebuffer(FRAMEBUFFER, fb_[i]);
        FramebufferTexture2D(FRAMEBUFFER, COLOR_ATTACHMENT0, GL_TEXTURE_2D, color_[i], 0);
        if (!i)
            FramebufferTexture2D(FRAMEBUFFER, DEPTH_ATTACHMENT, GL_TEXTURE_2D, depth_, 0);
        if (CheckFramebufferStatus(FRAMEBUFFER) != FRAMEBUFFER_COMPLETE)
            throw std::runtime_error("Post-processing framebuffer incomplete");
    }
    glBindTexture(GL_TEXTURE_2D, 0);
    BindFramebuffer(FRAMEBUFFER, 0);
}
void PostProcessing::present(int width, int height, float day, float rain, float sunHeight)
{
    glDisable(GL_DEPTH_TEST);
    glDisable(GL_BLEND);
    glDisable(GL_CULL_FACE);
    BindVertexArray(vao_);
    ActiveTexture(TEXTURE0);
    UseProgram(glow_);
    Uniform1f(threshold_, .55f + .24f * day);
    glViewport(0, 0, glowW_, glowH_);
    for (int i = 0; i < 3; ++i)
    {
        BindFramebuffer(FRAMEBUFFER, fb_[i == 1 ? 2 : 1]);
        glBindTexture(GL_TEXTURE_2D, color_[i]);
        Uniform1i(pass_, i);
        glDrawArrays(GL_TRIANGLES, 0, 3);
    }
    BindFramebuffer(FRAMEBUFFER, 0);
    glViewport(0, 0, width, height);
    UseProgram(resolve_);
    float lowSun = (sunHeight - .15f) * 4.f;
    Uniform3f(conditions_, day, rain, std::exp(-lowSun * lowSun) * day);
    glBindTexture(GL_TEXTURE_2D, color_[0]);
    ActiveTexture(TEXTURE0 + 1);
    glBindTexture(GL_TEXTURE_2D, color_[1]);
    glDrawArrays(GL_TRIANGLES, 0, 3);
    glBindTexture(GL_TEXTURE_2D, 0);
    ActiveTexture(TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, 0);
    BindVertexArray(0);
}
void PostProcessing::releaseTargets()
{
    if (depth_)
    {
        DeleteFramebuffers(3, fb_.data());
        glDeleteTextures(3, color_.data());
        glDeleteTextures(1, &depth_);
        depth_ = 0;
        w_ = h_ = 0;
    }
}
PostProcessing::~PostProcessing()
{
    releaseTargets();
    if (vao_)
        DeleteVertexArrays(1, &vao_);
    if (glow_)
        DeleteProgram(glow_);
    if (resolve_)
        DeleteProgram(resolve_);
}
void SceneTimer::enable()
{
    if (!ids_[0])
        GenQueries(GLsizei(ids_.size()), ids_.data());
}
void SceneTimer::begin()
{
    if (!ids_[0] || ++frame_ <= 60)
        return;
    if (pending_[cursor_])
    {
        GLint ready = 0;
        GetQueryObjectiv(ids_[cursor_], QUERY_RESULT_AVAILABLE, &ready);
        if (!ready)
            return;
        std::uint64_t ns = 0;
        GetQueryObjectui64v(ids_[cursor_], QUERY_RESULT, &ns);
        milliseconds_.push_back(double(ns) * 1e-6);
        pending_[cursor_] = false;
    }
    BeginQuery(TIME_ELAPSED, ids_[cursor_]);
    active_ = true;
}
void SceneTimer::end()
{
    if (!active_)
        return;
    EndQuery(TIME_ELAPSED);
    pending_[cursor_] = true;
    cursor_ = (cursor_ + 1) % unsigned(ids_.size());
    active_ = false;
}
std::string SceneTimer::report() const
{
    auto samples = milliseconds_;
    if (samples.empty())
        return "scene GPU: no samples";
    std::sort(samples.begin(), samples.end());
    auto percentile = [&](double p) { return samples[size_t(std::ceil(samples.size() * p)) - 1]; };
    std::ostringstream out;
    out << std::fixed << std::setprecision(2) << "scene GPU: samples=" << samples.size()
        << " median_ms=" << percentile(.5) << " p95_ms=" << percentile(.95);
    return out.str();
}
SceneTimer::~SceneTimer()
{
    if (ids_[0])
        DeleteQueries(GLsizei(ids_.size()), ids_.data());
}
} // namespace pz
