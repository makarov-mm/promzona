using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Overlays;

/// <summary>
/// Draws labelled vector arrows over the scene: forces, velocities,
/// accelerations, with their numbers next to them (Docs/ROADMAP.md, section A,
/// "arrows with numbers").
///
/// It lives in the UI layer rather than in the renderer for one reason: the
/// labels. Text drawn into the 3D scene would shrink, tilt and blur with the
/// camera exactly when the reader wants to check a number. Here the arrow
/// follows the scene while the number stays crisp and upright.
///
/// The widget never takes input: it is a readout laid over the experiment, and
/// a click passing "through" it must reach whatever the user was aiming at.
/// </summary>
public sealed class VectorOverlay : Widget
{
    private const float HeadFraction = 0.22f;
    private const float MaxHeadPixels = 14f;
    private const float MinimumArrowPixels = 6f;
    private const float ProbeWorldStep = 0.01f;
    private readonly List<SceneVector> _vectors = [with(16)];
    private float _largest;

    /// <summary>Camera of the current frame; the module refreshes it.</summary>
    public SceneProjection Projection { get; set; }

    /// <summary>Length the largest arrow of the group aims for, in logical pixels.</summary>
    public float TargetPixels = 130f;

    /// <summary>Unit suffix printed after each value.</summary>
    public string Units = string.Empty;

    /// <summary>Number format of the values.</summary>
    public string Format = "0.##";

    /// <summary>false hides the numbers and leaves bare arrows.</summary>
    public bool ShowLabels = true;

    /// <summary>false hides the scale bar.</summary>
    public bool ShowLegend = true;

    /// <summary>
    /// Height of the scale bar, logical pixels from the top. A scene showing
    /// two different quantities needs two overlays with two scales (comparing
    /// AU/yr against AU/yr2 on one scale would be meaningless), and their
    /// legends must not land on top of each other.
    /// </summary>
    public float LegendY = 16f;

    /// <summary>Current shared scale, logical pixels per unit of the quantity.</summary>
    public float PixelsPerUnit { get; private set; }

    /// <summary>Starts a new group. Modules call it once per frame.</summary>
    public void Begin()
    {
        _vectors.Clear();
        _placed.Clear();
        _largest = 0f;
    }

    /// <summary>
    /// Label chips already placed this frame, so the next one can step aside.
    ///
    /// Chips sit at the arrow TIP, which separates them when the arrows point
    /// different ways and not at all when they do not: the spring pendulum
    /// draws weight, spring force, net force and velocity from one bob, and
    /// three of the four are nearly parallel near the turning points. The
    /// labels landed on top of each other and the reader got one unreadable
    /// stack (09.08, from a screenshot).
    /// </summary>
    private readonly List<UiRect> _placed = [with(8)];

    /// <summary>Adds one vector to the current group.</summary>
    public void Add(in SceneVector vector)
    {
        float magnitude = vector.Magnitude;
        if (!float.IsFinite(magnitude) || magnitude <= 0f) return;
        if (!vector.Origin.IsFinite) return;

        _vectors.Add(vector);
        _largest = MathF.Max(_largest, magnitude);
    }

    /// <summary>
    /// Closes the group and refits the shared scale if it has drifted out of
    /// its band. Refitting on every frame instead would make every arrow
    /// breathe whenever the physics wobbled, and a diagram whose scale keeps
    /// moving cannot be read at all.
    /// </summary>
    public void End()
    {
        if (VectorScale.NeedsRefit(PixelsPerUnit, _largest, TargetPixels))
        {
            float fitted = VectorScale.Fit(_largest, TargetPixels);

            if (fitted > 0f)
            {
                PixelsPerUnit = fitted;
            }
        }
    }

    /// <summary>A readout never intercepts a click meant for the experiment.</summary>
    public override Widget? HitTest(float x, float y) => null;

    public override void Draw(Draw2D d)
    {
        if (_vectors.Count == 0 || !Projection.IsValid || PixelsPerUnit <= 0f) return;

        foreach (SceneVector vector in _vectors)
        {
            DrawVector(d, vector);
        }

        if (ShowLegend) DrawLegend(d);
    }

