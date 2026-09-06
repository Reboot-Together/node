using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private bool _constellationMode;
    private NoteInfo? _constellationNote;
    private SideDocumentPane? _constellationTargetPane;
    private NoteInfo? GraphSelectedNote => _constellationMode ? _constellationNote : _selected;

    private void DocumentMode_Click(object sender, RoutedEventArgs e) => ShowDocumentMode();

    private void ConstellationMode_Click(object sender, RoutedEventArgs e)
    {
        if (PrepareActiveDocumentForConstellation() is { } note) ShowConstellationMode(note);
    }

    private void WorkspaceGraph_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_constellationMode) ShowDocumentMode();
        else if (PrepareActiveDocumentForConstellation() is { } note) ShowConstellationMode(note);
    }

    private void WorkspaceEscape_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_constellationMode) return;
        args.Handled = true;
        ShowDocumentMode();
    }

    private void ShowDocumentMode()
    {
        if (!_constellationMode) return;

        CaptureCurrentGraphViewport();
        var selected = _constellationNote;
        var targetPane = _constellationTargetPane;
        _constellationMode = false;
        _constellationNote = null;
        _constellationTargetPane = null;
        ResetGraphDirectionalCursor();
        ConstellationPanel.Visibility = Visibility.Collapsed;
        DocumentGroupsHost.Visibility = Visibility.Visible;
        DocumentModeIndicator.Visibility = Visibility.Visible;
        ConstellationModeIndicator.Visibility = Visibility.Collapsed;

        if (selected is not null)
        {
            selected = _notes.FirstOrDefault(note =>
                note.Path.Equals(selected.Path, StringComparison.OrdinalIgnoreCase)) ?? selected;
            if (targetPane is not null && _sideDocumentPanes.Contains(targetPane))
            {
                _activeSideDocumentPane = targetPane;
                if (!targetPane.NotePath.Equals(selected.Path, StringComparison.OrdinalIgnoreCase)
                    && targetPane.LoadNote(selected))
                    RevealNoteInTree(selected);
            }
            else if (_selected?.Path.Equals(selected.Path, StringComparison.OrdinalIgnoreCase) != true)
            {
                _activeSideDocumentPane = null;
                Select(selected);
            }
        }

        DispatcherQueue.TryEnqueue(FocusActiveDocument);
    }

    private void ShowConstellationMode(NoteInfo selected)
    {
        if (_constellationMode) return;

        _constellationTargetPane = _activeSideDocumentPane is { } pane && _sideDocumentPanes.Contains(pane)
            ? pane
            : null;
        _constellationNote = selected;
        _constellationMode = true;
        ConstellationTitleText.Text = selected.Title;
        UpdateBacklinks(selected);
        DocumentGroupsHost.Visibility = Visibility.Collapsed;
        ConstellationPanel.Visibility = Visibility.Visible;
        DocumentModeIndicator.Visibility = Visibility.Collapsed;
        ConstellationModeIndicator.Visibility = Visibility.Visible;

        DispatcherQueue.TryEnqueue(() =>
        {
            var restoreGraphViewport = PrepareGraphViewport(selected);
            DrawGraph(centerCurrentNode: !restoreGraphViewport);
            if (restoreGraphViewport) RestoreGraphViewport(selected);
            GraphScroll.Focus(FocusState.Programmatic);
        });
    }

    private void SelectGraphNote(NoteInfo note)
    {
        if (!_constellationMode)
        {
            SelectNoteInActiveDocument(note);
            return;
        }

        CaptureCurrentGraphViewport();
        note = _notes.FirstOrDefault(candidate =>
            candidate.Path.Equals(note.Path, StringComparison.OrdinalIgnoreCase)) ?? note;
        _constellationNote = note;
        ConstellationTitleText.Text = note.Title;
        UpdateBacklinks(note);
        var restoreGraphViewport = PrepareGraphViewport(note);
        DrawGraph(centerCurrentNode: !restoreGraphViewport);
        if (restoreGraphViewport) RestoreGraphViewport(note);
    }

    private void HandleWorkspaceModeMessage(string? messageType)
    {
        if (messageType == "workspace-mode-toggle")
        {
            if (_constellationMode) ShowDocumentMode();
            else if (PrepareActiveDocumentForConstellation() is { } note) ShowConstellationMode(note);
        }
        else if (messageType == "workspace-mode-document" && _constellationMode)
        {
            ShowDocumentMode();
        }
    }
}
