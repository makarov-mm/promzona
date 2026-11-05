namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public const int InitialPeople = 4860, PeopleTarget = 5265, PeopleCap = 6165, CarCap = 2200, AnimalCap = 2000;
    public int OriginalPassengers { get; private set; }
    public int OriginalOtherCars { get; private set; }
    public int InitialPassengerTarget { get; private set; }
    public int PassengerTarget { get; private set; }
    public int OtherCarTarget { get; private set; }
    internal int AnimalPopulationRevision;
    public int OriginalPigeons { get; private set; }
    public int InitialPigeonTarget { get; private set; }
    public int PigeonTarget { get; private set; }
    public int OtherAnimalTarget { get; private set; }

    private void ExpandPopulation()
    {
        OriginalPassengers = Cars.Count(c => c is { Kind: 0, Hero: false });
        OriginalOtherCars = Cars.Count - OriginalPassengers - 1;
        InitialPassengerTarget = (OriginalPassengers * 3 + 1) / 2;
        PassengerTarget = (int)Math.Ceiling(InitialPassengerTarget * 1.1);
        OtherCarTarget = (int)Math.Ceiling(OriginalOtherCars * 1.1);
        int extra = InitialPassengerTarget - OriginalPassengers;

        for (int tries = 0; extra > 0 && tries < 40000; tries++)
        {
            _cancel.ThrowIfCancellationRequested();
            int i = Pick(Nodes), j = Pick(Nodes), d = Pick(4);
            if (!HasRoad(i, j, (d + 2) % 4)) continue;
            var c = MakeCar(i, j, d, 12 + Random() * 15, false);
            c.Kind = 0;
            if ((c.Position - PlayerPosition).Length < 22 || !GroundCarSpawnClear(c) || !CarClear(c.Position, c.Heading, c)) continue;
            Cars.Add(c);
            Register(c);
            extra--;
        }

        for (int tries = 0; Pedestrians.Count < InitialPeople && tries < 12000; tries++)
        {
            _cancel.ThrowIfCancellationRequested();
            TrySidewalkPerson(false);
        }
    }

    private void ReplenishPopulation()
    {
        if (Cars.Count < CarCap)
        {
            int passengers = 0, others = 0;

            foreach (CityCar car in Cars)
            {
                if (car is { Hero: false, Driverless: false, Destroyed: false, Falling: false })
                {
                    if (car.Kind == 0) 
                        passengers++;
                    else 
                        others++;
                }
            }

            if (passengers < PassengerTarget) 
                SpawnCar(true);
            else if (others < OtherCarTarget) 
                SpawnCar(false);
        }

        if (Pedestrians.Count < PeopleCap
            &&
            Pedestrians.Count(p => p is { IsPlayer: false, Dead: false, ServiceId: 0 }) + TransitRiders.Count < PeopleTarget)
        {
            SpawnPerson();
        }
    }
}
