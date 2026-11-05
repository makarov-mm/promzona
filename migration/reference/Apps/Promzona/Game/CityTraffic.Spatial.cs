namespace Promzona.Game;

// Actor simulation stays global. These cells only prune local neighbour queries.
public sealed partial class CityTraffic
{
    private readonly Dictionary<(int, int), List<CityCar>> _carCells = [];
    private readonly Dictionary<(int, int), List<CityPedestrian>> _peopleCells = [];
    // Actors listed by the last full rebuild plus GridInsert calls since: when the
    // live lists no longer add up to this, something was removed.
    private int _gridActors, _gridInserted;

    private static (int X, int Z) CarCell(CityPoint p) => ((int)Math.Floor(p.X / 16), (int)Math.Floor(p.Z / 16));
    private static (int X, int Z) PersonCell(CityPoint p) => ((int)Math.Floor(p.X / 8), (int)Math.Floor(p.Z / 8));

    private void RebuildActorGrid()
    {
        // Once per frame and after every actor-list surgery (entry, respawn,
        // load): no temporary lists for the clearing pass.
        foreach (List<CityCar> list in _carCells.Values) list.Clear();
        foreach (List<CityPedestrian> list in _peopleCells.Values) list.Clear();

        foreach (CityCar car in Cars) GridInsert(car);
        foreach (CityPedestrian p in Pedestrians) GridInsert(p);
        _gridActors = Cars.Count + Pedestrians.Count;
        _gridInserted = 0;
    }

    /// <summary>The per-substep update. Between substeps only the physical bubble,
    /// the transit vehicles and (after RuleStep) the far actors move, so each actor
    /// is moved to its new cell only when the cell changed - a floor and a compare
    /// per actor instead of clearing every cell and re-adding all ~8 000 entries
    /// several times a frame. An actor removed since the last rebuild would stay
    /// listed in its cell; the count check sends that case through the full
    /// rebuild, so a query never returns a removed actor for longer than before.</summary>
    private void RefreshActorGrid()
    {
        if (Cars.Count + Pedestrians.Count != _gridActors + _gridInserted)
        {
            RebuildActorGrid();
            return;
        }

        foreach (CityCar car in Cars)
        {
            (int X, int Z) key = CarCell(car.Position);
            if (key == car.GridCell) continue;
            if (_carCells.TryGetValue(car.GridCell, out List<CityCar>? old)) old.Remove(car);
            if (!_carCells.TryGetValue(key, out List<CityCar>? list)) _carCells[key] = list = [];
            list.Add(car);
            car.GridCell = key;
        }

        foreach (CityPedestrian p in Pedestrians)
        {
            (int X, int Z) key = PersonCell(p.Position);
            if (key == p.GridCell) continue;
            if (_peopleCells.TryGetValue(p.GridCell, out List<CityPedestrian>? old)) old.Remove(p);
            if (!_peopleCells.TryGetValue(key, out List<CityPedestrian>? list)) _peopleCells[key] = list = [];
            list.Add(p);
            p.GridCell = key;
        }
    }

    // Register calls these so an actor created between two rebuilds (a dispatched
    // responder, a deployed crew) is visible to the grid-pruned clearance checks
    // of the same frame. Removed actors stay listed until the next rebuild, at
    // most one substep later.
    private void GridInsert(CityCar car)
    {
        (int X, int Z) key = CarCell(car.Position);

        if (!_carCells.TryGetValue(key, out List<CityCar>? list))
        {
            _carCells[key] = list = [];
        }

        list.Add(car);
        car.GridCell = key;
        _gridInserted++;
    }

    private void GridInsert(CityPedestrian p)
    {
        (int X, int Z) key = PersonCell(p.Position);

        if (!_peopleCells.TryGetValue(key, out List<CityPedestrian>? list))
        {
            _peopleCells[key] = list = [];
        }

        list.Add(p);
        p.GridCell = key;
        _gridInserted++;
    }

    private CellQuery<CityCar> NearbyCars(CityPoint p, double radius) => new(_carCells, p, radius, 16);
    private CellQuery<CityPedestrian> NearbyPeople(CityPoint p, double radius) => new(_peopleCells, p, radius, 8);

    // Foreach uses the value enumerator directly: these hot queries no longer
    // allocate an iterator for every driver, walker and physics substep. LINQ
    // callers retain IEnumerable compatibility and the original cell/list order.
    internal readonly struct CellQuery<T>(Dictionary<(int, int), List<T>> cells, CityPoint centre, double radius, double size) : IEnumerable<T>
    {
        public Enumerator GetEnumerator() => new(cells, centre, radius, size);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            private readonly Dictionary<(int, int), List<T>> _cells;
            private readonly int _lastX, _firstZ, _lastZ;
            private int _x, _z;
            private bool _hasList, _finished;
            private List<T>.Enumerator _items;
            internal Enumerator(Dictionary<(int, int), List<T>> cells, CityPoint p, double radius, double size)
            {
                _cells = cells;
                _x = (int)Math.Floor((p.X-radius)/size);
                _lastX = (int)Math.Floor((p.X+radius)/size);
                _z = _firstZ = (int)Math.Floor((p.Z-radius)/size);
                _lastZ = (int)Math.Floor((p.Z+radius)/size);
                _hasList = _finished = false;
                _items = default;
            }
            public T Current => _items.Current;
            object? System.Collections.IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (_finished) return false;
                while (true)
                {
                    if (_hasList && _items.MoveNext()) return true;
                    if (_x > _lastX) { _finished = true; return false; }
                    _hasList = _cells.TryGetValue((_x, _z), out List<T>? items);
                    if (_hasList) _items = items!.GetEnumerator();
                    if (_z == _lastZ) { _z = _firstZ; _x++; } else _z++;
                }
            }
            public void Dispose() => _items.Dispose();
            public void Reset() => throw new NotSupportedException();
        }
    }
}
