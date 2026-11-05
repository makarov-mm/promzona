using MakarovPhysics.UI;

namespace MakarovPhysics.Experiments;

/// <summary>What a body editor needs from a model's roster of bodies: a count, a name and a mass each, a way to
/// change the mass and to remove one. The model decides what a mass means (M☉, kg, N-body units).</summary>
public interface IBodyRoster
{
    int Count { get; }
    string NameOf(int index);
    double MassOf(int index);
    void SetMass(int index, double mass);
    bool Remove(int index);
}

/// <summary>
/// The shared body editor (S3 of ASTRONOMY-WOW-PLAN-2026-10-19, 20.10, tail1536): a list of the bodies, the
/// selected one's card, a mass slider and a Remove button, on any panel, for any model that exposes an
/// <see cref="IBodyRoster"/>. NBody carries the same gestures of its own (tail1530); this is the one the
/// others share. Strings come through <paramref name="text"/>: "Bodies", "Mass", "Remove", "None".
/// </summary>
public sealed class BodyEditor
{
    private readonly IBodyRoster _roster;
    private readonly Dropdown _list;
    private readonly UiLabel _card;
    private readonly Slider _mass;
    private readonly UiButton _remove;
    private readonly Func<string, string> _text;
    private readonly Func<int, string> _describe;
    private readonly Action _changed;
    private int _selected = -1;

    /// <summary>The selected body's index in the roster, or −1.</summary>
    public int Selected
    {
        get => _selected;
        set { _selected = value; Sync(); }
    }

    /// <param name="describe">One line for the card about body i (position, speed, period - the model's choice).</param>
    /// <param name="changed">Called after a mass change or a removal, so the module can restart or redraw.</param>
    public BodyEditor(UiPanel panel, Func<string, string> text, IBodyRoster roster, double massMin, double massMax, string massFormat,
        Func<int, string> describe, Action changed)
    {
        _roster = roster; _text = text; _describe = describe; _changed = changed;
        _list = panel.Add(new Dropdown(text("Bodies"), Names(), 0));
        _list.Changed += i => { _selected = i - 1; Sync(); };
        _card = panel.Add(new UiLabel("") { Dim = true, Wrap = true });
        _mass = panel.Add(new Slider(text("Mass"),
            () => _selected >= 0 && _selected < _roster.Count ? _roster.MassOf(_selected) : massMin,
            v => { if (_selected >= 0 && _selected < _roster.Count) { _roster.SetMass(_selected, v); _changed(); } },
            massMin, massMax) { Logarithmic = massMax / massMin > 50, Format = massFormat });
        _remove = panel.Add(new UiButton(text("Remove"), () =>
        {
            if (_selected < 0 || _selected >= _roster.Count) return;
            _roster.Remove(_selected);
            _selected = -1;
            Refresh();
            _changed();
        }));
        Sync();
    }

    private string[] Names()
    {
        var names = new string[_roster.Count + 1];
        names[0] = _text("None");
        for (int i = 0; i < _roster.Count; i++) names[i + 1] = _roster.NameOf(i);
        return names;
    }

    /// <summary>The roster changed outside (a body added or the preset reloaded): rebuild the list.</summary>
    public void Refresh()
    {
        _list.Items = Names();
        if (_selected >= _roster.Count) _selected = -1;
        _list.Selected = _selected + 1;
        Sync();
    }

    /// <summary>Called each UI frame: the card follows the body.</summary>
    public void Update()
    {
        if (_selected >= 0 && _selected < _roster.Count) _card.Text = _describe(_selected);
    }

    private void Sync()
    {
        bool on = _selected >= 0 && _selected < _roster.Count;
        _card.Visible = on; _mass.Visible = on; _remove.Visible = on;
        if (on) _card.Text = _describe(_selected);
    }
}
