using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;

namespace MakarovPhysics.Experiments;
public abstract partial class CosmosModule
{
    private void ResetView()
    {
        Camera.ResetFrame();
        Camera.Yaw = _cameraView == 2 ? 0 : InitialYaw;
        Camera.Pitch = _cameraView == 1 ? 1.48f : _cameraView == 2 ? .03f : _cameraView == 3 ? .18f : InitialPitch;
    }

    /// <summary>The body the "Follow" viewpoint keeps in frame (W10): by
    /// default the largest body that is not self-luminous - the planet of a
    /// star-and-planet scene - else the largest body of all; null when the
    /// scene has none. A module with a better idea (the spacecraft, the moon)
    /// overrides.</summary>
    protected virtual CosmicBody? FollowTarget()
    {
        CosmicBody? best = null;
        var bodies = this.Scene.Bodies; // qualified: layercheck reads a bare `Scene.` as a module type
        foreach (var b in bodies)
            if (!b.Emissive && (best is null || b.Size.X > best.Value.Size.X)) best = b;
        if (best is null)
            foreach (var b in bodies)
                if (best is null || b.Size.X > best.Value.Size.X) best = b;
        return best;
    }

    // Camera modes beyond the three fixed viewpoints: 3 follows a body (the
    // target rides on it, the frame fits its disc, the reader's own zoom and
    // orbit stay), 4 is a slow cinematic orbit that a drag interrupts.
    private double _cameraClock;
    private void SteerCamera(float aspect, double now)
    {
        double dt = _cameraClock == 0 ? 0 : Math.Clamp(now - _cameraClock, 0, .1);
        _cameraClock = now;
        if (_cameraView == 3 && FollowTarget() is { } body)
        {
            Camera.Target = body.Center;
            if (!Camera.HandHeld)
            {
                Camera.Distance = Math.Clamp(Camera.FitDistance(body.Size.X * 1.35f, aspect, 1.2f), Camera.MinDistance, Camera.MaxDistance);
            }
            return;
        }
        if (_cameraView == 4 && !Camera.HandHeld)
            Camera.Yaw += (float)(dt * .07);
        Camera.FramePoints(_envelope, aspect, _framing, 1.075f);
    }

    private static string F(double value)
    {
        double v = Math.Abs(value);
        return value.ToString(v != 0 && (v < .001 || v >= 1e6) ? "0.###E+0" : v < 1 ? "0.####" : "0.###");
    }

    private static void FitText(Draw2D d, string text, float x, float y, float width, UiColor color)
    {
        if (width <= 2)
            return;
        if (d.MeasureText(text) > width)
        {
            while (text.Length > 0 && d.MeasureText(text + "…") > width)
                text = text[..^1];
            text += "…";
        }

        d.Text(x, y, text, color);
    }

    private void DrawInstruments(Draw2D d, UiRect a)
    {
        if (!_instruments || !InstrumentsEnabled || Context == null || Context.ViewportHeight / (Context.GetService<UiContext>()?.Scale ?? 1) <= 430)
            return;
        var readings = Readings();
        int count = Math.Min(readings.Length, a.W < 650 ? 2 : 4);
        if (count == 0)
            return;
        float width = (a.W - (count - 1) * 8) / count;
        for (int i = 0; i < count; i++)
        {
            var r = new UiRect(a.X + i * (width + 8), a.Bottom - 67, width, 67);
            var accent = i % 2 == 0 ? SceneDashboard.Blue : SceneDashboard.Gold;
            SceneDashboard.Card(d, r, accent);
            FitText(d, T(readings[i].Key), r.X + 12, r.Y + 12, r.W - 24, SceneDashboard.Muted);
            FitText(d, F(readings[i].Value) + " " + readings[i].Unit, r.X + 12, r.Y + 38, r.W - 24, accent);
        }
    }
}
