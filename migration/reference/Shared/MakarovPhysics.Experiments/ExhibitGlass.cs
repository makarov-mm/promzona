using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Reusable apparatus glass with Fresnel reflections, absorption and subtle
/// screen-space refraction. It resolves the caller's current colour target;
/// it never samples the texture it is drawing into.</summary>
public sealed class ExhibitGlass : IDisposable
{
    private Shader? _shader = new(DefaultShaders.LitVertex, Fragment);
    private uint _vao, _vbo;
    private int _count;
    private float[] _data = [];
    public Vec3 Absorption { get; set; } = new(.025f, .006f, .003f);
    public bool Liquid { get; set; }
    /// <summary>A section through deep water: absorption with depth, no studio
    /// softbox reflected on an imaginary vertical glass wall. Opt-in only.</summary>
    public bool WaterSection { get; set; }
    public float OpticalDepth { get; set; } = 2.4f;
    private float _waterLow, _waterHigh;
    private uint _capture, _texture;
    private int _width, _height;

    /// <summary>Optional stylised in-scattering for coloured display liquids; zero preserves clear glass.</summary>
    public Vec3 Scattering { get; set; } = Vec3.Zero;

    public void SetGeometry(DioramaGeometry geometry)
    {
        List<SurfaceVertex> points = geometry.SurfacePoints;

        if (points.Count == 0)
        {
            _count = 0;
            return;
        }

        if (_vao == 0)
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, 24, 12);
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }

        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);

        if (_data.Length < points.Count * 6)
        {
            _data = new float[Math.Max(points.Count * 6, _data.Length * 2)];
            GL.BufferData(GL.ARRAY_BUFFER, _data.Length * sizeof(float), GL.DYNAMIC_DRAW);
        }

        for (int i = 0; i < points.Count; i++)
        {
            SurfaceVertex p = points[i];
            int k = i * 6;
            _data[k] = p.Position.X;
            _data[k + 1] = p.Position.Y;
            _data[k + 2] = p.Position.Z;
            _data[k + 3] = p.Normal.X;
            _data[k + 4] = p.Normal.Y;
            _data[k + 5] = p.Normal.Z;
        }

        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, points.Count * 6);
        _count = points.Count;
        if(WaterSection)
        {
            _waterLow=points.Min(p=>p.Position.Y);
            _waterHigh=points.Max(p=>p.Position.Y);
        }
    }

    private void DrawMesh()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, _count);
        GL.BindVertexArray(0);
    }

    private void EnsureTarget(int width, int height)
    {
        if (_texture != 0 && width == _width && height == _height)
            return;

        if (_capture == 0)
            _capture = GL.GenFramebuffer();

        if (_texture == 0)
            _texture = GL.GenTexture();

        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _texture);
        GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, width, height, 0, GL.RGBA, GL.UNSIGNED_BYTE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
        GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, _capture);
        GL.FramebufferTexture2D(GL.DRAW_FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, _texture, 0);
        GL.DrawBuffer(GL.COLOR_ATTACHMENT0);

        if (GL.CheckFramebufferStatus(GL.DRAW_FRAMEBUFFER) != GL.FRAMEBUFFER_COMPLETE)
            throw new InvalidOperationException("Exhibit glass refraction framebuffer incomplete");

        _width = width;
        _height = height;
    }

    public void Draw(Mat4 view, Mat4 proj, Vec3 eye, float strength = 1)
    {
        if (_shader is not { } s || _count == 0 || strength <= 0)
            return;

        (int X, int Y, int Width, int Height) viewport = GL.PhysicalViewport();

        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        uint draw = (uint)GL.GetInteger(0x8CA6), read = (uint)GL.GetInteger(0x8CAA);
        bool scissor = GL.IsEnabled(GL.SCISSOR_TEST), blend = GL.IsEnabled(GL.BLEND), cull = GL.IsEnabled(GL.CULL_FACE);
        uint cullMode = (uint)GL.GetInteger(0x0B45);
        bool depthWrite = GL.GetInteger(0x0B72) != 0;
        uint blendSrc = (uint)GL.GetInteger(0x80C9), blendDst = (uint)GL.GetInteger(0x80C8);

        try
        {
            GL.Disable(GL.SCISSOR_TEST);
            EnsureTarget(viewport.Width, viewport.Height);
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, draw);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, _capture);
            GL.BlitFramebuffer(viewport.X, viewport.Y, viewport.X + viewport.Width, viewport.Y + viewport.Height, 0, 0, viewport.Width, viewport.Height, GL.COLOR_BUFFER_BIT, GL.NEAREST);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);

            if (scissor)
                GL.Enable(GL.SCISSOR_TEST);

            GL.DepthMask(false);
            GL.Enable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            s.Use();
            s.SetMatrix4("uView", view);
            s.SetMatrix4("uProj", proj);
            s.SetMatrix4("uModel", Mat4.Identity);
            s.SetVec3("uViewPos", eye);
            s.SetVec4("uViewport", viewport.X, viewport.Y, viewport.Width, viewport.Height);
            s.SetFloat("uStrength", strength);
            s.SetVec3("uAbsorption", Absorption);
            s.SetVec3("uScattering", Scattering);
            s.SetFloat("uWater", Liquid ? 1 : 0);
            s.SetInt("uSection", WaterSection ? 1 : 0);
            s.SetVec3("uDepth", new(_waterLow,_waterHigh,OpticalDepth));
            s.SetInt("uScene", 0);
            GL.ActiveTexture(GL.TEXTURE0);
            GL.BindTexture(GL.TEXTURE_2D, _texture);
            if(WaterSection)
            {
                GL.Disable(GL.CULL_FACE);
                s.SetInt("uBack",0);
                DrawMesh();
            }
            else
            {
                GL.CullFace(GL.FRONT);
                s.SetInt("uBack", 1);
                DrawMesh();
                GL.CullFace(GL.BACK);
                s.SetInt("uBack", 0);
                DrawMesh();
            }
        }
        finally
        {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);
            GL.DepthMask(depthWrite);
            GL.CullFace(cullMode);
            GL.BlendFunc(blendSrc, blendDst);

            if (cull)
                GL.Enable(GL.CULL_FACE);
            else
                GL.Disable(GL.CULL_FACE);

            if (blend)
                GL.Enable(GL.BLEND);
            else
                GL.Disable(GL.BLEND);

            if (scissor)
                GL.Enable(GL.SCISSOR_TEST);
            else
                GL.Disable(GL.SCISSOR_TEST);
        }
    }

    public void Dispose()
    {
        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao);
            _vao = 0;
        }

        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        _count = 0;
        _shader?.Dispose();
        _shader = null;

        if (_texture != 0)
        {
            GL.DeleteTexture(_texture);
            _texture = 0;
        }

        if (_capture != 0)
        {
            GL.DeleteFramebuffer(_capture);
            _capture = 0;
        }
    }

    private const string Fragment = @"#version 330 core
