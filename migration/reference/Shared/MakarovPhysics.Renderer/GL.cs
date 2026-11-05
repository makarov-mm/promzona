using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.Renderer;

/// <summary>A low-level wrapper for the OpenGL functions used by the renderer on Windows.</summary>
/// <remarks>
/// Requires C# 10 or later, unsafe code enabled, and the Mat4 and Shader types from the project.
/// Use on a single rendering thread with a current OpenGL context; call Init first.
/// Call Init again after creating or switching contexts. OpenGL state belongs to the context.
/// Pointers and offsets passed to low-level methods must reference valid memory regions or buffers.
/// </remarks>
public static unsafe class GL
{
    /// <summary>Bit mask for clearing or copying the color buffer.</summary>
    public const uint COLOR_BUFFER_BIT = 0x00004000;
    /// <summary>Bit mask for clearing or copying the depth buffer.</summary>
    public const uint DEPTH_BUFFER_BIT = 0x00000100;
    /// <summary>Fragment depth testing; enabled through Enable.</summary>
    public const uint DEPTH_TEST = 0x0B71;
    /// <summary>Culling of the selected polygon faces.</summary>
    public const uint CULL_FACE = 0x0B44;
    /// <summary>Multisampling; requires a buffer with multiple samples per pixel.</summary>
    public const uint MULTISAMPLE = 0x809D;
    /// <summary>Framebuffer binding target for both reading and drawing.</summary>
    public const uint FRAMEBUFFER = 0x8D40;
    /// <summary>The first color attachment point of a framebuffer.</summary>
    public const uint COLOR_ATTACHMENT0 = 0x8CE0;
    /// <summary>Status indicating that a framebuffer is complete and ready for rendering.</summary>
    public const uint FRAMEBUFFER_COMPLETE = 0x8CD5;
    /// <summary>Framebuffer attachment point for a depth texture or renderbuffer.</summary>
    public const uint DEPTH_ATTACHMENT = 0x8D00;
    /// <summary>Target for operations on a renderbuffer, which stores an image for a framebuffer.</summary>
    public const uint RENDERBUFFER = 0x8D41;
    /// <summary>Binding target for the framebuffer used for reading and copying.</summary>
    public const uint READ_FRAMEBUFFER = 0x8CA8;
    /// <summary>Binding target for the framebuffer used for drawing.</summary>
    public const uint DRAW_FRAMEBUFFER = 0x8CA9;
    /// <summary>Parameter name for querying the maximum multisample count; not the count itself.</summary>
    public const uint MAX_SAMPLES = 0x8D57;
    /// <summary>External pixel format containing only a depth component.</summary>
    public const uint DEPTH_COMPONENT = 0x1902;
    /// <summary>Internal depth format with 24 bits per value.</summary>
    public const uint DEPTH_COMPONENT24 = 0x81A6;
    /// <summary>Nearest-texel filtering; also used when copying between framebuffers.</summary>
    public const uint NEAREST = 0x2600;
    /// <summary>No buffer selected, for example no color buffer in a depth-only framebuffer.</summary>
    public const uint NONE = 0;
    /// <summary>Blending of the fragment output color with the color in the destination buffer.</summary>
    public const uint BLEND = 0x0BE2;
    /// <summary>Depth comparison that passes when the incoming depth is less than the stored depth.</summary>
    public const uint LESS = 0x0201;
    /// <summary>Front polygon faces or the front color buffer, depending on the operation.</summary>
    public const uint FRONT = 0x0404;
    /// <summary>Back polygon faces or the back color buffer, depending on the operation.</summary>
    public const uint BACK = 0x0405;
    /// <summary>Counterclockwise vertex winding defines the front face of a polygon.</summary>
    public const uint CCW = 0x0901;
    /// <summary>Blend factor equal to one.</summary>
    public const uint ONE = 1;
    /// <summary>Blend factor equal to zero.</summary>
    public const uint ZERO = 0;
    /// <summary>Blend factor equal to the corresponding components of the source color.</summary>
    public const uint SRC_COLOR = 0x0300;
    /// <summary>Internal RGBA format with four 16-bit floating-point components, suitable for HDR.</summary>
    public const uint RGBA16F = 0x881A;
    /// <summary>Internal 8-bit RGBA format whose colour channels are sRGB-encoded: the sampler decodes them to linear light, so a photograph is read once, not gamma-corrected twice.</summary>
    public const uint SRGB8_ALPHA8 = 0x8C43;
    /// <summary>Capability flag for glPolygonOffset on filled polygons.</summary>
    public const uint POLYGON_OFFSET_FILL = 0x8037;
    /// <summary>Texture parameter for the anisotropic filtering degree (EXT_texture_filter_anisotropic, core since 4.6).</summary>
    public const uint TEXTURE_MAX_ANISOTROPY = 0x84FE;
    /// <summary>Query name for the largest anisotropy degree the driver supports; zero when the extension is absent.</summary>
    public const uint MAX_TEXTURE_MAX_ANISOTROPY = 0x84FF;
    /// <summary>Blend factor equal to the alpha component of the source color.</summary>
    public const uint SRC_ALPHA = 0x0302;
    /// <summary>Blend factor equal to one minus the alpha component of the source color.</summary>
    public const uint ONE_MINUS_SRC_ALPHA = 0x0303;

    /// <summary>Point primitives; each vertex is drawn separately.</summary>
    public const uint POINTS = 0x0000;
    /// <summary>Uses the point size written by the shader to gl_PointSize.</summary>
    public const uint PROGRAM_POINT_SIZE = 0x8642;
    /// <summary>Independent triangles; each group of three vertices defines one primitive.</summary>
    public const uint TRIANGLES = 0x0004;
    /// <summary>Independent line segments; each pair of vertices defines one primitive.</summary>
    public const uint LINES = 0x0001;
    /// <summary>A connected series of line segments joining consecutive vertices.</summary>
    public const uint LINE_STRIP = 0x0003;
    /// <summary>A strip of triangles that share vertices.</summary>
    public const uint TRIANGLE_STRIP = 0x0005;

    /// <summary>Component type: a 32-bit floating-point value.</summary>
    public const uint FLOAT = 0x1406;
    /// <summary>External single-channel format containing the red component.</summary>
    public const uint RED = 0x1903;
    /// <summary>Internal single-channel format containing a 32-bit floating-point value.</summary>
    public const uint R32F = 0x822E;
    /// <summary>Internal single-channel format containing an 8-bit unsigned normalized value.</summary>
    public const uint R8 = 0x8229;
    /// <summary>Component or index type: a 32-bit unsigned integer.</summary>
    public const uint UNSIGNED_INT = 0x1405;
    /// <summary>Component or index type: an 8-bit unsigned integer.</summary>
    public const uint UNSIGNED_BYTE = 0x1401;
    /// <summary>The false value of GLboolean.</summary>
    public const byte FALSE = 0;
    /// <summary>The true value of GLboolean.</summary>
    public const byte TRUE = 1;

