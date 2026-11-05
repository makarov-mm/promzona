namespace MakarovPhysics.Renderer;

/// <summary>
/// The reusable glow pipeline for "luminous" scenes (Chladni embers, Boids
/// murmurations, N-body star fields, chaos attractors):
///
///   BeginScene(fade) → draw additive HDR content → EndScene → Composite
///
/// BeginScene binds an RGBA16F offscreen buffer. With fade &lt; 1 the previous
/// frame is first multiplied by that factor instead of being cleared - that
/// IS the motion-trail accumulator (bright paths linger and decay). Composite
/// extracts pixels above BloomThreshold into a half-resolution ping-pong
/// gaussian blur, then draws scene + bloom·strength through an ACES-style
/// tonemap ADDITIVELY over whatever is already on screen, so the module's
/// normal background stays underneath the glow.
///
/// Both scene targets carry a depth buffer (the multisampled one since
/// 12.09, the single-sampled fallback since 19.10), cleared at BeginScene:
/// depth-tested geometry inside the pass is tested against ITSELF only, never
/// against what the module drew to the screen before. Glow content is
/// additive and orderless, so for the glow recipe that is no restriction;
/// the Opaque recipe repeats its solids depth-only into the backbuffer after
/// the composite, so later content can hide behind them.
/// </summary>
public sealed class PostFxStack : IDisposable
{
    public float BloomStrength = 0.9f;

    /// <summary>
    /// Internal render scale of the stack, 0.25..1 (default 1). At 1 the
    /// scene target matches the viewport - on a 5K monitor that is a
    /// ~350 MB multisampled RGBA16F cleared, resolved and blurred EVERY
    /// frame, and on a light module the stack becomes the frame's main
    /// cost (Induction, 17.08). A module whose stack carries ONLY glow -
    /// the contract pattern, solids in the backbuffer - can run at 0.5:
    /// four times fewer pixels through every stage, and half-resolution
    /// softness flatters additive light. Set before the first BeginScene;
    /// changing it later just triggers one transactional resize.
    /// </summary>
    public float SceneScale = 1f;
    public float BloomThreshold = 0.55f;
    public float Exposure = 1.0f;

    /// <summary>The most any pixel may put into the bloom, in scene units,
    /// after the threshold (15.09, screenshot audit: FractalTree, Galaxy,
    /// L-systems and the Schwarz lantern were washed out to white). Additive
    /// scenes with thousands of overlapping strokes reach HDR values of ten
    /// and more; an unbounded extraction bloomed them into a fog that ACES
    /// then saturated. Capped, a bright region blooms as much as a bright
    /// region and no more. The threshold is also taken in OUTPUT space -
    /// divided by the exposure - so raising the exposure does not lower the
    /// bar for what counts as a highlight.</summary>
    public float BloomCap = 2.5f;

    /// <summary>
    /// Composite the GLOW ALONE, without the scene it was extracted from.
    ///
    /// The default composite is ACES(scene + bloom) added over the backbuffer,
    /// which is right when the stack holds content that is nowhere else - the
    /// contract pattern, glow in the stack and solids on screen. A module that
    /// has ALREADY drawn its picture to the backbuffer and sends the same
    /// picture through the stack only to find its highlights gets that picture
    /// added to itself: Dendrite did, and its ice crystal came out as white
    /// squares (screenshot review 21.09 - "lifts the crystal's own highlights"
    /// was the intent, doubling the crystal was the effect). With GlowOnly the
    /// scene term is dropped and only what bloomed is added.
    /// </summary>
    public bool GlowOnly;

    /// <summary>0 = off. Darkens the composited glow toward the frame corners
    /// (the Metal-reference "focus the eye at the centre" touch). Only the glow
    /// layer is vignetted - the module's own background stays untouched.</summary>
    public float Vignette = 0f;

