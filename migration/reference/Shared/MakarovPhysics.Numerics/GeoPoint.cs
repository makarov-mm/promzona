namespace MakarovPhysics.Numerics;

/// <summary>Double-precision Euclidean constructions, independent of presentation.</summary>
public readonly record struct GeoPoint(double X, double Y)
{
    public static GeoPoint operator +(GeoPoint a, GeoPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static GeoPoint operator -(GeoPoint a, GeoPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static GeoPoint operator *(GeoPoint a, double s) => new(a.X * s, a.Y * s);
    public static GeoPoint operator /(GeoPoint a, double s) => new(a.X / s, a.Y / s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public bool Finite => double.IsFinite(X) && double.IsFinite(Y);
    public GeoPoint Perp => new(-Y, X);
    public static double Dot(GeoPoint a, GeoPoint b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(GeoPoint a, GeoPoint b) => a.X * b.Y - a.Y * b.X;
    public static GeoPoint Polar(double angle, double radius = 1) => new(radius * Math.Cos(angle), radius * Math.Sin(angle));
}
