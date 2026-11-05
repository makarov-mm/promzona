using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

/// <summary>Two axle tire impulses, bounded by a combined friction circle.
/// Coordinates are metres/seconds/kg, body XY is city XZ; body angle = -heading.
/// Collision response itself is supplied by the shared Sandbox2D rigid-body solver.</summary>
public static class VehicleDynamics
{
    // A wreck has no motor or steering. Retain momentum and the solver's finite
    // mass/inertia; support friction dissipates energy without cancelling impacts.
    internal static void StepWreck(CityCar car, double dt)
    {
        var b = car.Body;
        bool supported = car.WreckSupported || car.Lift <= .03 && car.VerticalVelocity <= 0;
        if (supported)
        {
            if (car.TerrainActive && car.Lift <= .03)
            {
                double h = -b.Angle;
                double forward = -9.81 * Math.Sin(car.TerrainPitch);
                double side = 9.81 * Math.Sin(car.TerrainRoll);
                b.Vx += (forward * Math.Sin(h) + side * Math.Cos(h)) * dt;
                b.Vy += (forward * Math.Cos(h) - side * Math.Sin(h)) * dt;
            }
            double speed = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
            double friction = Math.Max(0, 1 - 3.4 * dt / Math.Max(1e-9, speed));
            b.Vx *= friction; b.Vy *= friction;
        }
        double drag = Math.Exp(-dt * (supported ? .16 : .12));
        b.Vx *= drag; b.Vy *= drag;
        b.W *= Math.Exp(-dt * (supported ? 2.4 : .25));
        if (b.Vx * b.Vx + b.Vy * b.Vy > .0001 || Math.Abs(b.W) > .02) b.Wake();
    }

    public static void Step(CityCar car, DriveInput raw, double dt, double traction = 1)
    {
        DriveInput input = raw.Sanitized();
        Body2D b = car.Body;

        if (car.Falling) return;
        if (car.Destroyed) { StepWreck(car, dt); return; }

        if (car.Recovering)
        {
            b.Vx = b.Vy = b.W = 0;
            return;
        }

        if (!car.Grounded)
        {
            b.Vx *= Math.Exp(-.12 * dt);
            b.Vy *= Math.Exp(-.12 * dt);
            b.W *= Math.Exp(-.5 * dt);
            return;
        }

        double heading = -b.Angle, s = Math.Sin(heading), c = Math.Cos(heading);

        if (car.TerrainActive)
        {
            CityPoint slope = CityTerrain.Current.Gradient(b.X, b.Y);
            double factor = 9.81 / (1 + slope.X * slope.X + slope.Z * slope.Z);
            b.Vx -= slope.X * factor * dt;
            b.Vy -= slope.Z * factor * dt;
        }

        double speed = b.Vx * s + b.Vy * c;
        double steerLimit = .68 / (1 + Math.Pow(Math.Abs(speed) / 22, 2));
        car.Steering += Math.Clamp(input.Steering * steerLimit - car.Steering, -1.8 * dt, 1.8 * dt);
        bool opposing = input.Throttle * speed < -.35;
        bool braking = input.Brake || opposing;
        car.Braking = braking || input.Handbrake;

        double engine = car.Kind switch
        {
            11 => 185000,
            10 => 26000,
            8 => 14000,
            7 => 24000,
            6 => 14000,
            5 => 24000,
            1 => 10500,
            2 => 18000,
            3 => 10500,
            4 => 14000,
            _ => 8200
        };

        double maximum = car.Kind==11?14:car.Kind is 2 or 5 or 10 ? 19 : 27;
        double pedal = braking || input.Handbrake ? 0 : input.Throttle;
        double force = pedal * engine * Math.Clamp(1 - Math.Abs(speed) / (pedal < 0 ? 7 : maximum), 0, 1);
        double drag = .018 * car.Mass * 9.81 + .43 * speed * speed;
        double rolling = Math.CopySign(Math.Min(Math.Abs(speed) * car.Mass / dt, drag), speed);
        double longitudinal = (force - rolling) * dt * .5;
        // Space is a front-biased emergency stop. A steered, fast entry can loosen
        // the rear, but a straight stop retains lateral control on both axles.
        double rearSlip = input.Handbrake
            ? Math.Clamp((Math.Abs(speed) - 5) / 10, 0, 1) * Math.Min(1, Math.Abs(car.Steering) / steerLimit)
            : 0;
        double frontShare = input.Handbrake ? .58 : .5;
        double surfaceGrip = .88 * 9.81 * Math.Cos(car.TerrainPitch) * Math.Cos(car.TerrainRoll)
            * Math.Clamp(traction, .5, 1);
        b.Wake();

        // Rear then front: point velocity includes angular velocity, so sideways
        // impacts create slip and rotation instead of being overwritten by a route.
        for (int axle = -1; axle <= 1; axle += 2)
        {
            double rx = s * car.Wheelbase * .5 * axle, ry = c * car.Wheelbase * .5 * axle;
            double wheel = heading + (axle > 0 ? car.Steering : 0);
            double fx = Math.Sin(wheel), fy = Math.Cos(wheel), ly = -fx;
            double vx = b.Vx - b.W * ry, vy = b.Vy + b.W * rx;
            double cross = rx * ly - ry * fy, effective = b.InvMass + cross * cross * b.InvI;
            double lateralRate = axle < 0 ? 18 - 8 * rearSlip : 18;
            double lateral = -(vx * fy + vy * ly) * (1 - Math.Exp(-dt * lateralRate)) / effective;
            double axleLoad = car.Mass * (axle > 0 ? frontShare : 1 - frontShare);
            double drive = longitudinal;

            if (braking || input.Handbrake)
            {
                double wheelSpeed = vx * fx + vy * fy;
                double lever = rx * fy - ry * fx;
                double stop = -wheelSpeed / (b.InvMass + lever * lever * b.InvI);
                double brakeLimit = axleLoad * (input.Handbrake ? 9.5 : 7.8) * dt;
                drive = Math.Clamp(stop, -brakeLimit, brakeLimit);
            }

            double grip = axleLoad * surfaceGrip * dt * (axle < 0 ? 1 - .12 * rearSlip : 1);
            double impulse = Math.Sqrt(drive * drive + lateral * lateral);

            if (impulse > grip)
            {
                double scale = grip / impulse;
                drive *= scale;
                lateral *= scale;
            }

            ApplyImpulse(b, fx * drive + fy * lateral, fy * drive + ly * lateral, rx, ry);
        }

        if (input.Handbrake)
        {
            // Arcade stability assist, expressed as a bounded yaw correction.
            // Follow the steered turn within the available road grip; centring
            // the wheels arrests a developing spin even with the brake held.
            double yawLimit = surfaceGrip / Math.Max(5, Math.Abs(speed));
            double targetYaw = Math.Clamp(-speed * Math.Tan(car.Steering) / car.Wheelbase, -yawLimit, yawLimit);
            double correction = (targetYaw - b.W) * (1 - Math.Exp(-7 * dt));
            double maxCorrection = surfaceGrip * dt / car.Wheelbase;
            b.W += Math.Clamp(correction, -maxCorrection, maxCorrection);
        }

        b.W *= Math.Exp(-.12 * dt);
    }

    public static void ApplyImpulse(Body2D b, double jx, double jy, double rx = 0, double ry = 0)
    {
        b.Wake();
        b.Vx += jx * b.InvMass;
        b.Vy += jy * b.InvMass;
        b.W += (rx * jy - ry * jx) * b.InvI;
    }
}
