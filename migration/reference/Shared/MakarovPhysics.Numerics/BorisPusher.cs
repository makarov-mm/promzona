namespace MakarovPhysics.Numerics;

/// <summary>
/// Boris pusher for a charge in electric and magnetic fields. It rotates velocity
/// exactly in a magnetic field and therefore does not numerically pump energy.
/// </summary>
public static class BorisPusher
{
    /// <param name="qm">Charge-to-mass ratio q/m, C/kg.</param>
    public static void Step(
        ref double vx, ref double vy, ref double vz,
        ref double px, ref double py, ref double pz,
        double ex, double ey, double ez,
        double bx, double by, double bz,
        double qm, double dt)
    {
        if (!double.IsFinite(qm)) throw new ArgumentOutOfRangeException(nameof(qm));
        if (!double.IsFinite(dt)) throw new ArgumentOutOfRangeException(nameof(dt));

        double h = 0.5 * qm * dt;

        // Half-kick from E.
        vx += h * ex;
        vy += h * ey;
        vz += h * ez;

        // Rotation around B.
        double tx = h * bx, ty = h * by, tz = h * bz;
        double t2 = tx * tx + ty * ty + tz * tz;
        double sx = 2 * tx / (1 + t2), sy = 2 * ty / (1 + t2), sz = 2 * tz / (1 + t2);

        double ux = vx + (vy * tz - vz * ty);
        double uy = vy + (vz * tx - vx * tz);
        double uz = vz + (vx * ty - vy * tx);

        vx += uy * sz - uz * sy;
        vy += uz * sx - ux * sz;
        vz += ux * sy - uy * sx;

        // Half-kick from E, then drift.
        vx += h * ex;
        vy += h * ey;
        vz += h * ez;
        px += vx * dt;
        py += vy * dt;
        pz += vz * dt;
    }
}
