namespace MakarovPhysics.Numerics;

/// <summary>ODE right-hand side: dy/dt = f(t, y); the result is written to dydt.</summary>
public delegate void OdeRhs(double t, double[] y, double[] dydt);
