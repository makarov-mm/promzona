using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Instruments;

/// <summary>
/// Base for measuring instruments: objects the user drags onto the scene and
/// reads values off. The parameter panel sets what the experiment does; an
/// instrument answers what it is doing right now, which is the interaction PhET
/// leans on and this suite had nowhere (see Docs/ROADMAP.md, section 1).
///
/// An instrument lives in logical UI coordinates because the user grabs it on
/// screen, but every number it prints is world units taken through
/// <see cref="Projection"/> and <see cref="Plane"/>. Handles are the draggable
/// points (a ruler has two ends, a protractor a vertex and two arms); dragging
/// the body moves them together.
/// </summary>
public abstract class InstrumentWidget : Widget
{
    /// <summary>Grab radius around a handle, logical pixels.</summary>
    protected const float HandleRadius = 11f;

    private int _dragHandle = -1;
    private bool _dragBody;
    private float _grabX, _grabY;

    /// <summary>Camera of the current frame; refreshed by InstrumentSet.Sync.</summary>
    public SceneProjection Projection { get; set; }

    /// <summary>Surface the readings are taken on; refreshed by InstrumentSet.Sync.</summary>
    public ScenePlane Plane { get; set; }

    /// <summary>Caption shown on the toggle that stows and unstows this instrument.</summary>
    public string Title = string.Empty;

    /// <summary>Unit suffix printed next to the reading.</summary>
    public string Units = "m";

    /// <summary>Reading units per world unit (22.09): a scene drawn at a scale
    /// - the brachistochrone track is model metres × WorldScale - hands its
    /// ruler 1/scale, and the ruler reads model metres, not scene metres.
    /// 1 leaves every existing instrument as it was.</summary>
    public double UnitsPerWorld = 1.0;

    /// <summary>Number format of the reading.</summary>
    public string Format = "0.###";

    /// <summary>Raised after the user moves the instrument.</summary>
    public event Action? Moved;

    protected abstract int HandleCount { get; }
    protected abstract void GetHandle(int index, out float x, out float y);
    protected abstract void SetHandle(int index, float x, float y);

    /// <summary>Initial position for a viewport of this size, in logical units.</summary>
    public abstract void PlaceDefault(float viewportWidth, float viewportHeight);

    /// <summary>Slack around the handles that still counts as the instrument body.</summary>
    protected virtual float BodyMargin => 10f;

    /// <summary>
    /// An alternative screen-to-world map, for modules that have no 3D camera.
    ///
    /// The projection path assumes a view and a projection matrix and a plane
    /// in the scene, which is right for every module that draws a world. A
    /// schematic editor draws a sheet: it has a pan and a zoom and no camera
    /// at all, and forcing it to synthesise matrices so an instrument could
    /// undo them would be arithmetic performed twice to arrive back where it
    /// started. When this is set it takes precedence.
    /// </summary>
    public Func<float, float, Vec3?>? ScreenToWorld { get; set; }

    /// <summary>true when the reading is currently measurable.</summary>
    protected bool CanMeasure => ScreenToWorld is not null || (Projection.IsValid && Plane.IsValid);

    /// <summary>World point under a logical UI point, on the measurement plane.</summary>
    protected bool TryPick(float x, float y, out Vec3 world)
    {
        world = Vec3.Zero;

        if (ScreenToWorld is not null)
        {
            if (ScreenToWorld(x, y) is not { } mapped) return false;

            world = mapped;
            return true;
        }

        return Projection.IsValid && Plane.IsValid && Projection.TryScreenToPlane(x, y, Plane, out world);
    }

    /// <summary>World point under a handle.</summary>
    protected bool TryPickHandle(int index, out Vec3 world)
    {
        GetHandle(index, out float x, out float y);
        return TryPick(x, y, out world);
    }

    /// <summary>Recomputes Rect from the handles. Call after moving any of them.</summary>
    protected void UpdateBounds()
    {
        if (HandleCount <= 0)
        {
            Rect = new UiRect(0f, 0f, 0f, 0f);
            return;
        }

        MeasureExtent(out float minX, out float minY, out float maxX, out float maxY);
        float margin = BodyMargin;
        Rect = new UiRect(minX - margin, minY - margin, maxX - minX + 2f * margin, maxY - minY + 2f * margin);
    }

    /// <summary>
    /// Keeps the instrument inside the render area by shifting it as a whole.
    /// Clamping the handles one by one would stretch a ruler against the edge
    /// and change the reading, which is exactly the number the user trusts.
    /// </summary>
    protected void ClampToView()
    {
        if (!Projection.IsValid || HandleCount <= 0) return;

        float width = Projection.LogicalWidth;
        float height = Projection.LogicalHeight;
        if (!(width > 0f) || !(height > 0f)) return;

        const float margin = 6f;
        MeasureExtent(out float minX, out float minY, out float maxX, out float maxY);

        // Clamp the visible body, not the handles: otherwise half a ruler hangs
        // off the edge while its endpoint dutifully sits inside.
        minX -= BodyMargin;
        minY -= BodyMargin;
        maxX += BodyMargin;
        maxY += BodyMargin;

        float shiftX = 0f, shiftY = 0f;

        if (minX < margin) 
            shiftX = margin - minX;
        else if (maxX > width - margin) 
            shiftX = width - margin - maxX;
        if (minY < margin) 
            shiftY = margin - minY;
        else if (maxY > height - margin) 
            shiftY = height - margin - maxY;

        if (shiftX == 0f && shiftY == 0f) return;

        for (int i = 0; i < HandleCount; i++)
        {
            GetHandle(i, out float x, out float y);
            SetHandle(i, x + shiftX, y + shiftY);
        }
    }