    private void DrawVector(Draw2D d, in SceneVector vector)
    {
        if (!Projection.TryWorldToScreen(vector.Origin, out float x0, out float y0)) return;
        if (!vector.Value.TryNormalize(out Vec3 direction)) return;

        // The arrow points where the vector points IN THE SCENE, so the screen
        // direction comes from projecting a short step along it rather than
        // from the raw components.
        if (!Projection.TryWorldToScreen(vector.Origin + direction * ProbeWorldStep, out float xp, out float yp)) return;

        float dx = xp - x0, dy = yp - y0;
        float screenLength = MathF.Sqrt(dx * dx + dy * dy);

        float length = vector.Magnitude * PixelsPerUnit;
        if (!float.IsFinite(length) || length < MinimumArrowPixels) return;

        if (!(screenLength > 1e-4f))
        {
            // Pointing straight at or away from the camera: there is no
            // direction to draw on screen, so mark the point instead of
            // inventing an arrow that would read as a sideways vector.
            d.RectOutline(x0 - 4f, y0 - 4f, 8f, 8f, vector.Color);
            return;
        }

        float ux = dx / screenLength, uy = dy / screenLength;
        float x1 = x0 + ux * length, y1 = y0 + uy * length;

        d.Line(x0, y0, x1, y1, vector.Color, 2.5f);

        float head = MathF.Min(length * HeadFraction, MaxHeadPixels);
        float bx = x1 - ux * head, by = y1 - uy * head;
        float nx = -uy * head * 0.45f, ny = ux * head * 0.45f;
        d.Line(x1, y1, bx + nx, by + ny, vector.Color, 2.5f);
        d.Line(x1, y1, bx - nx, by - ny, vector.Color, 2.5f);

        if (!ShowLabels) return;

        string text = vector.Label.Length > 0
            ? $"{vector.Label} = {vector.Magnitude.ToString(Format)}{Suffix()}"
            : vector.Magnitude.ToString(Format) + Suffix();

        // stepped PERPENDICULAR to its own arrow, so a chip that has to move
        // still reads as belonging to the arrow it names
        DrawChip(d, x1 + ux * 6f, y1 + uy * 6f - Theme.FontSize * 0.5f, text, vector.Color, -uy, ux);
    }

    private string Suffix() => Units.Length > 0 ? " " + Units : string.Empty;

    /// <summary>
    /// The scale bar. A shared linear scale is only useful if the reader can
    /// tell what it is, and a quantized one always prints as a round number.
    /// </summary>
    private void DrawLegend(Draw2D d)
    {
        float step = VectorScale.LegendStep(PixelsPerUnit, TargetPixels);
        if (step <= 0f) return;

        float bar = step * PixelsPerUnit;
        if (!(bar > 4f)) return;

        float x = MathF.Max(8f, Projection.LogicalWidth * 0.5f - bar * 0.5f);
        float y = LegendY;

        d.Line(x, y, x + bar, y, Theme.TextDim, 2f);
        d.Line(x, y - 4f, x, y + 4f, Theme.TextDim, 2f);
        d.Line(x + bar, y - 4f, x + bar, y + 4f, Theme.TextDim, 2f);

        string text = step.ToString(Format) + Suffix();
        float width = d.MeasureText(text);
        d.Text(x + (bar - width) * 0.5f, y + 6f, text, Theme.TextDim);
    }

    private void DrawChip(Draw2D d, float x, float y, string text, UiColor color, float stepX = 0f, float stepY = 1f)
    {
        const float padX = 5f, padY = 3f;
        float w = d.MeasureText(text) + 2f * padX;
        float h = Theme.FontSize + 2f * padY;

        // Try the wanted place first, then a few steps either side of it. Six
        // attempts is enough for the four vectors any module draws at once,
        // and giving up quietly after them beats searching forever for a spot
        // that a crowded frame may not have.
        float bestX = x, bestY = y;
        for (int attempt = 0; attempt < 7; attempt++)
        {
            int sign = attempt % 2 == 0 ? 1 : -1;

            // A step of one line height clears nothing when the chip is
            // ninety pixels wide and the step is mostly sideways: the label
            // has to move by its OWN extent along the direction it moves in.
            // Replayed on the spring pendulum's four near-parallel arrows,
            // that is the difference between six overlapping pairs and none.
            float reach = MathF.Abs(stepX) * w + MathF.Abs(stepY) * h + 3f;
            float distance = (attempt + 1) / 2 * reach;
            float tx = Math.Clamp(x + stepX * distance * sign, 2f, MathF.Max(2f, Projection.LogicalWidth - w - 2f));
            float ty = Math.Clamp(y + stepY * distance * sign, 2f, MathF.Max(2f, Projection.LogicalHeight - h - 2f));

            if (attempt == 0)
            {
                bestX = tx; 
                bestY = ty;
            }

            if (!Overlaps(tx, ty, w, h))
            {
                bestX = tx; 
                bestY = ty; break;
            }
        }

        _placed.Add(new UiRect(bestX, bestY, w, h));
        d.Rect(bestX, bestY, w, h, new UiColor(0.03f, 0.035f, 0.05f, 0.82f));
        d.Text(bestX + padX, bestY + padY - 1f, text, color);
    }

    private bool Overlaps(float x, float y, float w, float h) => _placed.Any(r => x < r.X + r.W && x + w > r.X && y < r.Y + r.H && y + h > r.Y);
}