    /// <summary>Target for operations on a two-dimensional texture.</summary>
    public const uint TEXTURE_2D = 0x0DE1;
    /// <summary>The first texture unit; subsequent units are specified as TEXTURE0 + index.</summary>
    public const uint TEXTURE0 = 0x84C0;
    /// <summary>The second texture unit.</summary>
    public const uint TEXTURE1 = 0x84C1;
    /// <summary>External pixel format with R, G, B, and A components.</summary>
    public const uint RGBA = 0x1908;
    /// <summary>Internal RGBA format with four 8-bit unsigned normalized components.</summary>
    public const uint RGBA8 = 0x8058;
    /// <summary>External pixel format with components ordered as B, G, R, and A.</summary>
    public const uint BGRA = 0x80E1;
    /// <summary>Texture filtering parameter for minification.</summary>
    public const uint TEXTURE_MIN_FILTER = 0x2801;
    /// <summary>Texture filtering parameter for magnification.</summary>
    public const uint TEXTURE_MAG_FILTER = 0x2800;
    /// <summary>Linear filtering of neighboring texels.</summary>
    public const uint LINEAR = 0x2601;
    /// <summary>Trilinear filtering: linear filtering within and between two mip levels.</summary>
    public const uint LINEAR_MIPMAP_LINEAR = 0x2703;
    /// <summary>Parameter controlling how texture coordinate S is handled outside the texture bounds.</summary>
    public const uint TEXTURE_WRAP_S = 0x2802;
    /// <summary>Parameter controlling how texture coordinate T is handled outside the texture bounds.</summary>
    public const uint TEXTURE_WRAP_T = 0x2803;
    /// <summary>Clamps texture coordinates to the texture edge without repeating.</summary>
    public const uint CLAMP_TO_EDGE = 0x812F;
    /// <summary>Repeats the texture periodically outside the coordinate range from 0 to 1.</summary>
    public const uint REPEAT = 0x2901;
    /// <summary>Alignment of uploaded pixel rows in bytes: 1, 2, 4, or 8.</summary>
    public const uint UNPACK_ALIGNMENT = 0x0CF5;

    /// <summary>Binding target for a buffer containing vertex attributes.</summary>
    public const uint ARRAY_BUFFER = 0x8892;
    /// <summary>Index buffer binding target; the binding is stored in the current VAO.</summary>
    public const uint ELEMENT_ARRAY_BUFFER = 0x8893;
    /// <summary>Driver usage hint: data is specified infrequently and used repeatedly for drawing.</summary>
    public const uint STATIC_DRAW = 0x88E4;
    /// <summary>Driver usage hint: data is updated frequently and used repeatedly for drawing.</summary>
    public const uint DYNAMIC_DRAW = 0x88E8;
    /// <summary>Restricts framebuffer modifications to the rectangle specified by Scissor.</summary>
    public const uint SCISSOR_TEST = 0x0C11;
    /// <summary>String parameter identifying the OpenGL implementation vendor.</summary>
    public const uint VENDOR = 0x1F00;
    /// <summary>String parameter identifying the renderer in use.</summary>
    public const uint RENDERER = 0x1F01;
    /// <summary>String parameter identifying the OpenGL version.</summary>
    public const uint VERSION = 0x1F02;

    /// <summary>Vertex shader type.</summary>
    public const uint VERTEX_SHADER = 0x8B31;
    /// <summary>Fragment shader type.</summary>
    public const uint FRAGMENT_SHADER = 0x8B30;
    /// <summary>Shader compilation status parameter; zero indicates failure.</summary>
    public const uint COMPILE_STATUS = 0x8B81;
    /// <summary>Program link status parameter; zero indicates failure.</summary>
    public const uint LINK_STATUS = 0x8B82;
    /// <summary>Required shader or program log buffer size, including the terminating null byte.</summary>
    public const uint INFO_LOG_LENGTH = 0x8B84;

    /// <summary>Both polygon faces; used when selecting the rasterization mode.</summary>
    public const uint FRONT_AND_BACK = 0x0408;
    /// <summary>Rasterizes polygons with filled interiors.</summary>
    public const uint FILL = 0x1B02;
    /// <summary>Rasterizes polygons as wireframe edges.</summary>
    public const uint LINE = 0x1B01;

