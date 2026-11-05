using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;

namespace MakarovPhysics.Experiments;
/// <summary>Shared presentation for the robotics workshop: refractive guards,
/// compact instruments and camera presets. Physics remains in each module.</summary>
public abstract class RoboticsExhibit : ExhibitModule
{
    protected readonly DioramaGeometry GuardGeometry = new()
    {
        CaptureSurface = true
    };
    private ExhibitGlass? _guards;
    protected bool Guards = true;
    private UiLabel? _telemetry;
    protected override bool Table => false;
    protected override double MaximumTimeScale => 2;
    protected virtual string StatusKey => "Ready";
    protected virtual float OverviewYaw => .45f;

    protected RoboticsExhibit()
    {
        Dt = 1d / 120;
        Camera.Yaw = .45f;
        Camera.Pitch = .38f;
    }

    public override void Initialize(IModuleContext context)
    {
        Camera.Yaw = OverviewYaw;
        base.Initialize(context);
        _guards = new();
    }

    protected void Command(Action action, bool run = false)
    {
        if (State == SimulationState.Stopped || run)
            Start();
        action();
    }

    public override void BuildUi(object root)
    {
        if (root is not UiPanel p)
            return;
        _telemetry = p.Add(new UiLabel("") { Wrap = true });
        Controls(p);
        var views = p.Add(new Dropdown(Tr("Module.Lab.Camera"), [Tr("Module.Lab.Overview"), Tr("Module.Lab.Close"), Tr("Module.Lab.Top")], 0));
        views.Changed += i =>
        {
            Camera.Yaw = OverviewYaw + (i == 1 ? .75f : 0);
            Camera.Pitch = i == 2 ? 1.15f : i == 1 ? .20f : .38f;
            Camera.ResetFrame();
        };
        p.Add(new Slider(Tr("Module.TimeScale"), () => TimeScale, v => TimeScale = v, .1, 2) { Format = "0.0", Logarithmic = true });
        p.Add(new UiLabel(Tr("Module.Lab.Hint")) { Wrap = true });
        OnUiFrame();
    }

    protected override void OnUiFrame()
    {
        if (_telemetry is not null)
            _telemetry.Text = Tr("Module.Lab." + StatusKey) + "\n" + string.Join("\n", Readings().Select(r => $"{Tr("Module.Lab." + r.Key)}: {r.Value:0.##} {r.Unit}"));
    }

    protected void Row(UiPanel p, string a, Action action, string b, Action other)
    {
        var row = p.Add(new RoboticsRow { DrawBackground = false });
        Button(row, a, action);
        Button(row, b, other);
    }

    protected override void DrawTransparent(Mat4 view, Mat4 projection, Vec3 eye)
    {
        if (!Guards || GuardGeometry.SurfacePoints.Count == 0)
            return;
        _guards?.SetGeometry(GuardGeometry);
        _guards?.Draw(view, projection, eye, .55f);
    }

    public override void Dispose()
    {
        _guards?.Dispose();
        base.Dispose();
    }
}
