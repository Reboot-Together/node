namespace AsterismApp;

public enum GraphLabelMode
{
    FocusOnly,
    Orbit,
    Detail
}

public enum GraphCursorDirection
{
    East,
    NorthEast,
    North,
    NorthWest,
    West,
    SouthWest,
    South,
    SouthEast
}

public static class GraphViewportService
{
    public const double MinimumZoom = .18;
    public const double MaximumZoom = 2.4;

    public static double ChangeZoom(double currentZoom, bool zoomIn, double factor) =>
        Math.Clamp(currentZoom * (zoomIn ? factor : 1 / factor), MinimumZoom, MaximumZoom);

    public static GraphPoint CalculateZoomedViewportOffset(
        GraphPoint currentOffset,
        GraphPoint pointerInViewport,
        double zoomRatio,
        GraphPoint newContentSize,
        GraphPoint viewportSize)
    {
        var horizontal = (currentOffset.X + pointerInViewport.X) * zoomRatio - pointerInViewport.X;
        var vertical = (currentOffset.Y + pointerInViewport.Y) * zoomRatio - pointerInViewport.Y;
        return new GraphPoint(
            Math.Clamp(horizontal, 0, Math.Max(0, newContentSize.X - viewportSize.X)),
            Math.Clamp(vertical, 0, Math.Max(0, newContentSize.Y - viewportSize.Y)));
    }

    public static GraphLabelMode LabelMode(double zoom, bool hovering) =>
        zoom >= 1.1 ? GraphLabelMode.Detail
        : hovering || zoom >= .7 ? GraphLabelMode.Orbit
        : GraphLabelMode.FocusOnly;

    public static double VisualScale(double zoom) => Math.Clamp(zoom, .28, 1.35);

    public static double NodeRadius(double zoom, bool selected, int degree)
    {
        var baseRadius = selected ? 3.5 : 1.75;
        var degreeRadius = Math.Min(selected ? 1.25 : 1, Math.Max(0, degree) * .15);
        return (baseRadius + degreeRadius) * VisualScale(zoom);
    }

    public static GraphCursorDirection QuantizeCursorDirection(GraphPoint previous, GraphPoint current)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        var angle = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
        return (GraphCursorDirection)((int)Math.Round(angle / 45, MidpointRounding.AwayFromZero) % 8);
    }
}
