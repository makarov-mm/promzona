namespace MakarovPhysics.Renderer;

public sealed class Carousel : IDisposable
{
    public int Columns { get; }
    public int Rings { get; }
    // Scaled 15% on 09.08. The RADIUS is what matters, not PanelWidth: a
    // panel is clamped to 94% of the arc pitch 2πR/n, so at R = 6 with
    // seventeen columns the cards were 2.085 wide however large PanelWidth
    // was. Growing the radius grows the pitch and lets them grow with it -
    // 2.085 to 2.397, which is the 15% asked for.
    //
    // The camera needs no change: FitCamera derives its distance from these
    // numbers and moved from 26.2 to 29.7 by itself.
    //
    // Raised again on 10.08, 6.9 -> 7.67, when the rings went from eighteen
    // columns to twenty. The pitch is 2πR/n, so more columns mean smaller
    // cards for a fixed radius: twenty at R = 6.9 would have given 2.038
    // against the 2.264 the eighteen-column rings had, throwing away most of
    // what the previous rescale bought. 7.67 is the radius that returns the
    // card to exactly 2.264 at twenty columns.
    //
    // 15.08: twenty-one columns (126 modules, six rings). Same arithmetic, and
    // it is proportional - the card width is 0.94 · 2πR/n, so holding it fixed
    // means R scales with n: 7.67 · 21/20 = 8.0535, which reproduces 2.264 to
    // the fourth decimal. The camera needs no edit: FitCamera derives its
    // distance from Radius and pulls back by itself.
    //
    // 21.08: +10% visual, by request. Same lever as 09.08: the radius. The
    // FRONT plane stays put - FitCamera's vertical is (height)/tan + Radius,
    // so growing Radius moves the camera back by exactly the growth and the
    // front cards keep their screen distance while their WORLD width rides
    // the pitch: 0.94 · 2π · 8.8589 / 21 = 2.490, which is 2.264 + 10%.
    public float Radius = 8.8589f;
    public float RingSpacing = 2.875f;
    public float PanelWidth = 3.68f;
    public float PanelHeight = 2.3f;

    /// <summary>Panel width for a ring: the standard width, auto-shrunk when a
    /// dense ring's angular pitch could no longer fit it. 2πR/n is the arc per
    /// panel; 94% of it leaves a visible seam. The catalog was sized for ~11
    /// columns - without this clamp a 16- or 21-panel ring overlaps its own
    /// cards into an unreadable pile-up.</summary>
    /// <summary>
    /// ...and a ring with one MORE module than its neighbours pays for it in
    /// card size: eighteen columns at R = 6.9 gives 2.264 against the
    /// seventeen-column 2.397, which is visible as a smaller row.
    ///
    /// So the rings must stay even. The catalogue is at 103, and the five
    /// modules still on the list - Bell, the quantum eraser, Davisson-Germer,
    /// the torsion pendulum and pulleys - bring it to 108, which is 6 x 18
    /// exactly (09.08).
    /// </summary>
    public float PanelWidthFor(int ring)
    {
        // In a grid every cell is the same width or the columns do not line up.
        // Rings differ on the cylinder only because their circumferences do.
        if (_layout == LayoutKind.Grid) return PanelWidth;
        int n = ring >= 0 && ring < _ringSizes.Length ? _ringSizes[ring] : _ringSizes[0];
        float pitch = 2f * MathF.PI * Radius / Math.Max(1, n);
        // 08.09: 0.94 → 0.88 of the pitch - the 6 % seam read as cards touching
        // on a 20-column ring (Michael's screenshot); a 12 % seam breathes
        return MathF.Min(PanelWidth, 0.88f * pitch);
    }
    /// <summary>
    /// How the same panels are arranged.
    ///
    /// `Ring` is the cylinder the product shipped with. `Grid` lays the VERY
    /// SAME slabs flat in one plane, grouped by theme - not a different widget
    /// and not a two-dimensional card list, because the panels stay
    /// three-dimensional and everything already built on that keeps working:
    /// the lit bodies, the hover push-out, the ray-vs-panel picking. Two
    /// reviewers asked for a static grid in release week; a third wanted
    /// filters by domain, which the grid gives for free by grouping.
    ///
    /// The whole difference lives in <see cref="PanelBasis"/>. Rendering,
    /// picking and hovering never learn which arrangement they are in.
    /// </summary>
    public enum LayoutKind { Ring, Grid }

