namespace AsterismApp;

public readonly record struct GraphPoint(double X, double Y)
{
    public static GraphPoint operator +(GraphPoint left, GraphPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static GraphPoint operator -(GraphPoint left, GraphPoint right) => new(left.X - right.X, left.Y - right.Y);
}

public sealed record GraphLayout(
    IReadOnlyDictionary<string, GraphPoint> Points,
    IReadOnlyDictionary<string, List<string>> Links,
    IReadOnlyDictionary<string, int> Degrees,
    IReadOnlySet<string> SelectedNeighbors);

public sealed class GraphLayoutService
{
    private string? _cachedKey;
    private Dictionary<string, GraphPoint> _cachedPoints = new(StringComparer.OrdinalIgnoreCase);
    internal int SimulationRuns { get; private set; }

    public GraphLayout Calculate(
        IReadOnlyList<NoteInfo> notes,
        IReadOnlyDictionary<string, List<string>> links,
        double width,
        double height,
        string? selectedTitle)
    {
        var cacheKey = CreateCacheKey(notes, links, width, height);
        Dictionary<string, GraphPoint> points;
        if (_cachedKey == cacheKey)
        {
            points = new Dictionary<string, GraphPoint>(_cachedPoints, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            points = notes.ToDictionary(note => note.Title, note => InitialPoint(note.Title, width, height), StringComparer.OrdinalIgnoreCase);
            var forces = notes.ToDictionary(note => note.Title, _ => new GraphPoint(0, 0), StringComparer.OrdinalIgnoreCase);
            for (var step = 0; step < 140; step++)
            {
                foreach (var title in forces.Keys.ToList()) forces[title] = new GraphPoint(0, 0);
                for (var left = 0; left < notes.Count; left++)
                    for (var right = left + 1; right < notes.Count; right++)
                        AddRepulsion(notes[left].Title, notes[right].Title, points, forces);
                foreach (var (source, targets) in links)
                    foreach (var target in targets)
                        AddAttraction(source, target, points, forces);
                foreach (var title in points.Keys.ToList())
                {
                    var point = points[title];
                    var force = forces[title];
                    points[title] = new GraphPoint(Math.Clamp(point.X + force.X, 24, width - 24), Math.Clamp(point.Y + force.Y, 24, height - 24));
                }
            }
            _cachedKey = cacheKey;
            _cachedPoints = new Dictionary<string, GraphPoint>(points, StringComparer.OrdinalIgnoreCase);
            SimulationRuns++;
        }

        if (selectedTitle is not null && points.TryGetValue(selectedTitle, out var selectedPoint))
        {
            var offsetX = width / 2 - selectedPoint.X;
            var offsetY = height / 2 - selectedPoint.Y;
            foreach (var title in points.Keys.ToList())
            {
                var point = points[title];
                points[title] = new GraphPoint(Math.Clamp(point.X + offsetX, 24, width - 24), Math.Clamp(point.Y + offsetY, 24, height - 24));
            }
        }

        var selectedNeighbors = links
            .Where(pair => pair.Key.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase) || pair.Value.Contains(selectedTitle ?? "", StringComparer.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Key.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase) ? pair.Value.AsEnumerable() : [pair.Key])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var degrees = notes.ToDictionary(note => note.Title, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var (source, targets) in links)
            foreach (var target in targets)
            {
                degrees[source]++;
                degrees[target]++;
            }

        return new GraphLayout(points, links, degrees, selectedNeighbors);
    }

    private static string CreateCacheKey(
        IReadOnlyList<NoteInfo> notes,
        IReadOnlyDictionary<string, List<string>> links,
        double width,
        double height)
    {
        var nodes = string.Join("\u001f", notes.Select(note => note.Title).OrderBy(title => title, StringComparer.OrdinalIgnoreCase));
        var edges = string.Join("\u001f", links
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(pair => pair.Value
                .OrderBy(target => target, StringComparer.OrdinalIgnoreCase)
                .Select(target => $"{pair.Key}\u001e{target}")));
        return $"{width:F2}\u001d{height:F2}\u001d{nodes}\u001d{edges}";
    }

    private static GraphPoint InitialPoint(string title, double width, double height)
    {
        var random = new Random(StringComparer.Ordinal.GetHashCode(title));
        return new GraphPoint(width * (.12 + random.NextDouble() * .76), height * (.14 + random.NextDouble() * .72));
    }

    private static void AddRepulsion(string a, string b, IReadOnlyDictionary<string, GraphPoint> points, IDictionary<string, GraphPoint> forces)
    {
        var first = points[a]; var second = points[b];
        var dx = first.X - second.X; var dy = first.Y - second.Y;
        var distance = Math.Max(18, Math.Sqrt(dx * dx + dy * dy));
        var pull = Math.Min(5.5, 1800 / (distance * distance));
        var x = dx / distance * pull; var y = dy / distance * pull;
        forces[a] += new GraphPoint(x, y); forces[b] -= new GraphPoint(x, y);
    }

    private static void AddAttraction(string a, string b, IReadOnlyDictionary<string, GraphPoint> points, IDictionary<string, GraphPoint> forces)
    {
        var first = points[a]; var second = points[b];
        var dx = second.X - first.X; var dy = second.Y - first.Y;
        var distance = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var pull = (distance - 118) * .008;
        var x = dx / distance * pull; var y = dy / distance * pull;
        forces[a] += new GraphPoint(x, y); forces[b] -= new GraphPoint(x, y);
    }
}
