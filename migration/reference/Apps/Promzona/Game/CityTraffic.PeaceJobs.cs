namespace Promzona.Game;

/// <summary>Two nonviolent arcade missions. The authoritarian response is game
/// fiction, not a legal classification of distributing leaflets or speaking.</summary>
public sealed partial class CityTraffic
{
    // Primitive fields share the existing format-3 clock serialization.
    private CityPoint _peaceSite0, _peaceSite1, _peaceSite2, _peaceSite3, _peacePrevious;
    private int _peaceCount, _peaceIndex;
    private double _peaceSeconds, _peaceCrimeSeconds, _peaceSoundAt;
    private bool _peaceEnabled;
    public bool PeaceJobActive => _jobKind is JobKind.Leaflets or JobKind.PeaceBroadcast;
    public bool PeaceEscape => PeaceJobActive && _jobStage == 2;
    public int PeaceSitesDone => _peaceIndex;
    public int PeaceSiteCount => _peaceCount;
    public double PeaceProgress => _peaceSeconds;
    public bool PeaceEnabled => _peaceEnabled;
    public CityCar? BroadcastCar => _jobKind == JobKind.PeaceBroadcast ? Cars.Find(c => c.Id == _jobCarId) : null;
    public bool BroadcastAudible => _peaceEnabled && _jobStage == 1 && BroadcastCar is { } car &&
        PlayerControls(car) && !PlayerUnavailable && !car.Destroyed && !car.Burning && !car.Falling && !DroneActive;
    public const int BroadcastSpeakerId = int.MaxValue - 16;
    public CityPoint PeaceSite(int index) => index switch { 0 => _peaceSite0, 1 => _peaceSite1, 2 => _peaceSite2, _ => _peaceSite3 };

    private void ResetPeaceJob()
    {
        _peaceSite0 = _peaceSite1 = _peaceSite2 = _peaceSite3 = _peacePrevious = default;
        _peaceCount = _peaceIndex = 0;
        _peaceSeconds = _peaceCrimeSeconds = _peaceSoundAt = 0;
        _peaceEnabled = false;
    }

    public bool StartPeaceJob(JobKind kind)
    {
        if (PlayerUnavailable || DroneActive || EnteringCarId >= 0 || _jobKind != JobKind.None ||
            kind is not (JobKind.Leaflets or JobKind.PeaceBroadcast)) return false;
        if (kind == JobKind.PeaceBroadcast && (OnFoot || PassengerMode || Hero.Kind is not (0 or 1 or 2 or 8) ||
            Hero.Burning || Hero.Falling || !Hero.Grounded || Hero.Recovering || Math.Abs(Hero.Speed) > .8))
            return PeaceRefused("peace_need_car");

        // Existing public places, then an exact clear pavement point nearby.
        // Never put a mission marker on a roof, a bridge, a beach or in water.
        var candidates = BusStops.Concat(FoodSites.Select(s => s.Interaction))
            .Where(p => (p - PlayerPosition).Length < 700)
            .OrderBy(p => (p - PlayerPosition).Length).ToArray();
        var places = new List<CityPoint>();
        foreach (var centre in candidates)
        {
            if (places.Any(p => (p - centre).Length < 100)) continue;
            CityPoint? clear = null;
            for (int n = 0; n < 24; n++)
            {
                double angle = n * Math.Tau / 8, radius = 2.8 + n / 8 * 1.2;
                var p = centre + new CityPoint(Math.Sin(angle), Math.Cos(angle)) * radius;
                if (!CityWater.UrbanSite(p, 1) || CityWater.Depth(p) > .01 || CityRoadSurface.Contains(p) ||
                    CityRoadLevels.Reserved(p, 3) || !ClearPlayerPoint(p)) continue;
                clear = p; break;
            }
            if (clear is { } place) places.Add(place);
            if (places.Count == (kind == JobKind.Leaflets ? 4 : 3)) break;
        }
        if (places.Count < (kind == JobKind.Leaflets ? 4 : 3)) return PeaceRefused("contract_unavailable");
        ResetPeaceJob();
        _peaceSite0 = places[0]; _peaceSite1 = places[1]; _peaceSite2 = places[2];
        _peaceSite3 = places.Count == 4 ? places[3] : places[2]; _peaceCount = places.Count;
        _peacePrevious = PlayerPosition;
        _jobKind = kind; _jobStage = 1; _jobTarget = places[0]; _jobStarted = Time; _jobDeadline = Time + 720;
        _jobFare = kind == JobKind.Leaflets ? 300 : 500;
        _jobCarId = kind == JobKind.PeaceBroadcast ? Hero.Id : -1;
        Message(kind == JobKind.Leaflets ? "peace_leaflets" : "peace_broadcast", 6);
        return true;
    }

