using System.Globalization;
namespace Promzona.Game;

/// <summary>Finite, game-only command vocabulary. No script evaluation or file access.</summary>
public sealed class CityConsole
{
    public bool Open;
    public string Text = "";
    public int Caret;
    public readonly List<string> Lines = ["PROMZONA / help — команды / commands"];
    private readonly List<string> _history = [];
    private int _cursor;

    public static readonly string[] Commands =
    [
        "help", 
        "money", 
        "health", 
        "repair", 
        "god", 
        "wanted",
        "weapon",
        "spawn",
        "hour", 
        "rain", 
        "cycle",
        "speed",
        "clear"
    ];

    public void Write(string text)
    {
        foreach (var line in text.Split('\n')) Lines.Add(line);
        if (Lines.Count > 100) Lines.RemoveRange(0, Lines.Count - 100);
    }

    public void Insert(char c)
    {
        if (!char.IsControl(c) && Text.Length < 160)
        {
            Text = Text.Insert(Caret, c.ToString());
            Caret++;
        }
    }

    public void History(int direction)
    {
        _cursor = Math.Clamp(_cursor + direction, 0, _history.Count);
        Text = _cursor == _history.Count ? "" : _history[_cursor];
        Caret = Text.Length;
    }

    public void Complete()
    {
        string[] choices = [.. Commands.Where(c => c.StartsWith(Text, StringComparison.OrdinalIgnoreCase))];

        if (choices.Length == 1)
        {
            Text = choices[0] + " ";
            Caret = Text.Length;
        }
        else
        {
            Write(string.Join("  ", choices));
        }
    }

    public string Submit()
    {
        string line = Text.Trim();

        if (line.Length > 0)
        {
            Write("> " + line);
            _history.Add(line);

            if (_history.Count > 50)
            {
                _history.RemoveAt(0);
            }
        }

        _cursor = _history.Count;
        Text = string.Empty;
        Caret = 0;
        return line;
    }

    public static string Execute(CityModel model, string line, Action<double>? speed = null)
    {
        if (line.Length > 160) return "Command too long";
        string[] a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (a.Length == 0) return "";
        CityTraffic t = model.Traffic;

        double Number(double min, double max)
        {
            if (a.Length != 2 ||
                !double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ||
                !double.IsFinite(n) || n < min || n > max) throw new ArgumentException($"Expected number {min}..{max}");
            return n;
        }

        try
        {
            switch (a[0].ToLowerInvariant())
            {
                case "help":
                    return
                        "money 0..999999999 | health | repair | god on/off | wanted 0..6\nweapon pistol/automatic/bazooka/flamethrower [ammo]\nspawn tank/helicopter/car/ambulance/police/rosguard/fsb/btr/pills/energy\nhour 0..24 | rain on/off | cycle on/off | speed 0.1..3 | clear\nF1 or ~: close; Up/Down: history; Tab: complete\nHelicopter: WASD, Q ascend, Z descend, L auto-land, R recover, Space brake; land before F exit.\nOn foot: K reconnaissance drone; WASD / Q / Z / mouse, LMB scan, F return.";
                case "money": t.SetMoney((int)Number(0, 999999999)); break;
                case "health": t.ConsoleHeal(); break;
                case "repair": t.ConsoleRepair(); break;
                case "god": t.GodMode = Toggle(); break;
                case "wanted": t.ConsoleWanted(Number(0, CityTraffic.MaxWantedLevel)); break;
                case "hour": model.SetHour(Number(0, 24)); break;
                case "rain": model.SetRain(Toggle()); break;
                case "cycle": model.DayNightCycle = Toggle(); break;
                case "speed":
                    if (speed is null) return "Speed control unavailable";
                    speed(Number(.1, 3));
                    break;
                case "weapon":
                    if (a.Length is < 2 or > 3 || !Enum.TryParse<CityWeaponId>(a[1], true, out var id) ||
                        CityWeapons.Find(id) is not { } w) return "Unknown weapon; see help";
                    int ammo = w.PickupAmmo;
                    if (a.Length == 3 && (!int.TryParse(a[2], out ammo) || ammo < 1 || ammo > 9999))
                        return "Ammo: 1..9999";
                    t.Inventory.Give(id, ammo);
                    break;
                case "spawn":
                    if (a.Length != 2)
                        return "Use spawn tank/helicopter/car/ambulance/police/rosguard/fsb/btr/pills/energy";
                    return t.ConsoleSpawn(a[1].ToLowerInvariant());
                default: return "Unknown command. Type help.";
            }

            return "OK: " + line;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }

        bool Toggle()
        {
            if (a is not [_, "on" or "off"]) throw new ArgumentException("Use on / off");
            return a[1] == "on";
        }
    }
}