    private LayoutKind _layout = LayoutKind.Ring;

    public LayoutKind Layout
    {
        get => _layout;
        set { _layout = value; RebuildGrid(); }
    }

    /// <summary>Cells across in grid mode.</summary>
    public int GridColumns { get; set; } = 6;

    /// <summary>Vertical pan in grid mode, world units. Plays the part
    /// <see cref="Rotation"/> plays on the cylinder.</summary>
    public float GridScroll;

    /// <summary>
    /// World y of the FIRST row's centre, set by the screen from what the
    /// camera can see.
    ///
    /// Without it the grid hangs from the origin, so the first row sits on the
    /// horizon and the whole upper half of the window is empty sky before the
    /// reader has scrolled anywhere (30.08). A cylinder is centred on the
    /// origin because it extends both ways; a grid only goes down.
    /// </summary>
    public float GridTop;

    /// <summary>Gap between cells, as a fraction of the cell.</summary>
    public float GridGap = 0.14f;

    /// <summary>Blank rows left between one theme and the next.</summary>
    public float GridThemeGap = 0.55f;

    // grid cell per module index, filled by RebuildGrid
    private int[] _gridCol = [];
    private float[] _gridRow = [];
    private float _gridRows;

    /// <summary>Total height of the grid in world units - what a scrollbar
    /// would need, and what clamps the pan.</summary>
    public float GridHeight => _gridRows * (PanelHeight * (1f + GridGap));

