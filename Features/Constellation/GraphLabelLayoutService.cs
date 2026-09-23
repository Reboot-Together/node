namespace AsterismApp;

public enum GraphLabelRole
{
    Focus,
    Selected,
    Neighbor,
    Global,
    Summary
}

public sealed record GraphLabelCandidate(
    string Title,
    GraphPoint Anchor,
    double AnchorRadius,
    double FontSize,
    double MaximumWidth,
    int Priority,
    GraphLabelRole Role);

public sealed record GraphLabelPlacement(
    GraphLabelCandidate Candidate,
    GraphPoint Position,
    double Width,
    double Height);

public readonly record struct GraphLineObstacle(GraphPoint From, GraphPoint To);

public sealed class GraphLabelLayoutService
{
    private static readonly double[] AlternativeAngles =
    [0, Math.PI / 4, -Math.PI / 4, Math.PI / 2, -Math.PI / 2, 3 * Math.PI / 4, -3 * Math.PI / 4, Math.PI];
    private static readonly double[] AlternativeDistances = [0, 12, 24];

    public IReadOnlyList<GraphLabelPlacement> Arrange(
        IEnumerable<GraphLabelCandidate> candidates,
        GraphPoint focus,
        double width,
        double height,
        IEnumerable<GraphLineObstacle>? lineObstacles = null)
    {
        var placements = new List<GraphLabelPlacement>();
        var obstacles = lineObstacles?.ToList() ?? [];
        foreach (var candidate in candidates.OrderBy(item => item.Priority))
        {
            var (labelWidth, labelHeight) = EstimateSize(candidate.Title, candidate.FontSize, candidate.MaximumWidth);
            var preferredAngle = PreferredAngle(candidate, focus);
            GraphPoint? accepted = null;
            foreach (var extraDistance in AlternativeDistances)
            {
                foreach (var angleOffset in AlternativeAngles)
                {
                    var position = PositionAt(
                        candidate,
                        preferredAngle + angleOffset,
                        labelWidth,
                        labelHeight,
                        width,
                        height,
                        extraDistance);
                    if (placements.All(existing => !Intersects(position, labelWidth, labelHeight, existing))
                        && obstacles.All(obstacle => !Intersects(position, labelWidth, labelHeight, obstacle)))
                    {
                        accepted = position;
                        break;
                    }
                }
                if (accepted is not null) break;
            }

            if (accepted is null && candidate.Role is GraphLabelRole.Focus or GraphLabelRole.Selected)
                accepted = PositionAt(candidate, preferredAngle, labelWidth, labelHeight, width, height);
            if (accepted is not null)
                placements.Add(new GraphLabelPlacement(candidate, accepted.Value, labelWidth, labelHeight));
        }
        return placements;
    }

    private static double PreferredAngle(GraphLabelCandidate candidate, GraphPoint focus)
    {
        if (candidate.Role is GraphLabelRole.Focus or GraphLabelRole.Selected) return 0;
        var dx = candidate.Anchor.X - focus.X;
        var dy = candidate.Anchor.Y - focus.Y;
        return Math.Abs(dx) + Math.Abs(dy) < .1 ? Math.PI / 2 : Math.Atan2(dy, dx);
    }

    private static GraphPoint PositionAt(
        GraphLabelCandidate candidate,
        double angle,
        double labelWidth,
        double labelHeight,
        double width,
        double height,
        double extraDistance = 0)
    {
        var directionX = Math.Cos(angle);
        var directionY = Math.Sin(angle);
        var gap = candidate.AnchorRadius + 6 + extraDistance;
        var edgeX = candidate.Anchor.X + directionX * gap;
        var edgeY = candidate.Anchor.Y + directionY * gap;
        var x = Math.Abs(directionX) < .35
            ? edgeX - labelWidth / 2
            : directionX > 0 ? edgeX : edgeX - labelWidth;
        var y = edgeY - labelHeight / 2;
        return new GraphPoint(
            Math.Clamp(x, 4, Math.Max(4, width - labelWidth - 4)),
            Math.Clamp(y, 4, Math.Max(4, height - labelHeight - 4)));
    }

    private static bool Intersects(GraphPoint position, double width, double height, GraphLabelPlacement existing)
    {
        const double padding = 5;
        return position.X < existing.Position.X + existing.Width + padding
            && position.X + width + padding > existing.Position.X
            && position.Y < existing.Position.Y + existing.Height + padding
            && position.Y + height + padding > existing.Position.Y;
    }

    private static bool Intersects(
        GraphPoint position,
        double width,
        double height,
        GraphLineObstacle obstacle)
    {
        const double padding = 3;
        var left = position.X - padding;
        var right = position.X + width + padding;
        var top = position.Y - padding;
        var bottom = position.Y + height + padding;
        if (Contains(obstacle.From) || Contains(obstacle.To)) return true;

        return SegmentsIntersect(obstacle.From, obstacle.To, new GraphPoint(left, top), new GraphPoint(right, top))
            || SegmentsIntersect(obstacle.From, obstacle.To, new GraphPoint(right, top), new GraphPoint(right, bottom))
            || SegmentsIntersect(obstacle.From, obstacle.To, new GraphPoint(right, bottom), new GraphPoint(left, bottom))
            || SegmentsIntersect(obstacle.From, obstacle.To, new GraphPoint(left, bottom), new GraphPoint(left, top));

        bool Contains(GraphPoint point) =>
            point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom;
    }

    private static bool SegmentsIntersect(GraphPoint a, GraphPoint b, GraphPoint c, GraphPoint d)
    {
        static double Cross(GraphPoint first, GraphPoint second, GraphPoint third) =>
            (second.X - first.X) * (third.Y - first.Y)
            - (second.Y - first.Y) * (third.X - first.X);

        const double epsilon = .001;
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) - epsilon
            || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) - epsilon
            || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) - epsilon
            || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y) - epsilon)
            return false;
        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);
        return abC * abD <= epsilon && cdA * cdB <= epsilon;
    }

    private static (double Width, double Height) EstimateSize(string text, double fontSize, double maximumWidth)
    {
        var units = text.Sum(character => character > 255 ? 1d : .58);
        var naturalWidth = Math.Max(28, units * fontSize + 8);
        var width = Math.Min(maximumWidth, naturalWidth);
        var lineCount = naturalWidth > maximumWidth ? 2 : 1;
        return (width, fontSize * 1.45 * lineCount + 4);
    }
}
