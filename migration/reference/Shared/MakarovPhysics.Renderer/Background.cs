namespace MakarovPhysics.Renderer;

public sealed class Background : IDisposable
{
    private Shader _sh = null!; // created in InitGL
    private uint _vao, _vbo;

    public void InitGL()
    {
        _sh = new Shader(VS, FS);
        float[] tri = [-1f, -1f, 3f, -1f, -1f, 3f]; // fullscreen triangle
        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferData(GL.ARRAY_BUFFER, tri, GL.STATIC_DRAW);
        GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    /// <summary>style: 0 - starfield, 1 - neutral laboratory gradient, 2 - engineering grid.</summary>
    public void Render(float aspect, float time, int style = 0)
    {
        _sh.Use();
        _sh.SetFloat("uAspect", aspect);
        _sh.SetFloat("uTime", time);
        _sh.SetInt("uStyle", style);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, 3);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _sh.Dispose();

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

    private const string VS = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUV;
void main()
{
    vUV = aPos * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

    private const string FS = @"#version 330 core
in vec2 vUV;
uniform float uAspect;
uniform float uTime;
out vec4 FragColor;

float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }

float vnoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1, 0)), c = hash21(i + vec2(0, 1)), d = hash21(i + vec2(1, 1));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * vnoise(p); p = p * 2.02; a *= 0.5; }
    return v;
}

// Stars: square cells (sp is already aspect-corrected); a random offset inside
// each cell hides the grid. The smoothing width comes from the CONTINUOUS cell
// coordinate g (no fract in it) - otherwise the derivative explodes at cell
// seams and produces bright dashes along the borders.
float starField(vec2 sp, float scale, float thr, float radius)
{
    vec2 g = sp * scale;
    float px = max(fwidth(g.x), fwidth(g.y));   // ~1 pixel in cell units, smooth
    vec2 id = floor(g);
    vec2 f = fract(g) - 0.5;
    float h = hash21(id);
    if (h < thr) return 0.0;

    vec2 off = (vec2(hash21(id + 1.7), hash21(id + 9.1)) - 0.5) * 0.7;
    float d = length(f - off);
    float r = radius * (0.5 + 0.8 * hash21(id + 4.3));   // varied star sizes
    float aa = max(px, 1e-4);
    float core = 1.0 - smoothstep(r - aa, r + aa, d);
    float tw = 0.55 + 0.45 * sin(uTime * 1.4 + h * 60.0); // twinkle
    return core * tw;
}

// A particle on an elliptic orbit - a tight halo (physics/space accent)
vec3 orbiter(vec2 c, float fk, vec3 color)
{
    float a = uTime * (0.08 + 0.04 * fk) + fk * 2.1;
    vec2 p = vec2(cos(a) * (0.55 + 0.12 * fk), sin(a) * (0.28 + 0.07 * fk));
    float d = length(c - p);
    return color * exp(-d * d * 140.0) * 0.10;
}

uniform int uStyle;   // 0 stars, 1 laboratory, 2 grid

void main()
{
    vec2 uv = vUV;
    vec2 c = uv - 0.5; c.x *= uAspect;
    vec2 sp = vec2(uv.x * uAspect, uv.y);   // star coordinates: square cells

    // 1: neutral laboratory backdrop - a soft gray gradient with a hint of
    // texture, so mechanics experiments do not float in outer space
    if (uStyle == 1)
    {
        // studio look instead of flat gray: a deep blue-slate floor rising
        // into cool light, plus a soft warm key light off to the upper left -
        // the lit orange bobs and steel rods pop against it instead of
        // dissolving into a monochrome wash
        // 21.08 review, brighter: 0.075->0.115 bottom, 0.170->0.240 top -
        // every laboratory-backed module reads lighter from this one gradient
        vec3 lab = mix(vec3(0.115, 0.125, 0.160), vec3(0.240, 0.252, 0.295), uv.y);
        lab += (fbm(c * 3.0) - 0.5) * 0.016;              // faint paper grain
        vec2 key = c - vec2(-0.45, 0.35);
        lab += vec3(0.058, 0.040, 0.022) * exp(-dot(key, key) * 2.4);
        lab += vec3(0.008, 0.012, 0.020) * exp(-dot(c, c) * 2.2);
        lab *= 1.0 - dot(c, c) * 0.32;                    // gentle vignette
        FragColor = vec4(lab, 1.0);
        return;
    }

    // 2: dark engineering grid - for fields and abstract models
    if (uStyle == 2)
    {
        vec3 bg = mix(vec3(0.050, 0.055, 0.070), vec3(0.080, 0.085, 0.105), uv.y);
        vec2 g = c * 10.0;
        vec2 aa = fwidth(g);
        vec2 f = abs(fract(g) - 0.5);
        float minor = 1.0 - smoothstep(vec2(0.0), aa * 1.2, f).x
                    * smoothstep(0.0, aa.y * 1.2, f.y);
        vec2 g5 = g / 5.0;
        vec2 aa5 = fwidth(g5);
        vec2 f5 = abs(fract(g5) - 0.5);
        float major = 1.0 - smoothstep(0.0, aa5.x * 1.2, f5.x)
                    * smoothstep(0.0, aa5.y * 1.2, f5.y);
        bg += vec3(0.030, 0.034, 0.042) * minor;
        bg += vec3(0.040, 0.046, 0.058) * major;
        bg *= 1.0 - dot(c, c) * 0.35;
        FragColor = vec4(bg, 1.0);
        return;
    }

    // 3: stable diagram canvas. No animation or marks that could be mistaken for data.
    if (uStyle == 3)
    {
        vec3 paper = mix(vec3(0.025, 0.035, 0.055), vec3(0.055, 0.075, 0.105), uv.y);
        paper += vec3(0.012, 0.018, 0.025) * exp(-dot(c, c) * 2.0);
        FragColor = vec4(paper, 1.0);
        return;
    }

    // 0: deep-space gradient
    vec3 col = mix(vec3(0.02, 0.025, 0.04), vec3(0.045, 0.06, 0.10), uv.y);

    // nebula - soft and quiet
    vec2 q = c * 1.5;
    float n = fbm(q * 1.3 + vec2(uTime * 0.015, uTime * 0.010));
    float neb = smoothstep(0.45, 1.0, n);
    vec3 nebCol = mix(vec3(0.08, 0.05, 0.16), vec3(0.04, 0.12, 0.16), fbm(q * 2.0 + 3.1));
    col += nebCol * neb * 0.13;

    // soft glow behind the cylinder
    col += vec3(0.05, 0.09, 0.16) * exp(-dot(c, c) * 2.8);

    // stars: large sparse ones plus fine dust, with slight parallax
    col += vec3(0.90, 0.94, 1.00) * starField(sp + vec2(uTime * 0.004, 0.0), 24.0, 0.88, 0.05)  * 1.0;
    col += vec3(0.70, 0.80, 1.00) * starField(sp + vec2(uTime * 0.008, 0.0), 50.0, 0.93, 0.035) * 0.55;

    // orbiting particles
    col += orbiter(c, 0.0, vec3(0.35, 0.70, 1.00));
    col += orbiter(c, 1.0, vec3(1.00, 0.55, 0.40));
    col += orbiter(c, 2.0, vec3(0.60, 0.50, 1.00));

    // vignette
    col *= 1.0 - dot(c, c) * 0.45;

    FragColor = vec4(col, 1.0);
}";
}
