namespace Promzona.Game;

public sealed partial class CityTraffic
{
    // Destroyed is a gameplay state, never an infinite-mass collision body.
    // Also used when loading old saves, including airborne frozen helicopters.
    private static void RestoreWreckBody(CityCar c)
    {
        c.Body.InvMass = 1 / c.Mass;
        c.Body.InvI = 3 / (c.Mass * (c.HalfWidth * c.HalfWidth + c.HalfLength * c.HalfLength));
        c.Body.Wake();
        c.Command = default;
        c.Driverless = true;
        c.Braking = c.PanicDriver = c.AirDeparting = false;
        c.RecoveryTime = 0;
        c.Phase = 2; // No route layer lock or automatic recovery after destruction.
        c.WreckSupported = false;
    }

    private void WreckAfterContacts(CityCar c, double dt)
    {
        double dx = c.Body.Vx - c.LastVx, dz = c.Body.Vy - c.LastVz;
        double hit = Math.Sqrt(dx * dx + dz * dz);
        if (hit > 1.5 && c.ImpactCooldown <= 0)
        {
            c.ImpactCooldown = .4;
            c.RollVelocity += Math.Clamp((dx * Math.Cos(c.Heading) - dz * Math.Sin(c.Heading)) * .16, -3, 3);
            c.PitchVelocity += Math.Clamp((dx * Math.Sin(c.Heading) + dz * Math.Cos(c.Heading)) * .09, -2, 2);
            c.VerticalVelocity = Math.Max(c.VerticalVelocity, Math.Min(1.5, Math.Max(0, hit - 6) * .10));
            Sound("impact", c.Position, Math.Min(.8, hit / 12));
            Emit(c.Body.X, c.Lift + .5, c.Body.Y, 2, 6, 2, c.RoadLevel);
        }
        WreckVerticalStep(c, dt, true);
    }

    private void FarWreckStep(CityCar c, double dt)
    {
        // No contacts outside the local physics bubble. Keep ballistic motion and
        // surface support so leaving and returning cannot freeze a wreck in midair.
        UpdateTerrainSupport(c, true);
        VehicleDynamics.StepWreck(c, dt);
        c.Body.X += c.Body.Vx * dt;
        c.Body.Y += c.Body.Vy * dt;
        c.Body.Angle += c.Body.W * dt;
        UpdateTerrainSupport(c, true);
        WreckVerticalStep(c, dt, false);
    }

    private void WreckVerticalStep(CityCar c, double dt, bool effects)
    {
        double previous = c.SurfaceHeight + c.Lift;
        c.VerticalVelocity -= 9.81 * dt;
        double current = previous + c.VerticalVelocity * dt;
        double support = c.SurfaceHeight;
        void Top(double height)
        {
            if (previous >= height - .04 && current <= height + .025) support = Math.Max(support, height);
        }
        if (_useScenery)
        {
            // Roof queries use city buckets for both local and far wrecks.
            var layout = CityLayout.Current;
            int reach = 1 + (int)Math.Ceiling(c.HalfLength / Spacing);
            int ci = BlockIndex(c.Body.X), cj = BlockIndex(c.Body.Y);
            for (int j = cj - reach; j <= cj + reach; j++)
            for (int i = ci - reach; i <= ci + reach; i++)
            foreach (int id in layout.BuildingIds(i, j))
            {
                var block = layout.Blocks[id];
                double top = CityTerrain.Ground(block.X, block.Z) + block.Height;
                if (previous >= top - .04 && current <= top + .025 &&
                    HelicopterFootprint(c, block.X, block.Z, block.HalfX, block.HalfZ, 0)) Top(top);
            }
            foreach (var road in CityRoadLevels.All)
                if (!road.Tunnel && road.Contains(c.Body.X, c.Body.Y)) Top(road.Floor(c.Body.X, c.Body.Y));
            if (c.Physical)
            foreach (var slab in _activeScenery)
            {
                if (!_sceneryHeights.TryGetValue(slab, out var bounds) && !_levelStatics.TryGetValue(slab, out bounds)) continue;
                if (previous < bounds.High - .04 || current > bounds.High + .025) continue;
                var extent = SceneryExtent(slab);
                if (Math.Abs(c.Body.X - slab.X) > extent.X + c.HalfLength || Math.Abs(c.Body.Y - slab.Y) > extent.Z + c.HalfLength) continue;
                if (HelicopterFootprint(c, slab.X, slab.Y, Math.Max(slab.HalfW, slab.Radius), Math.Max(slab.HalfH, slab.Radius), -slab.Angle)) Top(bounds.High);
            }
        }
        c.WreckSupported = current <= support + .025 && c.VerticalVelocity <= 0;
        if (c.WreckSupported)
        {
            double landingSpeed = -c.VerticalVelocity;
            current = support;
            c.VerticalVelocity = landingSpeed > 1.8 ? landingSpeed * .12 : 0;
            if (c.VerticalVelocity > 0) c.WreckSupported = false;
            if (effects && landingSpeed > 2 && (c.Position - PlayerPosition).Length < 160)
            {
                Sound("impact", c.Position, Math.Min(1, landingSpeed / 12));
                Emit(c.Body.X, current - c.SurfaceHeight + .3, c.Body.Y, 1, 8, 1.5, c.RoadLevel);
            }
        }
        c.Lift = Math.Max(0, current - c.SurfaceHeight);
        if (c.WreckSupported)
        {
            c.RollVelocity -= 9 * Math.Sin(2 * c.Roll) * dt;
            c.PitchVelocity -= 12 * Math.Sin(2 * c.Pitch) * dt;
        }
        double damping = Math.Exp(-dt * (c.WreckSupported ? 2.8 : .18));
        c.RollVelocity *= damping; c.PitchVelocity *= damping;
        c.Roll += c.RollVelocity * dt; c.Pitch += c.PitchVelocity * dt;
        // No thrust; the final rotor coasts down after the aircraft is destroyed.
        if (c.Kind == 12) c.RotorPhase = (c.RotorPhase + dt * 28 * Math.Exp(-Math.Max(0, Time - c.DeadSince) * 1.5)) % Math.Tau;
    }
}
