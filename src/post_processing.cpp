#include "post_processing.hpp"
#include <algorithm>
#include <cmath>
#include <format>
#include <iterator>

namespace Promzona
{
using namespace gl;
void PostProcessing::initialize(GLuint glowProgram, GLuint resolveProgram, GLuint occlusionProgram)
{
    glow_ = glowProgram;
    resolve_ = resolveProgram;
    occlusion_ = occlusionProgram;
    GenVertexArrays(1, &vao_);
    UseProgram(glow_);
    Uniform1i(GetUniformLocation(glow_, "source"), 0);
    pass_ = GetUniformLocation(glow_, "pass");
    threshold_ = GetUniformLocation(glow_, "threshold");
    UseProgram(occlusion_);
    Uniform1i(GetUniformLocation(occlusion_, "depthImage"), 0);
    lens_ = GetUniformLocation(occlusion_, "lens");
    samples_ = GetUniformLocation(occlusion_, "samples");
    UseProgram(resolve_);
    Uniform1i(GetUniformLocation(resolve_, "sceneImage"), 0);
    Uniform1i(GetUniformLocation(resolve_, "glowImage"), 1);
    Uniform1i(GetUniformLocation(resolve_, "depthImage"), 2);
    Uniform1i(GetUniformLocation(resolve_, "occlusionImage"), 3);
    bloom_ = GetUniformLocation(resolve_, "enableBloom");
    shaded_ = GetUniformLocation(resolve_, "enableOcclusion");
    clip_ = GetUniformLocation(resolve_, "clip");
    conditions_ = GetUniformLocation(resolve_, "conditions");
    UseProgram(0);
}
void PostProcessing::resize(int w, int h)
{
    if (w == w_ && h == h_)
        return;
    if (!depth_)
    {
        GenFramebuffers(GLsizei(fb_.size()), fb_.data());
        glGenTextures(GLsizei(color_.size()), color_.data());
        glGenTextures(1, &depth_);
        GenFramebuffers(1, &effectsFb_);
    }
    w_ = w;
    h_ = h;
    glowW_ = (w + 3) / 4;
    glowH_ = (h + 3) / 4;
    ActiveTexture(TEXTURE0);
    enum class Kind
    {
        colour,
        depth,
        shade
    };
    auto allocate = [](GLuint tex, int width, int height, Kind kind)
    {
        // Depth and the shading made from it are read texel by texel.
        const bool exact = kind != Kind::colour;
        glBindTexture(GL_TEXTURE_2D, tex);
        glTexImage2D(GL_TEXTURE_2D, 0,
                     kind == Kind::depth   ? DEPTH_COMPONENT24
                     : kind == Kind::shade ? R8
                                           : GL_RGBA8,
                     width, height, 0,
                     kind == Kind::depth   ? GL_DEPTH_COMPONENT
                     : kind == Kind::shade ? GL_RED
                                           : GL_RGBA,
                     kind == Kind::depth ? GL_UNSIGNED_INT : GL_UNSIGNED_BYTE, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, exact ? GL_NEAREST : GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, exact ? GL_NEAREST : GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, CLAMP_TO_EDGE);
    };
    allocate(depth_, w, h, Kind::depth);
    for (unsigned i = 0; i < fb_.size(); ++i)
    {
        if (i == 3)
            allocate(color_[i], (w + 1) / 2, (h + 1) / 2, Kind::shade);
        else
            allocate(color_[i], i ? glowW_ : w, i ? glowH_ : h, Kind::colour);
        BindFramebuffer(FRAMEBUFFER, fb_[i]);
        FramebufferTexture2D(FRAMEBUFFER, COLOR_ATTACHMENT0, GL_TEXTURE_2D, color_[i], 0);
        if (!i)
            FramebufferTexture2D(FRAMEBUFFER, DEPTH_ATTACHMENT, GL_TEXTURE_2D, depth_, 0);
        if (CheckFramebufferStatus(FRAMEBUFFER) != FRAMEBUFFER_COMPLETE)
            throw std::runtime_error("Post-processing framebuffer incomplete");
    }
    // Same colour, no depth attachment: translucent fragments can read the opaque depth safely.
    BindFramebuffer(FRAMEBUFFER, effectsFb_);
    FramebufferTexture2D(FRAMEBUFFER, COLOR_ATTACHMENT0, GL_TEXTURE_2D, color_[0], 0);
    if (CheckFramebufferStatus(FRAMEBUFFER) != FRAMEBUFFER_COMPLETE)
        throw std::runtime_error("Particle framebuffer incomplete");
    glBindTexture(GL_TEXTURE_2D, 0);
    BindFramebuffer(FRAMEBUFFER, 0);
}
void PostProcessing::beginEffects(unsigned depthUnit) const
{
    BindFramebuffer(FRAMEBUFFER, effectsFb_);
    ActiveTexture(TEXTURE0 + depthUnit);
    glBindTexture(GL_TEXTURE_2D, depth_);
    ActiveTexture(TEXTURE0);
}
void PostProcessing::present(int width, int height, float day, float rain, float sunHeight, bool bloom,
                             const ContactShading &contact)
{
    glDisable(GL_DEPTH_TEST);
    glDisable(GL_BLEND);
    glDisable(GL_CULL_FACE);
    BindVertexArray(vao_);
    ActiveTexture(TEXTURE0);
    const bool shaded = contact.samples > 0;
    if (shaded)
    {
        UseProgram(occlusion_);
        Uniform4f(lens_, contact.scaleX, contact.scaleY, contact.nearPlane, contact.farPlane);
        Uniform1i(samples_, contact.samples);
        BindFramebuffer(FRAMEBUFFER, fb_[3]);
        glViewport(0, 0, (w_ + 1) / 2, (h_ + 1) / 2);
        glBindTexture(GL_TEXTURE_2D, depth_);
        glDrawArrays(GL_TRIANGLES, 0, 3);
    }
    if (bloom)
    {
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
    }
    BindFramebuffer(FRAMEBUFFER, 0);
    glViewport(0, 0, width, height);
    UseProgram(resolve_);
    Uniform1i(bloom_, bloom);
    Uniform1i(shaded_, shaded);
    Uniform2f(clip_, contact.nearPlane, contact.farPlane);
    float lowSun = (sunHeight - .15f) * 4.f;
    Uniform3f(conditions_, day, rain, std::exp(-lowSun * lowSun) * day);
    const GLuint images[] = {color_[0], color_[1], depth_, color_[3]};
    for (unsigned unit = std::size(images); unit-- > 0;)
    {
        ActiveTexture(TEXTURE0 + unit);
        glBindTexture(GL_TEXTURE_2D, images[unit]);
    }
    glDrawArrays(GL_TRIANGLES, 0, 3);
    for (unsigned unit = std::size(images); unit-- > 0;)
    {
        ActiveTexture(TEXTURE0 + unit);
        glBindTexture(GL_TEXTURE_2D, 0);
    }
    BindVertexArray(0);
}
void PostProcessing::releaseTargets()
{
    if (depth_)
    {
        DeleteFramebuffers(GLsizei(fb_.size()), fb_.data());
        glDeleteTextures(GLsizei(color_.size()), color_.data());
        glDeleteTextures(1, &depth_);
        DeleteFramebuffers(1, &effectsFb_);
        effectsFb_ = 0;
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
    if (occlusion_)
        DeleteProgram(occlusion_);
}
void SceneTimer::enable()
{
    if (!ids_.front().front())
        for (auto &frame : ids_)
            GenQueries(GLsizei(frame.size()), frame.data());
}
void SceneTimer::begin()
{
    if (!ids_.front().front() || ++frame_ <= 60)
        return;
    if (pending_[cursor_])
    {
        GLint ready = 0;
        GetQueryObjectiv(ids_[cursor_][finish], QUERY_RESULT_AVAILABLE, &ready);
        if (!ready)
            return;
        std::array<std::uint64_t, finish + 1> stamps{};
        for (size_t i = 0; i < stamps.size(); ++i)
            GetQueryObjectui64v(ids_[cursor_][i], QUERY_RESULT, &stamps[i]);
        for (int i = 0; i < finish; ++i)
            milliseconds_[i].push_back(double(stamps[i + 1] - stamps[i]) * 1e-6);
        milliseconds_[finish].push_back(double(stamps.back() - stamps.front()) * 1e-6);
        pending_[cursor_] = false;
    }
    active_ = true;
    mark(shadows);
}
void SceneTimer::mark(Stage stage)
{
    if (active_)
        QueryCounter(ids_[cursor_][stage], TIMESTAMP);
}
void SceneTimer::end()
{
    if (!active_)
        return;
    mark(finish);
    pending_[cursor_] = true;
    cursor_ = (cursor_ + 1) % unsigned(ids_.size());
    active_ = false;
}
std::string SceneTimer::report() const
{
    if (milliseconds_.back().empty())
        return "scene GPU: no samples";
    constexpr std::array names{"shadows", "reflection", "opaque", "particles", "post", "scene"};
    std::string result;
    for (size_t i = 0; i < names.size(); ++i)
    {
        auto samples = milliseconds_[i];
        std::ranges::sort(samples);
        auto percentile = [&](double p) { return samples[size_t(std::ceil(samples.size() * p)) - 1]; };
        result += std::format("{}{} GPU: samples={} median_ms={:.2f} p95_ms={:.2f}", i ? "\n" : "", names[i],
                              samples.size(), percentile(.5), percentile(.95));
    }
    return result;
}
SceneTimer::~SceneTimer()
{
    if (ids_.front().front())
        for (const auto &frame : ids_)
            DeleteQueries(GLsizei(frame.size()), frame.data());
}
} // namespace Promzona
