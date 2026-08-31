using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace NodeApp;

public sealed partial class MainWindow
{
    private readonly GraphLayoutService _graphLayoutService = new();
    private double _graphZoom = 1;
    private Dictionary<string, GraphPoint> _graphPoints = new(StringComparer.OrdinalIgnoreCase);
    private bool _graphPanning;
    private uint _graphPointerId;
    private Windows.Foundation.Point _graphDragStart;
    private double _graphHorizontalStart;
    private double _graphVerticalStart;

    private void GraphZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _graphZoom = Math.Min(1.8, _graphZoom + .2);
        DrawGraph();
    }

    private void GraphZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _graphZoom = Math.Max(.65, _graphZoom - .2);
        DrawGraph();
    }

    private void GraphCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(GraphCanvas).Properties.MouseWheelDelta;
        if (delta == 0) return;

        _graphZoom = Math.Clamp(_graphZoom + (delta > 0 ? .1 : -.1), .65, 1.8);
        e.Handled = true;
        DrawGraph();
    }

    private void DrawGraph()
    {
        if (GraphCanvas is null) return;

        GraphZoomText.Text = $"{_graphZoom:P0}";
        GraphCanvas.Width = 1200 * _graphZoom;
        GraphCanvas.Height = 800 * _graphZoom;
        GraphCanvas.Children.Clear();

        var notes = _notes
            .GroupBy(note => note.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (notes.Count == 0) return;

        var links = _linkService.Build(notes);
        var selectedTitle = _selected?.Title;
        var layout = _graphLayoutService.Calculate(
            notes,
            links,
            GraphCanvas.Width,
            GraphCanvas.Height,
            selectedTitle);
        _graphPoints = new Dictionary<string, GraphPoint>(layout.Points, StringComparer.OrdinalIgnoreCase);

        foreach (var (source, targets) in layout.Links)
        {
            foreach (var target in targets)
            {
                var from = layout.Points[source];
                var to = layout.Points[target];
                var highlighted = source.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase)
                    || target.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase);
                GraphCanvas.Children.Add(new Line
                {
                    X1 = from.X,
                    Y1 = from.Y,
                    X2 = to.X,
                    Y2 = to.Y,
                    Stroke = new SolidColorBrush(highlighted
                        ? ColorHelper.FromArgb(210, 16, 163, 127)
                        : ColorHelper.FromArgb(105, 92, 92, 92)),
                    StrokeThickness = highlighted ? 1.8 : 1
                });
            }
        }

        foreach (var note in notes)
        {
            var point = layout.Points[note.Title];
            var selected = note.Title.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase);
            var connected = layout.SelectedNeighbors.Contains(note.Title);
            var radius = (selected ? 12 : 7) + Math.Min(5, layout.Degrees[note.Title] * .8);

            if (selected)
                AddCircle(point, radius + 6, ColorHelper.FromArgb(45, 16, 163, 127), note, false);
            AddCircle(point, radius, NodeColor(note), note, true);

            var label = new TextBlock
            {
                Text = note.Title,
                Foreground = new SolidColorBrush(selected || connected
                    ? ColorHelper.FromArgb(255, 35, 35, 35)
                    : ColorHelper.FromArgb(195, 105, 105, 105)),
                FontSize = selected ? 11 : 10,
                FontWeight = selected
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                IsHitTestVisible = false,
                MaxWidth = 115,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(label, point.X + radius + 5);
            Canvas.SetTop(label, point.Y - 7);
            Canvas.SetZIndex(label, 3);
            GraphCanvas.Children.Add(label);
        }

        DispatcherQueue.TryEnqueue(CenterCurrentGraphNode);
    }

    private void CenterCurrentGraphNode()
    {
        if (_selected is null || !_graphPoints.TryGetValue(_selected.Title, out var point)) return;

        var horizontal = Math.Max(0, point.X - GraphScroll.ViewportWidth / 2);
        var vertical = Math.Max(0, point.Y - GraphScroll.ViewportHeight / 2);
        GraphScroll.ChangeView(horizontal, vertical, null, true);
    }

    private void GraphScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(CenterCurrentGraphNode);

    private void GraphCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, GraphCanvas)) return;

        _graphPanning = false;
        if (_selected is not null && _graphPoints.ContainsKey(_selected.Title))
        {
            CenterCurrentGraphNode();
        }
        else
        {
            var horizontal = Math.Max(0, (GraphCanvas.Width - GraphScroll.ViewportWidth) / 2);
            var vertical = Math.Max(0, (GraphCanvas.Height - GraphScroll.ViewportHeight) / 2);
            GraphScroll.ChangeView(horizontal, vertical, null, true);
        }

        e.Handled = true;
    }

    private void GraphCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _graphPanning = true;
        _graphPointerId = e.Pointer.PointerId;
        _graphDragStart = e.GetCurrentPoint(GraphScroll).Position;
        _graphHorizontalStart = GraphScroll.HorizontalOffset;
        _graphVerticalStart = GraphScroll.VerticalOffset;
        GraphCanvas.CapturePointer(e.Pointer);
    }

    private void GraphCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_graphPanning || e.Pointer.PointerId != _graphPointerId) return;

        var point = e.GetCurrentPoint(GraphScroll).Position;
        GraphScroll.ChangeView(
            _graphHorizontalStart - (point.X - _graphDragStart.X),
            _graphVerticalStart - (point.Y - _graphDragStart.Y),
            null,
            true);
        e.Handled = true;
    }

    private void GraphCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_graphPanning || e.Pointer.PointerId != _graphPointerId) return;

        _graphPanning = false;
        GraphCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void AddCircle(GraphPoint point, double radius, Windows.UI.Color color, NoteInfo note, bool clickable)
    {
        var circle = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(180, 255, 255, 255)),
            StrokeThickness = clickable ? 1 : 0,
            Opacity = clickable ? 1 : .8
        };
        Canvas.SetLeft(circle, point.X - radius);
        Canvas.SetTop(circle, point.Y - radius);
        Canvas.SetZIndex(circle, clickable ? 2 : 1);
        if (clickable)
        {
            ToolTipService.SetToolTip(circle, note.Title);
            circle.Tapped += (_, _) => Select(note);
        }
        GraphCanvas.Children.Add(circle);
    }

    private static Windows.UI.Color NodeColor(NoteInfo note) =>
        note.Metadata.Source.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)
            ? ColorHelper.FromArgb(255, 16, 163, 127)
            : note.Metadata.Type.Equals("Daily", StringComparison.OrdinalIgnoreCase)
                ? ColorHelper.FromArgb(255, 94, 149, 255)
                : ColorHelper.FromArgb(255, 151, 151, 151);
}
