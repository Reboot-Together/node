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
    IReadOnlySet<string> SelectedNeighbors,
    IReadOnlyDictionary<string, string> EgoParents);

public sealed class GraphLayoutService
{
    private string? _cachedKey;
    private Dictionary<string, GraphPoint> _cachedPoints = new(StringComparer.OrdinalIgnoreCase);
    internal int SimulationRuns { get; private set; }

    public static IReadOnlyDictionary<string, int> RelationshipDepths(
        string? selectedTitle,
        IReadOnlyDictionary<string, List<string>> links,
        int maximumDepth)
    {
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(selectedTitle) || maximumDepth < 0) return depths;

        var queue = new Queue<string>();
        depths[selectedTitle] = 0;
        queue.Enqueue(selectedTitle);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var nextDepth = depths[current] + 1;
            if (nextDepth > maximumDepth) continue;

            if (links.TryGetValue(current, out var outgoingTargets))
                foreach (var target in outgoingTargets)
                    Add(target, nextDepth);
            foreach (var (source, incomingTargets) in links)
                if (incomingTargets.Contains(current, StringComparer.OrdinalIgnoreCase))
                    Add(source, nextDepth);
        }

        return depths;

        void Add(string title, int depth)
        {
            if (depths.ContainsKey(title)) return;
            depths[title] = depth;
            queue.Enqueue(title);
        }
    }

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

        IReadOnlyDictionary<string, string> egoParents =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            egoParents = ArrangeEgoConstellation(points, links, selectedTitle, width, height);
        }

        var degrees = notes.ToDictionary(note => note.Title, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var (source, targets) in links)
            foreach (var target in targets)
            {
                degrees[source]++;
                degrees[target]++;
            }

        return new GraphLayout(points, links, degrees, selectedNeighbors, egoParents);
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
        var maximumRadius = Math.Max(60, (Math.Min(width, height) / 2 - 38) * .75);

        if (others.Count >= 3 && directionalBias > .32)
        {
            var phase = StableSeed(selectedTitle) / (double)int.MaxValue * Math.PI * 2;
            var spacing = Math.PI * 2 / others.Count;
            for (var index = 0; index < others.Count; index++)
            {
                var item = others[index];
                var jitter = ((StableSeed(item.Key) & 255) / 255d - .5) * Math.Min(.16, spacing * .22);
                var angle = phase + index * spacing + jitter;
                var radius = Math.Clamp(item.Distance * .75, 32, maximumRadius);
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

    private static IReadOnlyDictionary<string, string> ArrangeEgoConstellation(
        IDictionary<string, GraphPoint> points,
        IReadOnlyDictionary<string, List<string>> links,
        string selectedTitle,
        double width,
        double height)
    {
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!points.ContainsKey(selectedTitle)) return parents;

        var depths = RelationshipDepths(selectedTitle, links, 2);
        var firstRing = depths
            .Where(pair => pair.Value == 1 && points.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .OrderBy(StableSeed)
            .ThenBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var secondRing = depths
            .Where(pair => pair.Value == 2 && points.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .OrderBy(StableSeed)
            .ThenBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (firstRing.Count == 0) return parents;

        var children = firstRing.ToDictionary(
            title => title,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var title in firstRing) parents[title] = selectedTitle;
        foreach (var title in secondRing)
        {
            var primaryParent = firstRing
                .Where(candidate => AreConnected(links, candidate, title))
                .OrderBy(candidate => children[candidate].Count)
                .ThenBy(candidate => StableSeed($"{title}\u001f{candidate}"))
                .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (primaryParent is null) continue;
            parents[title] = primaryParent;
            children[primaryParent].Add(title);
        }

        var focus = new GraphPoint(width / 2, height / 2);
        points[selectedTitle] = focus;
        var minimumDimension = Math.Min(width, height);
        var innerRadius = Math.Clamp(minimumDimension * .12, 80, 115);
        var outerRadius = Math.Clamp(minimumDimension * .27, innerRadius + 100, 250);
        var phase = StableSeed(selectedTitle) / (double)int.MaxValue * Math.PI * 2;
        var weights = firstRing.ToDictionary(
            title => title,
            title => Math.Max(1d, children[title].Count + .6),
            StringComparer.OrdinalIgnoreCase);
        var totalWeight = weights.Values.Sum();
        var sectorStart = phase;
        foreach (var parent in firstRing)
        {
            var sectorSpan = Math.PI * 2 * weights[parent] / totalWeight;
            var parentAngle = sectorStart + sectorSpan / 2;
            points[parent] = Polar(focus, innerRadius, parentAngle);

            var group = children[parent]
                .OrderBy(StableSeed)
                .ThenBy(title => title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var usableSpan = Math.Min(sectorSpan * .78, Math.PI * .86);
            for (var index = 0; index < group.Count; index++)
            {
                var fraction = group.Count == 1 ? .5 : (index + .5) / group.Count;
                var angle = parentAngle - usableSpan / 2 + usableSpan * fraction;
                var radiusJitter = ((StableSeed(group[index]) & 255) / 255d - .5) * 14;
                points[group[index]] = Polar(focus, outerRadius + radiusJitter, angle);
            }
            sectorStart += sectorSpan;
        }

        var backgroundMinimumRadius = Math.Min(
            minimumDimension / 2 - 42,
            outerRadius + 72);
        foreach (var title in points.Keys.Where(title => !depths.ContainsKey(title)).ToList())
        {
            var point = points[title];
            var dx = point.X - focus.X;
            var dy = point.Y - focus.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= backgroundMinimumRadius) continue;

            var angle = distance > .001
                ? Math.Atan2(dy, dx)
                : StableSeed(title) / (double)int.MaxValue * Math.PI * 2;
            points[title] = Polar(focus, backgroundMinimumRadius, angle);
        }

        foreach (var title in points.Keys.ToList())
            points[title] = new GraphPoint(
                Math.Clamp(points[title].X, 24, width - 24),
                Math.Clamp(points[title].Y, 24, height - 24));
        return parents;
    }

    private static bool AreConnected(
        IReadOnlyDictionary<string, List<string>> links,
        string first,
        string second) =>
        (links.TryGetValue(first, out var firstTargets)
            && firstTargets.Contains(second, StringComparer.OrdinalIgnoreCase))
        || (links.TryGetValue(second, out var secondTargets)
            && secondTargets.Contains(first, StringComparer.OrdinalIgnoreCase));

    private static GraphPoint Polar(GraphPoint center, double radius, double angle) => new(
        center.X + Math.Cos(angle) * radius,
        center.Y + Math.Sin(angle) * radius);

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
        return new GraphPoint(width * (.18 + random.NextDouble() * .64), height * (.18 + random.NextDouble() * .64));
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
        var distance = Math.Max(14, Math.Sqrt(dx * dx + dy * dy));
        var pull = Math.Min(4.5, 1050 / (distance * distance));
        var x = dx / distance * pull; var y = dy / distance * pull;
        forces[a] += new GraphPoint(x, y); forces[b] -= new GraphPoint(x, y);
    }

    private static void AddAttraction(string a, string b, IReadOnlyDictionary<string, GraphPoint> points, IDictionary<string, GraphPoint> forces)
    {
        var first = points[a]; var second = points[b];
        var dx = second.X - first.X; var dy = second.Y - first.Y;
        var distance = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var pull = (distance - 82) * .01;
        var x = dx / distance * pull; var y = dy / distance * pull;
        forces[a] += new GraphPoint(x, y); forces[b] -= new GraphPoint(x, y);
    }
}
