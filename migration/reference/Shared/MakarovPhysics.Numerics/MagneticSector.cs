namespace MakarovPhysics.Numerics;

public static class MagneticSector
{
    public static double Radius(double volts, double massU, double charge, double field) => Math.Sqrt(2 * volts * massU * 1.66053906660e-27 / (charge * 1.602176634e-19)) / field;
}
