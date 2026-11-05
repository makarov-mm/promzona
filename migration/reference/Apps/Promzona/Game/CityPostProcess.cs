using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Game-only, bounded screen-space contact shading and edge antialiasing.
/// Owns its targets; the caller's framebuffer and viewport survive resize/load/disposal.</summary>
internal sealed class CityPostProcess : IDisposable
{
    private Shader? _occlusion, _compose;
    private uint _scene, _color, _depth, _aoTarget, _ao, _vao;
    private int _width, _height;
    public long TargetBytes => (long)_width*_height*12 + (long)((_width+1)/2)*((_height+1)/2)*4;
    public CityPostProcess()
    {
        try
        {
            string vertex = CityShaderSource.Read("PostProcess.Fullscreen.vert");
            _occlusion = new(vertex, CityShaderSource.Read("PostProcess.Occlusion.frag"));
            _compose = new(vertex, CityShaderSource.Read("PostProcess.Compose.frag"));
            _vao = GL.GenVertexArray();
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    private static uint Image(int width,int height,int format,uint channel,uint type,bool linear)
    {
        uint texture=GL.GenTexture();GL.BindTexture(GL.TEXTURE_2D,texture);
        GL.TexImage2D(GL.TEXTURE_2D,0,format,width,height,0,channel,type);
        GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MIN_FILTER,(int)(linear?GL.LINEAR:GL.NEAREST));
        GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MAG_FILTER,(int)(linear?GL.LINEAR:GL.NEAREST));
        GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_S,(int)GL.CLAMP_TO_EDGE);
        GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_T,(int)GL.CLAMP_TO_EDGE);
        return texture;
    }
    private static uint Target(uint color,uint depth=0)
    {
        uint target=GL.GenFramebuffer();GL.BindFramebuffer(GL.FRAMEBUFFER,target);
        GL.FramebufferTexture2D(GL.FRAMEBUFFER,GL.COLOR_ATTACHMENT0,GL.TEXTURE_2D,color,0);
        if(depth!=0)GL.FramebufferTexture2D(GL.FRAMEBUFFER,GL.DEPTH_ATTACHMENT,GL.TEXTURE_2D,depth,0);
        GL.DrawBuffer(GL.COLOR_ATTACHMENT0);
        if(GL.CheckFramebufferStatus(GL.FRAMEBUFFER)!=0x8CD5){GL.DeleteFramebuffer(target);throw new InvalidOperationException("City post-process framebuffer is incomplete");}
        return target;
    }
    private void Resize(int width,int height)
    {
        if(width==_width&&height==_height)return;
        ReleaseTargets();
        try {
            _width=width;_height=height;GL.ActiveTexture(GL.TEXTURE0);
            _color=Image(width,height,0x881A,GL.RGBA,GL.FLOAT,true);
            _depth=Image(width,height,0x81A6,GL.DEPTH_COMPONENT,GL.UNSIGNED_INT,false);
            _scene=Target(_color,_depth);
            _ao=Image((width+1)/2,(height+1)/2,0x822E,0x1903,GL.FLOAT,false);
            _aoTarget=Target(_ao);
        }catch{ReleaseTargets();throw;}
    }
    public void Render(Action scene,OrbitCamera camera,int quality)
    {
        var viewport=GL.PhysicalViewport();if(viewport.Width<1||viewport.Height<1){scene();return;}
        uint draw=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
        bool scissor=GL.IsEnabled(GL.SCISSOR_TEST);int scale=GL.RenderScale;
        try {
            GL.RenderScale=1;GL.Disable(GL.SCISSOR_TEST);Resize(viewport.Width,viewport.Height);
            GL.BindFramebuffer(GL.FRAMEBUFFER,_scene);GL.Viewport(0,0,_width,_height);
            GL.ClearColor(0,0,0,1);GL.DepthMask(true);GL.Clear(GL.COLOR_BUFFER_BIT|GL.DEPTH_BUFFER_BIT);
            scene();
            GL.Disable(GL.DEPTH_TEST);GL.Disable(GL.BLEND);GL.Disable(GL.CULL_FACE);GL.DepthMask(false);
            GL.BindVertexArray(_vao);
            GL.BindFramebuffer(GL.FRAMEBUFFER,_aoTarget);GL.Viewport(0,0,(_width+1)/2,(_height+1)/2);
            var projection=camera.Projection((float)_width/_height);
            _occlusion!.Use();_occlusion.SetInt("uDepth",0);_occlusion.SetVec2("uInvSize",1f/_width,1f/_height);
            _occlusion.SetVec4("uLens",projection[0,0],projection[1,1],camera.Near,camera.Far);
            _occlusion.SetInt("uSamples",quality>1?12:8);
            GL.ActiveTexture(GL.TEXTURE0);GL.BindTexture(GL.TEXTURE_2D,_depth);GL.DrawArrays(GL.TRIANGLES,0,3);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,draw);GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);
            _compose!.Use();_compose.SetInt("uColor",0);_compose.SetInt("uDepth",1);_compose.SetInt("uAo",2);
            _compose.SetVec2("uInvSize",1f/_width,1f/_height);_compose.SetVec2("uClip",camera.Near,camera.Far);
            GL.ActiveTexture(GL.TEXTURE0);GL.BindTexture(GL.TEXTURE_2D,_color);
            GL.ActiveTexture(GL.TEXTURE0+1);GL.BindTexture(GL.TEXTURE_2D,_depth);
            GL.ActiveTexture(GL.TEXTURE0+2);GL.BindTexture(GL.TEXTURE_2D,_ao);GL.DrawArrays(GL.TRIANGLES,0,3);
        }
        finally {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,draw);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);
            GL.RenderScale=scale;GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);
            GL.ActiveTexture(GL.TEXTURE0);GL.BindVertexArray(0);GL.DepthMask(true);GL.Enable(GL.DEPTH_TEST);GL.Enable(GL.CULL_FACE);
            if(scissor)GL.Enable(GL.SCISSOR_TEST);else GL.Disable(GL.SCISSOR_TEST);
        }
    }
    private void ReleaseTargets()
    {
        if(_scene!=0)GL.DeleteFramebuffer(_scene);if(_aoTarget!=0)GL.DeleteFramebuffer(_aoTarget);
        if(_color!=0)GL.DeleteTexture(_color);if(_depth!=0)GL.DeleteTexture(_depth);if(_ao!=0)GL.DeleteTexture(_ao);
        _scene=_aoTarget=_color=_depth=_ao=0;_width=_height=0;
    }
    public void Dispose(){ReleaseTargets();_occlusion?.Dispose();_compose?.Dispose();_occlusion=_compose=null;if(_vao!=0)GL.DeleteVertexArray(_vao);_vao=0;}
}