    /// <summary>Returns an extension function address for the current OpenGL context.</summary>
    [DllImport("opengl32.dll", EntryPoint = "wglGetProcAddress", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr wglGetProcAddress(string name);
    /// <summary>Returns the address of a function exported by a loaded Windows module.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    /// <summary>Loads a Windows library and returns its handle, or zero on failure.</summary>
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    /// <summary>Returns the current OpenGL context of the calling thread, or zero.</summary>
    [DllImport("opengl32.dll", ExactSpelling = true)]
    private static extern IntPtr wglGetCurrentContext();

    /// <summary>Handle to opengl32.dll, retained for the process lifetime to keep function pointers valid.</summary>
    private static IntPtr _opengl32;
    /// <summary>Indicates that every required function address has been loaded successfully.</summary>
    private static bool _initialized;

    /// <summary>The current positive multiplier for Viewport and Scissor coordinates.</summary>
    private static int _renderScale = 1;

    /// <summary>Checks that initialization succeeded before accessing native function pointers.</summary>
    private static void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("OpenGL: call GL.Init() with a current context before using GL functions.");
    }

    /// <summary>Calculates an array size in bytes using pointer-sized arithmetic with overflow checking.</summary>
    private static nint GetByteCount<T>(int count) where T : unmanaged
        => checked((nint)count * sizeof(T));

    /// <summary>Looks up a function in the current context driver, then in opengl32.dll; throws if it is unavailable.</summary>
    private static IntPtr GetProc(string name)
    {
        IntPtr p = wglGetProcAddress(name);
        // Some drivers return the sentinel values 1, 2, 3, or -1 instead of NULL.
        if (p == IntPtr.Zero || p == (IntPtr)1 || p == (IntPtr)2 || p == (IntPtr)3 || p == (IntPtr)(-1))
            p = GetProcAddress(_opengl32, name);
        if (p == IntPtr.Zero)
            throw new InvalidOperationException($"OpenGL: failed to load function '{name}'.");
        return p;
    }

    /// <summary>Pointer to glClear, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _Clear;
    /// <summary>Pointer to glClearColor, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<float, float, float, float, void> _ClearColor;
    /// <summary>Pointer to glViewport, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, int, int, int, void> _Viewport;
    /// <summary>Pointer to glIsEnabled, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, byte> _IsEnabled;
    /// <summary>Pointer to glEnable, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _Enable;
    /// <summary>Pointer to glDisable, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _Disable;
    /// <summary>Pointer to glDepthFunc, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _DepthFunc;
    /// <summary>Pointer to glDepthMask, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<byte, void> _DepthMask;
    /// <summary>Pointer to glColorMask, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<byte, byte, byte, byte, void> _ColorMask;
    /// <summary>Pointer to glCullFace, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _CullFace;
    /// <summary>Pointer to glFrontFace, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _FrontFace;
    /// <summary>Pointer to glBlendFunc, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _BlendFunc;
    /// <summary>Pointer to glGetError, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint> _GetError;
    /// <summary>Pointer to glPolygonMode, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _PolygonMode;
    /// <summary>Pointer to glPointSize, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<float, void> _PointSize;

    /// <summary>Pointer to glDrawArrays, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, int, void> _DrawArrays;
    /// <summary>Pointer to glDrawElements, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, uint, void*, void> _DrawElements;
    /// <summary>Pointer to glDrawElementsInstanced, loaded by Init (W8: one call for a thousand rocks).</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, uint, void*, int, void> _DrawElementsInstanced;
    /// <summary>Pointer to glDrawArraysInstanced, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, int, int, void> _DrawArraysInstanced;
    /// <summary>Pointer to glVertexAttribDivisor, loaded by Init: a divisor of 1 advances an attribute per instance.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _VertexAttribDivisor;
    /// <summary>Pointer to glPolygonOffset, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<float, float, void> _PolygonOffset;
    /// <summary>Pointer to glBlendFuncSeparate, loaded by Init: colour and alpha blended by their own factors.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, uint, uint, void> _BlendFuncSeparate;

    /// <summary>Pointer to glGenBuffers, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _GenBuffers;
    /// <summary>Pointer to glDeleteBuffers, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _DeleteBuffers;
    /// <summary>Pointer to glBindBuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _BindBuffer;
    /// <summary>Pointer to glBufferData, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void> _BufferData;
    /// <summary>Pointer to glBufferSubData, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, nint, nint, void*, void> _BufferSubData;
    /// <summary>Pointer to glScissor, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, int, int, int, void> _Scissor;
    /// <summary>Pointer to glGetString, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, byte*> _GetString;
    /// <summary>Pointer to glGetStringi, loaded by Init; the only legal way to list extensions in a core profile.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, byte*> _GetStringi;
    /// <summary>Pointer to glGenVertexArrays, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _GenVertexArrays;
    /// <summary>Pointer to glDeleteVertexArrays, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _DeleteVertexArrays;
    /// <summary>Pointer to glBindVertexArray, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _BindVertexArray;
    /// <summary>Pointer to glEnableVertexAttribArray, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _EnableVertexAttribArray;
    /// <summary>Pointer to glVertexAttribPointer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, uint, byte, int, void*, void> _VertexAttribPointer;

    /// <summary>Pointer to glCreateShader, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint> _CreateShader;
    /// <summary>Pointer to glShaderSource, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void> _ShaderSource;
    /// <summary>Pointer to glCompileShader, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _CompileShader;
    /// <summary>Pointer to glGetShaderiv, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, int*, void> _GetShaderiv;
    /// <summary>Pointer to glGetShaderInfoLog, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void> _GetShaderInfoLog;
    /// <summary>Pointer to glDeleteShader, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _DeleteShader;

    /// <summary>Pointer to glCreateProgram, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint> _CreateProgram;
    /// <summary>Pointer to glAttachShader, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _AttachShader;
    /// <summary>Pointer to glLinkProgram, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _LinkProgram;
    /// <summary>Pointer to glGetProgramiv, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, int*, void> _GetProgramiv;
    /// <summary>Pointer to glGetProgramInfoLog, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void> _GetProgramInfoLog;
    /// <summary>Pointer to glUseProgram, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _UseProgram;
    /// <summary>Pointer to glDeleteProgram, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _DeleteProgram;

    /// <summary>Pointer to glGetUniformLocation, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, byte*, int> _GetUniformLocation;
    /// <summary>Pointer to glUniform1i, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, int, void> _Uniform1i;
    /// <summary>Pointer to glUniform1f, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, float, void> _Uniform1f;
    /// <summary>Pointer to glUniform2f, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, float, float, void> _Uniform2f;
    /// <summary>Pointer to glUniform3f, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, float, float, float, void> _Uniform3f;
    /// <summary>Pointer to glUniform4f, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, float, float, float, float, void> _Uniform4f;
    /// <summary>Pointer to glUniformMatrix3fv, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, int, byte, float*, void> _UniformMatrix3fv;
    /// <summary>Pointer to glUniformMatrix4fv, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, int, byte, float*, void> _UniformMatrix4fv;

    /// <summary>Pointer to glGenTextures, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _GenTextures;
    /// <summary>Pointer to glDeleteTextures, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _DeleteTextures;
    /// <summary>Pointer to glBindTexture, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _BindTexture;
    /// <summary>Pointer to glActiveTexture, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _ActiveTexture;
    /// <summary>Pointer to glTexImage2D, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> _TexImage2D;
    /// <summary>Pointer to glTexParameteri, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, int, void> _TexParameteri;
    /// <summary>Pointer to glGenerateMipmap, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _GenerateMipmap;
    /// <summary>Pointer to glPixelStorei, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, void> _PixelStorei;
    /// <summary>Pointer to glTexSubImage2D, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> _TexSubImage2D;
    /// <summary>Pointer to glGenFramebuffers, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _GenFramebuffers;
    /// <summary>Pointer to glDeleteFramebuffers, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _DeleteFramebuffers;
    /// <summary>Pointer to glBindFramebuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _BindFramebuffer;
    /// <summary>Pointer to glFramebufferTexture2D, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void> _FramebufferTexture2D;
    /// <summary>Pointer to glDrawBuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _DrawBuffer;
    /// <summary>Pointer to glGetIntegerv, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int*, void> _GetIntegerv;
    /// <summary>Pointer to glGenRenderbuffers, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _GenRenderbuffers;
    /// <summary>Pointer to glDeleteRenderbuffers, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _DeleteRenderbuffers;
    /// <summary>Pointer to glBindRenderbuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _BindRenderbuffer;
    /// <summary>Pointer to glRenderbufferStorageMultisample, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, int, uint, int, int, void> _RenderbufferStorageMultisample;
    /// <summary>Pointer to glRenderbufferStorage, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, int, int, void> _RenderbufferStorage;
    /// <summary>Pointer to glFramebufferRenderbuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint, uint, uint, void> _FramebufferRenderbuffer;
    /// <summary>Pointer to glBlitFramebuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void> _BlitFramebuffer;
    /// <summary>Pointer to glReadBuffer, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, void> _ReadBuffer;
    /// <summary>Pointer to glCheckFramebufferStatus, loaded by Init.</summary>
    private static delegate* unmanaged[Stdcall]<uint, uint> _CheckFramebufferStatus;

    /// <summary>Loads all OpenGL functions used by this wrapper for the current Windows context.</summary>
    /// <remarks>Make a context current on the rendering thread before calling this method. Call it again after creating or switching contexts. The driver must support every required function. Concurrent calls from multiple threads are not supported.</remarks>
    public static void Init()
    {
        // Calling Init again refreshes function addresses after creating or switching contexts.
        _initialized = false;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("This OpenGL loader requires Windows.");
        if (wglGetCurrentContext() == IntPtr.Zero)
            throw new InvalidOperationException("OpenGL: no current context. Make a context current before calling GL.Init().");

        // Keep the library loaded for the process lifetime because static fields retain its function pointers.
        // Reuse the loaded module on retries to avoid incrementing the LoadLibrary reference count.
        if (_opengl32 == IntPtr.Zero)
        {
            _opengl32 = LoadLibrary("opengl32.dll");
            if (_opengl32 == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Failed to load opengl32.dll.");
        }

        _Clear = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glClear");
        _ClearColor = (delegate* unmanaged[Stdcall]<float, float, float, float, void>)GetProc("glClearColor");
        _Viewport = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)GetProc("glViewport");
        _Enable = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glEnable");
        _IsEnabled = (delegate* unmanaged[Stdcall]<uint, byte>)GetProc("glIsEnabled");
        _Disable = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glDisable");
        _DepthFunc = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glDepthFunc");
        _DepthMask = (delegate* unmanaged[Stdcall]<byte, void>)GetProc("glDepthMask");
        _ColorMask = (delegate* unmanaged[Stdcall]<byte, byte, byte, byte, void>)GetProc("glColorMask");
        _CullFace = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glCullFace");
        _FrontFace = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glFrontFace");
        _BlendFunc = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glBlendFunc");
        _GetError = (delegate* unmanaged[Stdcall]<uint>)GetProc("glGetError");
        _PolygonMode = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glPolygonMode");
        _PointSize = (delegate* unmanaged[Stdcall]<float, void>)GetProc("glPointSize");

        _DrawArrays = (delegate* unmanaged[Stdcall]<uint, int, int, void>)GetProc("glDrawArrays");
        _DrawElements = (delegate* unmanaged[Stdcall]<uint, int, uint, void*, void>)GetProc("glDrawElements");
        _DrawElementsInstanced = (delegate* unmanaged[Stdcall]<uint, int, uint, void*, int, void>)GetProc("glDrawElementsInstanced");
        _DrawArraysInstanced = (delegate* unmanaged[Stdcall]<uint, int, int, int, void>)GetProc("glDrawArraysInstanced");
        _VertexAttribDivisor = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glVertexAttribDivisor");
        _PolygonOffset = (delegate* unmanaged[Stdcall]<float, float, void>)GetProc("glPolygonOffset");
        _BlendFuncSeparate = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, void>)GetProc("glBlendFuncSeparate");

        _GenBuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glGenBuffers");
        _DeleteBuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glDeleteBuffers");
        _BindBuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glBindBuffer");
        _BufferData = (delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void>)GetProc("glBufferData");
        _BufferSubData = (delegate* unmanaged[Stdcall]<uint, nint, nint, void*, void>)GetProc("glBufferSubData");
        _Scissor = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)GetProc("glScissor");
        _GetString = (delegate* unmanaged[Stdcall]<uint, byte*>)GetProc("glGetString");
        _GetStringi = (delegate* unmanaged[Stdcall]<uint, uint, byte*>)GetProc("glGetStringi");
        _GenVertexArrays = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glGenVertexArrays");
        _DeleteVertexArrays = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glDeleteVertexArrays");
        _BindVertexArray = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glBindVertexArray");
        _EnableVertexAttribArray = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glEnableVertexAttribArray");
        _VertexAttribPointer = (delegate* unmanaged[Stdcall]<uint, int, uint, byte, int, void*, void>)GetProc("glVertexAttribPointer");

