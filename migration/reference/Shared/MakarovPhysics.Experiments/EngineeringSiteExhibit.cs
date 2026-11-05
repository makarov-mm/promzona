using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;

namespace MakarovPhysics.Experiments;

/// <summary>A site for the Engineering exhibits that stand in a landscape or a plant room rather than on a bench: the
/// three author views of <see cref="MechanismExhibit"/>, and scenery - ground, water, buildings, a city block -
/// built once by the module and drawn with the experiment in the same depth and shadow passes. Scenery is presentation
/// geometry; it never enters a model. A module whose scenery depends on a setting asks for it to be rebuilt.</summary>
public abstract class EngineeringSiteExhibit : MechanismExhibit
{
    private StudioSurface? _site;
    private ScannedSite? _scans;
    private bool _stale = true;
    private readonly PresentationClock _presentation = new();
    protected override bool DetailedSite => true;
    protected override Vec3? SkySun => LightDirection;
    protected override double EnvironmentTime => _presentation.Seconds;
    protected override double SurfaceTime => _presentation.Seconds;

    protected override void OnVisualFrame(double realDt)
    {
        base.OnVisualFrame(realDt);
        _presentation.Advance(realDt, State == SimulationState.Running);
    }

    protected override void ResetCore()
    {
        _presentation.Reset();
        base.ResetCore();
    }

    protected EngineeringSiteExhibit()
    {
        // sites span a plant room to a valley: let the camera come close to a machine and stand back from a bridge
        Camera.MinDistance = 1;
        Camera.MaxDistance = 75;
    }

    /// <summary>The site's fixed scenery, in the experiment's coordinates.</summary>
    protected abstract void BuildSite(DioramaGeometry g);

    /// <summary>Scenery that wears a photographed map (brick, plaster, slate, rusted iron, boards); drawn in the same
    /// depth, shadow and colour passes as the procedural site, and procedural itself when a map is missing.</summary>
    protected virtual void BuildScannedSite(ScannedSite s) { }

    /// <summary>The scenery is built again before the next frame.</summary>
    protected void InvalidateSite() => _stale = true;

    /// <summary>A choice that changes how the plant is run without starting the experiment again: a pump stopped or a
    /// strategy changed keeps the water where it is.</summary>
    protected void LiveChoice(UiPanel p, string key, string[] choices, int index, Action<int> set)
    {
        Dropdown d = p.Add(new Dropdown(Tr("Module.Lab." + key), choices.Select(k => Tr("Module.Lab." + k)).ToArray(), index));
        d.Changed += set;
    }

    protected override float StudioFloorY => -.04f;
    protected override float SurfaceFill => WorkshopFinish ? .65f : 1.25f;
    protected override Vec3 LightDirection => WorkshopFinish
        ? new Vec3(-.48f, .74f, .55f).Normalized() : base.LightDirection;
    protected override Vec3 LightColor => !WorkshopFinish ? base.LightColor
        : EnvironmentStyle == EnvironmentKind.DaySky ? new(2.7f, 2.55f, 2.3f)
        : Info.Id is "EngGlassBlowing" or "EngPotteryWheel" or "EngMusicBox" ? new(2.8f, 2.45f, 2.05f)
        : new(2.65f, 2.75f, 2.85f);

    private void EnsureSite()
    {
        if (!_stale) return;
        var g = new DioramaGeometry { CaptureSurface = true, BevelBoxes = true, PrecisionFinish = WorkshopFinish };
        BuildSite(g);
        if (WorkshopFinish) ProductionArchitecture.Add(g, Info.Id);
        EngineeringSiteFinish.Add(g, Info.Id);
        EngineeringArchitecture.Add(g, Info.Id);
        LivingLandscape.MeadowSite(g, Info.Id);
        if (Context is { } context)
        {
            _scans ??= new ScannedSite(context.Shared);
            _scans.Begin();
            BuildScannedSite(_scans);
            _scans.Commit(g);
        }
        _site?.Dispose();
        _site = new StudioSurface(Math.Max(3, g.SurfacePoints.Count)) { DetailedFinish = true, WorkshopFinish = WorkshopFinish, Outdoor = EnvironmentStyle == EnvironmentKind.DaySky };
        _site.Update(g.SurfacePoints);
        _stale = false;
    }

    protected override void DrawAdditionalDepth(Mat4 lightSpace)
    {
        EnsureSite();
        _site?.DrawDepth(lightSpace, (float)SurfaceTime);
        _scans?.DrawDepth(lightSpace);
    }

    protected override void DrawAdditionalEmission(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, float time)
    {
        EnsureSite();
        _site?.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, 0, time, emissionOnly: true);
    }
    protected override void DrawAdditionalOpaque(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, uint shadow)
    {
        EnsureSite();
        if (_site is not null) _site.Lighting = SceneLighting;
        _site?.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, shadow, (float)SurfaceTime, environmentFill: SurfaceFill);
        _scans?.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, shadow, SceneLighting, SurfaceFill);
    }

    public override void Dispose()
    {
        _site?.Dispose();
        _site = null;
        _scans?.Dispose();
        _scans = null;
        base.Dispose();
    }
}
