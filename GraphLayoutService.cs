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

        var selectedNeighbors = links
            .Where(pair => pair.Key.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase) || pair.Value.Contains(selectedTitle ?? "", StringComparer.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Key.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase) ? pair.Value.AsEnumerable() : [pair.Key])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedTitle is not null && points.TryGetValue(selectedTitle, out var selectedPoint))
        {
            var offsetX = width / 2 - selectedPoint.X;
            var offsetY = height / 2 - selectedPoint.Y;
            foreach (var title in points.Keys.ToList())
            {
                var point = points[title];
                points[title] = new GraphPoint(point.X + offsetX, point.Y + offsetY);
            }

            BalanceAroundSelected(points, selectedTitle, width, height);
            BringSelectedNeighborsCloser(points, selectedTitle, selectedNeighbors, width, height);
        }

        var degrees = notes.ToDictionary(note => note.Title, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var (source, targets) in links)
            foreach (var target in targets)
            {
                degrees[source]++;
                degrees[target]++;
            }

        return new GraphLayout(points, links, degrees, selectedNeighbors);
    }

    private static void BalanceAroundSelected(
        IDictionary<string, GraphPoint> points,
        string selectedTitle,
        double width,
        double height)
    {
        if (!points.TryGetValue(selectedTitle, out var focus)) return;
        var others = points
            .Where(pair => !pair.Key.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase))
            .Select(pair =>
            {
                var dx = pair.Value.X - focus.X;
                var dy = pair.Value.Y - focus.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                return (pair.Key, Distance: distance, Angle: Math.Atan2(dy, dx));
            })
            .Where(item => item.Distance > .001)
            .OrderBy(item => item.Angle)
            .ToList();
        if (others.Count == 0) return;

        var meanX = others.Average(item => Math.Cos(item.Angle));
        var meanY = others.Average(item => Math.Sin(item.Angle));
        var directionalBias = Math.Sqrt(meanX * meanX + meanY * meanY);
        var maximumRadius = Math.Max(80, Math.Min(width, height) / 2 - 38);

        if (others.Count >= 3 && directionalBias > .32)
        {
            var phase = StableSeed(selectedTitle) / (double)int.MaxValue * Math.PI * 2;
            var spacing = Math.PI * 2 / others.Count;
            for (var index = 0; index < others.Count; index++)
            {
                var item = others[index];
                var jitter = ((StableSeed(item.Key) & 255) / 255d - .5) * Math.Min(.16, spacing * .22);
                var angle = phase + index * spacing + jitter;
                var radius = Math.Clamp(item.Distance, 42, maximumRadius);
                points[item.Key] = new GraphPoint(
                    focus.X + Math.Cos(angle) * radius,
                    focus.Y + Math.Sin(angle) * radius);
            }
        }

        foreach (var title in points.Keys.ToList())
        {
            var point = points[title];
            points[title] = new GraphPoint(
                Math.Clamp(point.X, 24, width - 24),
                Math.Clamp(point.Y, 24, height - 24));
        }
    }

    private static void BringSelectedNeighborsCloser(
        IDictionary<string, GraphPoint> points,
        string selectedTitle,
        IEnumerable<string> selectedNeighbors,
        double width,
        double height)
    {
        if (!points.TryGetValue(selectedTitle, out var focus)) return;
        var neighbors = selectedNeighbors
            .Where(points.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var additionalRoom = Math.Min(9, Math.Max(0, neighbors.Count - 3) * 1.5);
        var maximumDistance = Math.Clamp(Math.Min(width, height) * .06 + additionalRoom, 39, 52);
        foreach (var title in neighbors)
        {
            var point = points[title];
            var dx = point.X - focus.X;
            var dy = point.Y - focus.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= maximumDistance || distance < .001) continue;

            var scale = maximumDistance / distance;
            points[title] = new GraphPoint(
                Math.Clamp(focus.X + dx * scale, 24, width - 24),
                Math.Clamp(focus.Y + dy * scale, 24, height - 24));
        }
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
        var random = new Random(StableSeed(title));
        return new GraphPoint(width * (.12 + random.NextDouble() * .76), height * (.14 + random.NextDouble() * .72));
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)(hash & int.MaxValue);
        }
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