    /// <summary>
    /// The OPAQUE recipe: the stack holds a picture that REPLACES what is on
    /// screen where it was drawn and leaves the rest alone (19.10, the
    /// astronomy bodies: planets and stars in linear HDR, bloom on the Sun,
    /// composited over the star dome without the dome shining through them).
    /// BeginScene then clears alpha to ZERO, every opaque draw writes alpha 1,
    /// the multisample resolve leaves fractional alpha on silhouettes, and the
    /// composite blends (ONE, ONE_MINUS_SRC_ALPHA): on a body the tonemapped
    /// scene replaces the screen, outside it only the bloom is added, and the
    /// edge takes the resolved coverage - antialiased against the dome.
    ///
    /// The curve is not ACES. The body shaders used to tonemap themselves with
    /// 1 - exp(-x·exposure) and encode with gamma 2.2 before this path existed;
    /// the composite applies exactly that pair, so a planet looks as it did,
    /// only now the Sun has a halo and the maps are decoded once.
    /// </summary>
    public bool Opaque;

    /// <summary>Lower-left corner of the viewport the composite is drawn into,
    /// in physical pixels (default 0,0). A module whose scene viewport does not
    /// start at the window origin - a side-by-side pair, a panel inset - sets
    /// these from GL.PhysicalViewport() so the picture lands where it was drawn.</summary>
    public int ViewportX, ViewportY;

    private int _width, _height;
    private int _viewportWidth, _viewportHeight;
    private uint _sceneFbo, _sceneTexture, _sceneDepth;
    private readonly uint[] _blurFbo = new uint[2];
    private readonly uint[] _blurTexture = new uint[2];

    private Shader? _fadeShader;      // multiplies the kept frame (trails)
    private Shader? _extractShader;   // threshold + downsample into blur[0]
    private Shader? _blurShader;      // separable gaussian, ping-pong
    private Shader? _compositeShader; // ACES(scene + bloom), additive to screen
    private uint _vao, _vbo;          // fullscreen triangle

    public PostFxStack()
    {
        // if the 3rd shader (or the VAO/VBO) fails, the ones already created
        // must not leak: the constructor throws, so no caller ever gets to
        // Dispose this instance - roll back with the (idempotent) Dispose
        try
        {
            _fadeShader = new Shader(FullscreenVertex, FadeFragment);
            _extractShader = new Shader(FullscreenVertex, ExtractFragment);
            _blurShader = new Shader(FullscreenVertex, BlurFragment);
            _compositeShader = new Shader(FullscreenVertex, CompositeFragment);

            float[] triangle = { -1f, -1f, 3f, -1f, -1f, 3f };
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, triangle, GL.STATIC_DRAW);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // The multisampled scene target: colour + depth renderbuffers, resolved
    // into _sceneTexture at EndScene. Zero when MSAA is unavailable - then
    // _sceneFbo is drawn into directly, exactly the old path.
    private uint _msaaFbo, _msaaColor, _msaaDepth;
    private int _msaaSamples;

    /// <summary>Samples of the scene target, 0 when running single-sampled -
    /// the log line in the host prints it next to the window's own MSAA.</summary>
    public int SceneMsaaSamples => _msaaSamples;

    private static uint MakeColorTexture(int width, int height)
    {
        uint texture = GL.GenTexture();
        GL.BindTexture(GL.TEXTURE_2D, texture);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
        GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA16F, width, height, 0, GL.RGBA, GL.FLOAT);
        return texture;
    }