        _CreateShader = (delegate* unmanaged[Stdcall]<uint, uint>)GetProc("glCreateShader");
        _ShaderSource = (delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void>)GetProc("glShaderSource");
        _CompileShader = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glCompileShader");
        _GetShaderiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)GetProc("glGetShaderiv");
        _GetShaderInfoLog = (delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void>)GetProc("glGetShaderInfoLog");
        _DeleteShader = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glDeleteShader");

        _CreateProgram = (delegate* unmanaged[Stdcall]<uint>)GetProc("glCreateProgram");
        _AttachShader = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glAttachShader");
        _LinkProgram = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glLinkProgram");
        _GetProgramiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)GetProc("glGetProgramiv");
        _GetProgramInfoLog = (delegate* unmanaged[Stdcall]<uint, int, int*, byte*, void>)GetProc("glGetProgramInfoLog");
        _UseProgram = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glUseProgram");
        _DeleteProgram = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glDeleteProgram");

        _GetUniformLocation = (delegate* unmanaged[Stdcall]<uint, byte*, int>)GetProc("glGetUniformLocation");
        _Uniform1i = (delegate* unmanaged[Stdcall]<int, int, void>)GetProc("glUniform1i");
        _Uniform1f = (delegate* unmanaged[Stdcall]<int, float, void>)GetProc("glUniform1f");
        _Uniform2f = (delegate* unmanaged[Stdcall]<int, float, float, void>)GetProc("glUniform2f");
        _Uniform3f = (delegate* unmanaged[Stdcall]<int, float, float, float, void>)GetProc("glUniform3f");
        _Uniform4f = (delegate* unmanaged[Stdcall]<int, float, float, float, float, void>)GetProc("glUniform4f");
        _UniformMatrix3fv = (delegate* unmanaged[Stdcall]<int, int, byte, float*, void>)GetProc("glUniformMatrix3fv");
        _UniformMatrix4fv = (delegate* unmanaged[Stdcall]<int, int, byte, float*, void>)GetProc("glUniformMatrix4fv");

        _GenTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glGenTextures");
        _DeleteTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glDeleteTextures");
        _BindTexture = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glBindTexture");
        _ActiveTexture = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glActiveTexture");
        _TexImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)GetProc("glTexImage2D");
        _TexParameteri = (delegate* unmanaged[Stdcall]<uint, uint, int, void>)GetProc("glTexParameteri");
        _GenerateMipmap = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glGenerateMipmap");
        _PixelStorei = (delegate* unmanaged[Stdcall]<uint, int, void>)GetProc("glPixelStorei");
        _TexSubImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)GetProc("glTexSubImage2D");
        _GenFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glGenFramebuffers");
        _DeleteFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glDeleteFramebuffers");
        _BindFramebuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glBindFramebuffer");
        _FramebufferTexture2D = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void>)GetProc("glFramebufferTexture2D");
        _DrawBuffer = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glDrawBuffer");
        _GetIntegerv = (delegate* unmanaged[Stdcall]<uint, int*, void>)GetProc("glGetIntegerv");
        _GenRenderbuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glGenRenderbuffers");
        _DeleteRenderbuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)GetProc("glDeleteRenderbuffers");
        _BindRenderbuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)GetProc("glBindRenderbuffer");
        _RenderbufferStorageMultisample = (delegate* unmanaged[Stdcall]<uint, int, uint, int, int, void>)GetProc("glRenderbufferStorageMultisample");
        _RenderbufferStorage = (delegate* unmanaged[Stdcall]<uint, uint, int, int, void>)GetProc("glRenderbufferStorage");
        _FramebufferRenderbuffer = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, void>)GetProc("glFramebufferRenderbuffer");
        _BlitFramebuffer = (delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void>)GetProc("glBlitFramebuffer");
        _ReadBuffer = (delegate* unmanaged[Stdcall]<uint, void>)GetProc("glReadBuffer");
        _CheckFramebufferStatus = (delegate* unmanaged[Stdcall]<uint, uint>)GetProc("glCheckFramebufferStatus");

        _extensions = null; // a new context may export a different set
        _initialized = true;
    }

    /// <summary>Clears the buffers selected by the bit mask, respecting Scissor and write masks.</summary>
    public static void Clear(uint mask)
    {
        EnsureInitialized();
        _Clear(mask);
    }
    /// <summary>Sets the RGBA color used by subsequent color buffer clears.</summary>
    public static void ClearColor(float r, float g, float b, float a)
    {
        EnsureInitialized();
        _ClearColor(r, g, b, a);
    }
    /// <summary>Positive integer scale for Viewport and Scissor coordinates and dimensions; use 1 for the UI pass.</summary>
    public static int RenderScale
    {
        get => _renderScale;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Render scale must be positive.");
            _renderScale = value;
        }
    }

    /// <summary>Sets the viewport transformation to pixel coordinates; coordinates and dimensions are multiplied by RenderScale.</summary>
    public static (int X,int Y,int Width,int Height) PhysicalViewport()
    {
        EnsureInitialized();
        int* values=stackalloc int[4];
        _GetIntegerv(0x0BA2,values);
        return (values[0],values[1],values[2],values[3]);
    }
    public static (int X,int Y,int Width,int Height) PhysicalScissor()
    {
        EnsureInitialized();int* values=stackalloc int[4];_GetIntegerv(0x0C10,values);
        return (values[0],values[1],values[2],values[3]);
    }
    public static void RestorePhysicalScissor(int x,int y,int width,int height){EnsureInitialized();_Scissor(x,y,width,height);}
    public static void RestorePhysicalViewport(int x,int y,int width,int height)
    {
        EnsureInitialized(); _Viewport(x,y,width,height);
    }
    public static void Viewport(int x, int y, int w, int h)
    {
        EnsureInitialized();
        if (w < 0) throw new ArgumentOutOfRangeException(nameof(w));
        if (h < 0) throw new ArgumentOutOfRangeException(nameof(h));
        int scale = RenderScale;
        _Viewport(checked(x * scale), checked(y * scale), checked(w * scale), checked(h * scale));
    }
    /// <summary>Enables the OpenGL capability specified by cap.</summary>
    public static void Enable(uint cap)
    {
        EnsureInitialized();
        _Enable(cap);
    }
    /// <summary>Returns whether the specified OpenGL capability is enabled in the current context.</summary>
    public static bool IsEnabled(uint cap)
    {
        EnsureInitialized();
        return _IsEnabled(cap) != 0;
    }
    /// <summary>Disables the OpenGL capability specified by cap.</summary>
    public static void Disable(uint cap)
    {
        EnsureInitialized();
        _Disable(cap);
    }
    /// <summary>Sets the comparison function for incoming and stored depth values.</summary>
    public static void DepthFunc(uint func)
    {
        EnsureInitialized();
        _DepthFunc(func);
    }
    /// <summary>Enables or disables depth writes without enabling the depth test itself.</summary>
    public static void DepthMask(bool writable)
    {
        EnsureInitialized();
        _DepthMask(writable ? TRUE : FALSE);
    }
    /// <summary>Independently enables or disables writes to R, G, B, and A without changing depth writes.</summary>
    public static void ColorMask(bool r, bool g, bool b, bool a)
    {
        EnsureInitialized();
        _ColorMask(r ? TRUE : FALSE, g ? TRUE : FALSE, b ? TRUE : FALSE, a ? TRUE : FALSE);
    }
    /// <summary>Selects which polygon faces are culled; requires CULL_FACE to be enabled.</summary>
    public static void CullFace(uint mode)
    {
        EnsureInitialized();
        _CullFace(mode);
    }
    /// <summary>Sets the vertex winding order that defines the front face of a polygon.</summary>
    public static void FrontFace(uint mode)
    {
        EnsureInitialized();
        _FrontFace(mode);
    }
    /// <summary>Sets the source and destination color blend factors.</summary>
    public static void BlendFunc(uint src, uint dst)
    {
        EnsureInitialized();
        _BlendFunc(src, dst);
    }
    /// <summary>Restores the renderer baseline for an opaque 3D scene between rendering passes.</summary>
    /// <remarks>Resets scaling, depth state, color writes, face culling, blending, Scissor, point size, framebuffer binding, polygon mode, and the active texture unit. This is not a complete OpenGL state reset: the caller must set the viewport, program, VAO, buffers, and textures.</remarks>
    public static void ResetStateDefaults()
    {
        EnsureInitialized();
        RenderScale = 1;
        Enable(DEPTH_TEST);
        DepthFunc(LESS);
        DepthMask(true);
        ColorMask(true, true, true, true);
        Enable(CULL_FACE);
        CullFace(BACK);
        FrontFace(CCW);
        Disable(BLEND);
        Disable(SCISSOR_TEST);
        Disable(PROGRAM_POINT_SIZE);
        PointSize(1.0f);
        BindFramebuffer(FRAMEBUFFER, 0);
        PolygonMode(FRONT_AND_BACK, FILL);
        ActiveTexture(TEXTURE0);
        // Resetting state invalidates the program binding cached by Shader.
        Shader.InvalidateBinding();
    }
    /// <summary>Returns and clears one OpenGL error flag; zero indicates no error.</summary>
    public static uint GetError()
    {
        EnsureInitialized();
        return _GetError();
    }
    /// <summary>Sets the polygon rasterization mode to filled or wireframe rendering.</summary>
    public static void PolygonMode(uint face, uint mode)
    {
        EnsureInitialized();
        _PolygonMode(face, mode);
    }
    /// <summary>Draws count consecutive vertices from the current VAO, starting at first.</summary>
    public static void DrawArrays(uint mode, int first, int count)
    {
        EnsureInitialized();
        _DrawArrays(mode, first, count);
    }
    /// <summary>Sets the point size in pixels; used when PROGRAM_POINT_SIZE is disabled.</summary>
    public static void PointSize(float size)
    {
        EnsureInitialized();
        _PointSize(size);
    }
    /// <summary>Draws primitives using count indices; offset is a byte offset into the bound index buffer.</summary>
    /// <summary>Draws the bound index range once per instance; per-instance attributes advance by their divisor.</summary>
    public static void DrawElementsInstanced(uint mode, int count, uint type, nint offset, int instances)
    {
        EnsureInitialized();
        _DrawElementsInstanced(mode, count, type, (void*)offset, instances);
    }
    /// <summary>Draws the vertex range once per instance.</summary>
    public static void DrawArraysInstanced(uint mode, int first, int count, int instances)
    {
        EnsureInitialized();
        _DrawArraysInstanced(mode, first, count, instances);
    }
    /// <summary>How many instances share one value of the attribute: 0 per vertex (default), 1 per instance.</summary>
    public static void VertexAttribDivisor(uint index, uint divisor)
    {
        EnsureInitialized();
        _VertexAttribDivisor(index, divisor);
    }
    /// <summary>Depth offset for coplanar geometry (POLYGON_OFFSET_FILL must be enabled).</summary>
    public static void PolygonOffset(float factor, float units)
    {
        EnsureInitialized();
        _PolygonOffset(factor, units);
    }
    /// <summary>Separate blend factors for colour and alpha.</summary>
    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
    {
        EnsureInitialized();
        _BlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
    }
    public static void DrawElements(uint mode, int count, uint type, nint offset)
    {
        EnsureInitialized();
        _DrawElements(mode, count, type, (void*)offset);
    }
    /// <summary>Returns a new buffer object name; storage is allocated by BufferData.</summary>
    public static uint GenBuffer()
    {
        EnsureInitialized();
        uint id = 0;
        _GenBuffers(1, &id);
        return id;
    }
    /// <summary>Returns a new vertex array object (VAO) name.</summary>
    public static uint GenVertexArray()
    {
        EnsureInitialized();
        uint id = 0;
        _GenVertexArrays(1, &id);
        return id;
    }
    /// <summary>Deletes a buffer object; OpenGL ignores a zero name.</summary>
    public static void DeleteBuffer(uint id)
    {
        EnsureInitialized();
        _DeleteBuffers(1, &id);
    }
    /// <summary>Deletes a VAO without deleting its vertex or index buffers.</summary>
    public static void DeleteVertexArray(uint id)
    {
        EnsureInitialized();
        _DeleteVertexArrays(1, &id);
    }
    /// <summary>Binds a buffer to the specified target; zero removes the binding.</summary>
    public static void BindBuffer(uint target, uint id)
    {
        EnsureInitialized();
        _BindBuffer(target, id);
    }
    /// <summary>Selects a VAO containing vertex attribute settings and an index buffer binding.</summary>
    public static void BindVertexArray(uint id)
    {
        EnsureInitialized();
        _BindVertexArray(id);
    }
    /// <summary>Enables the vertex attribute at the specified index in the current VAO.</summary>
    public static void EnableVertexAttribArray(uint index)
    {
        EnsureInitialized();
        _EnableVertexAttribArray(index);
    }
    /// <summary>Describes a vertex attribute; stride and offset are in bytes within the bound ARRAY_BUFFER.</summary>
    /// <remarks>normalized controls conversion of integer data to a normalized range. Integer shader inputs require glVertexAttribIPointer, which is not included in this wrapper.</remarks>
    public static void VertexAttribPointer(uint index, int size, uint type, bool normalized, int stride, nint offset)
    {
        EnsureInitialized();
        _VertexAttribPointer(index, size, type, normalized ? TRUE : FALSE, stride, (void*)offset);
    }

    /// <summary>Creates or replaces storage for the bound buffer and copies the entire data array.</summary>
    public static void BufferData<T>(uint target, T[] data, uint usage) where T : unmanaged
    {
        EnsureInitialized();
        if (data is null) throw new ArgumentNullException(nameof(data));
        nint sizeBytes = GetByteCount<T>(data.Length);
        fixed (T* p = data)
        {
            _BufferData(target, sizeBytes, p, usage);
        }
    }
    /// <summary>Creates or replaces buffer storage of sizeBytes bytes without uploading data; its contents are undefined.</summary>
    public static void BufferData(uint target, nint sizeBytes, uint usage)
    {
        EnsureInitialized();
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        _BufferData(target, sizeBytes, null, usage);
    }
    /// <summary>Copies the first count array elements into an existing buffer at the byte offset offsetBytes.</summary>
    /// <remarks>The destination range must fit within the storage of the bound buffer. OpenGL validates storage bounds and the buffer binding.</remarks>
    public static void BufferSubData<T>(uint target, nint offsetBytes, T[] data, int count) where T : unmanaged
    {
        EnsureInitialized();
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (count < 0 || count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
        if (offsetBytes < 0) throw new ArgumentOutOfRangeException(nameof(offsetBytes));
        nint sizeBytes = GetByteCount<T>(count);
        _ = checked(offsetBytes + sizeBytes);
        fixed (T* p = data)
        {
            _BufferSubData(target, offsetBytes, sizeBytes, p);
        }
    }

    /// <summary>Sets a clipping rectangle from the lower-left corner; coordinates and dimensions are multiplied by RenderScale.</summary>
    public static void Scissor(int x, int y, int w, int h)
    {
        EnsureInitialized();
        if (w < 0) throw new ArgumentOutOfRangeException(nameof(w));
        if (h < 0) throw new ArgumentOutOfRangeException(nameof(h));
        int scale = RenderScale;
        _Scissor(checked(x * scale), checked(y * scale), checked(w * scale), checked(h * scale));
    }

    /// <summary>Returns an OpenGL string parameter decoded as UTF-8, or an empty string for a null pointer.</summary>
    public static string GetString(uint name)
    {
        EnsureInitialized();
        byte* p = _GetString(name);
        if (p == null) return string.Empty;
        int len = 0;
        while (p[len] != 0) len++;
        return Encoding.UTF8.GetString(p, len);
    }

    private static HashSet<string>? _extensions;
    /// <summary>Whether the context exports the named extension (for example "GL_EXT_texture_filter_anisotropic").
    /// Read once through glGetStringi, since a core profile refuses GetString(EXTENSIONS) with an error.</summary>
    public static bool HasExtension(string name)
    {
        EnsureInitialized();
        if (_extensions == null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            int count = GetInteger(0x821D); // GL_NUM_EXTENSIONS
            for (uint i = 0; i < (uint)count; i++)
            {
                byte* p = _GetStringi(0x1F03, i); // GL_EXTENSIONS
                if (p == null) continue;
                int len = 0;
                while (p[len] != 0) len++;
                set.Add(Encoding.UTF8.GetString(p, len));
            }
            _extensions = set;
        }
        return _extensions.Contains(name);
    }

    /// <summary>Creates a shader of the specified type; zero indicates failure.</summary>
    public static uint CreateShader(uint type)
    {
        EnsureInitialized();
        return _CreateShader(type);
    }

    /// <summary>Uploads shader source as UTF-8 with an explicit byte length, without compiling it.</summary>
    public static void ShaderSource(uint shader, string src)
    {
        EnsureInitialized();
        if (src is null) throw new ArgumentNullException(nameof(src));
        // The terminator ensures a non-null pointer even when the source text is empty.
        byte[] bytes = Encoding.UTF8.GetBytes(src + "\0");
        fixed (byte* p = bytes)
        {
            byte* pp = p;
            int len = bytes.Length - 1;
            _ShaderSource(shader, 1, &pp, &len);
        }
    }

    /// <summary>Compiles shader source; check COMPILE_STATUS and the information log for the result.</summary>
    public static void CompileShader(uint shader)
    {
        EnsureInitialized();
        _CompileShader(shader);
    }

    /// <summary>Reads an integer shader parameter, such as COMPILE_STATUS or INFO_LOG_LENGTH.</summary>
    public static int GetShaderInt(uint shader, uint pname)
    {
        EnsureInitialized();
        int v = 0;
        _GetShaderiv(shader, pname, &v);
        return v;
    }

    /// <summary>Returns the shader compilation log decoded as UTF-8, excluding the terminating null byte.</summary>
    public static string GetShaderInfoLog(uint shader)
    {
        EnsureInitialized();
        int len = GetShaderInt(shader, INFO_LOG_LENGTH);
        if (len <= 0) return string.Empty;
        byte[] buf = new byte[len];
        int written = 0;
        fixed (byte* p = buf) _GetShaderInfoLog(shader, len, &written, p);
        return Encoding.UTF8.GetString(buf, 0, Math.Max(0, Math.Min(written, buf.Length)));
    }

    /// <summary>Marks a shader for deletion; deletion may be deferred until it is detached from all programs.</summary>
    public static void DeleteShader(uint shader)
    {
        EnsureInitialized();
        _DeleteShader(shader);
    }
    /// <summary>Creates an empty shader program; zero indicates failure.</summary>
    public static uint CreateProgram()
    {
        EnsureInitialized();
        return _CreateProgram();
    }
    /// <summary>Attaches a shader to a program, whether or not the shader has been compiled yet.</summary>
    public static void AttachShader(uint program, uint shader)
    {
        EnsureInitialized();
        _AttachShader(program, shader);
    }
    /// <summary>Links a program from its attached shaders; check LINK_STATUS for the result.</summary>
    public static void LinkProgram(uint program)
    {
        EnsureInitialized();
        _LinkProgram(program);
    }

    /// <summary>Reads an integer program parameter, such as LINK_STATUS or INFO_LOG_LENGTH.</summary>
    public static int GetProgramInt(uint program, uint pname)
    {
        EnsureInitialized();
        int v = 0;
        _GetProgramiv(program, pname, &v);
        return v;
    }

    /// <summary>Returns the program link log decoded as UTF-8, excluding the terminating null byte.</summary>
    public static string GetProgramInfoLog(uint program)
    {
        EnsureInitialized();
        int len = GetProgramInt(program, INFO_LOG_LENGTH);
        if (len <= 0) return string.Empty;
        byte[] buf = new byte[len];
        int written = 0;
        fixed (byte* p = buf) _GetProgramInfoLog(program, len, &written, p);
        return Encoding.UTF8.GetString(buf, 0, Math.Max(0, Math.Min(written, buf.Length)));
    }

    /// <summary>Selects the program used by subsequent draw calls and uniform updates.</summary>
    public static void UseProgram(uint program)
    {
        EnsureInitialized();
        _UseProgram(program);
    }
    /// <summary>Marks a program for deletion; a current program remains alive until it is unbound.</summary>
    public static void DeleteProgram(uint program)
    {
        EnsureInitialized();
        _DeleteProgram(program);
    }

    /// <summary>Returns the location of the named uniform; -1 indicates that no active uniform was found.</summary>
    public static int GetUniformLocation(uint program, string name)
    {
        EnsureInitialized();
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (name.IndexOf('\0') >= 0)
            throw new ArgumentException("Uniform name must not contain a null character.", nameof(name));
        byte[] b = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = b) return _GetUniformLocation(program, p);
    }

    /// <summary>Sets an integer uniform, including a texture unit index for a sampler; loc = -1 is ignored.</summary>
    public static void Uniform1i(int loc, int v)
    {
        EnsureInitialized();
        _Uniform1i(loc, v);
    }
    /// <summary>Sets a scalar floating-point uniform in the current program; loc = -1 is ignored.</summary>
    public static void Uniform1f(int loc, float v)
    {
        EnsureInitialized();
        _Uniform1f(loc, v);
    }
    /// <summary>Sets a two-component floating-point uniform in the current program; loc = -1 is ignored.</summary>
    public static void Uniform2f(int loc, float x, float y)
    {
        EnsureInitialized();
        _Uniform2f(loc, x, y);
    }
    /// <summary>Sets a three-component floating-point uniform in the current program; loc = -1 is ignored.</summary>
    public static void Uniform3f(int loc, float x, float y, float z)
    {
        EnsureInitialized();
        _Uniform3f(loc, x, y, z);
    }
    /// <summary>Sets a four-component floating-point uniform in the current program; loc = -1 is ignored.</summary>
    public static void Uniform4f(int loc, float x, float y, float z, float w)
    {
        EnsureInitialized();
        _Uniform4f(loc, x, y, z, w);
    }
    /// <summary>Uploads one 4x4 matrix without transposing it; data starting at Mat4.M must contain 16 contiguous floats in column-major order.</summary>
    /// <remarks>Mat4 is passed by value, so the address of its field remains valid for the native call. The Mat4 type must provide a field layout compatible with the OpenGL matrix format.</remarks>
    public static void UniformMatrix4(int loc, Mat4 m)
    {
        EnsureInitialized();
        _UniformMatrix4fv(loc, 1, FALSE, (float*)&m.M);
    }
    /// <summary>Uploads one 3x3 matrix from the first nine array elements in column-major order, without transposing it.</summary>
    public static void UniformMatrix3(int loc, float[] m3)
    {
        EnsureInitialized();
        if (m3 is null) throw new ArgumentNullException(nameof(m3));
        if (m3.Length < 9)
            throw new ArgumentException("A 3x3 matrix requires at least 9 values.", nameof(m3));
        fixed (float* p = m3) _UniformMatrix3fv(loc, 1, FALSE, p);
    }

    /// <summary>Returns a new texture name; its format and storage are specified separately.</summary>
    public static uint GenTexture()
    {
        EnsureInitialized();
        uint id = 0;
        _GenTextures(1, &id);
        return id;
    }
    /// <summary>Deletes a texture; OpenGL ignores a zero name.</summary>
    public static void DeleteTexture(uint id)
    {
        EnsureInitialized();
        _DeleteTextures(1, &id);
    }
    /// <summary>Binds a texture to the specified target on the active texture unit.</summary>
    public static void BindTexture(uint target, uint id)
    {
        EnsureInitialized();
        _BindTexture(target, id);
    }
    /// <summary>Selects the active texture unit using TEXTURE0 + index, rather than the index alone.</summary>
    public static void ActiveTexture(uint unit)
    {
        EnsureInitialized();
        _ActiveTexture(unit);
    }
    /// <summary>Generates a mipmap chain from the base level of the bound texture.</summary>
    public static void GenerateMipmap(uint target)
    {
        EnsureInitialized();
        _GenerateMipmap(target);
    }
    /// <summary>Sets an integer pixel packing or unpacking parameter, such as UNPACK_ALIGNMENT.</summary>
    public static void PixelStorei(uint pname, int param)
    {
        EnsureInitialized();
        _PixelStorei(pname, param);
    }
    /// <summary>Sets an integer parameter of the bound texture, such as filtering or wrapping.</summary>
    public static void TexParameteri(uint target, uint pname, int param)
    {
        EnsureInitialized();
        _TexParameteri(target, pname, param);
    }
    /// <summary>Creates or replaces a two-dimensional texture level and uploads pixel data when provided.</summary>
    /// <remarks>When a pixel unpack buffer is bound, pixels is treated as an offset into that buffer, including zero. Otherwise, null allocates storage without uploading data. Data size and layout must match format, type, and PixelStorei settings; border must be zero for modern OpenGL.</remarks>
    public static void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, void* pixels)
    {
        EnsureInitialized();
        _TexImage2D(target, level, internalFormat, w, h, border, format, type, pixels);
    }
    /// <summary>Creates a texture level using a null data pointer; unbind any pixel unpack buffer to allocate storage without uploading data.</summary>
    /// <remarks>When a pixel unpack buffer is bound, pixels is treated as an offset into that buffer, including zero. Otherwise, null allocates storage without uploading data. Data size and layout must match format, type, and PixelStorei settings; border must be zero for modern OpenGL.</remarks>
    public static void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type)
    {
        EnsureInitialized();
        TexImage2D(target, level, internalFormat, w, h, border, format, type, null);
    }
    /// <summary>Updates a rectangle of an existing texture level without reallocating storage.</summary>
    /// <remarks>pixels must reference enough data for the specified format and PixelStorei settings; when a pixel unpack buffer is bound, it is a byte offset.</remarks>
    public static void TexSubImage2D(uint target, int level, int x, int y, int w, int h, uint format, uint type, void* pixels)
    {
        EnsureInitialized();
        _TexSubImage2D(target, level, x, y, w, h, format, type, pixels);
    }

    /// <summary>Returns a new framebuffer name; attachments are configured separately.</summary>
    public static uint GenFramebuffer()
    {
        EnsureInitialized();
        uint fbo = 0;
        _GenFramebuffers(1, &fbo);
        return fbo;
    }
    /// <summary>Deletes a framebuffer without deleting its attached textures or renderbuffers.</summary>
    public static void DeleteFramebuffer(uint fbo)
    {
        EnsureInitialized();
        _DeleteFramebuffers(1, &fbo);
    }
    /// <summary>Binds a framebuffer for reading, drawing, or both; zero selects the default
    /// framebuffer of the current OpenGL context (the window).</summary>
    public static void BindFramebuffer(uint target, uint fbo)
    {
        EnsureInitialized();
        _BindFramebuffer(target, fbo);
    }
    /// <summary>Attaches a two-dimensional texture level to a framebuffer; texture = 0 removes the attachment.</summary>
    public static void FramebufferTexture2D(uint target, uint attachment, uint texTarget, uint texture, int level)
    {
        EnsureInitialized();
        _FramebufferTexture2D(target, attachment, texTarget, texture, level);
    }
    /// <summary>Selects the color buffer for drawing; NONE disables color output, for example when rendering a depth map.</summary>
    public static void DrawBuffer(uint mode)
    {
        EnsureInitialized();
        _DrawBuffer(mode);
    }

    /// <summary>Reads a single integer state parameter, such as MAX_SAMPLES or UNPACK_ALIGNMENT.</summary>
    /// <remarks>Only parameters that return a single value are supported. Vector parameters such as GL_VIEWPORT are invalid here: glGetIntegerv would write multiple values and corrupt memory. Such queries require a separate buffer of the appropriate length.</remarks>
    public static int GetInteger(uint pname)
    {
        EnsureInitialized();
        int v = 0;
        _GetIntegerv(pname, &v);
        return v;
    }

    /// <summary>Returns a new renderbuffer name; storage dimensions and format are specified separately.</summary>
    public static uint GenRenderbuffer()
    {
        EnsureInitialized();
        uint rb = 0;
        _GenRenderbuffers(1, &rb);
        return rb;
    }
    /// <summary>Deletes the named renderbuffer; OpenGL ignores a zero name.</summary>
    public static void DeleteRenderbuffer(uint rb)
    {
        EnsureInitialized();
        _DeleteRenderbuffers(1, &rb);
    }
    /// <summary>Binds a renderbuffer; the standard target is RENDERBUFFER.</summary>
    public static void BindRenderbuffer(uint target, uint rb)
    {
        EnsureInitialized();
        _BindRenderbuffer(target, rb);
    }
    /// <summary>Allocates multisample renderbuffer storage; samples = 0 is valid and disables multisampling.</summary>
    public static void RenderbufferStorageMultisample(uint target, int samples, uint format, int w, int h)
    {
        EnsureInitialized();
        _RenderbufferStorageMultisample(target, samples, format, w, h);
    }

    /// <summary>Allocates renderbuffer storage with the specified format and dimensions, without multisampling.</summary>
    public static void RenderbufferStorage(uint target, uint format, int w, int h)
    {
        EnsureInitialized();
        _RenderbufferStorage(target, format, w, h);
    }

    /// <summary>Attaches a renderbuffer to a framebuffer; rb = 0 removes the attachment.</summary>
    public static void FramebufferRenderbuffer(uint target, uint attachment, uint rbTarget, uint rb)
    {
        EnsureInitialized();
        _FramebufferRenderbuffer(target, attachment, rbTarget, rb);
    }
    /// <summary>Copies selected buffers from READ_FRAMEBUFFER to DRAW_FRAMEBUFFER or performs a multisample resolve.</summary>
    /// <remarks>Rectangle coordinates are physical pixels and are not multiplied by RenderScale. Use NEAREST for depth copies and multisample resolves; dimensions and formats must satisfy OpenGL restrictions.</remarks>
    public static void BlitFramebuffer(int sx0, int sy0, int sx1, int sy1,
                                       int dx0, int dy0, int dx1, int dy1, uint mask, uint filter)
    {
        EnsureInitialized();
        _BlitFramebuffer(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter);
    }

    /// <summary>Selects the color buffer for reading; NONE disables color reads.</summary>
    public static void ReadBuffer(uint mode)
    {
        EnsureInitialized();
        _ReadBuffer(mode);
    }
    /// <summary>Returns the status of the framebuffer bound to target; FRAMEBUFFER_COMPLETE indicates readiness.</summary>
    public static uint CheckFramebufferStatus(uint target)
    {
        EnsureInitialized();
        return _CheckFramebufferStatus(target);
    }
}