in vec3 vWorldPos,vNormal;
uniform mat4 uView;
uniform vec3 uViewPos;
uniform vec4 uViewport;
uniform sampler2D uScene;
uniform float uStrength,uWater;
uniform vec3 uAbsorption,uScattering;
uniform int uBack,uSection;
uniform vec3 uDepth;
out vec4 FragColor;
void main(){
 vec3 n=normalize(vNormal),v=normalize(uViewPos-vWorldPos);
 if(uSection==1) {
     vec2 uv=(gl_FragCoord.xy-uViewport.xy)/uViewport.zw;
     vec3 behind=texture(uScene,clamp(uv,vec2(.001),vec2(.999))).rgb;
     float depth=clamp((uDepth.y-vWorldPos.y)/max(.01,uDepth.y-uDepth.x),0.,1.);
     float path=(.35+1.8*depth)*uDepth.z/max(.45,abs(dot(n,v)));
     vec3 transmission=exp(-vec3(.48,.20,.12)*path);
     vec3 scatter=mix(vec3(.065,.30,.36),vec3(.025,.13,.21),depth);
     vec3 water=behind*transmission+scatter*(1.-transmission);
     FragColor=vec4(water,clamp(uStrength,0.,1.));return;
 }
 float mu=abs(dot(n,v));float fres=.04+.96*pow(1.-mu,5.);
 vec3 r=reflect(-v,n),ne=mat3(uView)*n;
 // Broad studio softboxes, narrow highlight and a cool rim. No opaque wire outline.
 float strip=pow(max(0.,dot(r,normalize(vec3(-.55,.38,.74)))),14.);
 float fill=pow(max(0.,dot(r,normalize(vec3(.72,.20,-.65)))),10.);
 float glint=pow(max(0.,dot(n,normalize(v+normalize(vec3(-.5,.8,.45))))),180.);
 vec3 reflection=vec3(.30,.40,.50)+vec3(.85,.94,1.0)*strip+vec3(.55,.71,.82)*fill;
 vec2 uv=(gl_FragCoord.xy-uViewport.xy)/uViewport.zw;
 vec2 offset=ne.xy*(1.-mu)*.010*uStrength;
 vec3 transmitted=texture(uScene,clamp(uv+offset,vec2(.001),vec2(.999))).rgb;
 transmitted*=exp(-uAbsorption*(1.+2.*pow(1.-mu,2.))*uStrength);
 transmitted=mix(transmitted,vec3(.09,.52,.64),.16*uWater);
 transmitted+=uScattering*(.25+.28*mu)*uStrength;
 vec3 color=mix(transmitted,reflection,min(.62,fres*.65*uStrength));
 color+=vec3(.75,.84,.91)*(glint*.50+strip*.22)*uStrength;
 float alpha=uBack==1?.13*uStrength:.90;
 FragColor=vec4(color,alpha);
}";
}
