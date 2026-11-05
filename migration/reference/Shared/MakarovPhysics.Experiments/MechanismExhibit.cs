using MakarovPhysics.Renderer;
using MakarovPhysics.UI;

namespace MakarovPhysics.Experiments;

/// <summary>Inspection lighting and three author views for the Engineering transmission benches.
/// View changes never reset the solver. Automatic framing adapts to the panel and window size;
/// dragging or zooming keeps the reader's framing until a view button is pressed.</summary>
public abstract class MechanismExhibit : ExhibitModule
{
    private int _view;
    protected virtual bool ShowCameraViews => true;
    private bool _overviewCaptured;
    private float _overviewYaw, _overviewPitch;
    protected override bool Table => false;
    protected override bool StageVisible => false;
    protected override float StudioFloorY => -.07f;
    protected override float SurfaceFill => 1.8f;
    protected override EnvironmentKind EnvironmentStyle => EnvironmentKind.Studio;

    // Fixed envelopes include the full moving mechanism, not a single frozen pose.
    // Detail views intentionally omit the motor and the back of the stand.
    protected virtual (Vec3 Low, Vec3 High) DetailBounds => Info.Id switch
    {
        "EngHelicalGears" => (new(-1.12f,.45f,-.3f),new(1.42f,2.15f,.42f)),
        "EngBevelGears" => (new(-.55f,.65f,-.35f),new(1.1f,2f,.85f)),
        "EngPlanetaryGear" => (new(-1.15f,.35f,-.25f),new(1.15f,2.3f,.52f)),
        "EngCompoundPlanetary" => (new(-.95f,.45f,-.75f),new(.95f,2.2f,.85f)),
        "EngHarmonicDrive" => (new(-.77f,.42f,-.13f),new(.77f,2.02f,.64f)),
        "EngCycloidalReducer" => (new(-.83f,.38f,-.15f),new(.83f,2.05f,.8f)),
        "EngDifferential" => (new(-.65f,.65f,-.35f),new(.65f,1.95f,.65f)),
        "EngFrictionClutch" => (new(-.9f,.4f,-.52f),new(.65f,1.95f,.52f)),
        "EngBallScrew" => (new(-1.1f,.38f,-.34f),new(1.15f,1.35f,.38f)),
        "EngLeadScrew" => (new(-.6f,.3f,-.34f),new(.6f,2.3f,.45f)),
        "EngCamFollower" => (new(-.62f,.32f,-.14f),new(.62f,2.6f,.48f)),
        "EngFourBar" => (new(-1.5f,.18f,-1.15f),new(1.5f,.85f,1.15f)),
        _ => (Lower + new Vec3(.12f,.22f,.12f), Upper - new Vec3(.12f,.12f,.08f))
    };

    public override void BuildUi(object root)
    {
        if (root is UiPanel panel && ShowCameraViews)
        {
            if (!_overviewCaptured)
            {
                _overviewYaw = Camera.Yaw;
                _overviewPitch = Camera.Pitch;
                _overviewCaptured = true;
            }
            // Separate full-width buttons remain legible at 150% and in CJK.
            var row = panel.Add(new UiPanel { DrawBackground = false });
            var buttons = new List<UiButton>();
            string[] keys = ["Camera.Overview", "Camera.Close", "Camera.Oblique"];
            for (int i = 0; i < keys.Length; i++)
            {
                int index = i;
                var b = row.Add(new UiButton(Tr(keys[i]), () =>
                {
                    _view = index;
                    Camera.Yaw = index == 0 ? _overviewYaw : index == 1 ? DetailYaw : ObliqueYaw;
                    Camera.Pitch = index == 0 ? _overviewPitch : index == 1 ? DetailPitch : ObliquePitch;
                    Camera.ResetFrame();
                    for (int j = 0; j < buttons.Count; j++) buttons[j].Selected = j == index;
                }) { Selected = i == _view });
                buttons.Add(b);
            }
        }
        base.BuildUi(root);
    }

    protected virtual (Vec3 Low, Vec3 High) ObliqueBounds => (Lower, Upper);
    protected virtual float ObliqueYaw => -.65f;
    protected virtual float ObliquePitch => .55f;
    protected virtual float DetailYaw => Info.Id switch
    {
        "EngBevelGears" => .68f,
        "EngDifferential" => -.45f,
        "EngFrictionClutch" => .5f,
        "EngBallScrew" or "EngLeadScrew" => -.2f,
        "EngCompoundPlanetary" => .48f,
        _ => .05f
    };
    protected virtual float DetailPitch => Info.Id switch
    {
        "EngFourBar" => 1.45f,
        "EngFrictionVariator" => .65f,
        "EngBevelGears" or "EngDifferential" => .38f,
        "EngBallScrew" => .16f,
        _ => .10f
    };
    protected override void FrameCamera(float aspect, SceneInsets? insets)
    {
        var bounds = _view == 1 ? DetailBounds : _view == 2 ? ObliqueBounds : (Lower, Upper);
        Camera.FrameScene(bounds.Item1, bounds.Item2, aspect, insets, 1.08f);
    }
}
