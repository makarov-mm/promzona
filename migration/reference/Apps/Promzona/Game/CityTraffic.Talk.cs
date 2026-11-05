namespace Promzona.Game;

/// <summary>Talking to people on F (29.10.2026). A walker facing someone within
/// two metres gets one line, chosen by the other's role and state: officers
/// shout at a wanted player, toughs warn, elders point to the clinic, drunks
/// and the homeless ask for five dollars (a second F gives it), ordinary people
/// gossip, give directions to the market or ask for a lift, which makes them a
/// hailer the car can pick up. The session turns the key into the sentence.</summary>
public sealed partial class CityTraffic
{
    public string Speech { get; private set; } = "";
    public double SpeechUntil { get; private set; }
    public int SpeechPersona { get; private set; }
    public CityPoint SpeechPlace { get; private set; }
    public bool SpeechHasPlace { get; private set; }
    /// <summary>Set by the model from the clock; toughs are ruder after dark.</summary>
    public bool Night;
    public int Talks { get; private set; }
    public int Gifts { get; private set; }
    private int _askId = -1;
    private double _askUntil, _talkAt;

    private void ResetTalk() { Speech = ""; SpeechUntil = _askUntil = _talkAt = 0; SpeechPersona = 0; SpeechPlace = default; SpeechHasPlace = Night = false; Talks = Gifts = 0; _askId = -1; }

    /// <summary>The living person the walker faces within reach, nearest first.</summary>
    public CityPedestrian? NearbyTalker()
    {
        if (!OnFoot || PlayerUnavailable || Player.KnockedFor > 0 || Player.Detached) return null;
        var f = new CityPoint(Math.Sin(PlayerViewHeading), Math.Cos(PlayerViewHeading));
        CityPedestrian? best = null; double near = 1.9;
        foreach (var p in NearbyPeople(Player.Position, 2.2))
        {
            if (p.IsPlayer || p.Dead || p.KnockedFor > 0 || p.Detached || p.RoadLevel != Player.RoadLevel) continue;
            var d = p.Position - Player.Position; double distance = d.Length;
            if (distance > near || distance < .01 || CityPoint.Dot(d, f) / distance < .35) continue;
            near = distance; best = p;
        }
        return best;
    }

    /// <summary>A pending request for money that the next F answers.</summary>
    public bool AskPending => _askId >= 0 && Time < _askUntil;

    public bool Talk()
    {
        if (Time < _talkAt || !OnFoot || PlayerUnavailable) return false;
        _talkAt = Time + .5;
        if (AskPending)
        {
            var asker = Pedestrians.Find(x => x.Id == _askId); _askId = -1;
            if (asker is not null && !asker.Dead && (asker.Position - Player.Position).Length < 3)
            {
                if (Money < 5) return Say(asker, "nocash");
                Money -= 5; Gifts++; asker.Fear = 0; asker.Wait = Math.Max(asker.Wait, 2);
                Sound("spend", PlayerPosition, .5, ui: true);
                return Say(asker, "thanks");
            }
        }
        var p = NearbyTalker();
        if (p is null) return false;
        // Both stop and face each other for a moment.
        var toward = p.Position - Player.Position;
        p.Heading = Math.Atan2(-toward.X, -toward.Z);
        p.Wait = Math.Max(p.Wait, 2.5); p.VoiceAt = Time + 3;
        if (p.Fear > .25) return Say(p, "scared");
        if (p.Officer) return Say(p, WantedLevel > 0 ? "officer_wanted" : "officer");
        if (p.Injured > 0) return Say(p, "injured", NearestClinic(p.Position));
        if (p.Hailing) return Say(p, "hailer", TaxiDestination(p));
        switch (p.Persona)
        {
            case 3: return Say(p, Night ? "tough_night" : "tough");
            case 4: return Say(p, "medic");
            case 7: return Say(p, "night");
            case 11: return Say(p, "beach");
            case 2 or 8: return (p.Id % 3 == 0) ? Say(p, "elder") : Say(p, "elder_clinic", NearestClinic(p.Position));
            case 9: _askId = p.Id; _askUntil = Time + 4; return Say(p, "drunk_ask");
            case 10: _askId = p.Id; _askUntil = Time + 4; return Say(p, "homeless_ask");
        }
        switch ((p.Id * 7) % 5)
        {
            case 0:
                // Not while crossing: a hailer stands still, and one left on the
                // carriageway would hold the traffic at that crossing for 45 s.
                if (p.GroupId < 0 && p.ServiceId == 0 && !p.Crossing && TaxiDestination(p) is { } to)
                {
                    // A lift wanted: the person waits at the kerb like a hailer for the car to come.
                    ReleasePerson(p); p.Hailing = true; p.HailUntil = Time + 45; p.RoleHome = p.Position;
                    return Say(p, "ride", to);
                }
                return Say(p, "small");
            case 1:
                MarketSite? stall = null; double distance = double.MaxValue;
                foreach (var m in Markets) { double d = (m.Interaction - p.Position).Length; if (d < distance) { distance = d; stall = m; } }
                return stall is { } market ? Say(p, "market", market.Interaction) : Say(p, "rumour");
            case 2: return Say(p, CityLayout.Current.HasIndustry ? "rumour" : "small");
            case 3: return Say(p, "cabbie");
            default: return Say(p, "small");
        }
    }

    private bool Say(CityPedestrian p, string key, CityPoint? place = null)
    {
        Speech = key; SpeechUntil = Time + 3.5; SpeechPersona = p.Persona; Talks++;
        SpeechHasPlace = place is not null; SpeechPlace = place ?? default;
        return true;
    }
}
