namespace MakarovPhysics.Renderer;
/// <summary>Material identifiers carried by streamed experiment geometry.</summary>
public enum SurfaceMaterial
{
    Automatic,
    Enamel,
    Steel,
    Brass,
    Wood,
    Rubber,
    Ceramic,
    Water,
    Emissive,
    Flat,
    Copper,
    Stone,
    Foil,
    OpticalMirror,
    SpaceMirror,
    MachinedMetal,
    CastMetal,
    Concrete,
    MarineWater,
    Landscape,
    Foliage,
    Bark,
    ArchitecturalGlass,
    Paving,
    GrassBlade,
    DeepWater,
    Polymer,
    Textile,
    WetClay,
    Brick,
    Plaster,
    Paper,
    Silicon,
    LacqueredWood,
    BrushedAluminium,
    GlazedTile,
    DryPlant,
    Wax,
    // 30.10.2026: finishes for the Engineering and Earth sites that stand in a cave, a desert, a glacier
    // or a museum. Ids 38+ always take the per-material bump of the main path, also under WorkshopFinish.
    Rust,
    Sand,
    Ice,
    Molten,
    Rock,
    Snow,
    Patina
}

/// <summary>Unlit albedo and analytic normals; Local follows the moving part.</summary>
public readonly record struct SurfaceVertex(Vec3 Position, Vec3 Normal, Vec3 Color, Vec3 Local, SurfaceMaterial Material);
/// <summary>A batched material surface using the suite's common PBR lighting.
/// Geometry, shadow and colour passes share the same vertices.</summary>
public sealed class StudioSurface : IDisposable
{
    private const int Stride = 13;
    private readonly float[] _data;
    private Shader? _shader, _depth;
    private uint _vao, _vbo;
    private int _count;
    public bool DetailedFinish { get; set; }
    /// <summary>Opt-in filtered production materials and analytic workshop reflections.</summary>
    public bool WorkshopFinish { get; set; }
    public bool Outdoor { get; set; }
    public float AtmosphereDensity { get; set; } = 1;
    public OutdoorLighting? Lighting { get; set; }
    public StudioSurface(int capacity = 600000)
    {
        _data = new float[capacity * Stride];
        try
        {
            _shader = new Shader(Vertex, Fragment);
            _depth = new Shader(DepthVertex, DefaultShaders.DepthFragment);
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, (nint)(_data.Length * sizeof(float)), GL.DYNAMIC_DRAW);
            for (uint i = 0; i < 5; i++)
            {
                GL.VertexAttribPointer(i, i == 4 ? 1 : 3, GL.FLOAT, false, Stride * sizeof(float), (int)i * 3 * sizeof(float));
                GL.EnableVertexAttribArray(i);
            }

            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Update(IReadOnlyList<SurfaceVertex> vertices, SurfaceMaterial automatic = SurfaceMaterial.Enamel)
    {
        if (vertices.Count > _data.Length / Stride)
            throw new InvalidOperationException("Studio surface geometry budget exceeded");
        _count = vertices.Count;
        for (int i = 0; i < _count; i++)
        {
            var v = vertices[i];
            int k = i * Stride;
            _data[k] = v.Position.X;
            _data[k + 1] = v.Position.Y;
            _data[k + 2] = v.Position.Z;
            _data[k + 3] = v.Normal.X;
            _data[k + 4] = v.Normal.Y;
            _data[k + 5] = v.Normal.Z;
            _data[k + 6] = v.Color.X;
            _data[k + 7] = v.Color.Y;
            _data[k + 8] = v.Color.Z;
            _data[k + 9] = v.Local.X;
            _data[k + 10] = v.Local.Y;
            _data[k + 11] = v.Local.Z;
            _data[k + 12] = (float)(v.Material == SurfaceMaterial.Automatic ? automatic : v.Material);
        }

        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * Stride);
    }

    public void DrawDepth(Mat4 lightSpace) => DrawDepth(lightSpace,0);

