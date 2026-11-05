namespace MakarovPhysics.RigidBodies2D;

/// <summary>One rigid body in the plane. Static bodies have InvMass = 0.</summary>
public sealed class Body2D
{
    public Shape2D Shape;
    public double X, Y; // centre of mass
    public double Angle; // radians
    public double Vx, Vy, W; // linear and angular velocity
    public double InvMass, InvI;
    public double Radius; // circles
    public double HalfW, HalfH; // boxes
    public double Friction = 0.45;
    public double Restitution = 0.15;
    public int MaterialIndex; // palette index for the renderer
    public bool Sleeping;
    public double SleepTimer;
    public bool IsStatic => InvMass == 0;

    public void Wake()
    {
        Sleeping = false;
        SleepTimer = 0;
    }

    /// <summary>Velocity of a world point rigidly attached to the body.</summary>
    public (double vx, double vy) VelocityAt(double px, double py) => (Vx - W * (py - Y), Vy + W * (px - X));
}
