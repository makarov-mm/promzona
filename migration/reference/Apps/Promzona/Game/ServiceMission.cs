namespace Promzona.Game;

public sealed class ServiceMission
{
    public int CarId, PatientId, Stage;
    public CityService Kind;
    public double Timer, Repath;
    public CityPoint Destination;
    public List<int> Medics = [];
}