    public void DrawDepth(Mat4 lightSpace, float time) => DrawDepthTranslated(lightSpace, Vec3.Zero, time);

    public void DrawDepthTranslated(Mat4 lightSpace, Vec3 offset) => DrawDepthTranslated(lightSpace,offset,0);

    public void DrawDepthTranslated(Mat4 lightSpace, Vec3 offset, float time)
    {
        if (_depth is null || _count == 0)
            return;
        _depth.Use();
        _depth.SetMatrix4("uModel", Mat4.Translate(offset));
        _depth.SetFloat("uTime", time);
        _depth.SetMatrix4("uLightSpace", lightSpace);
        DrawRaw();
    }

    public void Draw(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace, uint shadow, float time, float ambient = .52f, bool emissionOnly = false, float environmentFill = 0)
        => DrawTranslated(view, projection, eye, light, lightColor, lightSpace, shadow, time, Vec3.Zero, ambient, emissionOnly, environmentFill);

    public void DrawTranslated(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace, uint shadow, float time, Vec3 offset, float ambient = .52f, bool emissionOnly = false, float environmentFill = 0)
    {
        if (_shader is null || _count == 0)
            return;
        var s = _shader;
        s.Use();
        s.SetVec3("uOffset", offset);
        s.SetMatrix4("uView", view);
        s.SetMatrix4("uProj", projection);
        Materials.ApplyLight(s, light, lightColor, ambient, .82f, .25f);
        s.SetVec3("uViewPos", eye);
        s.SetFloat("uTime", time);
        s.SetInt("uDetailedFinish", DetailedFinish ? 1 : 0);
        s.SetInt("uWorkshopFinish", WorkshopFinish ? 1 : 0);
        s.SetInt("uOutdoor", Outdoor ? 1 : 0);
        s.SetInt("uLocalLighting", Lighting is null ? 0 : 1);
        Lighting?.Apply(s);
        s.SetFloat("uAtmosphereDensity",Math.Clamp(AtmosphereDensity,0,1));
        s.SetFloat("uEnvironmentFill", environmentFill);
        s.SetMatrix4("uLightSpace", lightSpace);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .72f);
        s.SetInt("uShadowMap", 6);
        s.SetInt("uEmissionOnly", emissionOnly ? 2 : 0);
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
        DrawRaw();
        GL.ActiveTexture(GL.TEXTURE0);
    }

    private void DrawRaw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, _count);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _shader = null;
        _depth?.Dispose();
        _depth = null;
        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao);
            _vao = 0;
        }
    }

    private const string Wind = @"
vec3 foliageWind(vec3 p,vec3 local,int material,float time) {
    if(material==24) {
        float bend=max(0.,local.y);
        float gust=sin(p.x*.7+p.z*.45-time*1.25)+.35*sin(p.z*1.9-time*2.1);
        p.x+=bend*gust*.22;p.z+=bend*gust*.08;
    } else if(material==20) {
        float flutter=sin(p.x*3.1+p.z*2.2-time*1.4)*.003;
        p.x+=flutter;p.y+=flutter*.3;
    }
    return p;
}
";
    private const string DepthVertex = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=3) in vec3 aLocal;
layout(location=4) in float aMaterial;
uniform mat4 uModel,uLightSpace;
uniform float uTime;
" + Wind + @"
void main(){gl_Position=uLightSpace*uModel*vec4(foliageWind(aPos,aLocal,int(aMaterial+.5),uTime),1);}
";
    private const string Vertex = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec3 aColor;
layout(location=3) in vec3 aLocal;
layout(location=4) in float aMaterial;
uniform mat4 uView, uProj;
uniform vec3 uOffset;
out vec3 vWorldPos, vNormal, vColor, vLocal;
flat out int vMaterial;
uniform float uTime;
" + Wind + @"
void main() {
    vWorldPos=foliageWind(aPos,aLocal,int(aMaterial+.5),uTime)+uOffset; vNormal=aNormal; vColor=aColor; vLocal=aLocal;
    vMaterial=int(aMaterial+.5); gl_Position=uProj*uView*vec4(vWorldPos,1);
}";
    private const string Fragment = @"#version 330 core
