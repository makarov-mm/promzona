namespace Promzona.Game;

/// <summary>Signed pedals: W forward, S brake/reverse; positive steering turns toward +X when heading is zero.</summary>
public readonly record struct DriveInput(double Throttle, double Steering, bool Brake = false, bool Handbrake = false)
{
    public DriveInput Sanitized() => new(double.IsFinite(Throttle) ? Math.Clamp(Throttle, -1, 1) : 0, double.IsFinite(Steering) ? Math.Clamp(Steering, -1, 1) : 0, Brake, Handbrake);
}
