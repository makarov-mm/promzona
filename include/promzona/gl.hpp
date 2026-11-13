#pragma once
#include <windows.h>
#include <GL/gl.h>
#include <cstddef>
#include <cstdint>
#include <stdexcept>
namespace pz::gl
{
// clang-format off
#define PZ_GL_FUNCTIONS(X) \
    X(CreateShader, GLuint, GLenum) \
    X(ShaderSource, void, GLuint, GLsizei, const char *const *, const GLint *) \
    X(CompileShader, void, GLuint) \
    X(GetShaderiv, void, GLuint, GLenum, GLint *) \
    X(GetShaderInfoLog, void, GLuint, GLsizei, GLsizei *, char *) \
    X(DeleteShader, void, GLuint) \
    X(CreateProgram, GLuint, void) \
    X(AttachShader, void, GLuint, GLuint) \
    X(LinkProgram, void, GLuint) \
    X(GetProgramiv, void, GLuint, GLenum, GLint *) \
    X(GetProgramInfoLog, void, GLuint, GLsizei, GLsizei *, char *) \
    X(UseProgram, void, GLuint) \
    X(DeleteProgram, void, GLuint) \
    X(GetUniformLocation, GLint, GLuint, const char *) \
    X(UniformMatrix4fv, void, GLint, GLsizei, GLboolean, const GLfloat *) \
    X(Uniform1i, void, GLint, GLint) \
    X(Uniform1f, void, GLint, GLfloat) \
    X(Uniform2f, void, GLint, GLfloat, GLfloat) \
    X(Uniform3f, void, GLint, GLfloat, GLfloat, GLfloat) \
    X(Uniform3fv, void, GLint, GLsizei, const GLfloat *) \
    X(Uniform4f, void, GLint, GLfloat, GLfloat, GLfloat, GLfloat) \
    X(GenBuffers, void, GLsizei, GLuint *) \
    X(BindBuffer, void, GLenum, GLuint) \
    X(BufferData, void, GLenum, std::ptrdiff_t, const void *, GLenum) \
    X(DeleteBuffers, void, GLsizei, const GLuint *) \
    X(GenVertexArrays, void, GLsizei, GLuint *) \
    X(BindVertexArray, void, GLuint) \
    X(DeleteVertexArrays, void, GLsizei, const GLuint *) \
    X(VertexAttribPointer, void, GLuint, GLint, GLenum, GLboolean, GLsizei, const void *) \
    X(EnableVertexAttribArray, void, GLuint) \
    X(VertexAttribDivisor, void, GLuint, GLuint) \
    X(DrawElementsInstanced, void, GLenum, GLsizei, GLenum, const void *, GLsizei) \
    X(ActiveTexture, void, GLenum) \
    X(GenFramebuffers, void, GLsizei, GLuint *) \
    X(BindFramebuffer, void, GLenum, GLuint) \
    X(FramebufferTexture2D, void, GLenum, GLenum, GLenum, GLuint, GLint) \
    X(CheckFramebufferStatus, GLenum, GLenum) \
    X(DeleteFramebuffers, void, GLsizei, const GLuint *) \
    X(GenQueries, void, GLsizei, GLuint *) \
    X(DeleteQueries, void, GLsizei, const GLuint *) \
    X(BeginQuery, void, GLenum, GLuint) \
    X(EndQuery, void, GLenum) \
    X(GetQueryObjectiv, void, GLuint, GLenum, GLint *) \
    X(GetQueryObjectui64v, void, GLuint, GLenum, std::uint64_t *) \
    X(GenerateMipmap, void, GLenum)
#define PZ_GL_DECLARE(name, ret, ...) inline ret(APIENTRY *name)(__VA_ARGS__) = nullptr;
PZ_GL_FUNCTIONS(PZ_GL_DECLARE)
#undef PZ_GL_DECLARE
// clang-format on
inline void load()
{
#define PZ_GL_LOAD(name, ret, ...)                                                                           \
    name = reinterpret_cast<decltype(name)>(wglGetProcAddress("gl" #name));                                  \
    if (!name || reinterpret_cast<std::uintptr_t>(name) <= 3 || reinterpret_cast<std::intptr_t>(name) == -1) \
        throw std::runtime_error("OpenGL function unavailable: " #name);
    PZ_GL_FUNCTIONS(PZ_GL_LOAD)
#undef PZ_GL_LOAD
}
constexpr GLenum ARRAY_BUFFER = 0x8892, ELEMENT_ARRAY_BUFFER = 0x8893, STATIC_DRAW = 0x88e4,
                 DYNAMIC_DRAW = 0x88e8, VERTEX_SHADER = 0x8b31, FRAGMENT_SHADER = 0x8b30,
                 COMPILE_STATUS = 0x8b81, LINK_STATUS = 0x8b82, TEXTURE0 = 0x84c0, RGBA32F = 0x8814,
                 CLAMP_TO_EDGE = 0x812f, FRAMEBUFFER = 0x8d40, DEPTH_ATTACHMENT = 0x8d00,
                 FRAMEBUFFER_COMPLETE = 0x8cd5, DEPTH_COMPONENT24 = 0x81a6, COLOR_ATTACHMENT0 = 0x8ce0,
                 TIME_ELAPSED = 0x88bf, QUERY_RESULT = 0x8866, QUERY_RESULT_AVAILABLE = 0x8867, R8 = 0x8229,
                 RGBA16F = 0x881a, TEXTURE_CUBE_MAP = 0x8513, TEXTURE_CUBE_MAP_POSITIVE_X = 0x8515,
                 TEXTURE_WRAP_R = 0x8072, TEXTURE_CUBE_MAP_SEAMLESS = 0x884f;
} // namespace pz::gl