in vec3 vWorldPos, vNormal, vColor, vLocal;
flat in int vMaterial;
uniform float uTime, uEnvironmentFill;
uniform int uEmissionOnly, uDetailedFinish, uOutdoor, uWorkshopFinish;
uniform float uAtmosphereDensity;
" + DefaultShaders.PbrLightUniforms + DefaultShaders.PbrFunctions + OutdoorAtmosphere.Glsl + @"
float hashSurface(vec3 p) { return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453); }
float noiseSurface(vec3 p) {
    vec3 i=floor(p), f=fract(p); f=f*f*(3.-2.*f);
    return mix(mix(mix(hashSurface(i),hashSurface(i+vec3(1,0,0)),f.x),
                   mix(hashSurface(i+vec3(0,1,0)),hashSurface(i+vec3(1,1,0)),f.x),f.y),
               mix(mix(hashSurface(i+vec3(0,0,1)),hashSurface(i+vec3(1,0,1)),f.x),
                   mix(hashSurface(i+vec3(0,1,1)),hashSurface(i+vec3(1,1,1)),f.x),f.y),f.z);
}
float fineStripe(float phase) { float fw=fwidth(phase); return sin(phase)*exp(-fw*fw*.3); }
// Screen-space surface-gradient bump: follows arbitrary rotated primitives
// without a tangent attribute; broad patterns affect colour, not the normal.
vec3 surfaceBump(vec3 n,float height,float strength) {
    vec3 sx=dFdx(vWorldPos), sy=dFdy(vWorldPos);
    vec3 r1=cross(sy,n),r2=cross(n,sx);
    float det=dot(sx,r1);
    if(abs(det)<1e-10)return n;
    vec3 grad=sign(det)*(dFdx(height)*r1+dFdy(height)*r2);
    return normalize(abs(det)*n-strength*grad);
}
" + OutdoorLighting.LocalLightingGlsl + WorkshopSurface.WorkshopLightingGlsl + @"
// Molten rock: dark cooling crust plates with glowing cracks between them; the glow is emission and is added after
// the lit result, so a lava lake lights its own crust at night and blooms in the glow pass.
float moltenCrack(vec3 p) {
    float cells=noiseSurface(p*3.1+vec3(0.,uTime*.035,0.))+.45*noiseSurface(p*7.3-vec3(uTime*.02,0.,0.));
    return 1.-smoothstep(.035,.13,abs(cells-.72));
}
vec3 moltenGlow(vec3 p) {
    float crack=moltenCrack(p);
    float pulse=.85+.15*sin(uTime*1.7+noiseSurface(p*2.)*6.2832);
    return mix(vec3(1.25,.30,.04),vec3(2.4,1.25,.32),crack*crack)*crack*pulse;
}
void main() {
    vec3 c=max(vColor,vec3(0)), n=normalize(vNormal), p=vLocal;
    // Black opaque occluders share the emission pass depth buffer. Light
    // behind an electrode or a filter must not bloom through it.
    if(uEmissionOnly==2) { FragColor=vec4(vMaterial==8?c*1.5:vMaterial==41?moltenGlow(p)*.9:vec3(0),1); return; }
    if(vMaterial==9) { FragColor=vec4(outdoorLightResult(c,c,n),1); return; }
    if(vMaterial==25) { FragColor=vec4(c,1); return; }
    if(vMaterial==8) { FragColor=vec4(c,1); return; }
    if(vMaterial==19 && uOutdoor==1) {
        vec3 sun=normalize(uLightDir);
        float shade=uShadowStrength>0.?mix(1.,shadowFactor(vWorldPos,max(0.,dot(n,sun))),uShadowStrength):1.;
        FragColor=vec4(outdoorLightResult(outdoorGroundWithHaze(vWorldPos,n,c,sun,uViewPos,uTime,shade,uAtmosphereDensity),c,n),1);return;
    }
    // Keep atmospheric water/vegetation and literal graph colours on their existing paths.
    if(uWorkshopFinish==1 && vMaterial<38 && vMaterial!=7 && vMaterial!=18 &&
       vMaterial!=19 && vMaterial!=20 && vMaterial!=21 && vMaterial!=23 && vMaterial!=24) {
        FragColor=shadeWorkshop(c,n,p,vMaterial);FragColor.rgb=outdoorLightResult(FragColor.rgb,c,n);return;
    }
    float rough=.36,metal=0.,spec=1.,height=0.,bump=.12;
    if(vMaterial==19) {
        c=fieldAlbedo(vWorldPos.xz,c);rough=.95;spec=.12;
        float soil=noiseSurface(vWorldPos*26.);
        height=soil*.008;bump=.26;
    } else if(vMaterial==20||vMaterial==24) {
        rough=.85;spec=.18;
        float veins=fineStripe(p.x*230.+p.z*17.);
        c*=.87+.13*noiseSurface(p*52.);height=veins*.001;bump=.12;
    } else if(vMaterial==21) {
        float groove=noiseSurface(vec3(p.x*85.,p.y*6.,p.z*85.));
        c*=.65+.55*groove;rough=.94;spec=.12;height=groove*.016;bump=.4;
    } else if(vMaterial==22) {
        rough=.12;metal=.58;spec=.6;
    } else if(vMaterial==23) {
        vec2 tile=abs(fract(p.xz/vec2(.8,.5))-.5);
        float seam=1.-smoothstep(.475,.493,max(tile.x,tile.y));
        c*=.72+.25*seam;rough=.78;spec=.25;
        height=seam*.0008+noiseSurface(p*100.)*.0001;bump=.12;
    } else if(vMaterial==17) {
        // Quiet concrete at room scale. Filter fine aggregate before it becomes pixel noise.
        float macro=noiseSurface(p*3.2);
        float footprint=length(fwidth(p*105.));
        float aggregate=mix(noiseSurface(p*105.),.5,smoothstep(.35,1.7,footprint));
        c*=.87+.13*macro+.065*aggregate;
        if(uDetailedFinish==1) {
            // Formwork pores and softly weathered lower wall, independent of camera.
            float pores=smoothstep(.69,.82,noiseSurface(p*24.));
            c*=1.-pores*.045;
        }
        rough=.79;spec=.32;height=(aggregate-.5)*.0008;bump=.14;
    } else if(vMaterial==18) {
        // Three nonparallel wave trains avoid the old rectangular ripple pattern.
        float a=dot(p.xz,vec2(2.1,1.3))-uTime*.42;
        float b=dot(p.xz,vec2(-1.2,3.7))-uTime*.63;
        float q=dot(p.xz,vec2(5.3,2.2))-uTime*.91;
        float rip=sin(a)*.55+sin(b+.4*sin(a))*.28+sin(q)*.12;
        height=rip*.018;rough=.21;bump=.28;
        vec3 v=normalize(uViewPos-vWorldPos);
        float fresnel=pow(1.-abs(dot(n,v)),4.);
        c=mix(c*.78,vec3(.40,.62,.73),fresnel*.55);
        c+=vec3(.008,.018,.022)*(rip+1.);
    } else if(vMaterial==15) {
        // Filtered tool marks in the part's own frame: circular facing on gear
        // webs and a finer axial finish. The pattern rotates with the part.
        float rings=fineStripe(length(p.xy)*720.);
        float brush=fineStripe(p.z*520.);
        height=(rings+brush*.35)*.00045;bump=.16;
        rough=.25+.025*rings;metal=.94;c*=.96+.025*rings;
    } else if(vMaterial==16) {
        float castGrain=noiseSurface(p*125.);
        height=castGrain*.002;bump=.22;rough=.57;metal=.38;
        c*=.91+.10*castGrain;
    } else if(vMaterial==2||vMaterial==3||vMaterial==10) {
        float brush=fineStripe(p.y*460.+sin(p.x*11.)*.6);
        rough=vMaterial==2?.27:.23;metal=.90;
        height=brush*.0012; c*=.93+.055*brush;
    } else if(vMaterial==4) {
        float grain=noiseSurface(vec3(p.x*1.3,p.y*8.,p.z*10.));
        float pores=fineStripe(p.z*170.+grain*5.);
        float seam=smoothstep(.01,.025,abs(fract(p.z/.42)-.5));
        c*=.78+.24*grain+.035*pores; c*=.91+.09*seam;
        rough=.68;spec=.38;height=pores*.0004; bump=.10;
    } else if(vMaterial==5) {
        rough=.91;spec=.25;c*=.94+.06*noiseSurface(p*85.);
    } else if(vMaterial==6) {
        rough=.20; height=noiseSurface(p*90.)*.0008;bump=.1;
    } else if(vMaterial==7) {
        float ripple=sin(p.x*7.+p.z*3.-uTime*1.1)*sin(p.z*11.-uTime*.8);
        height=ripple*.012;rough=.16;bump=.25;
        vec3 v=normalize(uViewPos-vWorldPos);
        c=mix(c*.70,vec3(.43,.68,.79),pow(1.-abs(dot(n,v)),4.)*.48);
        c+=vec3(.015,.032,.045)*(ripple+1.);
    } else if(vMaterial==12) {
        float wrinkle=noiseSurface(p*16.)+.25*noiseSurface(p*35.);
        height=wrinkle*.008;rough=.29;metal=.92;bump=.35;
        c*=.90+.16*wrinkle;
    } else if(vMaterial==13||vMaterial==14) {
        rough=.08;metal=.98;spec=1.2;
    } else if(vMaterial==11) {
        float fleck=noiseSurface(p*42.);c*=.80+.24*fleck;rough=.83;spec=.3;height=fleck*.003;
    } else if(vMaterial==26) {
        // moulded polymer: a faint mould grain, no metal
        rough=.42;spec=.55;height=noiseSurface(p*140.)*.0003;bump=.1;
    } else if(vMaterial==27) {
        // plain weave at thread scale plus the cloth's own slack wrinkles
        vec2 uv=workshopPlane(p,n)*620.;
        float warp=fineStripe(uv.x),weft=fineStripe(uv.y);
        float wrinkle=noiseSurface(p*5.)+.5*noiseSurface(p*13.);
        c*=.88+.06*warp+.06*weft+.08*wrinkle;rough=.9;spec=.16;
        height=(warp+weft)*.00035+wrinkle*.006;bump=.22;
    } else if(vMaterial==28) {
        // wet slip with throwing rings
        float rings=fineStripe(p.y*900.+noiseSurface(p*30.)*2.);
        c*=.92+.06*noiseSurface(p*7.)+.03*rings;rough=.26;spec=.95;height=rings*.0006;bump=.18;
    } else if(vMaterial==29) {
        // stretcher bond with recessed mortar and chipped faces
        vec2 cell=workshopPlane(p,n)/vec2(.24,.075);
        cell.x+=mod(floor(cell.y),2.)*.5;
        vec2 edge=min(fract(cell),1.-fract(cell))*vec2(.24,.075);
        float mortar=smoothstep(.004,.008,min(edge.x,edge.y));
        float brick=hashSurface(vec3(floor(cell),7.));
        float chip=noiseSurface(p*55.);
        c=mix(vec3(.36,.33,.29),c*(.78+.32*brick)*(.9+.14*chip),mortar);
        rough=.9;spec=.22;height=(mortar-1.)*.006+chip*.0012;bump=.35;
    } else if(vMaterial==30) {
        // trowelled plaster: broad undulation and fine sand
        float broad=noiseSurface(p*2.3),sand=noiseSurface(p*150.);
        c*=.92+.08*broad+.03*sand;rough=.9;spec=.2;height=broad*.004+sand*.0004;bump=.2;
    } else if(vMaterial==31) {
        float fibre=noiseSurface(vec3(p.x*400.,p.y*400.,p.z*90.));
        c*=.96+.04*fibre;rough=.86;spec=.22;height=fibre*.00015;bump=.08;
    } else if(vMaterial==32) {
        vec3 v=normalize(uViewPos-vWorldPos);float angle=1.-abs(dot(n,v));
        c*=mix(vec3(.72,.84,1.),vec3(1.,.66,.45),angle);rough=.1;metal=.75;
    } else if(vMaterial==33) {
        float grain=noiseSurface(vec3(p.x*1.3,p.y*8.,p.z*10.));
        float annual=fineStripe(p.z*95.+grain*9.+sin(p.x*2.)*2.);
        c*=.78+.26*grain+.07*annual;rough=.24;spec=1.05;height=annual*.0003;bump=.1;
    } else if(vMaterial==34) {
        float brush=fineStripe(p.y*900.+sin(p.x*13.)*.5);
        rough=.3;metal=.95;height=brush*.0006;bump=.14;c*=.95+.04*brush;
    } else if(vMaterial==35) {
        // glaze: glassy with a slow waviness that bends the reflections
        rough=.1;spec=1.2;height=noiseSurface(p*9.)*.0015;bump=.2;
    } else if(vMaterial==36) {
        float vein=fineStripe(p.y*160.+noiseSurface(p*20.)*3.);
        c*=.82+.2*noiseSurface(p*30.)+.05*vein;rough=.92;spec=.1;height=vein*.0005;bump=.15;
    } else if(vMaterial==37) {
        rough=.45;spec=.45;height=noiseSurface(p*40.)*.0006;bump=.12;
    } else if(vMaterial==38) {
        // rust: oxide blooms over dark steel, pitted where the oxide is thickest
        float bloom=smoothstep(.38,.66,noiseSurface(p*8.5)+.35*noiseSurface(p*31.)-.12);
        float flake=noiseSurface(p*95.),pit=smoothstep(.62,.8,noiseSurface(p*170.));
        vec3 oxide=mix(vec3(.30,.13,.055),vec3(.55,.27,.10),flake);
        c=mix(c*.55,oxide,bloom);
        rough=mix(.42,.93,bloom);metal=mix(.85,.04,bloom);spec=mix(1.,.35,bloom);
        height=bloom*flake*.0018-pit*bloom*.0012;bump=.42;
    } else if(vMaterial==39) {
        // sand: wind ripples across local x, grains below a pixel fade to their mean
        float wobble=noiseSurface(p*2.2)*5.;
        float ripple=sin(p.x*52.+wobble);ripple=ripple*.5+.5;ripple=ripple*ripple;
        float footprint=length(fwidth(p*700.));
        float grain=mix(noiseSurface(p*700.),.5,smoothstep(.4,1.6,footprint));
        c*=.9+.08*noiseSurface(p*3.)+.07*grain+.04*ripple;
        rough=.96;spec=.1;height=ripple*.004+grain*.0002;bump=.3;
    } else if(vMaterial==40) {
        // glacier ice: glassy, blue in its depth, crazed by fractures
        float fracture=1.-smoothstep(0.,.035,abs(noiseSurface(p*5.5)-.5));
        float bubbles=smoothstep(.8,.9,noiseSurface(p*60.));
        c=mix(c,vec3(.62,.80,.92),.35)*(1.-.22*fracture)+bubbles*.05;
        rough=.07;spec=1.25;height=-fracture*.002+noiseSurface(p*18.)*.0012;bump=.3;
    } else if(vMaterial==41) {
        // molten rock: the lit part is the crust; the cracks glow after lighting
        float crack=moltenCrack(p);
        c=mix(c*.18,vec3(.08,.03,.02),crack);rough=.82;spec=.3;
        height=(1.-crack)*noiseSurface(p*14.)*.006-crack*.004;bump=.45;
    } else if(vMaterial==42) {
        // bedded rock: strata along local y, joints, weathered faces
        float band=noiseSurface(vec3(p.x*.55,p.y*6.5,p.z*.55));
        float joint=1.-smoothstep(0.,.035,abs(noiseSurface(p*2.7)-.5));
        float face=noiseSurface(p*11.),grit=noiseSurface(p*58.);
        c*=(.7+.42*band)*(1.-.35*joint)*(.9+.14*face);
        rough=.9;spec=.25;height=band*.018+face*.008+grit*.0015-joint*.02;bump=.55;
    } else if(vMaterial==43) {
        // snow: wind-smoothed drifts, faint crust
        float drift=noiseSurface(p*3.4)+.4*noiseSurface(p*11.);
        c=mix(c,vec3(.92,.95,1.),.82)*(.94+.06*drift);
        rough=.72;spec=.55;height=drift*.008+noiseSurface(p*60.)*.0008;bump=.25;
    } else if(vMaterial==44) {
        // bronze under verdigris: green crust in blooms, bare metal on the high spots
        float verd=smoothstep(.42,.64,noiseSurface(p*7.)+.3*noiseSurface(p*29.)-.08);
        float crust=noiseSurface(p*88.);
        c=mix(c,mix(vec3(.24,.46,.38),vec3(.44,.66,.55),crust),verd);
        rough=mix(.33,.86,verd);metal=mix(.88,0.,verd);spec=mix(1.,.3,verd);
        height=verd*crust*.0014;bump=.3;
    } else { rough=.35; height=noiseSurface(p*80.)*.0006; }
    if(uEnvironmentFill>0. && !gl_FrontFacing)n=-n;
    if(uDetailedFinish==1 && vMaterial==1) {
        float peel=noiseSurface(p*210.);height=peel*.0012;bump=.2;rough=.30;
    }
    n=surfaceBump(n,height,bump);
    FragColor=shadePbr(vWorldPos,n,c,rough,metal,spec);
    if(vMaterial==18 && uDetailedFinish==1) {
        // Fresnel reflection of the SAME sky as above the scene, with analytic
        // capillary slopes. Macro elevation remains the module's computed mesh.
        vec2 x=p.xz;
        float a=dot(x,vec2(3.1,1.7))-uTime*.75;
        float b=dot(x,vec2(-2.3,5.1))-uTime*1.1;
        float q=dot(x,vec2(9.7,4.2))-uTime*1.6;
        vec2 slope=cos(a)*vec2(.075,.041)+cos(b)*vec2(-.031,.069)+cos(q)*vec2(.025,.011);
        n=normalize(vNormal+vec3(-slope.x,0,-slope.y)*.42);
        vec3 v=normalize(uViewPos-vWorldPos),r=reflect(-v,n);
        float fres=.035+.965*pow(1.-max(0.,dot(n,v)),5.);
        // Rough-water reflection is angularly filtered. Sharp cloud detail on
        // capillary normals creates false bright rings and horizon shimmer.
        vec3 sky=outdoorSky(r,vWorldPos,normalize(uLightDir),uTime)*.34;
        sky+=outdoorSky(normalize(r+vec3(.11,.08,0)),vWorldPos,normalize(uLightDir),uTime)*.33;
        sky+=outdoorSky(normalize(r+vec3(-.11,.08,0)),vWorldPos,normalize(uLightDir),uTime)*.33;
        float glint=pow(max(0.,dot(n,normalize(v+normalize(uLightDir)))),220.);
        vec3 deep=mix(vec3(.025,.16,.20),c*.70,.46);
        FragColor.rgb=mix(deep,sky,clamp(fres*.70,.08,.70))+vec3(1.,.93,.76)*glint*.65;
        FragColor.rgb*=mix(1.,cloudShade(vWorldPos,normalize(uLightDir),uTime),uAtmosphereDensity);
        return;
    }
    if(vMaterial==22) {
        vec3 r=reflect(-normalize(uViewPos-vWorldPos),n);
        vec3 sky=uOutdoor==1?outdoorSky(r,vWorldPos,normalize(uLightDir),uTime)*uAtmosphereDensity:vec3(.38,.52,.60);
        FragColor.rgb=outdoorLightResult(mix(c*.4,sky,.62),c,n);return;
    }
    if(vMaterial==20||vMaterial==24) {
        float transmission=pow(max(0.,dot(-normalize(uViewPos-vWorldPos),normalize(uLightDir))),3.);
        FragColor.rgb+=c*(.20+.27*transmission);
    }
    if(uOutdoor==1)FragColor.rgb*=mix(1.,cloudShade(vWorldPos,normalize(uLightDir),uTime),uAtmosphereDensity);
    // Optional broad cool/warm area-light reflection for isolated instruments.
    // Default zero preserves the existing lab scenes and their lighting.
    vec3 reflected=reflect(-normalize(uViewPos-vWorldPos),n);
    float blue=pow(max(0.,dot(reflected,normalize(vec3(.8,.5,.4)))),8.);
    float warm=pow(max(0.,dot(reflected,normalize(vec3(-.6,.7,.2)))),13.);
    if(vMaterial==12||vMaterial==13) {
        float horizon=smoothstep(-.2,.6,reflected.y);
        float strip=pow(max(0.,dot(reflected,normalize(vec3(-.2,.6,.9)))),7.);
        FragColor.rgb+=c*(.13+.23*horizon)+c*strip*.70;
    }
    if(vMaterial==14) {
        // Broad sunshield / cold-sky reflections give the curved optical
        // surface a readable shape without painting grime onto a mirror.
        float horizon=smoothstep(-.13,.32,reflected.y+.18*sin(reflected.x*8.));
        float band=exp(-pow((reflected.y-.15*sin(reflected.x*5.))/.13,2.));
        vec3 sky=mix(vec3(.06,.10,.19),vec3(.83,.91,1.1),horizon);
        FragColor.rgb=c*(sky*.83+vec3(.56,.44,.25)*band)+FragColor.rgb*.45;
    }
    if(vMaterial==15 || (uDetailedFinish==1 && metal>.35)) {
        // Broad neutral studio panels make curved metal readable between the
        // small direct highlights. Tint the reflection by the metal itself.
        float ceiling=smoothstep(-.45,.75,reflected.y);
        float softbox=exp(-pow((reflected.x+.30)/.52,2.))
            *smoothstep(-.65,.25,reflected.z);
        vec3 studio=mix(vec3(.14,.17,.22),vec3(.52,.59,.67),ceiling);
        FragColor.rgb+=c*(studio*.68+softbox*.34);
    }
    FragColor.rgb+=uEnvironmentFill*(c*(.05+.10*max(n.y,0.))+
        metal*(vec3(.18,.30,.48)*blue+vec3(.46,.35,.22)*warm));
    if(vMaterial==40) {
        // light scattered back out of the ice body: a cold blue that does not depend on the key light
        vec3 v=normalize(uViewPos-vWorldPos);
        FragColor.rgb+=vec3(.03,.11,.17)*(.6+.4*(1.-abs(dot(n,v))));
    }
    if(vMaterial==43 || vMaterial==39) {
        // sparse sun glints on snow crystals and quartz grains; filtered so they do not crawl
        vec3 cell=floor(vLocal*(vMaterial==43?420.:650.));
        float sparkle=step(vMaterial==43?.992:.997,hashSurface(cell));
        float facing=pow(max(0.,dot(reflect(-normalize(uLightDir),n),normalize(uViewPos-vWorldPos))),6.);
        float footprint=length(fwidth(vLocal*420.));
        FragColor.rgb+=uLightColor*sparkle*facing*(1.-smoothstep(.6,1.6,footprint))*(vMaterial==43?.9:.5);
    }
    FragColor.rgb=outdoorLightResult(FragColor.rgb,c,n);
    if(vMaterial==41)FragColor.rgb+=moltenGlow(p);
}";
}