    private bool PeaceRefused(string message) { Message(message); return false; }
    public bool PeaceCanInteract => PeaceJobActive && !PeaceEscape && !PlayerUnavailable && !DroneActive &&
        (JobKind == JobKind.PeaceBroadcast ? BroadcastCar is { } c && PlayerControls(c) :
            OnFoot && Player.RoadLevel == 0 && !Player.Detached && Player.KnockedFor <= 0 &&
            (PlayerPosition - _jobTarget).Length < 2.2 && ClearSight(PlayerPosition, _jobTarget));

    /// <summary>E toggles the PA; leaflets take three uninterrupted seconds in the circle.</summary>
    public bool InteractPeaceJob()
    {
        if (!PeaceCanInteract) return false;
        if (Time < _jobInteractAt) return true;
        _jobInteractAt = Time + .35;
        if (_jobKind == JobKind.PeaceBroadcast) _peaceEnabled = !_peaceEnabled;
        else _peaceEnabled = true;
        return true;
    }

    private void ValidatePeaceJob()
    {
        if (!PeaceJobActive) { ResetPeaceJob(); return; }
        int expected = _jobKind == JobKind.Leaflets ? 4 : 3;
        if (_peaceCount != expected || _peaceIndex < 0 || _peaceIndex > expected ||
            _jobStage is not (1 or 2) || (_jobStage == 2) != (_peaceIndex == expected) ||
            _peaceSeconds < 0 || _peaceSeconds > 30 || _peaceCrimeSeconds < 0 || _peaceCrimeSeconds > 8 ||
            Enumerable.Range(0, expected).Any(i => !CityWater.UrbanSite(PeaceSite(i), .5) || CityWater.Depth(PeaceSite(i)) > .01))
            ClearJob();
    }

    private void PeaceJobStep(double dt)
    {
        if (PlayerUnavailable || Time >= _jobDeadline) { CancelContract(); return; }
        if (PeaceEscape)
        {
            // Completion cannot be farmed by pressing E or replaying a death/respawn.
            if (WantedLevel == 0) CompleteContract();
            return;
        }
        double travel = (PlayerPosition - _peacePrevious).Length;
        _peacePrevious = PlayerPosition;
        if (_jobKind == JobKind.Leaflets)
        {
            if (!PeaceCanInteract || Player.Speed > .5 || PlayerFireHeld || Player.BurnUntil > Time)
            { _peaceEnabled = false; _peaceSeconds = 0; return; }
            if (!_peaceEnabled) return;
            _peaceSeconds += dt;
            if (_peaceSeconds >= 3)
            {
                AddCrime(.65); Sound("paper", PlayerPosition, .55);
                NextPeaceSite();
            }
            return;
        }
        var car = BroadcastCar;
        if (car is null || car.Destroyed || car.Falling) { CancelContract(); return; }
        if (!BroadcastAudible) { _peaceEnabled = false; return; }
        // Noise attracts pursuit even outside the target district. Driving time
        // counts only on streets near the next marker, at a useful street speed.
        _peaceCrimeSeconds += dt;
        if (_peaceCrimeSeconds >= 8) { _peaceCrimeSeconds -= 8; AddCrime(.35); }
        if (Time >= _peaceSoundAt)
        {
            _peaceSoundAt = Time + 7;
            Sound("peace_pa", car.Position, .7, speakerId: BroadcastSpeakerId);
        }
        bool driving = !car.Recovering && car.Grounded && car.RoadLevel == 0 &&
            Math.Abs(car.Speed) is >= 2 and <= 18 && travel >= dt * 1 && travel <= dt * 25 + .05 &&
            CityRoadSurface.Contains(car.Position) && (car.Position - _jobTarget).Length < 70;
        if (!driving) return;
        _peaceSeconds += dt;
        if (_peaceSeconds >= 30) NextPeaceSite();
    }

    private void NextPeaceSite()
    {
        _peaceIndex++; _peaceSeconds = 0;
        if (_jobKind == JobKind.Leaflets) _peaceEnabled = false;
        if (_peaceIndex == _peaceCount)
        {
            _peaceEnabled = false; _jobStage = 2;
            Message("peace_escape", 6);
        }
        else { _jobTarget = PeaceSite(_peaceIndex); Message("peace_next"); }
    }
}
