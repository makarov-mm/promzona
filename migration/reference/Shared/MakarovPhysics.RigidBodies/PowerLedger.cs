namespace MakarovPhysics.RigidBodies;

/// <summary>The energy journal of a machine or a plant, in joules: work that sources bring in, energy delivered to
/// loads, energy lost, each kept by name, and the energy held in stores, read from the state whenever it is asked.
/// What came in less what went out, what was lost and what the stores gained is the imbalance; it is zero for a
/// model that keeps the first law and stays at its integrator's error for a numerical one. A flow may change sign:
/// a source that takes energy back (a grid absorbing a pump's surplus) books a negative input. Begin starts a new
/// account from the present stores.</summary>
public sealed class PowerLedger
{
    private readonly List<(string Name, Func<double> Energy)> _stores = [];
    private readonly Dictionary<string, double> _in = [], _out = [], _lost = [];
    private double _stored0;

    /// <summary>Registers a store whose energy is read from the model's state.</summary>
    public void Store(string name, Func<double> energy)
    {
        _stores.Add((name, energy));
        _stored0 += energy();
    }

    /// <summary>Forgets the flows and takes the present stores as the start of the account.</summary>
    public void Begin()
    {
        _in.Clear();
        _out.Clear();
        _lost.Clear();
        _stored0 = Stored;
    }

    /// <summary>Removes the stores too: for a model that rebuilds itself on reset.</summary>
    public void Clear()
    {
        _stores.Clear();
        _stored0 = 0;
        Begin();
    }

    public void Input(string name, double joules) => Add(_in, name, joules);
    public void Output(string name, double joules) => Add(_out, name, joules);
    public void Loss(string name, double joules) => Add(_lost, name, joules);

    private static void Add(Dictionary<string, double> book, string name, double joules) =>
        book[name] = book.GetValueOrDefault(name) + joules;

    public double In => _in.Values.Sum();
    public double Out => _out.Values.Sum();
    public double Lost => _lost.Values.Sum();
    public double Stored => _stores.Sum(s => s.Energy());
    public double StoredChange => Stored - _stored0;

    /// <summary>In − Out − Lost − change of the stores, J.</summary>
    public double Imbalance => In - Out - Lost - StoredChange;

    /// <summary>The largest of the books and the stores' change, for a relative check of the imbalance.</summary>
    public double Throughput => Math.Max(Math.Max(Math.Abs(In), Math.Abs(Out)), Math.Max(Math.Abs(Lost), Math.Abs(StoredChange)));

    public double InputOf(string name) => _in.GetValueOrDefault(name);
    public double OutputOf(string name) => _out.GetValueOrDefault(name);
    public double LossOf(string name) => _lost.GetValueOrDefault(name);
    public double StoreOf(string name) => _stores.Where(s => s.Name == name).Sum(s => s.Energy());
    public IEnumerable<string> Losses => _lost.Keys;
}