    /// <summary>
    /// Screen extent of the instrument. Defaults to the bounding box of the
    /// handles; panel-shaped instruments (a stopwatch is a box with one anchor)
    /// override it so both the hit box and the edge clamping see the real size.
    /// </summary>
    protected virtual void MeasureExtent(out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = float.MaxValue;
        minY = float.MaxValue;
        maxX = float.MinValue;
        maxY = float.MinValue;

        for (int i = 0; i < HandleCount; i++)
        {
            GetHandle(i, out float x, out float y);
            if (!float.IsFinite(x) || !float.IsFinite(y)) continue;
            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
        }

        if (minX > maxX)
        {
            minX = minY = maxX = maxY = 0f;
        }
    }

    protected int FindHandle(float x, float y)
    {
        int best = -1;
        float bestDistance = HandleRadius * HandleRadius;

        for (int i = 0; i < HandleCount; i++)
        {
            GetHandle(i, out float hx, out float hy);
            float dx = x - hx, dy = y - hy;
            float distance = dx * dx + dy * dy;
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Fine grained hit test. The bounding box of a diagonal ruler covers a
    /// quarter of the screen, and returning that as a hit would quietly steal
    /// every click meant for the scene underneath (dragging charges, aiming a
    /// cannon), so the box is only the cheap first pass.
    /// </summary>
    public override Widget? HitTest(float x, float y)
    {
        if (!Visible || !Enabled) return null;
        if (!Rect.Contains(x, y)) return null;
        return FindHandle(x, y) >= 0 || ContainsBody(x, y) ? this : null;
    }

    /// <summary>Shape of the instrument body, tested inside Rect.</summary>
    protected virtual bool ContainsBody(float x, float y) => true;

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;

        _dragHandle = FindHandle(x, y);
        _dragBody = _dragHandle < 0;
        _grabX = x;
        _grabY = y;
        Ui?.Capture(this);
        return true;
    }

    public override void OnMouseMove(float x, float y)
    {
        if (_dragHandle < 0 && !_dragBody) return;
        if (!float.IsFinite(x) || !float.IsFinite(y)) return;

        if (_dragHandle >= 0)
        {
            SetHandle(_dragHandle, x, y);
        }
        else
        {
            // Move by the delta rather than by an absolute position, so grabbing
            // the body anywhere keeps the instrument under the cursor.
            float dx = x - _grabX;
            float dy = y - _grabY;
            for (int i = 0; i < HandleCount; i++)
            {
                GetHandle(i, out float hx, out float hy);
                SetHandle(i, hx + dx, hy + dy);
            }
        }

        _grabX = x;
        _grabY = y;
        ClampToView();
        UpdateBounds();
        Moved?.Invoke();
    }

    public override void OnMouseUp(float x, float y, MouseButton b)
    {
        _dragHandle = -1;
        _dragBody = false;
        Ui?.ReleaseCapture(this);
    }

    // Translucent on purpose: the scene under an instrument has to stay
    // readable, or measuring one thing costs the sight of another.
    protected UiColor BodyColor => new(0.93f, 0.90f, 0.72f, 0.58f);
    protected UiColor EdgeColor => new(0.30f, 0.27f, 0.18f, 0.95f);
    protected UiColor InkColor => new(0.10f, 0.10f, 0.08f, 1f);
    protected UiColor HandleColor => Theme.Accent;

    protected void DrawHandles(Draw2D d)
    {
        const float size = 9f;
        for (int i = 0; i < HandleCount; i++)
        {
            GetHandle(i, out float x, out float y);
            d.Rect(x - size * 0.5f, y - size * 0.5f, size, size, HandleColor);
            d.RectOutline(x - size * 0.5f, y - size * 0.5f, size, size, EdgeColor);
        }
    }

    /// <summary>
    /// Draws the reading chip near a point, nudged so it stays on screen. The
    /// chip is opaque because a number over a bright simulation is unreadable.
    /// </summary>
    protected void DrawReadout(Draw2D d, float anchorX, float anchorY, string text)
    {
        const float padX = 7f, padY = 4f;
        float textWidth = d.MeasureText(text);
        float w = textWidth + 2f * padX;
        float h = Theme.FontSize + 2f * padY;

        float x = anchorX;
        float y = anchorY;

        if (Projection.IsValid)
        {
            x = Math.Clamp(x, 2f, MathF.Max(2f, Projection.LogicalWidth - w - 2f));
            y = Math.Clamp(y, 2f, MathF.Max(2f, Projection.LogicalHeight - h - 2f));
        }

        d.Rect(x, y, w, h, new UiColor(0.03f, 0.035f, 0.05f, 0.88f));
        d.RectOutline(x, y, w, h, Theme.PanelBorder.WithAlpha(0.7f));
        d.Text(x + padX, y + padY - 1f, text, Theme.Text);
    }

    /// <summary>Reading text, or a dash when the camera cannot be measured with.</summary>
    protected string FormatValue(double value, bool measured)
    {
        if (!measured || !double.IsFinite(value)) return "--";
        value *= UnitsPerWorld;
        return Units.Length > 0 ? value.ToString(Format) + " " + Units : value.ToString(Format);
    }
}
