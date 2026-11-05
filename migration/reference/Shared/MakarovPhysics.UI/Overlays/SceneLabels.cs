using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Overlays;

/// <summary>
/// Plain text anchored to points in the SCENE.
///
/// VectorOverlay already draws a label chip at the tip of an arrow, and every
/// module that wanted to name something in the scene had to invent an arrow to
/// hang the name on. Lagrange wanted the opposite: five points that need
/// naming and nothing to point at. Eight spheres in three colours, with the
/// legend living in a side panel, is a picture that has to be decoded rather
/// than read (09.08).
///
/// Same chip and the same stepping-aside as VectorOverlay, and for the same
/// reason: five markers close together produce five labels in one place, which
/// is worse than no labels at all. Here the step is straight up, because a
/// point has no direction of its own to move perpendicular to.
/// </summary>
public sealed class SceneLabels : Widget
{
    /// <summary>One name pinned to a world point. `Essential` marks a pin
    /// that IS the data - a bit's value, a point's letter, an algorithm's
    /// name, a reading - as against a caption that explains it; the
    /// captions switch hides only the latter (external audit 22.09, A03:
    /// with captions off the Hamming bits and the sorting race's row names
    /// vanished with the prose).</summary>
    public readonly record struct Pin(Vec3 Position, string Text, UiColor Color, bool Essential = false);

    private readonly List<Pin> _pins = [with(16)];
    private readonly List<UiRect> _placed = [with(16)];

    public SceneProjection Projection { get; set; }

    /// <summary>Pixels between the anchor point and the bottom of the chip -
    /// enough that the label clears the marker it names.</summary>
    public float Lift = 18f;

    /// <summary>Opt-in safe area for diagram labels and wrapped captions.</summary>
    public SceneInsets? Insets;

    /// <summary>Chip height follows the text size the host is drawing at.</summary>
    public float FontSize = 13f;

    public void Begin()
    {
        _pins.Clear();
        _placed.Clear();
    }

    public void Add(in Pin pin) => _pins.Add(pin);
    public void Add(Vec3 position, string text, UiColor color) => _pins.Add(new Pin(position, text, color));
    /// <summary>A pin that stays when the captions are off: a value, a
    /// letter, a name the experiment cannot be read without.</summary>
    public void AddEssential(Vec3 position, string text, UiColor color) => _pins.Add(new Pin(position, text, color, Essential: true));
    public void End() { }

    /// <summary>A readout never intercepts a click meant for the experiment.</summary>
    public override Widget? HitTest(float x, float y) => null;

    /// <summary>The host's "scene captions" checkbox (02.09): one switch for
    /// every module's pins - the story line, the "try" line, the part names.
    /// Off, the pins are still collected (modules keep calling Add) and
    /// simply not drawn, so nothing in a module changes.
    ///
    /// Named ShowCaptions since 12.09. It was `Enabled`, which HID
    /// Widget.Enabled (CS0108) - and the two mean different things: this one
    /// is static and global, that one is per-widget and instance. A reader
    /// who set `labels.Enabled = false` expecting one pin panel to go quiet
    /// would have silenced every caption in the suite, and the compiler said
    /// so in a warning nobody read.</summary>
    public static bool ShowCaptions = true;

    public override void Draw(Draw2D d)
    {
        if (!Projection.IsValid || _pins.Count == 0) return;
        _placed.Clear();

        foreach (bool essential in new[] { true, false })
        foreach (Pin pin in _pins)
        {
            if (pin.Essential != essential) continue;
            if (!ShowCaptions && !pin.Essential) continue;
            // Behind the camera, or off the side: TryWorldToScreen says so,
            // and a label drawn anyway would sit at a mirrored position, which
            // is worse than a missing one.
            if (!Projection.TryWorldToScreen(pin.Position, out float x, out float y)) continue;

            DrawChip(d, x, y, pin.Text, pin.Color);
        }
    }

    /// <summary>
    /// Draw the LEADER LINE from a label to the thing it names.
    ///
    /// A chip floating near an object says "something around here"; a line to
    /// a dot on the object says "this". It is the single cheapest thing that
    /// makes a scene read as a technical illustration instead of a picture
    /// with captions, and it is what the reference simulations do everywhere
    /// - "Vacuum Chamber" with a line to the chamber, "Cathode" with a line
    /// to the cathode (09.08).
    ///
    /// Drawn BEFORE the chip so the chip covers the line's end rather than
    /// the line crossing the text.
    /// </summary>
    public bool Leaders = true;

    private void DrawChip(Draw2D d, float anchorX, float anchorY, string text, UiColor color)
    {
        float textScale=Math.Clamp(Projection.LogicalHeight/1000f,1,1.4f);
        float x = anchorX, y = anchorY - Lift*textScale;
        const float padX = 5f, padY = 3f;
        float limit = Insets is null ? float.MaxValue : MathF.Max(24, MathF.Min(600, Projection.LogicalWidth - 24) - 2 * padX);
        float Measure(string value)=>d.MeasureText(value)*textScale;
        var lines = SceneDashboard.Wrap(text,limit,Measure);
        float w = lines.Max(Measure) + 2f * padX;
        float lineHeight = (Insets is null ? FontSize : MathF.Max(19, FontSize + 3))*textScale;
        float h = lines.Count * lineHeight + 2f * padY;
        float top = Insets is null ? 2 : Projection.LogicalHeight * Insets.Top + 4;
        float bottom = Insets is null ? Projection.LogicalHeight - 2 : Projection.LogicalHeight * (1 - Insets.Bottom) - 4;

        x -= w * 0.5f; // centred over the point it names

        float bestX = x, bestY = y;

        for (int attempt = 0; attempt < 15; attempt++)
        {
            int sign = attempt % 2 == 0 ? 1 : -1;
            float distance = (attempt + 1) / 2 * (h + 3f);

            float tx = Math.Clamp(x, 2f, MathF.Max(2f, Projection.LogicalWidth - w - 2f));
            float ty = Math.Clamp(y - distance * sign, top, MathF.Max(top, bottom - h));

            if (attempt == 0)
            {
                bestX = tx;
                bestY = ty;
            }

            if (!Overlaps(tx, ty, w, h))
            {
                bestX = tx; 
                bestY = ty; 
                break;
            }
        }

        _placed.Add(new UiRect(bestX, bestY, w, h));

        if (Leaders && anchorY >= top && anchorY <= bottom)
        {
            // from the nearest edge of the chip to the anchor, plus a dot ON
            // the thing: the dot is what turns "near" into "this"
            float cx = bestX + w * 0.5f;
            float cy = bestY + (anchorY > bestY ? h : 0f);
            var faint = new UiColor(color.R, color.G, color.B, 0.55f);
            d.Line(cx, cy, anchorX, anchorY, faint);
            d.Rect(anchorX - 2f, anchorY - 2f, 4f, 4f, faint);
        }

        d.Rect(bestX, bestY, w, h, new UiColor(0.03f, 0.035f, 0.05f, 0.78f));
        for (int i = 0; i < lines.Count; i++)
            d.TextScaled(bestX + padX, bestY + padY - 1f + i * lineHeight, lines[i], color,textScale);
    }

    private bool Overlaps(float x, float y, float w, float h) => _placed.Any(r => x < r.X + r.W && x + w > r.X && y < r.Y + r.H && y + h > r.Y);
}
