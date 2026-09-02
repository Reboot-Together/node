using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private const int MaximumDocumentGroups = 3;
    private readonly List<SideDocumentPane> _sideDocumentPanes = [];

    private async void OpenCurrentToSide_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await OpenNoteInSideAsync(_selected);
    }

    private async void OpenNoteToSide_Click(object sender, RoutedEventArgs e)
    {
        if (_contextNote is not null) await OpenNoteInSideAsync(_contextNote);
    }

    private async Task OpenNoteInSideAsync(NoteInfo note)
    {
        var existing = _sideDocumentPanes.FirstOrDefault(pane =>
            pane.NotePath.Equals(note.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.FocusEditor();
            return;
        }

        if (_sideDocumentPanes.Count >= MaximumDocumentGroups - 1)
        {
            await ShowMessage("문서를 더 열 수 없음", "한 화면에는 문서를 최대 3개까지 나란히 열 수 있습니다.");
            return;
        }

        SaveCurrent();
        var pane = new SideDocumentPane(
            note,
            _workspace.RootPath,
            FindNoteByTitle,
            SaveSideDocument,
            _uiLayoutSettings.FontScale,
            CurrentAccent.CssColor,
            CurrentSurface.Key);
        pane.CloseRequested += SideDocument_CloseRequested;
        pane.WorkspaceModeToggleRequested += SideDocument_WorkspaceModeToggleRequested;
        _sideDocumentPanes.Add(pane);
        DocumentGroupsHost.Children.Add(pane);
        RebuildDocumentGroupColumns();
        DispatcherQueue.TryEnqueue(pane.FocusEditor);
    }

    private void SideDocument_CloseRequested(object? sender, EventArgs e)
    {
        if (sender is not SideDocumentPane pane) return;
        pane.CloseRequested -= SideDocument_CloseRequested;
        pane.WorkspaceModeToggleRequested -= SideDocument_WorkspaceModeToggleRequested;
        _sideDocumentPanes.Remove(pane);
        DocumentGroupsHost.Children.Remove(pane);
        RebuildDocumentGroupColumns();
    }

    private void SideDocument_WorkspaceModeToggleRequested(object? sender, EventArgs e)
    {
        if (sender is SideDocumentPane pane)
        {
            pane.SaveNow();
            if (_selected?.Path.Equals(pane.NotePath, StringComparison.OrdinalIgnoreCase) != true)
                Select(pane.CurrentNote);
        }
        ShowConstellationMode();
    }

    private void RebuildDocumentGroupColumns()
    {
        DocumentGroupsHost.ColumnDefinitions.Clear();
        for (var index = 0; index <= _sideDocumentPanes.Count; index++)
            DocumentGroupsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(DocumentPanel, 0);
        for (var index = 0; index < _sideDocumentPanes.Count; index++)
            Grid.SetColumn(_sideDocumentPanes[index], index + 1);
    }

    private NoteInfo SaveSideDocument(NoteInfo previous, string title, string body)
    {
        if (previous.IsReadOnly) return previous;
        var linksChanged = !_linkService.ExtractTargets(previous.Body).SetEquals(_linkService.ExtractTargets(body));
        var saved = _repository.Save(previous.Path, title, body, previous.Metadata, previous.Title);
        var noteIndex = _notes.FindIndex(note =>
            note.Path.Equals(previous.Path, StringComparison.OrdinalIgnoreCase));
        if (noteIndex >= 0) _notes[noteIndex] = saved;
        else _notes.Add(saved);

        if (_selected?.Path.Equals(previous.Path, StringComparison.OrdinalIgnoreCase) == true
            && TitleBox.Text == previous.Title
            && MarkdownText.NormalizeNewlines(Editor.Text).Trim() == previous.Body)
        {
            _loading = true;
            _selected = saved;
            TitleBox.Text = saved.Title;
            Editor.Text = saved.Body;
            _loading = false;
            UpdateMarkdownPreview();
        }

        var titleChanged = !previous.Title.Equals(saved.Title, StringComparison.Ordinal);
        var bodyChanged = previous.Body != saved.Body;
        if (titleChanged) ApplySearch();
        if (titleChanged || linksChanged)
        {
            RefreshLinkIndex();
            UpdateBacklinks();
            DrawGraph();
        }
        if (titleChanged || bodyChanged) QueueSemanticRefresh();
        return saved;
    }

    private void SaveSideDocuments()
    {
        foreach (var pane in _sideDocumentPanes.ToArray()) pane.SaveNow();
    }

    private void RefreshSideDocumentAppearance()
    {
        foreach (var pane in _sideDocumentPanes)
            pane.RefreshAppearance(_uiLayoutSettings.FontScale, CurrentAccent.CssColor, CurrentSurface.Key);
    }
}
