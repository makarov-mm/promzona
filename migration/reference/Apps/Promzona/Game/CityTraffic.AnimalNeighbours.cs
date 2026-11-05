namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private readonly Dictionary<(int,int),List<CityAnimal>> _animalThreatCells=[];
    private void RebuildAnimalThreatGrid()
    {
        foreach(var cell in _animalThreatCells.Values)cell.Clear();
        foreach(var animal in Animals)
        {
            if(!animal.Physical||animal.Dead||animal.Height>=1||animal.Kind is not (AnimalKind.Cat or AnimalKind.Dog))continue;
            var key=((int)Math.Floor(animal.Position.X/8),(int)Math.Floor(animal.Position.Z/8));
            if(!_animalThreatCells.TryGetValue(key,out var cell))_animalThreatCells[key]=cell=[];
            cell.Add(animal);
        }
    }
    private IEnumerable<CityAnimal> NearbyAnimalThreats(CityPoint point,double radius)
    {
        if(radius<=0)yield break;
        for(int x=(int)Math.Floor((point.X-radius)/8);x<=(int)Math.Floor((point.X+radius)/8);x++)
        for(int z=(int)Math.Floor((point.Z-radius)/8);z<=(int)Math.Floor((point.Z+radius)/8);z++)
            if(_animalThreatCells.TryGetValue((x,z),out var cell))foreach(var animal in cell)yield return animal;
    }
}
