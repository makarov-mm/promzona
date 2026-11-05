namespace MakarovPhysics.Numerics;

/// <summary>Acceleration function: a = F(x, v, t)/m; the result is written to acc.</summary>
public delegate void Acceleration(double t, double[] x, double[] v, double[] acc);
