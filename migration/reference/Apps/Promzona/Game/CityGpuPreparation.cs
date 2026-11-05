using System.Runtime.InteropServices;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>Real actor draws into tiny private targets during loading. Linking a
/// shader alone does not prepare every driver's mesh/material combination.
/// One preparation step per loading frame; no world simulation or visible output.</summary>
internal sealed class CityGpuPreparation : IDisposable
{
    private uint _target, _depth, _hdr, _ldr;
    internal static readonly Vec3 Eye = new(0, 1, 4);
    internal static readonly Mat4 View = Mat4.LookAt(Eye, new(0, .7f, 0), Vec3.UnitY);
    internal static readonly Mat4 Projection = Mat4.Perspective(MathF.PI / 3, 1, .1f, 20);
    internal static readonly float[] Instance = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1, 0,0,1,0, 1,1,1,1];

    [DllImport("opengl32.dll")] private static extern void glFinish();
    [DllImport("opengl32.dll")] private static extern void glGetFloatv(uint name, [Out] float[] values);

    public void Run(Action<bool> draw)
    {
        // Loading UI may have a different FBO, viewport, program, scissor and
        // texture bindings. Restore all state touched here, including on failure.
        var viewport = GL.PhysicalViewport();
        uint previousDraw = (uint)GL.GetInteger(0x8CA6), previousRead = (uint)GL.GetInteger(0x8CAA);
        uint program = (uint)GL.GetInteger(0x8B8D), vao = (uint)GL.GetInteger(0x85B5);
        uint buffer = (uint)GL.GetInteger(0x8894), renderbuffer = (uint)GL.GetInteger(0x8CA7);
        uint activeTexture = (uint)GL.GetInteger(0x84E0), depthFunc = (uint)GL.GetInteger(0x0B74);
        bool depthWrite = GL.GetInteger(0x0B72) != 0;
        uint[] flags = [GL.DEPTH_TEST, GL.BLEND, GL.CULL_FACE, GL.SCISSOR_TEST];
        bool[] enabled = flags.Select(GL.IsEnabled).ToArray();
        float[] clear = new float[4], mask = new float[4];
        glGetFloatv(0x0C22, clear); glGetFloatv(0x0C23, mask);
        // These are the units used by both actor renderers, terrain and shadows.
        uint[] units = [0, 1, 2, 6, 9, 11];
        uint[] textures = new uint[units.Length];
        for (int i = 0; i < units.Length; i++)
        {
            GL.ActiveTexture(GL.TEXTURE0 + units[i]);
            textures[i] = (uint)GL.GetInteger(0x8069);
        }
        try
        {
            if (_target == 0)
            {
                _target = GL.GenFramebuffer();
                _depth = Attachment(GL.DEPTH_COMPONENT24);
                _hdr = Attachment(GL.RGBA16F);
                _ldr = Attachment(GL.RGBA8);
            }
            GL.Disable(GL.SCISSOR_TEST); GL.Disable(GL.BLEND); GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.DEPTH_TEST); GL.DepthFunc(GL.LESS); GL.DepthMask(true);
            GL.ColorMask(true, true, true, true); GL.ClearColor(0, 0, 0, 1);
            GL.RestorePhysicalViewport(0, 0, 16, 16);
            GL.BindFramebuffer(GL.FRAMEBUFFER, _target);
            GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.RENDERBUFFER, _depth);
            // Depth-only, HDR post-process and direct LDR paths all occur in play.
            foreach (uint color in new[] { 0u, _hdr, _ldr })
            {
                GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.RENDERBUFFER, color);
                GL.DrawBuffer(color == 0 ? GL.NONE : GL.COLOR_ATTACHMENT0);
                GL.ReadBuffer(color == 0 ? GL.NONE : GL.COLOR_ATTACHMENT0);
                if (GL.CheckFramebufferStatus(GL.FRAMEBUFFER) != GL.FRAMEBUFFER_COMPLETE)
                    throw new InvalidOperationException("Actor preparation framebuffer is incomplete");
                GL.Clear(GL.DEPTH_BUFFER_BIT | (color == 0 ? 0u : GL.COLOR_BUFFER_BIT));
                draw(color == 0);
            }
            // Deliberately only at load time: finish the submitted work before
            // reporting this preparation step complete, not on a gameplay frame.
            glFinish();
        }
        finally
        {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, previousDraw); GL.BindFramebuffer(GL.READ_FRAMEBUFFER, previousRead);
            GL.BindRenderbuffer(GL.RENDERBUFFER, renderbuffer);
            GL.RestorePhysicalViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            // The raw rebind bypasses Shader's binding cache, which still names the
            // actor program drawn above; without this its next Use() would be skipped.
            GL.UseProgram(program); Shader.InvalidateBinding(); GL.BindVertexArray(vao); GL.BindBuffer(GL.ARRAY_BUFFER, buffer);
            for (int i = 0; i < units.Length; i++)
            {
                GL.ActiveTexture(GL.TEXTURE0 + units[i]); GL.BindTexture(GL.TEXTURE_2D, textures[i]);
            }
            GL.ActiveTexture(activeTexture); GL.DepthFunc(depthFunc); GL.DepthMask(depthWrite);
            GL.ColorMask(mask[0] != 0, mask[1] != 0, mask[2] != 0, mask[3] != 0);
            GL.ClearColor(clear[0], clear[1], clear[2], clear[3]);
            for (int i = 0; i < flags.Length; i++) { if (enabled[i]) GL.Enable(flags[i]); else GL.Disable(flags[i]); }
        }
    }

    private static uint Attachment(uint format)
    {
        uint result = GL.GenRenderbuffer(); GL.BindRenderbuffer(GL.RENDERBUFFER, result);
        GL.RenderbufferStorage(GL.RENDERBUFFER, format, 16, 16);
        return result;
    }

    public void Dispose()
    {
        if (_target != 0) GL.DeleteFramebuffer(_target);
        if (_depth != 0) GL.DeleteRenderbuffer(_depth);
        if (_hdr != 0) GL.DeleteRenderbuffer(_hdr);
        if (_ldr != 0) GL.DeleteRenderbuffer(_ldr);
        _target = _depth = _hdr = _ldr = 0;
    }
}