    private static uint MakeFbo(uint colorTexture, uint depthRenderbuffer = 0)
    {
        uint framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(GL.FRAMEBUFFER, framebuffer);
        GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, colorTexture, 0);
        if (depthRenderbuffer != 0)
            GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.RENDERBUFFER, depthRenderbuffer);
        bool complete = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);

        if (!complete)
        {
            GL.DeleteFramebuffer(framebuffer);   // an incomplete FBO must not leak
            throw new InvalidOperationException("PostFxStack: framebuffer incomplete.");
        }

        return framebuffer;
    }

    private void EnsureSize(int width, int height)
    {
        float scale = Math.Clamp(SceneScale, 0.25f, 1f);
        int targetW = Math.Max(16, (int)(width * scale));
        int targetH = Math.Max(16, (int)(height * scale));
        if (targetW == _width && targetH == _height) return;
        int newWidth = targetW;
        int newHeight = targetH;

        // Transactional resize: build the complete new target set FIRST, into
        // locals. If any allocation fails, delete only what this attempt made -
        // the old, still-working targets survive and the stack keeps rendering
        // at the previous size instead of dying half-replaced.
        uint sceneTexture = 0, sceneFbo = 0, sceneDepth = 0;
        Span<uint> blurTexture = stackalloc uint[2];
        Span<uint> blurFbo = stackalloc uint[2];
        blurTexture.Clear();   // stack memory is not guaranteed zeroed - the
        blurFbo.Clear();       // catch below must never delete a random GL id
        uint msaaFbo = 0, msaaColor = 0, msaaDepth = 0;
        int msaaSamples;

        try
        {
            sceneTexture = MakeColorTexture(newWidth, newHeight);
            // The single-sampled target carries a depth buffer too (19.10):
            // without one, GL treats the depth test as always-pass, and on a
            // driver that gives no samples two planets would overlap by draw
            // order. Costs nothing on the multisampled path, which resolves
            // colour only.
            sceneDepth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(GL.RENDERBUFFER, sceneDepth);
            GL.RenderbufferStorage(GL.RENDERBUFFER, GL.DEPTH_COMPONENT24, newWidth, newHeight);
            GL.BindRenderbuffer(GL.RENDERBUFFER, 0);
            sceneFbo = MakeFbo(sceneTexture, sceneDepth);

            for (int i = 0; i < 2; i++)
            {
                blurTexture[i] = MakeColorTexture(newWidth / 2, newHeight / 2);
                blurFbo[i] = MakeFbo(blurTexture[i]);
            }

            // The multisampled scene, WITH a depth buffer. Two sins of the
            // first build die here at once: every glow module rendered its
            // whole scene into a plain colour texture, so (a) the window's
            // MSAA never touched it - the staircase Michael saw on every
            // bloomed module - and (b) there was NO depth attachment at all:
            // GL treats depth test against a missing buffer as always-pass,
            // so those scenes were living on draw order and did not know it.
            (msaaFbo, msaaColor, msaaDepth, msaaSamples) = TryMakeMsaaTarget(newWidth, newHeight);
        }
        catch
        {
            if (sceneFbo != 0) GL.DeleteFramebuffer(sceneFbo);
            if (sceneTexture != 0) GL.DeleteTexture(sceneTexture);
            if (sceneDepth != 0) GL.DeleteRenderbuffer(sceneDepth);

            for (int i = 0; i < 2; i++)
            {
                if (blurFbo[i] != 0) GL.DeleteFramebuffer(blurFbo[i]);
                if (blurTexture[i] != 0) GL.DeleteTexture(blurTexture[i]);
            }

            if (msaaFbo != 0) GL.DeleteFramebuffer(msaaFbo);
            if (msaaColor != 0) GL.DeleteRenderbuffer(msaaColor);
            if (msaaDepth != 0) GL.DeleteRenderbuffer(msaaDepth);
            throw;
        }

        // full success - now atomically swap ownership and drop the old set
        ReleaseTargets();
        _width = newWidth;
        _height = newHeight;
        _sceneTexture = sceneTexture;
        _sceneFbo = sceneFbo;
        _sceneDepth = sceneDepth;
        _msaaFbo = msaaFbo;
        _msaaColor = msaaColor;
        _msaaDepth = msaaDepth;
        _msaaSamples = msaaSamples;

        for (int i = 0; i < 2; i++)
        {
            _blurTexture[i] = blurTexture[i];
            _blurFbo[i] = blurFbo[i];
        }

        // fresh buffers start black (no stale trails from an old size); the
        // multisampled one too - the fade path multiplies whatever is there,
        // and renderbuffer memory arrives undefined
        GL.BindFramebuffer(GL.FRAMEBUFFER, _sceneFbo);
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);

        if (_msaaFbo != 0)
        {
            GL.BindFramebuffer(GL.FRAMEBUFFER, _msaaFbo);
            GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
        }

        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
    }

    /// <summary>
    /// A multisampled colour+depth pair for the scene, or all zeros when the
    /// driver has no samples to give. Samples are capped at 4: past that the
    /// cost grows and the staircase does not shrink visibly.
    /// </summary>
    private static (uint fbo, uint color, uint depth, int samples) TryMakeMsaaTarget(int w, int h)
    {
        int samples = Math.Min(4, GL.GetInteger(GL.MAX_SAMPLES));
        if (samples < 2) return (0, 0, 0, 0);
        uint fbo = 0, color = 0, depth = 0;

        try
        {
            color = GL.GenRenderbuffer();
            GL.BindRenderbuffer(GL.RENDERBUFFER, color);
            GL.RenderbufferStorageMultisample(GL.RENDERBUFFER, samples, GL.RGBA16F, w, h);

            depth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(GL.RENDERBUFFER, depth);
            GL.RenderbufferStorageMultisample(GL.RENDERBUFFER, samples, GL.DEPTH_COMPONENT24, w, h);
            GL.BindRenderbuffer(GL.RENDERBUFFER, 0);

            fbo = GL.GenFramebuffer();
            GL.BindFramebuffer(GL.FRAMEBUFFER, fbo);
            GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.RENDERBUFFER, color);
            GL.FramebufferRenderbuffer(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.RENDERBUFFER, depth);
            bool complete = GL.CheckFramebufferStatus(GL.FRAMEBUFFER) == GL.FRAMEBUFFER_COMPLETE;
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);

            if (!complete)
            {
                GL.DeleteFramebuffer(fbo);
                GL.DeleteRenderbuffer(color);
                GL.DeleteRenderbuffer(depth);
                return (0, 0, 0, 0); // quietly run single-sampled
            }

            return (fbo, color, depth, samples);
        }
        catch
        {
            if (fbo != 0) GL.DeleteFramebuffer(fbo);
            if (color != 0) GL.DeleteRenderbuffer(color);
            if (depth != 0) GL.DeleteRenderbuffer(depth);
            return (0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Bind the HDR buffer. trailFade = 0 clears it (no trails); 0...1 keeps the
    /// previous frame multiplied by the factor - the glow trail accumulator.
    /// With MSAA the previous frame survives in the multisampled buffer itself
    /// (it is never cleared on the fade path), so the accumulator works
    /// unchanged; only the resolve at EndScene is new.
    /// </summary>
    public void BeginScene(int viewportWidth, int viewportHeight, float trailFade = 0f)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        EnsureSize(viewportWidth, viewportHeight);

        GL.BindFramebuffer(GL.FRAMEBUFFER, _msaaFbo != 0 ? _msaaFbo : _sceneFbo);
        GL.Viewport(0, 0, _width, _height);

        if (trailFade <= 0f)
        {
            GL.ClearColor(0f, 0f, 0f, Opaque ? 0f : 1f);
            GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
        }
        else
        {
            // multiply the kept frame: dst · fade (ZERO/SRC_COLOR blending);
            // depth still clears - trails are colour memory, not geometry
            GL.Clear(GL.DEPTH_BUFFER_BIT);
            GL.Disable(GL.DEPTH_TEST);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.ZERO, GL.SRC_COLOR);
            _fadeShader!.Use();
            _fadeShader.SetFloat("uFade", Math.Min(trailFade, 0.999f));
            DrawTriangle();
            GL.Disable(GL.BLEND);
        }
    }

    /// <summary>
    /// Resolve the multisampled scene into the texture the bloom chain reads,
    /// then back to the default framebuffer and the real viewport.
    /// </summary>
    public void EndScene()
    {
        if (_msaaFbo != 0)
        {
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, _msaaFbo);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, _sceneFbo);
            GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, GL.COLOR_BUFFER_BIT, GL.NEAREST);
        }

        GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        GL.Viewport(ViewportX, ViewportY, _viewportWidth, _viewportHeight);
    }

    /// <summary>Blur the bright parts and draw glow + scene onto the screen.</summary>
    public void Composite() => CompositeTo(0);

    /// <summary>Composite into a caller-owned scene target (screenshots,
    /// supersampling and embedded previews need not use framebuffer zero).</summary>
    public void CompositeTo(uint framebuffer)
    {
        if (_compositeShader is null) return;

        // 1) threshold + downsample into blur[0]
        GL.BindFramebuffer(GL.FRAMEBUFFER, _blurFbo[0]);
        GL.Viewport(0, 0, _width / 2, _height / 2);
        _extractShader!.Use();
        _extractShader.SetInt("uScene", 0);
        _extractShader.SetFloat("uThreshold", BloomThreshold / MathF.Max(0.05f, Exposure));
        _extractShader.SetFloat("uCap", BloomCap);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _sceneTexture);
        DrawTriangle();

        // 2) two separable gaussian passes, ping-pong at half resolution
        for (int pass = 0; pass < 2; pass++)
        {
            GL.BindFramebuffer(GL.FRAMEBUFFER, _blurFbo[1 - pass % 2]);
            _blurShader!.Use();
            _blurShader.SetInt("uSource", 0);
            _blurShader.SetVec2("uDirection",
                pass % 2 == 0 ? 2f / (_width / 2f) : 0f,
                pass % 2 == 0 ? 0f : 2f / (_height / 2f));
            GL.BindTexture(GL.TEXTURE_2D, _blurTexture[pass % 2]);
            DrawTriangle();
        }

        // 3) composite over whatever the module already drew: additive for
        //    glow, premultiplied-over for an opaque picture (see Opaque)
        GL.BindFramebuffer(GL.FRAMEBUFFER, framebuffer);
        GL.Viewport(ViewportX, ViewportY, _viewportWidth, _viewportHeight);
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, Opaque ? GL.ONE_MINUS_SRC_ALPHA : GL.ONE);
        _compositeShader.Use();
        _compositeShader.SetInt("uScene", 0);
        _compositeShader.SetInt("uBloom", 1);
        _compositeShader.SetFloat("uBloomStrength", BloomStrength);
        _compositeShader.SetFloat("uSceneWeight", GlowOnly ? 0f : 1f);
        _compositeShader.SetFloat("uExposure", Exposure);
        _compositeShader.SetFloat("uVignette", Vignette);
        _compositeShader.SetFloat("uOpaque", Opaque ? 1f : 0f);
        GL.ActiveTexture(GL.TEXTURE0);
        GL.BindTexture(GL.TEXTURE_2D, _sceneTexture);
        GL.ActiveTexture(GL.TEXTURE1);
        GL.BindTexture(GL.TEXTURE_2D, _blurTexture[0]);
        GL.ActiveTexture(GL.TEXTURE0);
        DrawTriangle();
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
    }

    private void DrawTriangle()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, 3);
        GL.BindVertexArray(0);
    }

    private void ReleaseTargets()
    {
        if (_sceneFbo != 0)
        {
            GL.DeleteFramebuffer(_sceneFbo); 
            _sceneFbo = 0;
        }

        if (_sceneTexture != 0)
        {
            GL.DeleteTexture(_sceneTexture);
            _sceneTexture = 0;
        }

        if (_sceneDepth != 0)
        {
            GL.DeleteRenderbuffer(_sceneDepth);
            _sceneDepth = 0;
        }

        if (_msaaFbo != 0)
        {
            GL.DeleteFramebuffer(_msaaFbo);
            _msaaFbo = 0;
        }

        if (_msaaColor != 0)
        {
            GL.DeleteRenderbuffer(_msaaColor);
            _msaaColor = 0;
        }

        if (_msaaDepth != 0)
        {
            GL.DeleteRenderbuffer(_msaaDepth);
            _msaaDepth = 0;
        }

        _msaaSamples = 0;

        for (int i = 0; i < 2; i++)
        {
            if (_blurFbo[i] != 0)
            {
                GL.DeleteFramebuffer(_blurFbo[i]);
                _blurFbo[i] = 0;
            }

            if (_blurTexture[i] != 0)
            {
                GL.DeleteTexture(_blurTexture[i]);
                _blurTexture[i] = 0;
            }
        }
    }

    public void Dispose()
    {
        ReleaseTargets();
        _fadeShader?.Dispose(); 
        _fadeShader = null;

        _extractShader?.Dispose();
        _extractShader = null;

        _blurShader?.Dispose();
        _blurShader = null;

        _compositeShader?.Dispose(); 
        _compositeShader = null;

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

    private const string FullscreenVertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

    private const string FadeFragment = @"#version 330 core
uniform float uFade;
out vec4 FragColor;
void main() { FragColor = vec4(uFade, uFade, uFade, 1.0); }";

    private const string ExtractFragment = @"#version 330 core
in vec2 vUv;
uniform sampler2D uScene;
uniform float uThreshold;
uniform float uCap;
out vec4 FragColor;
void main()
{
    vec3 c = texture(uScene, vUv).rgb;
    // a soft knee above the threshold, capped: x / (1 + x / cap) rises like
    // x near the bar and levels off at cap, so a stroke drawn a thousand
    // times over does not bloom a thousand times brighter
    vec3 x = max(c - vec3(uThreshold), vec3(0.0));
    FragColor = vec4(x / (vec3(1.0) + x / uCap), 1.0);
}";

    private const string BlurFragment = @"#version 330 core
in vec2 vUv;
uniform sampler2D uSource;
uniform vec2 uDirection;
out vec4 FragColor;
void main()
{
    // 9-tap gaussian, weights sum to 1
    float w[5] = float[](0.227027, 0.194594, 0.121621, 0.054054, 0.016216);
    vec3 sum = texture(uSource, vUv).rgb * w[0];
    for (int i = 1; i < 5; i++)
    {
        sum += texture(uSource, vUv + uDirection * float(i)).rgb * w[i];
        sum += texture(uSource, vUv - uDirection * float(i)).rgb * w[i];
    }
    FragColor = vec4(sum, 1.0);
}";

    private const string CompositeFragment = @"#version 330 core
in vec2 vUv;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform float uBloomStrength;
uniform float uSceneWeight;   // 1 = scene + glow (default), 0 = the glow alone (GlowOnly)
uniform float uExposure;
uniform float uVignette;
uniform float uOpaque;        // 1 = premultiplied-over with the exposure curve (Opaque), 0 = additive ACES
out vec4 FragColor;

// ACES filmic approximation (Narkowicz)
vec3 Aces(vec3 x)
{
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec4 scene = texture(uScene, vUv);
    vec3 hdr = scene.rgb * uSceneWeight + texture(uBloom, vUv).rgb * uBloomStrength;
    if (uOpaque > 0.5)
    {
        // The curve the body shaders carried themselves before the HDR path:
        // soft exposure that never clips a photosphere to white, then gamma.
        // The resolved colour is premultiplied by coverage, so the curve is
        // applied to the un-premultiplied body and the coverage put back;
        // outside the body the bloom spills RAW, like the additive particles
        // and coronas drawn around it, not lifted by the gamma.
        float a = clamp(scene.a, 0.0, 1.0);
        vec3 bloom = texture(uBloom, vUv).rgb * uBloomStrength;
        vec3 body = a > 0.001
            ? pow(1.0 - exp(-max(scene.rgb / a + bloom, vec3(0.0)) * uExposure), vec3(1.0 / 2.2)) * a
            : vec3(0.0);
        FragColor = vec4(body + bloom * (1.0 - a), a);
        return;
    }
    vec3 tone = Aces(hdr * uExposure);
    // optional vignette on the glow layer: full at the centre, dimmed at corners
    tone *= 1.0 - uVignette * smoothstep(0.38, 0.78, distance(vUv, vec2(0.5)));
    FragColor = vec4(tone, 1.0);
}";
}
