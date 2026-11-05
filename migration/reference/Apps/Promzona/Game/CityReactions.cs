namespace Promzona.Game;

public static class CityReactions
{
    // Explicit directional rules: absence means no fear, not a symmetric relation.
    public static FearResponse Animal(AnimalKind animal, CityThreat threat) => (animal, threat) switch
    {
        (AnimalKind.Pigeon or AnimalKind.Bird, CityThreat.Person) => new FearResponse(3.5, 4, .20, true),
        (AnimalKind.Pigeon or AnimalKind.Bird, CityThreat.Cat) => new FearResponse(5.0, 5, .20, true),
        (AnimalKind.Pigeon or AnimalKind.Bird, CityThreat.Dog) => new FearResponse(6.0, 5, .20, true),
        (AnimalKind.Cat, CityThreat.Person) => new FearResponse(2.8, 3, .16, false),
        (AnimalKind.Cat, CityThreat.Dog) => new FearResponse(7, 4, .16, false),
        (_, CityThreat.Car) => new FearResponse(16, 4, .15, animal is AnimalKind.Pigeon or AnimalKind.Bird),
        (_, CityThreat.Gunfire) => new FearResponse(24, 6, .12, animal is AnimalKind.Pigeon or AnimalKind.Bird),
        _ => default
    };
}
