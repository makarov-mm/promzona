using Promzona.Game;
using System.Text.Json;

static partial class Tests
{
    static void RunWreckPhysics()
    {
        foreach (int kind in Enumerable.Range(0, 15)) Test("wreck body " + kind, () =>
        {
            var t = FootSandbox();
            var c = Npc(t, kind, 40, 0);
            if (kind == 9) { c.TransitRoute = 1; c.Body.InvI = 0; }
            c.Body.Vx = 4; c.Body.Vy = 2; c.Body.W = .3;
            c.Lift = 10; c.VerticalVelocity = -2; c.RollVelocity = .2;
            c.RecoveryTime = .3;
            Call(t, "Explode", c);
            Check(c.Destroyed && c.Driverless && !c.Recovering, "wreck has no driver or automatic recovery: " + kind);
            Check(Math.Abs(1 / c.Body.InvMass - c.Mass) < 1e-7 && c.Body.InvI > 0, "finite mass and rotational inertia: " + kind);
            Check(c.Body.Vx == 4 && c.Body.Vy == 2 && c.Body.W == .3 && c.VerticalVelocity == -2, "destruction retains momentum: " + kind);
            double oldX = c.Position.X, oldLift = c.Lift;
            for (int n = 0; n < 60; n++) t.Step(1d / 60, new(1, 1));
            Check(c.Position.X > oldX + 2 && c.Lift < oldLift - 4, "wreck translates and falls without route/motor constraints: " + kind);
            for (int n = 0; n < 180; n++) t.Step(1d / 60);
            Check(c.Lift < .03 && Math.Abs(c.VerticalVelocity) < .01 && double.IsFinite(c.Roll + c.Pitch), "wreck lands and stays finite: " + kind);
            double energy = Energy(c.Body);
            Drive(t.World, c, new(1, 1), 1);
            Check(Energy(c.Body) <= energy + .001, "throttle cannot power a wreck: " + kind);
        });

        Test("ramming wrecks", () =>
        {
            double Hit(int kind)
            {
                var t = FootSandbox();
                Place(t.Hero, 0, -14);
                var c = Npc(t, kind, 0, 0);
                Call(t, "Explode", c);
                t.GodMode = true; t.Hero.Body.Vy = 12;
                double peak = 0;
                for (int n = 0; n < 150; n++) { t.Step(1d / 120); peak = Math.Max(peak, c.Body.Vy); }
                Check(c.Position.Z > .03 && peak > .05, "ram moves wreck type " + kind);
                return peak;
            }
            double sedan = Hit(0), truck = Hit(2), tank = Hit(11), tram = Hit(9);
            Check(sedan > truck && truck > tank, "mass controls response: car > truck > tank");
            Check(tram < sedan, "derailed tram retains its large mass");
        });

        Test("rocket impulse moves an existing wreck", () =>
        {
            var t = FootSandbox();
            var c = Npc(t, 0, 40, 0);
            Call(t, "Explode", c);
            c.WreckSupported = true;
            int incidents = t.Incidents.Count;
            Call(t, "RocketBlast", new CityPoint(38, 0), c.Height * .5);
            Check(c.Body.Vx > 0 && c.VerticalVelocity > 0 && !c.WreckSupported, "blast wakes and launches a wreck with finite mass");
            Check(t.Incidents.Count == incidents + 1, "rocket does not re-explode an already destroyed vehicle");
        });

        Test("air patrol death, blast height and reactivation", () =>
        {
            var t = FootSandbox(); t.ConsoleWanted(6); Call(t, "DispatchAirPatrol");
            var c = t.Cars.Single(a => a.AirPatrol);
            Place(c, 1, 0, 2, 0); c.Lift = 35; c.Physical = false; c.AirDeparting = true;
            var person = Person(t, 2, 1); double health = t.Hero.Damage;
            Call(t, "Explode", c);
            Check(c.Physical && t.World.Bodies.Contains(c.Body) && !c.AirDeparting, "shot-down patrol enters local contact solver");
            Check(t.Hero.Damage == health && !person.Dead, "high-altitude explosion does not hit ground actors below");
            double initial = c.Lift;
            for (int n = 0; n < 60; n++) t.Step(1d / 60);
            Check(c.Lift < initial - 4 && c.VerticalVelocity < -8, "patrol loses altitude under gravity");
            int events = t.Incidents.Count; Call(t, "Explode", c);
            Check(t.Incidents.Count == events, "wreck cannot explode repeatedly");
        });

        Test("roofs, fall from a roof and height-filtered obstacles", () =>
        {
            CityLayout.Install(CityLayout.Generate(73921, gridSize:49, wideRoads:true, roadRevision:4));
            var t = QuietCity(); t.SpawnDistance = 5000; t.Props.Clear(); t.Pickups.Clear();
            var block = CityLayout.Current.Blocks.First(b => b.Height > 15 && b.HalfX > 2 && b.HalfZ > 2);
            var c = Npc(t, 12, block.X, block.Z);
            Place(t.Hero, block.X + block.HalfX + 15, block.Z);
            t.UpdateTerrainSupport(t.Hero); t.UpdateTerrainSupport(c);
            double roof = CityTerrain.Ground(block.X, block.Z) + block.Height;
            c.Lift = roof + 15 - c.SurfaceHeight; Call(t, "Explode", c);
            for (int n = 0; n < 240; n++) t.Step(1d / 60, new(0, 0, true));
            Console.WriteLine($"Roof wreck y {c.SurfaceHeight + c.Lift:F4}, roof {roof:F4}");
            Check(Math.Abs(c.SurfaceHeight + c.Lift - roof) < .08 && Math.Abs(c.VerticalVelocity) < .05, "aircraft rests on real building roof");
            Place(c, block.X + block.HalfX + 8, block.Z);
            t.UpdateTerrainSupport(c, true); double before = c.SurfaceHeight + c.Lift;
            for (int n = 0; n < 60; n++) t.Step(1d / 60, new(0, 0, true));
            Check(c.SurfaceHeight + c.Lift < before - 4, "roof support ends at building footprint");
        });

        Test("road layer support for destroyed vehicles", () =>
        {
            CityLayout.Install(CityLayout.Generate(73921, gridSize:49, wideRoads:true, roadRevision:4));
            foreach (bool tunnel in new[] {false, true})
            {
                var road = CityRoadLevels.All.First(r => r.Tunnel == tunnel);
                var t = QuietCity(); t.SpawnDistance = 5000; t.Props.Clear(); t.Pickups.Clear();
                double x = road.X - (tunnel ? 85 : 0), z = road.CentreZ(x);
                Place(t.Hero, x - 25, z + 18); t.UpdateTerrainSupport(t.Hero);
                var c = Npc(t, 12, x, z); t.UpdateTerrainSupport(c);
                double floor = road.Floor(x, z);
                c.Lift = Math.Max(10, floor + 12 - c.SurfaceHeight); Call(t, "Explode", c);
                for (int n = 0; n < 300; n++) t.Step(1d / 60, new(0, 0, true));
                Console.WriteLine($"Road wreck {road.Id} y {c.SurfaceHeight + c.Lift:F4}, floor {floor:F4}");
                Check(Math.Abs(c.SurfaceHeight + c.Lift - road.Floor(c.Position.X,c.Position.Z)) < .35, "wreck lands on " + (tunnel ? "open tunnel ramp" : "bridge deck"));
            }
        });

        Test("remote wreck and old save", () =>
        {
            CityLayout.Install(CityLayout.Generate(73921, gridSize:49, wideRoads:true, roadRevision:4));
            var model = new CityModel(); var t = model.Traffic; t.SpawnDistance = 5000;
            var c = t.Cars.First(a => a.Kind == 9); int id = c.Id;
            c.Lift = 24; c.Body.Vx = 3; c.Body.Vy = 0; c.Body.W = .1;
            Call(t, "Explode", c);
            // A fixture forcibly detaches the body, exercising far-rule stepping
            // without the near-bubble refresh overriding that state.
            t.World.RemoveBody(t.World.Bodies.IndexOf(c.Body)); c.Physical = false;
            double y = c.SurfaceHeight + c.Lift, x = c.Position.X;
            for (int n = 0; n < 10; n++) { Call(t, "RuleStep", .1); Call(t, "SyncFarPoses", .1); }
            Check(c.Position.X > x + 2 && c.SurfaceHeight + c.Lift < y - 4 && !t.World.Bodies.Contains(c.Body), "far transit wreck moves without local contacts");
            var save = JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(model), CityState.Json), CityState.Json)!;
            foreach (var car in save.Cars) car.State.Remove("WreckSupported");
            CityTraffic.ValidateWorld(save); t.RestoreWorld(save, model);
            c = t.Cars.Single(a => a.Id == id);
            Check(c.Destroyed && c.Body.InvMass > 0 && c.Body.InvI > 0 && c.VerticalVelocity < -8, "legacy save restores dynamic wreck and downward velocity");
            Place(t.Hero, c.Position.X + 20, c.Position.Z); t.UpdateTerrainSupport(t.Hero); Call(t, "RefreshDetail");
            Check(c.Physical && t.World.Bodies.Contains(c.Body), "returning to wreck reactivates local body");
        });
    }
}
