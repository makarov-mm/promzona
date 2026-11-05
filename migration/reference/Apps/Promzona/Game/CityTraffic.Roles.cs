namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private double _roleTimer;

    private void ResetRoles()
    {
        _roleTimer = 0;
        if (!CityLayout.Rivers) return;
        for (int side = 0; side < 4; side++)
        for (int n = 0; n < 36; n++)
        for (int member = 0; member < 3; member++)
        {
            double along = -LandEdge + 75 + n * (2 * LandEdge - 150) / 35, shore = LandEdge - 38;
            var at = side switch
            {
                0 => new CityPoint(shore, along), 1 => new CityPoint(-shore, along),
                2 => new CityPoint(along, shore), _ => new CityPoint(along, -shore)
            };
            at += new CityPoint((member - 1) * 1.2, member * 1.7);
            if (!ClearPlayerPoint(at)) continue;
            int i = Math.Clamp((int)Math.Round((at.X - Coordinate(0)) / 36), 0, Nodes - 1),
                j = Math.Clamp((int)Math.Round((at.Z - Coordinate(0)) / 36), 0, Nodes - 1);
            var p = MakePerson(i, j, 1, 1, at);
            p.Persona = 11;
            p.RoleHome = at;
            p.RoleClock = Random() * 8;
            Register(p);
            Pedestrians.Add(p);
            Reanchor(p);
        }
    }

    public void RolesStep(double dt, double hour)
    {
        if (Cars.Count == 0) return;
        if ((_roleTimer -= dt) > 0) return;
        _roleTimer = 4;
        bool night = hour >= 21 || hour < 5;
        for (int n = Pedestrians.Count - 1; n >= 0; n--)
        {
            var p = Pedestrians[n];
            // Night workers leave at dawn; a militia squad whose chase is over
            // leaves the scene the same way. Before this, every pursuit left two
            // to four armed officers wandering the streets as ordinary citizens
            // for the rest of the session, counted against the civilian target.
            bool offDuty = p.Officer && p.ServiceId == 0 && WantedLevel == 0;
            if (p.Id!=ContractActorId && (p.Persona == 7 && !night || offDuty) && (p.Position - PlayerPosition).Length > SpawnDistance && !p.Dead)
            {
                ReleasePerson(p);
                Remove(p.Body);
                Pedestrians.RemoveAt(n);
                ExitedPedestrians++;
                continue;
            }

            if (p.Persona is 8 or 9 or 10) p.Cruise = p.Persona == 9 ? .70 : p.Persona == 8 ? .92 : 1.05;
        }

        if (Pedestrians.Count >= PeopleCap) return;
        int nightCount = Pedestrians.Count(p => p.Persona == 7), beach = Pedestrians.Count(p => p.Persona == 11);
        int role = night && nightCount < 40 ? 7 : CityLayout.Rivers && beach < 420 ? 11 : 0;
        if (role == 0) return;
        for (int trial = 0; trial < 80; trial++)
        {
            int i = Pick(Nodes - 1), j = Pick(Nodes - 1);
            CityPoint at;
            if (role == 11)
            {
                double along = (Random() * 2 - 1) * (LandEdge - 45), shore = LandEdge - 34 - Random() * 10;
                int side = Pick(4);
                at = side switch
                {
                    0 => new(shore, along), 1 => new(-shore, along), 2 => new(along, shore), _ => new(along, -shore)
                };
            }
            else at = new(Coordinate(i) + Sidewalk, Coordinate(j) + 12 + Random() * 12);

            if ((at - PlayerPosition).Length < SpawnDistance || !ClearPlayerPoint(at)) continue;
            var p = MakePerson(i, j, 1, 1, at);
            p.Persona = role;
            p.RoleHome = at;
            p.RoleClock = Time + Random() * 8;
            Register(p);
            Pedestrians.Add(p);
            Reanchor(p);
            return;
        }
    }

    private bool RoleWalk(CityPedestrian p, double dt)
    {
        if (p.Persona is not (7 or 10 or 11) || p.Fear > .12 || p.Aiming) return false;
        if (p.RoleHome.Length < .01) p.RoleHome = p.Position;
        double phase = (Time + p.Id * 2) % (p.Persona == 10 ? 35 : 22);
        // Night workers pace a frontage, beach visitors linger by their towel,
        // homeless residents stop and inspect kerbside litter between short walks.
        if (phase < 10)
        {
            if (p.Physical) MovePerson(p, default, dt);
            else MoveFarSocial(p, default, dt);
            return true;
        }

        var target = p.RoleHome +
                     new CityPoint(Math.Sin(p.Id) * Math.Sin(phase * .3), Math.Cos(p.Id) * Math.Sin(phase * .3)) *
                     (p.Persona == 11 ? 5 : p.Persona == 10 ? 7 : 2.3);
        if (!ClearWalk(target)) target = p.RoleHome;
        var delta = target - p.Position;
        var velocity = delta.Length > .3 ? delta * (Math.Min(1, delta.Length) / Math.Max(.01, delta.Length)) : default;
        if (p.Physical) MovePerson(p, velocity, dt, target);
        else MoveFarSocial(p, velocity, dt);
        return true;
    }
}
