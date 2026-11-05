using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;

namespace MakarovPhysics.Experiments;
/// <summary>Opt-in display-only disassembly. No model or simulation callbacks.
/// Part anchors and geometry receive exactly the same world translation.</summary>
public readonly record struct AssemblyStudyPart(Vec3 Anchor, Vec3 Travel);
public sealed class AssemblyStudy : IDisposable
{
    private readonly IModuleContext _context;
    private readonly Func<string, string> _text;
    private readonly Action _frame;
    private readonly AssemblyStudyPart[] _parts;
    private readonly PartOverlay? _overlay;
    private UiPanel? _panel;
    private Checkbox? _toggle;
    private Dropdown? _picker;
    private UiLabel? _info;
    private SceneProjection? _projection;
    private bool _active;
    private float _amount;
    public bool Active => _active;
    public float Amount => _active ? _amount : 0;
    public bool Separated => Amount > .001f;
    public int Selected { get; private set; }
    public int Count => _parts.Length;

    public AssemblyStudy(IModuleContext context, Func<string, string> text, Action frame, AssemblyStudyPart[] parts)
    {
        _context = context;
        _text = text;
        _frame = frame;
        _parts = parts;
        if (context.GetService<UiContext>()is { } ui)
            _overlay = ui.Root.Add(new PartOverlay(this));
    }

    private string T(string key) => _text("Module.Study." + key);
    public void SetActive(bool active)
    {
        _active = active;
        if (_toggle is not null)
            _toggle.Value = active;
        if (_panel is not null)
            _panel.Visible = active;
        Refresh();
        _frame();
    }

    public void SetAmount(double value)
    {
        if (!double.IsFinite(value))
            return;
        _amount = (float)Math.Clamp(value, 0, 1);
        Refresh();
        _frame();
    }

    public void Select(int part)
    {
        Selected = Math.Clamp(part, 0, Count);
        if (_picker is not null)
            _picker.Selected = Selected;
        Refresh();
        if (Active)
            RevealInfo();
    }

