namespace MakarovPhysics.RigidBodies2D;

/// <summary>A pin joint between two bodies with an optional motor - enough
/// for a seesaw plank, a swing and the driven wheels of a car.</summary>
public sealed class RevoluteJoint2D
{
    /// <summary>Keep the legacy default; overlapping shafts can opt out of mutual contact.</summary>
    public bool CollideConnected = true;
    internal double MotorImpulse; // accumulated across solver iterations, reset each step
    public int A, B; // body indices
    public double LocalAx, LocalAy; // anchor in A's local frame
    public double LocalBx, LocalBy; // anchor in B's local frame
    public bool MotorEnabled;
    public double MotorSpeed; // target relative angular velocity, rad/s
    public double MaxMotorTorque = 40; // clamp on the motor impulse per second
}