    /// <summary>
    /// Place every panel on a cell, themes in ring order, each theme starting
    /// on a fresh row and separated by a gap.
    ///
    /// Computed once per layout change rather than per frame: it is pure
    /// arithmetic over the module list, and the module list does not move.
    /// </summary>
    private void RebuildGrid()
    {
        _gridCol = new int[_modules.Count];
        _gridRow = new float[_modules.Count];
        float row = 0;

        for (int ring = 0; ring < _ringSizes.Length; ring++)
        {
            int placed = 0;

            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i].Ring != ring) continue;
                _gridCol[i] = placed % GridColumns;
                _gridRow[i] = row + placed / GridColumns;
                placed++;
            }

            if (placed == 0) continue;
            row += (placed + GridColumns - 1) / GridColumns + GridThemeGap;
        }

        _gridRows = row;
    }

    public float SlabDepth = 0.32f; // slab thickness
    public float Rotation; // current rotation angle, rad
    public float SnapSpeed = 9f; // snap speed towards a column

    // auto-rotation: the cursor's horizontal position steers the cylinder
    public float SteerX; // -1..1, cursor offset from the center
    public float MaxSteerSpeed = 2.6f; // rad/s at the very edge

    /// <summary>
    /// false - the panel textures belong to an external cache (they survive
    /// screen transitions); Dispose then leaves them alone.
    /// </summary>
    public bool OwnsTextures = true;
    public int HoverIndex { get; private set; } = -1;
    private float? _snapTarget;  // chosen once per dead-zone entry, held until reached
    public event Action<CarouselModule>? ModuleActivated;
    private readonly List<CarouselModule> _modules;
    private readonly int[] _ringSizes; // panels per ring (rings may differ)
    private readonly float _step; // wheel/snap step: the densest ring
    private Shader _shader = null!; // created in InitGL
    private Shader _solid = null!; // created in InitGL
    private Mesh _box = null!; // created in InitGL
    private uint _vao, _vbo; // front-face quad

    public IReadOnlyList<CarouselModule> Modules => _modules;
    public bool FilterActive { get; set; }

    /// <summary>Reveal an existing card without changing catalogue order.</summary>
    public void FocusModule(int index)
    {
        if (index < 0 || index >= _modules.Count || !_modules[index].MatchesFilter) return;
        var panel = _modules[index];
        if (Layout == LayoutKind.Grid)
            ScrollGrid(_gridRow[index] * PanelHeight * (1 + GridGap) - GridViewHeight * .35f - GridScroll);
        else
        {
            Rotation = -panel.Column * 2 * MathF.PI / _ringSizes[panel.Ring];
            _snapTarget = Rotation;
        }
        SteerX = 0;
        SetHover(index);
    }

    /// <summary>Uniform grid: every ring holds the same number of columns.</summary>
    public Carousel(IEnumerable<CarouselModule> modules, int columns = 10, int rings = 3)
        : this(modules, UniformRingSizes(columns, rings)) { }

    private static int[] UniformRingSizes(int columns, int rings)
    {
        int[] sizes = new int[Math.Max(1, rings)];
        Array.Fill(sizes, Math.Max(1, columns));
        return sizes;
    }

    /// <summary>
    /// Ragged layout: ring r holds ringSizes[r] panels spread evenly over its
    /// own 2π - the catalog is allowed to grow one ring without breaking the
    /// others (a flat columns×rings split would shift every module after the
    /// first shorter ring and leave black holes in the last one).
    /// </summary>
    public Carousel(IEnumerable<CarouselModule> modules, IReadOnlyList<int> ringSizes)
    {
        _ringSizes = ringSizes.Count > 0 ? [.. ringSizes] : [1];
        for (int r = 0; r < _ringSizes.Length; r++)
        {
            _ringSizes[r] = Math.Max(1, _ringSizes[r]);
        }

        _modules = new List<CarouselModule>(modules);

        // Guard against modules.Count != ringSizes.Sum(). Fewer modules only
        // leaves trailing slots empty (harmless). More modules would spill past
        // the last ring's capacity - column >= ringSize then wraps onto an angle
        // already taken by another panel. Absorb any overflow into the last ring
        // so every module keeps a unique, in-range column instead of stacking.
        int capacity = _ringSizes.Sum();
        if (_modules.Count > capacity)
        {
            _ringSizes[^1] += _modules.Count - capacity;
        }

        Rings = _ringSizes.Length;
        Columns = _ringSizes.Max();
        _step = 2f * MathF.PI / Columns;
        int ring = 0, column = 0;

        foreach (CarouselModule module in _modules)
        {
            if (ring < Rings && column >= _ringSizes[ring]) { ring++; column = 0; }
            module.Ring = Math.Min(ring, Rings - 1);
            module.Column = column++;
        }
    }
    public void InitGL()
    {
        _shader = new Shader(VS, FS);
        _solid = new Shader(BodyVS, BodyFS);
        _box = Primitives.Cube();

        // unit quad: pos.xy in [-0.5..0.5], uv in [0..1], CCW when viewed from the front
        float[] quad =
        [
            //  x      y   u  v
            -0.5f, -0.5f,  0, 0,
             0.5f, -0.5f,  1, 0,
             0.5f,  0.5f,  1, 1,
            -0.5f, -0.5f,  0, 0,
             0.5f,  0.5f,  1, 1,
            -0.5f,  0.5f,  0, 1
        ];

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferData(GL.ARRAY_BUFFER, quad, GL.STATIC_DRAW);
        const int stride = 4 * sizeof(float);
        GL.VertexAttribPointer(0, 2, GL.FLOAT, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, GL.FLOAT, false, stride, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindVertexArray(0);
    }

    private float RingY(int ring) => (ring - (Rings - 1) / 2f) * RingSpacing;

    /// <summary>Panel basis: U (tangent), V (up), N (outward), C (center).</summary>
    /// <summary>
    /// Takes the INDEX, not the module. The grid needs to know which cell a
    /// panel sits in, and looking that up by identity would be a list scan per
    /// panel per frame - 140 panels scanned 140 deep, three times over between
    /// bodies, textures and picking. The index is in hand at every call site
    /// anyway (30.08).
    /// </summary>
    private void PanelBasis(int index, out Vec3 U, out Vec3 V, out Vec3 N, out Vec3 C)
    {
        CarouselModule m = _modules[index];

        if (_layout == LayoutKind.Grid)
        {
            GridBasis(index, out U, out V, out N, out C);
            return;
        }

        // each ring spreads ITS OWN panel count over the full circle
        float ringStep = 2f * MathF.PI / _ringSizes[Math.Clamp(m.Ring, 0, _ringSizes.Length - 1)];
        float a = m.Column * ringStep + Rotation;
        float s = MathF.Sin(a), c = MathF.Cos(a);
        N = new Vec3(s, 0, c);
        U = new Vec3(c, 0, -s);
        V = Vec3.UnitY;
        C = new Vec3(Radius * s, RingY(m.Ring), Radius * c);
    }

    /// <summary>
    /// The flat arrangement: same slabs, one plane, all facing the reader.
    ///
    /// The basis is constant - every panel shares it - so the only thing that
    /// varies is the centre. That is what makes this a layout change and not a
    /// second renderer.
    /// </summary>
    private void GridBasis(int index, out Vec3 U, out Vec3 V, out Vec3 N, out Vec3 C)
    {
        N = Vec3.UnitZ;
        U = Vec3.UnitX;
        V = Vec3.UnitY;

        int col = index >= 0 && index < _gridCol.Length ? _gridCol[index] : 0;
        float row = index >= 0 && index < _gridRow.Length ? _gridRow[index] : 0;

        float pitchX = PanelWidth * (1f + GridGap);
        float pitchY = PanelHeight * (1f + GridGap);
        float x = (col - (GridColumns - 1) / 2f) * pitchX;
        float y = GridTop - row * pitchY + GridScroll;
        C = new Vec3(x, y, 0);
    }

    /// <summary>Pan the grid, clamped so the reader cannot lose the panels off
    /// either end - the cylinder wraps and a grid does not.</summary>
    /// <summary>Height of what the camera can see, world units. Set by the
    /// screen; without it the pan cannot know where the end is.</summary>
    public float GridViewHeight { get; set; }

    public void ScrollGrid(float dy)
    {
        // The end of the scroll is where the LAST row reaches the bottom of the
        // window, not where it reaches the top. The old limit was the whole
        // content height minus one card, so the grid kept going long after the
        // last row had passed - a screen and a half of empty sky under two
        // cards (30.08).
        float content = _gridRows * PanelHeight * (1f + GridGap);
        float limit = MathF.Max(0f, content - MathF.Max(PanelHeight, GridViewHeight));
        GridScroll = Math.Clamp(GridScroll + dy, 0f, limit);
    }

    /// <summary>Wheel/keys: step by one column.</summary>
    public void Step(int dir)
    {
        Rotation -= dir * _step;
        // the held snap target is now stale - without this the next Update in
        // the dead zone would lerp the rotation straight back, undoing the step
        _snapTarget = null;
    }

    public void Update(float dt)
    {
        const float dead = 0.16f; // dead zone around the center
        float a = MathF.Abs(SteerX);

        if (a > dead)
        {
            float mag = (a - dead) / (1f - dead); // 0..1
            // cursor right of center → right panels come to the front
            Rotation -= MathF.Sign(SteerX) * mag * mag * MaxSteerSpeed * dt;
            _snapTarget = null;            // steering again - the old snap target is stale
        }
        else
        {
            // near the center - snap the closest column exactly to the front.
            // The target is chosen ONCE on entering the dead zone and held:
            // re-evaluating it every frame could switch rings mid-approach on
            // ragged layouts and bend/jitter the trajectory.
            _snapTarget ??= SnapTarget();
            Rotation += (_snapTarget.Value - Rotation) * MathF.Min(1f, dt * SnapSpeed);
        }

        // an endless session must not let the angle drift into float imprecision
        if (MathF.Abs(Rotation) > 4f * MathF.PI)
        {
            float wrapped = MathF.IEEERemainder(Rotation, 2f * MathF.PI);
            if (_snapTarget is { } t) _snapTarget = t + (wrapped - Rotation);
            Rotation = wrapped;
        }
    }

    /// <summary>
    /// Rotation that puts a REAL panel exactly at the front. A ring of N panels
    /// is aligned when the rotation is a multiple of its own pitch 2π/N; rings of
    /// different sizes share no common grid, so snapping to the densest ring
    /// (2π/Columns) leaves shorter rings slightly off-front. Pick, across all
    /// rings, the aligned rotation closest to the current one - whichever ring
    /// has a panel nearest the front snaps onto it exactly.
    /// </summary>
    private float SnapTarget()
    {
        float best = Rotation;
        float bestErr = float.MaxValue;

        foreach (int size in _ringSizes)
        {
            float pitch = 2f * MathF.PI / size;
            float t = MathF.Round(Rotation / pitch) * pitch;
            float err = MathF.Abs(t - Rotation);
            if (err < bestErr) { bestErr = err; best = t; }
        }

        return best;
    }

    /// <summary>Recompute the panel under the cursor (ray from the camera).</summary>
    public void UpdateHover(int mx, int my, int vpW, int vpH, OrbitCamera cam, float aspect)
    {
        // a minimized/mid-resize window must not turn the NDC math into NaN
        if (vpW <= 0 || vpH <= 0)
        {
            HoverIndex = -1;
            return;
        }

        Vec3 eye = cam.Position();
        Vec3 f = (cam.Target - eye).Normalized();
        Vec3 r = Vec3.Cross(f, Vec3.UnitY).Normalized();
        Vec3 u = Vec3.Cross(r, f);

        float ndcX = 2f * mx / vpW - 1f;
        float ndcY = 1f - 2f * my / vpH;
        float tanH = MathF.Tan(cam.Fovy / 2f);
        Vec3 dir = (f + r * (ndcX * tanH * aspect) + u * (ndcY * tanH)).Normalized();

        int best = -1;
        float bestT = float.MaxValue;
        float hh = PanelHeight / 2f;

        for (int i = 0; i < _modules.Count; i++)
        {
            float hw = PanelWidthFor(_modules[i].Ring) / 2f;
            if (HitPanel(i, eye, dir, hw, hh, out float t) && t < bestT)
            {
                bestT = t;
                best = i;
            }
        }

        // hysteresis: the hovered card is drawn pushed out and 4% larger, so
        // its visual edge is outside the base hit box. While the cursor still
        // hits the CURRENT card's expanded box, keep it - otherwise the edge
        // pixels flicker between hover and none every frame.
        if (best < 0 && HoverIndex >= 0 && HoverIndex < _modules.Count && _modules[HoverIndex].MatchesFilter && HitPanel(HoverIndex, eye, dir, PanelWidthFor(_modules[HoverIndex].Ring) / 2f * 1.08f, hh * 1.08f, out _, hoverOffset: true))
        {
            return; // keep the current hover
        }

        SetHover(best);
    }

    /// <summary>Ray-vs-panel test in base (or hover-shifted) geometry.</summary>
    private bool HitPanel(int index, Vec3 eye, Vec3 dir, float hw, float hh, out float t, bool hoverOffset = false)
    {
        t = 0;
        PanelBasis(index, out Vec3 U, out Vec3 V, out Vec3 N, out Vec3 C);
        if (hoverOffset) C += N * 0.4f; // match the pushed-out render position
        if (Vec3.Dot(N, eye - C) <= 0) return false; // panel faces away
        float denom = Vec3.Dot(dir, N);
        if (MathF.Abs(denom) < 1e-6f) return false;
        t = Vec3.Dot(C - eye, N) / denom;
        if (t <= 0) return false;
        Vec3 d = eye + dir * t - C;
        return MathF.Abs(Vec3.Dot(d, U)) <= hw && MathF.Abs(Vec3.Dot(d, V)) <= hh;
    }

    /// <summary>
    /// The panel currently closest to the front of the cylinder - of one ring,
    /// or of any ring when <paramref name="ring"/> is −1 (keyboard navigation).
    /// </summary>
    public int FrontIndex(int ring = -1)
    {
        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < _modules.Count; i++)
        {
            CarouselModule m = _modules[i];
            if (!m.MatchesFilter) continue;
            if (ring >= 0 && m.Ring != ring) continue;
            float ringStep = 2f * MathF.PI / _ringSizes[Math.Clamp(m.Ring, 0, _ringSizes.Length - 1)];
            float score = MathF.Cos(m.Column * ringStep + Rotation); // 1 = dead ahead
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>Keyboard navigation: highlight a panel as if the mouse hovered it.</summary>
    public void SetHover(int index) => HoverIndex = index >= 0 && index < _modules.Count && _modules[index].MatchesFilter ? index : -1;

    /// <summary>Activate the currently hovered panel, or the front one when nothing is hovered.</summary>
    public void ActivateFrontOrHover()
    {
        int index = HoverIndex >= 0 ? HoverIndex : FrontIndex();

        if (index >= 0 && _modules[index].MatchesFilter)
        {
            ModuleActivated?.Invoke(_modules[index]);
        }
    }

    public void ClearHover() => HoverIndex = -1;

    /// <summary>Activate the panel under the cursor.</summary>
    public void Activate()
    {
        if (HoverIndex >= 0 && HoverIndex < _modules.Count && _modules[HoverIndex].MatchesFilter)
        {
            ModuleActivated?.Invoke(_modules[HoverIndex]);
        }
    }

    public void Render(Mat4 view, Mat4 proj)
    {
        // 1) slab bodies (lit) - faces are shaded by normal, so the edges
        //    between the back and side faces stay visible
        _solid.Use();
        _solid.SetMatrix4("uView", view);
        _solid.SetMatrix4("uProj", proj);
        _solid.SetVec3("uLightDir", 0.45f, 0.75f, 0.55f);
        _solid.SetFloat("uAmbient", 0.5f);
        for (int i = 0; i < _modules.Count; i++)
        {
            var m = _modules[i];
            PanelBasis(i, out Vec3 U, out Vec3 V, out Vec3 N, out Vec3 C);
            bool hot = (i == HoverIndex);
            Vec3 cc = hot ? C + N * 0.4f : C;
            Vec3 center = cc - N * (SlabDepth / 2f + 0.01f);     // the body sinks inward
            Mat4 model = Mat4.FromColumns(U * PanelWidthFor(m.Ring), V * PanelHeight, N * SlabDepth, center);
            _solid.SetMatrix4("uModel", model);
            _solid.SetVec3("uColor", m.BodyColor * (!m.MatchesFilter ? .16f : hot ? 1.5f : 1f));
            _box.Draw();
        }

        // 2) textured front faces (GL culls the ones facing away)
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetInt("uTex", 0);

        for (int i = 0; i < _modules.Count; i++)
        {
            var m = _modules[i];
            if (m.Texture is null) continue;
            PanelBasis(i, out Vec3 U, out Vec3 V, out Vec3 N, out Vec3 C);
            bool hot = (i == HoverIndex);
            Vec3 cc = hot ? C + N * 0.4f : C;
            float sc = hot ? 1.04f : 1f;
            Mat4 model = Mat4.FromColumns(U * (PanelWidthFor(m.Ring) * sc), V * (PanelHeight * sc), N, cc);
            _shader.SetMatrix4("uModel", model);
            _shader.SetFloat("uHighlight", hot ? 1f : FilterActive && m.MatchesFilter ? .45f : 0f);
            _shader.SetFloat("uVisibility", m.MatchesFilter ? 1f : .16f);
            m.Texture.Bind(0);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(GL.TRIANGLES, 0, 6);
        }

        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _solid.Dispose();
        _box.Dispose();

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

        if (OwnsTextures)
        {
            foreach (CarouselModule m in _modules)
            {
                m.Texture?.Dispose();
            }
        }
    }

    // ---- panel shaders ----
    private const string VS = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec2 vUV;
void main()
{
    vUV = aUV;
    gl_Position = uProj * uView * uModel * vec4(aPos, 0.0, 1.0);
}";

    private const string FS = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex;
uniform float uHighlight;
uniform float uVisibility;
out vec4 FragColor;
void main()
{
    vec3 tex = texture(uTex, vUV).rgb;

    // border frame; bright blue when the panel is selected
    float e = min(min(vUV.x, 1.0 - vUV.x), min(vUV.y, 1.0 - vUV.y));
    float bw = 0.035;
    vec3 frameCold = vec3(0.18, 0.22, 0.30);
    vec3 frameHot  = vec3(0.35, 0.65, 1.00);
    vec3 frame = mix(frameCold, frameHot, uHighlight);

    vec3 color = (e < bw) ? frame : tex;
    float bright = mix(0.80, 1.18, uHighlight);   // unselected panels are slightly dimmed
    FragColor = vec4(color * bright * uVisibility, 1.0);
}";

    // ---- body shader: flat shading by face normal ----
    // For the cube's axis-aligned normals normalize(mat3(uModel)*n) gives the
    // correct direction even under non-uniform scaling, so faces shade correctly.
    private const string BodyVS = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vN;
void main()
{
    vN = mat3(uModel) * aNormal;
    gl_Position = uProj * uView * uModel * vec4(aPos, 1.0);
}";

    private const string BodyFS = @"#version 330 core
in vec3 vN;
uniform vec3 uColor;
uniform vec3 uLightDir;
uniform float uAmbient;
out vec4 FragColor;
void main()
{
    vec3 N = normalize(vN);
    float diff = max(dot(N, normalize(uLightDir)), 0.0);
    float shade = uAmbient + (1.0 - uAmbient) * diff;
    FragColor = vec4(uColor * shade, 1.0);
}";
}