    private void RevealInfo()
    {
        if (_info is null)
            return;
        _info.Ui?.Relayout();
        for (Widget? parent = _info.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not ScrollArea scroll)
                continue;
            for (int step = 0; step < 32; step++)
            {
                float top = scroll.Rect.Y + 8, bottom = scroll.Rect.Bottom - 8;
                bool tall = _info.Rect.H > bottom - top;
                if (_info.Rect.Y >= top && (tall || _info.Rect.Bottom <= bottom))
                    break;
                float previous = _info.Rect.Y;
                if (!scroll.OnMouseWheel(_info.Rect.Y < top ? 1 : -1) || Math.Abs(previous - _info.Rect.Y) < .1f)
                    break;
            }

            break;
        }
    }

    private void Refresh()
    {
        if (_info is not null)
            _info.Text = T(Selected == 0 ? "Path" : "Info" + Selected) + "\n" + T("Note");
    }

    public Vec3 Offset(int part) => part > 0 && part <= Count ? _parts[part - 1].Travel * Amount : Vec3.Zero;
    public Vec3 Anchor(int part) => _parts[part - 1].Anchor + Offset(part);
    public Vec3 Tint(int part, Vec3 color) => Active && part > 0 && Selected == part ? color * .65f + new Vec3(.35f, .24f, .075f) : color;
    public void BuildUi(UiPanel parent)
    {
        _toggle = parent.Add(new Checkbox(T("Title"), Active));
        _toggle.Changed += SetActive;
        _panel = parent.Add(new UiPanel { DrawBackground = false, Visible = Active });
        _panel.Add(new Slider(T("Explode"), () => _amount, SetAmount, 0, 1) { Format = "0.00" });
        _picker = _panel.Add(new Dropdown(T("Part"), Enumerable.Range(0, Count + 1).Select(i => T("Part" + i)).ToArray(), Selected));
        _picker.Changed += Select;
        _panel.Add(new UiButton(T("Assemble"), () => SetAmount(0)));
        _info = _panel.Add(new UiLabel("") { Wrap = true });
        Refresh();
    }

    public void Project(OrbitCamera camera)
    {
        float scale = _context.GetService<UiContext>()?.Scale ?? 1;
        _projection = new SceneProjection(camera.View(), camera.Projection((float)_context.ViewportWidth / Math.Max(1, _context.ViewportHeight)), _context.ViewportWidth, _context.ViewportHeight, scale);
    }

    /// <summary>Move only a newly appended vertex range; normals and local material coordinates stay unchanged.</summary>
    public void Move(DioramaGeometry g, int firstPoint, int firstSurface, int part)
    {
        if (!Active || (Amount == 0 && Selected != part))
            return;
        Vec3 offset = Offset(part);
        for (int i = firstPoint; i < g.Points.Count; i++)
        {
            var v = g.Points[i];
            g.Points[i] = new(v.Pos + offset, Tint(part, v.Color));
        }

        for (int i = firstSurface; i < g.SurfacePoints.Count; i++)
        {
            var v = g.SurfacePoints[i];
            g.SurfacePoints[i] = v with
            {
                Position = v.Position + offset,
                Color = Tint(part, v.Color)
            };
        }
    }

    private sealed class PartOverlay(AssemblyStudy study) : Widget
    {
        public readonly List<(UiRect Rect, int Part)> Targets = [];
        public override Widget? HitTest(float x, float y) => Shown && study.Active && Targets.Any(t => t.Rect.Contains(x, y)) ? this : null;
        public override bool OnMouseDown(float x, float y, MouseButton button)
        {
            if (button != MouseButton.Left || !study.Active)
                return false;
            foreach (var t in Targets)
                if (t.Rect.Contains(x, y))
                {
                    study.Select(t.Part);
                    return true;
                }

            return false;
        }

        public override void Draw(Draw2D d)
        {
            Targets.Clear();
            if (!Shown || !study.Active || study._projection is not { } projection)
                return;
            float scale = study._context.GetService<UiContext>()?.Scale ?? 1;
            float width = study._context.ViewportWidth / scale, height = study._context.ViewportHeight / scale;
            var insets = study._context.GetService<SceneInsets>();
            float top = Math.Max(56, height * (insets?.Top ?? 0) + 6), bottom = height * (1 - (insets?.Bottom ?? 0)) - 8;
            for (int part = 1; part <= study.Count; part++)
            {
                if (!projection.TryWorldToScreen(study.Anchor(part), out float x, out float y))
                    continue;
                var box = new UiRect(Math.Clamp(x + 20, 8, Math.Max(8, width - 36)), Math.Clamp(y - 40, top, Math.Max(top, bottom - 28)), 28, 28);
                // Separate nearby labels while leader lines keep the exact part anchor.
                float preferredY = box.Y;
                for (int pass = 0; pass < CountLimit; pass++)
                {
                    if (!Targets.Any(t => Math.Abs(t.Rect.X - box.X) < 30 && Math.Abs(t.Rect.Y - box.Y) < 30))
                        break;
                    float offset = (pass / 2 + 1) * 31 * (pass % 2 == 0 ? 1 : -1);
                    box = new UiRect(box.X, Math.Clamp(preferredY + offset, top, Math.Max(top, bottom - 28)), 28, 28);
                }

                var color = study.Selected == part ? SceneDashboard.Gold : SceneDashboard.Blue;
                d.Line(x, y, box.X + 14, box.Y + 14, color);
                d.Rect(box.X, box.Y, 28, 28, UiColor.FromBytes(13, 23, 34));
                d.RectOutline(box.X, box.Y, 28, 28, color, study.Selected == part ? 2 : 1);
                string number = part.ToString();
                d.Text(box.X + (28 - d.MeasureText(number)) / 2, box.Y + 6, number, color);
                Targets.Add((box, part));
            }
        }

        private const int CountLimit = 12;
    }

    public void Dispose()
    {
        if (_overlay is not null)
        {
            _overlay.Visible = false;
            _overlay.Targets.Clear();
        }
    }
}
