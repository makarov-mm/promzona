using Promzona.Game;

static partial class Tests
{
    static void RunCombatHud()
    {
        CityLayout.Install(CityLayout.Generate(73921));
        Test("moving aim and manual weapon choice", () =>
        {
            var t = FootSandbox();
            t.InteractVehicle();
            WalkPlace(t, -1.8, -15);
            t.Inventory.Give(CityWeaponId.Pistol);
            t.Inventory.Give(CityWeaponId.Automatic);
            t.Inventory.Cycle(-1);
            Check(t.Inventory.Active?.Id == CityWeaponId.Pistol, "cycle selects loaded sidearm");
            t.Inventory.Cycle(-1);
            Check(t.Inventory.Active == null, "cycle can select fists with guns owned");
            t.Inventory.Cycle(-1);
            Check(t.Inventory.Active?.Id == CityWeaponId.Automatic, "reverse cycle skips unavailable weapons");
            t.Inventory.Give(CityWeaponId.Bazooka, 1);
            t.Inventory.Select(CityWeaponId.Bazooka);
            t.Inventory.Consume(CityWeaponId.Bazooka);
            Check(t.Inventory.Active?.Id == CityWeaponId.Automatic, "last round restores best loaded weapon");
            t.WalkInput = new(0, 1);
            t.Sprint = true;
            t.PlayerFireHeld = true;
            Tick(t, .6);
            var motion = new CityPersonMotion();
            var a = motion.Sample(t.Player, t.Time);
            Tick(t, .1);
            var b = motion.Sample(t.Player, t.Time);
            Check(t.PlayerShotsFired > 0 && t.Player.Speed > 4.8, "player fires while sprinting");
            Check(a.To >= 512 && b.To >= 512 && a.To != b.To, "aimed run advances the leg phase");
        });
        Test("mounted carrier gun", () =>
        {
            var t = FootSandbox();
            Place(t.Hero, -1.8, 0);
            var carrier = Npc(t, 14, -1.8, -16);
            t.ConsoleWanted(5);
            Call(t, "RebuildActorGrid");
            Call(t, "AimCarrierAtPlayer", carrier, .5);
            Check(t.Hero.Damage > 0 && t.ShotsFired == 1, "hostile carrier fires a bullet and damages player vehicle");
            Call(t, "AimCarrierAtPlayer", carrier, .5);
            Check(t.ShotsFired == 1 && carrier.Damage == 0, "carrier respects cooldown and ignores own hull");
            t.InteractVehicle();
            WalkPlace(t, -1.8, -3.5);
            carrier.NextTurretShot = 0;
            Call(t, "AimCarrierAtPlayer", carrier, .5);
            Check(t.Player.GunDamage > 0, "hostile turret hits player on foot");
            carrier.Driverless = true;
            carrier.NextTurretShot = 0;
            Call(t, "AimCarrierAtPlayer", carrier, .5);
            Check(t.ShotsFired == 2, "empty carrier cannot fire autonomously");
            var u = FootSandbox();
            u.Hero.Kind = 14;
            Place(u.Hero, -1.8, -16);
            var target = Npc(u, 2, -1.8, 0);
            u.Hero.GunElevation = -.05;
            Call(u, "RebuildActorGrid");
            var preview = u.AimHit();
            Check(target.Damage == 0 && u.Shots.Count == 0, "turret reticle has no combat side effects");
            u.PlayerFireHeld = true;
            Call(u, "PlayerCombat");
            Check(target.Damage > 0 && u.Hero.Damage == 0 && u.PlayerShotsFired == 1 &&
                !u.Inventory.HasWeapons, "driver fires mounted gun without personal ammunition");
            Check(u.Shots.Count == 1 && (preview.Point-u.Shots[0].To).Length < .01, "reticle matches the bullet impact");
        });
    }
}
