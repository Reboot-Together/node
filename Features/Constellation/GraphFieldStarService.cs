namespace AsterismApp;

public readonly record struct GraphFieldStar(
    GraphPoint Position,
    double Radius,
    byte Alpha,
    int PaletteIndex,
    double MinimumOpacity,
    bool Twinkles);

public static class GraphFieldStarService
{
    public static IReadOnlyList<GraphFieldStar> Generate(
        double width,
        double height,
        double zoom)
    {
        if (width <= 36 || height <= 36) return [];

        var progress = Math.Clamp(
            (zoom - GraphViewportService.MinimumZoom)
            / (GraphViewportService.MaximumZoom - GraphViewportService.MinimumZoom),
            0,
            1);
        var targetCount = (int)Math.Round(260 - 120 * Math.Pow(progress, .7));
        var random = new Random(0x0A57E215);
        var stars = new List<GraphFieldStar>(targetCount);
        var attempts = 0;

        while (stars.Count < targetCount && attempts++ < targetCount * 40)
        {
            var x = 18 + random.NextDouble() * (width - 36);
            var y = 18 + random.NextDouble() * (height - 36);
            var candidate = new GraphPoint(x, y);
            if (stars.Any(star => Distance(star.Position, candidate) < 4)) continue;

            var brightness = random.NextDouble();
            var twinkles = stars.Count % 5 == 0;
            stars.Add(new GraphFieldStar(
                candidate,
                brightness > .96 ? 1.3 : brightness > .76 ? .9 : .7,
                brightness > .96 ? (byte)215 : brightness > .76 ? (byte)172 : (byte)132,
                random.Next(4),
                brightness > .96 ? .7 : .58,
                twinkles));
        }

        return stars;
    }

    private static double Distance(GraphPoint first, GraphPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
