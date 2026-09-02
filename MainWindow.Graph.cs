using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private const double GraphLogicalWidth = 720;
    private const double GraphLogicalHeight = 1200;
    private readonly GraphLayoutService _graphLayoutService = new();
    private readonly GraphLabelLayoutService _graphLabelLayoutService = new();
    private double _graphZoom = .72;
    private Dictionary<string, GraphPoint> _graphPoints = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, int> _graphRelationshipDepths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private GraphLayout? _activeGraphLayout;
    private readonly List<UIElement> _graphLabelElements = [];
    private string? _hoveredGraphTitle;
    private bool _graphPanning;
    private uint _graphPointerId;
    private Windows.Foundation.Point _graphDragStart;
    private double _graphHorizontalStart;
    private double _graphVerticalStart;
    private int _graphViewportRevision;
    private readonly List<Visual> _graphTwinkleVisuals = [];
    private GraphPoint? _graphCursorLastPoint;
    private GraphCursorDirection? _graphCursorDirection;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _graphCursorIdleTimer;
    private bool _graphCursorMoving;

    private void GraphZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _graphZoom = GraphViewportService.ChangeZoom(_graphZoom, zoomIn: true, 1.25);
        DrawGraph();
    }

    private void GraphZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _graphZoom = GraphViewportService.ChangeZoom(_graphZoom, zoomIn: false, 1.25);
        DrawGraph();
    }

    private void GraphCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(GraphScroll).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var previousZoom = _graphZoom;
        var nextZoom = GraphViewportService.ChangeZoom(previousZoom, delta > 0, 1.12);
        e.Handled = true;
        if (Math.Abs(nextZoom - previousZoom) < .001) return;

        var pointer = e.GetCurrentPoint(GraphScroll).Position;
        var targetOffset = GraphViewportService.CalculateZoomedViewportOffset(
            new GraphPoint(GraphScroll.HorizontalOffset, GraphScroll.VerticalOffset),
            new GraphPoint(pointer.X, pointer.Y),
            nextZoom / previousZoom,
            new GraphPoint(GraphLogicalWidth * nextZoom, GraphLogicalHeight * nextZoom),
            new GraphPoint(GraphScroll.ViewportWidth, GraphScroll.ViewportHeight));
        var viewportRevision = ++_graphViewportRevision;

        _graphZoom = nextZoom;
        DrawGraph(centerCurrentNode: false);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (viewportRevision != _graphViewportRevision) return;
            GraphScroll.ChangeView(targetOffset.X, targetOffset.Y, null, true);
        });
    }

    private void DrawGraph(bool centerCurrentNode = true)
    {
        if (GraphCanvas is null) return;

        var viewportRevision = centerCurrentNode ? ++_graphViewportRevision : _graphViewportRevision;

        GraphZoomText.Text = $"{_graphZoom:P0}";
        GraphCanvas.Width = GraphLogicalWidth * _graphZoom;
        GraphCanvas.Height = GraphLogicalHeight * _graphZoom;
        StopGraphTwinkles();
        GraphCanvas.Children.Clear();
        _graphLabelElements.Clear();
        AddConstellationField(GraphCanvas.Width, GraphCanvas.Height);

        var notes = _notes
            .GroupBy(note => note.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (notes.Count == 0) return;

        var selectedTitle = _selected?.Title;
        var graphLinks = MergeGraphLinks(_noteLinks, _semanticLinks, notes.Select(note => note.Title));
        _graphRelationshipDepths = GraphLayoutService.RelationshipDepths(selectedTitle, graphLinks, 2);
        var layout = _graphLayoutService.Calculate(
            notes,
            graphLinks,
            GraphLogicalWidth,
            GraphLogicalHeight,
            selectedTitle);
        _activeGraphLayout = layout;
        _graphPoints = layout.Points.ToDictionary(
            pair => pair.Key,
            pair => ScaleGraphPoint(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var visualScale = GraphViewportService.VisualScale(_graphZoom);

        foreach (var (source, targets) in layout.Links)
        {
            foreach (var target in targets)
            {
                var from = ScaleGraphPoint(layout.Points[source]);
                var to = ScaleGraphPoint(layout.Points[target]);
                var explicitLink = HasGraphEdge(_noteLinks, source, target);
                var direct = source.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase)
                    || target.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase);
                var withinConstellation = _graphRelationshipDepths.TryGetValue(source, out var sourceDepth)
                    && _graphRelationshipDepths.TryGetValue(target, out var targetDepth)
                    && sourceDepth <= 2
                    && targetDepth <= 2;
                var emphasis = direct ? 1 : withinConstellation ? 2 : 0;
                var line = new Line
                {
                    X1 = from.X,
                    Y1 = from.Y,
                    X2 = to.X,
                    Y2 = to.Y,
                    Stroke = new SolidColorBrush(explicitLink
                        ? emphasis switch
                        {
                            1 => GraphAccent(240, bright: true),
                            2 => GraphAccent(150),
                            _ => ColorHelper.FromArgb(72, 150, 150, 150)
                        }
                        : emphasis switch
                        {
                            1 => ColorHelper.FromArgb(120, 175, 175, 175),
                            2 => ColorHelper.FromArgb(82, 150, 150, 150),
                            _ => ColorHelper.FromArgb(38, 115, 115, 115)
                        }),
                    StrokeThickness = (emphasis switch { 1 => 1.6, 2 => 1.15, _ => .72 }) * visualScale
                };
                GraphCanvas.Children.Add(line);
            }
        }

        foreach (var note in notes)
        {
            var point = _graphPoints[note.Title];
            var selected = note.Title.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase);
            var radius = GraphViewportService.NodeRadius(_graphZoom, selected, layout.Degrees[note.Title]);

            AddStar(point, radius, StarColor(note), note, selected);
        }

        if (_hoveredGraphTitle is not null && !_graphPoints.ContainsKey(_hoveredGraphTitle))
            _hoveredGraphTitle = null;
        RefreshGraphLabels();

        if (centerCurrentNode)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (viewportRevision == _graphViewportRevision) CenterCurrentGraphNode();
            });
    }

    private void CenterCurrentGraphNode()
    {
        if (_selected is null || !_graphPoints.TryGetValue(_selected.Title, out var point)) return;

        var horizontal = Math.Max(0, point.X - GraphScroll.ViewportWidth / 2);
        var vertical = Math.Max(0, point.Y - GraphScroll.ViewportHeight / 2);
        GraphScroll.ChangeView(horizontal, vertical, null, true);
    }

    private GraphPoint ScaleGraphPoint(GraphPoint point) => new(point.X * _graphZoom, point.Y * _graphZoom);

    private void GraphScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var viewportRevision = ++_graphViewportRevision;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (viewportRevision == _graphViewportRevision) CenterCurrentGraphNode();
        });
    }

    private void GraphCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, GraphCanvas)) return;

        _graphPanning = false;
        _graphViewportRevision++;
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
        _graphViewportRevision++;
        _graphPanning = true;
        _graphPointerId = e.Pointer.PointerId;
        _graphDragStart = e.GetCurrentPoint(GraphScroll).Position;
        _graphHorizontalStart = GraphScroll.HorizontalOffset;
        _graphVerticalStart = GraphScroll.VerticalOffset;
        GraphCanvas.CapturePointer(e.Pointer);
    }

    private void GraphCanvas_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        var point = e.GetCurrentPoint(GraphScroll).Position;
        _graphCursorLastPoint = new GraphPoint(point.X, point.Y);
        GraphCanvas.SetCursor(GraphCursorDirection.East, moving: false);
        _graphCursorDirection = GraphCursorDirection.East;
        _graphCursorMoving = false;
    }

    private void GraphCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            ResetGraphDirectionalCursor();
    }

    private void GraphCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            UpdateGraphDirectionalCursor(e);
        if (!_graphPanning || e.Pointer.PointerId != _graphPointerId) return;

        var point = e.GetCurrentPoint(GraphScroll).Position;
        GraphScroll.ChangeView(
            _graphHorizontalStart - (point.X - _graphDragStart.X),
            _graphVerticalStart - (point.Y - _graphDragStart.Y),
            null,
            true);
        e.Handled = true;
    }

    private void UpdateGraphDirectionalCursor(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(GraphScroll).Position;
        var current = new GraphPoint(position.X, position.Y);
        if (_graphCursorLastPoint is { } previous)
        {
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            if (dx * dx + dy * dy >= 4)
            {
                var direction = GraphViewportService.QuantizeCursorDirection(previous, current);
                if (_graphCursorDirection != direction || !_graphCursorMoving)
                {
                    GraphCanvas.SetCursor(direction, moving: true);
                    _graphCursorDirection = direction;
                    _graphCursorMoving = true;
                }
                _graphCursorLastPoint = current;
                RestartGraphCursorIdleTimer();
            }
        }
        else
        {
            _graphCursorLastPoint = current;
        }
    }

    private void ResetGraphDirectionalCursor()
    {
        _graphCursorIdleTimer?.Stop();
        GraphCanvas?.ResetCursor();
        _graphCursorLastPoint = null;
        _graphCursorDirection = null;
        _graphCursorMoving = false;
    }

    private void RestartGraphCursorIdleTimer()
    {
        if (_graphCursorIdleTimer is null)
        {
            _graphCursorIdleTimer = DispatcherQueue.CreateTimer();
            _graphCursorIdleTimer.Interval = TimeSpan.FromMilliseconds(120);
            _graphCursorIdleTimer.IsRepeating = false;
            _graphCursorIdleTimer.Tick += (_, _) =>
            {
                if (_graphCursorDirection is not { } direction) return;
                GraphCanvas.SetCursor(direction, moving: false);
                _graphCursorMoving = false;
            };
        }

        _graphCursorIdleTimer.Stop();
        _graphCursorIdleTimer.Start();
    }

    private void GraphCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_graphPanning || e.Pointer.PointerId != _graphPointerId) return;

        _graphPanning = false;
        GraphCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void AddConstellationField(double width, double height)
    {
        var centerX = width / 2;
        var centerY = height / 2;
        var outerRadius = Math.Max(80, Math.Min(width, height) / 2 - 42);
        var gridBrush = new SolidColorBrush(ColorHelper.FromArgb(28, 145, 145, 145));
        foreach (var scale in new[] { 1d, .78, .56, .34 })
        {
            var ring = new Ellipse
            {
                Width = outerRadius * 2 * scale,
                Height = outerRadius * 2 * scale,
                Stroke = gridBrush,
                StrokeThickness = scale == 1 ? 1.1 : .7,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, centerX - ring.Width / 2);
            Canvas.SetTop(ring, centerY - ring.Height / 2);
            GraphCanvas.Children.Add(ring);
        }

        for (var index = 0; index < 16; index++)
        {
            var angle = Math.PI * 2 * index / 16;
            var spoke = new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX + Math.Cos(angle) * outerRadius,
                Y2 = centerY + Math.Sin(angle) * outerRadius,
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(17, 145, 145, 145)),
                StrokeThickness = .65,
                IsHitTestVisible = false
            };
            GraphCanvas.Children.Add(spoke);
        }

        if (_graphZoom >= .4)
        {
            foreach (var (text, x, y) in new[]
            {
                ("N", centerX, centerY - outerRadius + 13),
                ("E", centerX + outerRadius - 13, centerY),
                ("S", centerX, centerY + outerRadius - 17),
                ("W", centerX - outerRadius + 9, centerY)
            })
            {
                var marker = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Cascadia Mono"),
                    FontSize = 8,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(155, 155, 155, 155)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, x - 3);
                Canvas.SetTop(marker, y - 6);
                GraphCanvas.Children.Add(marker);
            }
        }

        var fieldStarCount = Math.Clamp((int)(40 + 50 * _graphZoom), 45, 150);
        for (var index = 0; index < fieldStarCount; index++)
        {
            var radius = index % 17 == 0 ? 1.25 : index % 7 == 0 ? .8 : .45;
            var alpha = index % 17 == 0 ? (byte)150 : (byte)72;
            var star = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(FieldStarColor(index, alpha)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(star, 24 + (index * 223 % Math.Max(1, (int)width - 48)));
            Canvas.SetTop(star, 20 + (index * 137 % Math.Max(1, (int)height - 40)));
            GraphCanvas.Children.Add(star);
            StartGraphTwinkle(star, $"field:{index}", index % 17 == 0 ? .35 : .16);
        }
    }

    private void AddStar(
        GraphPoint point,
        double radius,
        Windows.UI.Color color,
        NoteInfo note,
        bool selected)
    {
        if (selected)
        {
            var haloPadding = Math.Max(1, 2.5 * GraphViewportService.VisualScale(_graphZoom));
            var halo = new Ellipse
            {
                Width = (radius + haloPadding) * 2,
                Height = (radius + haloPadding) * 2,
                Fill = new SolidColorBrush(GraphAccent(58, bright: true)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(halo, point.X - radius - haloPadding);
            Canvas.SetTop(halo, point.Y - radius - haloPadding);
            Canvas.SetZIndex(halo, 1);
            GraphCanvas.Children.Add(halo);
        }

        var core = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = new SolidColorBrush(selected ? GraphAccent(255, bright: true) : color),
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(225, 255, 255, 255)),
            StrokeThickness = selected ? .85 : .55,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(core, point.X - radius);
        Canvas.SetTop(core, point.Y - radius);
        Canvas.SetZIndex(core, 3);
        GraphCanvas.Children.Add(core);
        StartGraphTwinkle(core, $"note:{note.Title}", selected ? .55 : .3);

        var hitRadius = Math.Max(8, radius);
        var hitTarget = new Ellipse
        {
            Width = hitRadius * 2,
            Height = hitRadius * 2,
            Fill = new SolidColorBrush(Colors.Transparent)
        };
        Canvas.SetLeft(hitTarget, point.X - hitRadius);
        Canvas.SetTop(hitTarget, point.Y - hitRadius);
        Canvas.SetZIndex(hitTarget, 5);
        ToolTipService.SetToolTip(hitTarget, note.Title);
        hitTarget.Tapped += (_, _) => Select(note);
        hitTarget.PointerEntered += (_, _) => SetHoveredGraphNode(note.Title);
        hitTarget.PointerExited += (_, _) => ClearHoveredGraphNode(note.Title);
        GraphCanvas.Children.Add(hitTarget);
    }

    private void StartGraphTwinkle(FrameworkElement star, string key, double minimumOpacity)
    {
        var seed = unchecked((uint)StringComparer.Ordinal.GetHashCode(key));
        var durationSeconds = 1.6 + seed % 31 / 10d;
        var delaySeconds = (seed >> 8) % 13 / 10d;
        var visual = ElementCompositionPreview.GetElementVisual(star);
        var easing = visual.Compositor.CreateCubicBezierEasingFunction(
            new Vector2(.42f, 0),
            new Vector2(.58f, 1));
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, (float)minimumOpacity, easing);
        animation.InsertKeyFrame(.5f, 1, easing);
        animation.InsertKeyFrame(1, (float)minimumOpacity, easing);
        animation.Duration = TimeSpan.FromSeconds(durationSeconds);
        animation.DelayTime = TimeSpan.FromSeconds(delaySeconds);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.Opacity = (float)minimumOpacity;
        visual.StartAnimation(nameof(Visual.Opacity), animation);
        _graphTwinkleVisuals.Add(visual);
    }

    private void StopGraphTwinkles()
    {
        foreach (var visual in _graphTwinkleVisuals)
            visual.StopAnimation(nameof(Visual.Opacity));
        _graphTwinkleVisuals.Clear();
    }

    private void SetHoveredGraphNode(string title)
    {
        if (_graphPanning || title.Equals(_hoveredGraphTitle, StringComparison.OrdinalIgnoreCase)) return;
        _hoveredGraphTitle = title;
        RefreshGraphLabels();
    }

    private void ClearHoveredGraphNode(string title)
    {
        if (!title.Equals(_hoveredGraphTitle, StringComparison.OrdinalIgnoreCase)) return;
        _hoveredGraphTitle = null;
        RefreshGraphLabels();
    }

    private void RefreshGraphLabels()
    {
        foreach (var element in _graphLabelElements) GraphCanvas.Children.Remove(element);
        _graphLabelElements.Clear();
        if (_activeGraphLayout is null || _graphPoints.Count == 0) return;

        var selectedTitle = _selected?.Title;
        var focusTitle = _hoveredGraphTitle is not null
            && _graphPoints.ContainsKey(_hoveredGraphTitle)
            && CanShowGraphLabel(_hoveredGraphTitle)
            ? _hoveredGraphTitle
            : selectedTitle;
        if (focusTitle is null || !_graphPoints.TryGetValue(focusTitle, out var focusPoint)) return;

        var hovering = _hoveredGraphTitle is not null;
        var mode = GraphViewportService.LabelMode(_graphZoom, hovering);
        var neighbors = GraphNeighborsOf(focusTitle, _activeGraphLayout.Links);
        var labelScale = Math.Clamp(Math.Sqrt(_graphZoom), .82, 1.1) * _uiLayoutSettings.FontScale;
        var candidates = new List<GraphLabelCandidate>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!focusTitle.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase))
            AddCandidate(focusTitle, GraphLabelRole.Focus, 0);

        var orderedNeighbors = neighbors
            .Where(title => _graphPoints.ContainsKey(title) && CanShowGraphLabel(title))
            .OrderByDescending(title => _activeGraphLayout.Degrees.GetValueOrDefault(title))
            .ThenBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mode is GraphLabelMode.Orbit or GraphLabelMode.Detail)
        {
            foreach (var title in orderedNeighbors.Take(8)) AddCandidate(title, GraphLabelRole.Neighbor, 2);
            if (mode == GraphLabelMode.Orbit && orderedNeighbors.Count > 8)
            {
                candidates.Add(new GraphLabelCandidate(
                    $"+{orderedNeighbors.Count - 8}",
                    new GraphPoint(focusPoint.X, focusPoint.Y + 26 * GraphViewportService.VisualScale(_graphZoom)),
                    0,
                    6 * labelScale,
                    40,
                    3,
                    GraphLabelRole.Summary));
            }
        }
        if (mode == GraphLabelMode.Detail)
        {
            foreach (var title in _graphPoints.Keys
                .Where(title => !included.Contains(title)
                    && !title.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase)
                    && CanShowGraphLabel(title))
                .OrderByDescending(title => _activeGraphLayout.Degrees.GetValueOrDefault(title))
                .ThenBy(title => title, StringComparer.OrdinalIgnoreCase))
                AddCandidate(title, GraphLabelRole.Global, 4);
        }

        var placements = _graphLabelLayoutService.Arrange(candidates, focusPoint, GraphCanvas.Width, GraphCanvas.Height);
        AddFocusOrbit(focusPoint, orderedNeighbors, mode, hovering);
        foreach (var placement in placements) AddGraphLabel(placement);

        void AddCandidate(string title, GraphLabelRole role, int priority)
        {
            if (!CanShowGraphLabel(title)
                || !included.Add(title)
                || !_graphPoints.TryGetValue(title, out var point)) return;
            var selected = title.Equals(selectedTitle, StringComparison.OrdinalIgnoreCase);
            var degree = _activeGraphLayout.Degrees.GetValueOrDefault(title);
            candidates.Add(new GraphLabelCandidate(
                title,
                point,
                GraphViewportService.NodeRadius(_graphZoom, selected, degree),
                (role is GraphLabelRole.Focus or GraphLabelRole.Selected ? 7 : 6.5) * labelScale,
                100 * labelScale,
                priority,
                role));
        }
    }

    private bool CanShowGraphLabel(string title) =>
        _selected is null || _graphRelationshipDepths.ContainsKey(title);

    private void AddFocusOrbit(GraphPoint focus, IReadOnlyList<string> neighbors, GraphLabelMode mode, bool hovering)
    {
        if (neighbors.Count < 2 || mode == GraphLabelMode.FocusOnly || mode == GraphLabelMode.Detail && !hovering) return;
        var distances = neighbors
            .Where(_graphPoints.ContainsKey)
            .Select(title =>
            {
                var point = _graphPoints[title];
                var dx = point.X - focus.X;
                var dy = point.Y - focus.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            })
            .Order()
            .ToList();
        if (distances.Count < 2) return;

        var radius = Math.Clamp(distances[distances.Count / 2], 30, Math.Min(GraphCanvas.Width, GraphCanvas.Height) * .42);
        var orbit = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = new SolidColorBrush(GraphAccent(25)),
            StrokeThickness = .7,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(orbit, focus.X - radius);
        Canvas.SetTop(orbit, focus.Y - radius);
        Canvas.SetZIndex(orbit, 2);
        GraphCanvas.Children.Add(orbit);
        _graphLabelElements.Add(orbit);
    }

    private void AddGraphLabel(GraphLabelPlacement placement)
    {
        var role = placement.Candidate.Role;
        var text = new TextBlock
        {
            Text = placement.Candidate.Title,
            Foreground = new SolidColorBrush(role switch
            {
                GraphLabelRole.Focus => GraphAccent(255, bright: true),
                GraphLabelRole.Selected => ColorHelper.FromArgb(255, 238, 242, 247),
                GraphLabelRole.Neighbor => ColorHelper.FromArgb(238, 218, 218, 218),
                GraphLabelRole.Summary => ColorHelper.FromArgb(225, 175, 175, 175),
                _ => ColorHelper.FromArgb(205, 155, 155, 155)
            }),
            FontSize = placement.Candidate.FontSize,
            FontWeight = role is GraphLabelRole.Focus or GraphLabelRole.Selected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal,
            IsHitTestVisible = false,
            Width = placement.Width,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        FrameworkElement element = text;
        if (role is GraphLabelRole.Focus or GraphLabelRole.Selected)
        {
            element = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(205, 24, 24, 24)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2, 0, 2, 1),
                Child = text,
                IsHitTestVisible = false
            };
        }
        Canvas.SetLeft(element, placement.Position.X);
        Canvas.SetTop(element, placement.Position.Y);
        Canvas.SetZIndex(element, 6);
        GraphCanvas.Children.Add(element);
        _graphLabelElements.Add(element);
    }

    private static HashSet<string> GraphNeighborsOf(string title, IReadOnlyDictionary<string, List<string>> links)
    {
        var neighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, targets) in links)
        {
            if (source.Equals(title, StringComparison.OrdinalIgnoreCase)) neighbors.UnionWith(targets);
            else if (targets.Contains(title, StringComparer.OrdinalIgnoreCase)) neighbors.Add(source);
        }
        neighbors.Remove(title);
        return neighbors;
    }

    private static Windows.UI.Color StarColor(NoteInfo note) => StablePaletteIndex(note.Title) switch
    {
        0 => ColorHelper.FromArgb(255, 220, 223, 228), // cool white
        1 => ColorHelper.FromArgb(255, 216, 225, 232), // pale blue
        2 => ColorHelper.FromArgb(255, 229, 223, 213), // soft ivory
        3 => ColorHelper.FromArgb(255, 217, 227, 221), // pale mint
        4 => ColorHelper.FromArgb(255, 228, 219, 223), // muted rose
        _ => ColorHelper.FromArgb(255, 224, 221, 231)  // pale lavender
    };

    private static Windows.UI.Color FieldStarColor(int index, byte alpha) => (index % 4) switch
    {
        0 => ColorHelper.FromArgb(alpha, 216, 222, 229),
        1 => ColorHelper.FromArgb(alpha, 228, 223, 214),
        2 => ColorHelper.FromArgb(alpha, 216, 226, 221),
        _ => ColorHelper.FromArgb(alpha, 224, 220, 229)
    };

    private static int StablePaletteIndex(string value)
    {
        var hash = 17;
        foreach (var character in value)
            hash = unchecked(hash * 31 + char.ToUpperInvariant(character));
        return (hash & int.MaxValue) % 6;
    }

    private Windows.UI.Color GraphAccent(byte alpha, bool bright = false)
    {
        var color = bright ? CurrentAccent.Bright : CurrentAccent.Accent;
        return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static IReadOnlyDictionary<string, List<string>> MergeGraphLinks(
        IReadOnlyDictionary<string, List<string>> explicitLinks,
        IReadOnlyDictionary<string, List<string>> semanticLinks,
        IEnumerable<string> noteTitles)
    {
        var titles = noteTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = titles.ToDictionary(title => title, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var links in new[] { explicitLinks, semanticLinks })
            foreach (var (source, targets) in links)
                if (merged.TryGetValue(source, out var sourceLinks))
                    foreach (var target in targets)
                        if (titles.Contains(target) && !source.Equals(target, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!merged[target].Contains(source)) sourceLinks.Add(target);
                        }
        return merged.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasGraphEdge(IReadOnlyDictionary<string, List<string>> links, string source, string target) =>
        links.TryGetValue(source, out var sourceTargets) && sourceTargets.Contains(target, StringComparer.OrdinalIgnoreCase)
        || links.TryGetValue(target, out var targetSources) && targetSources.Contains(source, StringComparer.OrdinalIgnoreCase);
}
