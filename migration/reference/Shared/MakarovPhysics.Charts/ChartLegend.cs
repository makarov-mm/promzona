using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>Measured footer shared by line and phase charts. Data changes do
/// not invalidate its layout; names, membership, width and font metrics do.</summary>
internal sealed class ChartLegend
{
    internal sealed record Entry(Series Series, string Name, bool Point, bool Frozen, string Text, float X, float Y);
    internal readonly List<Entry> Entries = [];
    internal float Height { get; private set; }
    private readonly List<(Series Series, string Name, bool Point, bool Frozen)> _sources = [];
    private float _width = -1, _fontSize, _scale;
    private bool _measured, _shown;

    internal void Update(List<Series> lines, List<Series> points, List<Series> snapshots, bool show, bool includeSnapshots, float width, float fontSize, float scale, Draw2D? draw)
    {
        int index = 0;
        bool same = Matches(lines, false, false, ref index)
            && Matches(points, true, false, ref index)
            && (!includeSnapshots || Matches(snapshots, false, true, ref index))
            && index == _sources.Count;

        if (same && _width == width && _fontSize == fontSize && _scale == scale && _measured == (draw is not null) && _shown == show) return;
        _sources.Clear();
        Remember(lines, false, false);
        Remember(points, true, false);
        if (includeSnapshots) Remember(snapshots, false, true);
        _width = width; _fontSize = fontSize; _scale = scale;
        _measured = draw is not null; _shown = show;
        Entries.Clear(); Height = 0;
        if (!show || width <= 24) return;
        float available = width - 12, x = 0, y = 0, step = fontSize + 5;

        foreach ((Series Series, string Name, bool Point, bool Frozen) source in _sources)
        {
            if (string.IsNullOrWhiteSpace(source.Name)) continue;
            string text = Fit(source.Name, available - 22, fontSize, draw);
            float size = Measure(text, fontSize, draw) + 22;
            if (x > 0 && x + size > available) { x = 0; y += step; }
            Entries.Add(new Entry(source.Series, source.Name, source.Point, source.Frozen, text, x, y));
            x += size + 12;
        }

        if (Entries.Count > 0)
        {
            Height = y + step + 7;
        }
    }

    private bool Matches(List<Series> list, bool point, bool frozen, ref int index)
    {
        foreach (Series series in list)
        {
            if (index >= _sources.Count) return false;
            (Series Series, string Name, bool Point, bool Frozen) old = _sources[index++];
            if (!ReferenceEquals(old.Series, series) || old.Name != series.Name || old.Point != point || old.Frozen != frozen) return false;
        }
        return true;
    }

    private void Remember(List<Series> list, bool point, bool frozen)
    {
        foreach (Series series in list)
        {
            _sources.Add((series, series.Name, point, frozen));
        }
    }

    private static float Measure(string text, float fontSize, Draw2D? draw) => draw?.MeasureText(text) ?? text.Length * fontSize * 0.6f;

    internal static string Fit(string text, float width, float fontSize, Draw2D? draw)
    {
        // Draw2D is single-line; do not let a caption insert fallback glyphs.
        if (text.Contains('\n') || text.Contains('\r')) text = text.Replace('\n', ' ').Replace('\r', ' ');
        if (Measure(text, fontSize, draw) <= width) return text;
        const string ellipsis = "…";
        if (Measure(ellipsis, fontSize, draw) > width) return string.Empty;
        int low = 0, high = text.Length;

        while (low < high)
        {
            int mid = (low + high + 1) / 2;

            if (Measure(text[..mid] + ellipsis, fontSize, draw) <= width)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (low > 0 && char.IsHighSurrogate(text[low - 1]))
        {
            low--;
        }

        return text[..low] + ellipsis;
    }
}
