using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private string? _contextFolder;
    private VaultItem? _draggedItem;

    private void VaultItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path) return;
        var item = FindVaultItem(path);
        if (item is not { IsFolder: true, IsRoot: false }) return;

        if (item.IsVirtual)
        {
            if (!_expandedFolders.Add(item.Path)) _expandedFolders.Remove(item.Path);
        }
        else
        {
            _folderExpansionService.ToggleExclusive(
                _workspace.RootPath,
                _folders,
                _expandedFolders,
                item.Path);
        }
        ApplySearch();
        e.Handled = true;
    }

    private void VaultItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string path) return;
        var item = FindVaultItem(path);
        if (item is null) return;
        if (item.IsVirtual)
        {
            e.Handled = true;
            return;
        }

        _contextNote = item.Note;
        _contextFolder = item.IsFolder ? item.Path : null;
        NoteList.SelectedItem = item;

        var menu = new MenuFlyout();
        if (item.IsFolder)
        {
            menu.Items.Add(MenuItem("새 노트", ContextCreateNote_Click));
            menu.Items.Add(MenuItem("새 폴더", CreateFolder_Click));
            if (!item.IsRoot)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(MenuItem("이름 변경", RenameFolder_Click));
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("탐색기에서 보기", ShowInExplorer_Click));
            if (!item.IsRoot)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(MenuItem("삭제", ContextDeleteFolder_Click));
            }
        }
        else
        {
            menu.Items.Add(MenuItem("옆에 열기", OpenNoteToSide_Click));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("이름 변경", RenameNote_Click));
            menu.Items.Add(MenuItem("복사본 만들기", DuplicateNote_Click));
            menu.Items.Add(MenuItem("폴더로 이동", MoveNote_Click));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("탐색기에서 보기", ShowInExplorer_Click));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("삭제", ContextDeleteNote_Click));
        }

        menu.ShowAt(element);
        e.Handled = true;
    }

    private void NoteList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        _contextNote = null;
        _contextFolder = _workspace.RootPath;

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("새 노트", ContextCreateNote_Click));
        menu.Items.Add(MenuItem("새 폴더", CreateFolder_Click));
        menu.ShowAt(NoteList, e.GetPosition(NoteList));
        e.Handled = true;
    }

    private void NoteList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedItem = e.Items.OfType<VaultItem>().FirstOrDefault();
        if (_draggedItem is null || _draggedItem.IsRoot || _draggedItem.IsVirtual)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetText(_draggedItem.Path);
    }

    private void VaultItem_DragOver(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path) return;
        var target = FindVaultItem(path);
        if (!CanDrop(_draggedItem, target)) return;

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = $"{target!.Name}(으)로 이동";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void VaultItem_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path) return;
        var target = FindVaultItem(path);
        var source = _draggedItem;
        _draggedItem = null;
        if (!CanDrop(source, target)) return;

        try
        {
            var selectedTitle = _selected?.Title;
            if (source!.IsFolder)
            {
                SaveCurrent();
                var destination = _repository.MoveFolder(source.Path, target!.Path);
                ReplaceExpandedFolderPath(source.Path, destination);
                ExpandFolder(target.Path);
                RefreshNotes();
                SelectByTitle(selectedTitle);
            }
            else if (source.Note is NoteInfo note)
            {
                if (_selected?.Path == note.Path) SaveCurrent();
                var moved = _repository.Move(note.Path, target!.Path);
                ExpandFolder(target.Path);
                RefreshNotes();
                if (_selected?.Path == note.Path || selectedTitle == note.Title) Select(moved);
            }
        }
        catch (Exception exception)
        {
            await ShowMessage("이동 실패", exception.Message);
        }
        finally
        {
            e.Handled = true;
        }
    }

    private static bool CanDrop(VaultItem? source, VaultItem? target)
    {
        if (source is null || source.IsRoot || source.IsVirtual || target is not { IsFolder: true } || target.IsVirtual) return false;
        var sourceParent = Path.GetDirectoryName(source.Path);
        if (sourceParent?.Equals(target.Path, StringComparison.OrdinalIgnoreCase) == true) return false;
        if (!source.IsFolder) return true;

        var sourcePrefix = source.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return !target.Path.Equals(source.Path, StringComparison.OrdinalIgnoreCase)
            && !target.Path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private VaultItem? FindVaultItem(string path) => _vaultItems.FirstOrDefault(item =>
        item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    private void ExpandFolder(string folder)
    {
        if (folder.Equals(BuiltInGuideService.FolderPath, StringComparison.OrdinalIgnoreCase))
            _expandedFolders.Add(folder);
        else
            _folderExpansionService.ExpandExclusive(_workspace.RootPath, _folders, _expandedFolders, folder);
    }

    private static MenuFlyoutItem MenuItem(string text, RoutedEventHandler handler)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += handler;
        return item;
    }

    private void ReplaceExpandedFolderPath(string source, string destination)
    {
        var sourcePrefix = source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var affected = _expandedFolders
            .Where(path => path.Equals(source, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var oldPath in affected)
        {
            _expandedFolders.Remove(oldPath);
            _expandedFolders.Add(destination + oldPath[source.Length..]);
        }
    }

    private void RemoveExpandedFolderBranch(string folder)
    {
        var prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _expandedFolders.RemoveWhere(path =>
            path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectByTitle(string? title)
    {
        if (title is null) return;
        var note = _notes.FirstOrDefault(item => item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (note is not null) Select(note);
    }
}
