namespace MakarovPhysics.Numerics;

/// <summary>Double-precision inertial coordinates. Units are chosen by the caller.</summary>
public readonly record struct AstroVector(double X, double Y, double Z = 0)
{
    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);
    public AstroVector Unit => Length > 0 ? this / Length : default;
    public static AstroVector operator +(AstroVector a, AstroVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static AstroVector operator -(AstroVector a, AstroVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static AstroVector operator -(AstroVector a) => a * -1;
    public static AstroVector operator *(AstroVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static AstroVector operator /(AstroVector a, double b) => a * (1 / b);
    public static double Dot(AstroVector a, AstroVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static AstroVector Cross(AstroVector a, AstroVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
