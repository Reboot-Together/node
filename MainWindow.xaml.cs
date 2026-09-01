using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI.Core;

namespace NodeApp;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceService _workspace = new();
    private readonly NoteRepository _repository;
    private readonly NoteLinkService _linkService = new();
    private readonly NoteImageService _imageService = new();
    private readonly VaultTreeService _vaultTreeService = new();
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _previewTimer;
    private List<NoteInfo> _notes = [];
    private IReadOnlyList<string> _folders = [];
    private IReadOnlyList<VaultItem> _vaultItems = [];
    private IReadOnlyDictionary<string, List<string>> _noteLinks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, bool>> _noteFoldStates = new(StringComparer.OrdinalIgnoreCase);
    private NoteInfo? _selected;
    private bool _loading;
    private bool _previewReady;

    public MainWindow()
    {
        _repository = new NoteRepository(_workspace.RootPath);
        InitializeComponent();
        CurrentVersionText.Text = $"v{UpdateService.CurrentVersionText}";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Node.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        AppWindow.Resize(new SizeInt32(1440, 920));
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(700);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveCurrent();
        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(350);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += (_, _) => UpdateMarkdownPreview();
        MarkdownPreview.Loaded += MarkdownPreview_Loaded;
        RefreshNotes();
        if (_notes.Count == 0) NewNote(); else Select(_notes[0]);
        Closed += (_, _) => SaveCurrent();
    }

    private void RefreshNotes()
    {
        _notes = _repository.Load();
        _folders = _vaultTreeService.LoadFolders(_workspace.RootPath);
        RefreshLinkIndex();
        StoragePathText.Text = $"저장 위치: {_workspace.RootPath}";
        ApplySearch();
    }

    private void RefreshLinkIndex() => _noteLinks = _linkService.Build(_notes);

    private void RefreshFolders()
    {
        _folders = _vaultTreeService.LoadFolders(_workspace.RootPath);
        ApplySearch();
    }

    private void ApplySearch()
    {
        var query = SearchBox?.Text.Trim() ?? "";
        _vaultItems = _vaultTreeService.Build(_workspace.RootPath, _notes, _folders, _expandedFolders, query);
        NoteList.ItemsSource = _vaultItems;
        if (_selected is not null)
            NoteList.SelectedItem = _vaultItems.FirstOrDefault(item => item.Note?.Path == _selected.Path);
    }

    private void NewNote(string? parentFolder = null)
    {
        SaveCurrent();
        var note = parentFolder is null
            ? _repository.Create()
            : _repository.CreateInFolder(parentFolder);
        if (parentFolder is not null) _expandedFolders.Add(parentFolder);
        RefreshNotes();
        Select(note, focusEditor: true);
    }

    private void Select(NoteInfo note, bool focusEditor = false)
    {
        _loading = true;
        _selected = note;
        TitleBox.Text = note.Title;
        CategoryBox.Text = note.Metadata.Category;
        SourceBox.Text = note.Metadata.Source;
        TypeBox.Text = note.Metadata.Type;
        Editor.Text = note.Body;
        RevealNoteInTree(note);
        _loading = false;
        ShowEditorAndPreview();
        UpdateBacklinks();
        DrawGraph();
        if (focusEditor) DispatcherQueue.TryEnqueue(() => Editor.Focus(FocusState.Programmatic));
    }

    private void RevealNoteInTree(NoteInfo note)
    {
        var treeChanged = false;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            SearchBox.Text = "";
            treeChanged = true;
        }
        foreach (var folder in _vaultTreeService.AncestorFolders(_workspace.RootPath, note.Path))
            treeChanged |= _expandedFolders.Add(folder);

        var item = treeChanged
            ? null
            : _vaultItems.FirstOrDefault(candidate => candidate.Note?.Path == note.Path);
        if (item is null)
        {
            ApplySearch();
            item = _vaultItems.FirstOrDefault(candidate => candidate.Note?.Path == note.Path);
        }
        if (item is null) return;

        NoteList.SelectedItem = item;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_selected?.Path == note.Path) NoteList.ScrollIntoView(item);
        });
    }

    private NoteMetadata MetadataFromEditor() => new(
        string.IsNullOrWhiteSpace(CategoryBox.Text) ? "Inbox" : CategoryBox.Text.Trim(),
        _selected?.Metadata.Created ?? DateTime.Today,
        string.IsNullOrWhiteSpace(SourceBox.Text) ? "Manual" : SourceBox.Text.Trim(),
        string.IsNullOrWhiteSpace(TypeBox.Text) ? "Note" : TypeBox.Text.Trim());

    private void SaveCurrent()
    {
        if (_loading || _selected is null) return;
        _saveTimer.Stop();
        var previous = _selected;
        var metadata = MetadataFromEditor();
        var title = MarkdownText.NormalizeTitle(TitleBox.Text);
        var body = MarkdownText.NormalizeNewlines(Editor.Text).Trim();
        if (TitleBox.Text != title)
        {
            _loading = true;
            TitleBox.Text = title;
            _loading = false;
        }
        if (previous.Title == title && previous.Body == body && previous.Metadata == metadata) return;

        var linksChanged = !_linkService.ExtractTargets(previous.Body).SetEquals(_linkService.ExtractTargets(body));
        _selected = _repository.Save(previous.Path, title, body, metadata, previous.Title);
        var noteIndex = _notes.FindIndex(note => note.Path.Equals(previous.Path, StringComparison.OrdinalIgnoreCase));
        if (noteIndex >= 0) _notes[noteIndex] = _selected;
        else _notes.Add(_selected);

        if (TitleBox.Text != _selected.Title)
        {
            _loading = true;
            TitleBox.Text = _selected.Title;
            _loading = false;
        }

        var titleChanged = !previous.Title.Equals(_selected.Title, StringComparison.Ordinal);
        var metadataChanged = previous.Metadata != _selected.Metadata;
        if (titleChanged || metadataChanged) ApplySearch();
        if (titleChanged || linksChanged)
        {
            RefreshLinkIndex();
            UpdateBacklinks();
        }
        if (titleChanged || linksChanged || metadataChanged) DrawGraph();
    }

    private void OpenDailyNote()
    {
        SaveCurrent();
        var today = DateTime.Today;
        var note = _notes.FirstOrDefault(item => item.Metadata.Type.Equals("Daily", StringComparison.OrdinalIgnoreCase) && item.Metadata.Created.Date == today);
        if (note is null)
        {
            var metadata = new NoteMetadata("Journal", today, "Daily", "Daily");
            note = _repository.Create(today.ToString("yyyy-MM-dd"), metadata);
            note = _repository.Save(note.Path, note.Title, "## 오늘 공부\n\n- \n\n## 메모\n\n", metadata);
            RefreshNotes();
        }
        Select(note);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (_selected is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private void NoteField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && _selected is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private async void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var controlDown = IsKeyDown(Windows.System.VirtualKey.Control)
            || IsKeyDown(Windows.System.VirtualKey.LeftControl)
            || IsKeyDown(Windows.System.VirtualKey.RightControl);

        if (e.Key == Windows.System.VirtualKey.V && controlDown)
        {
            DataPackageView? clipboard = null;
            try { clipboard = Clipboard.GetContent(); }
            catch { }
            if (clipboard?.Contains(StandardDataFormats.Bitmap) == true)
            {
                e.Handled = true;
                try
                {
                    var bitmap = await clipboard.GetBitmapAsync();
                    var relativePath = await _imageService.SavePngAsync(_workspace.RootPath, bitmap);
                    InsertAtEditorSelection($"![[{relativePath}]]");
                    Editor.Focus(FocusState.Programmatic);
                }
                catch (Exception exception)
                {
                    await ShowMessage("이미지를 붙여넣을 수 없음", $"클립보드 이미지를 저장하지 못했습니다.\n\n{exception.Message}");
                }
                return;
            }
        }

        if (e.Key != Windows.System.VirtualKey.Enter || !controlDown) return;

        e.Handled = true;
        SaveEditor();
    }

    private void InsertAtEditorSelection(string markdown)
    {
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        Editor.Text = Editor.Text.Remove(start, length).Insert(start, markdown);
        Editor.SelectionStart = start + markdown.Length;
        Editor.SelectionLength = 0;
    }

    private void Editor_Save_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveEditor();
    }

    private void Editor_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
            if (IsInside(focused, DocumentWorkspace) || MarkdownPreview.FocusState != FocusState.Unfocused) return;
            SaveEditor();
        });
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, container)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key) => InputKeyboardSource
        .GetKeyStateForCurrentThread(key)
        .HasFlag(CoreVirtualKeyStates.Down);

    private void SaveEditor()
    {
        _previewTimer.Stop();
        _saveTimer.Stop();
        SaveCurrent();
        UpdateMarkdownPreview();
    }

    private void ShowEditorAndPreview()
    {
        Editor.Visibility = Visibility.Visible;
        EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        EditorPreviewDividerColumn.Width = new GridLength(1);
        EditorPreviewDivider.Visibility = Visibility.Visible;
        MarkdownPreview.Visibility = Visibility.Visible;
        UpdateMarkdownPreview();
    }

    private void InspectorCollapse_Click(object sender, RoutedEventArgs e)
    {
        InspectorPanel.Visibility = Visibility.Collapsed;
        InspectorColumn.Width = new GridLength(0);
        InspectorOpenButton.Visibility = Visibility.Visible;
    }

    private void InspectorOpen_Click(object sender, RoutedEventArgs e)
    {
        InspectorColumn.Width = new GridLength(348);
        InspectorPanel.Visibility = Visibility.Visible;
        InspectorOpenButton.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(DrawGraph);
    }

    private async void MarkdownPreview_Loaded(object sender, RoutedEventArgs e)
    {
        if (_previewReady) return;
        try
        {
            await MarkdownPreview.EnsureCoreWebView2Async();
            MarkdownPreview.CoreWebView2.WebMessageReceived += MarkdownPreview_WebMessageReceived;
            _previewReady = true;
            UpdateMarkdownPreview();
        }
        catch (Exception exception)
        {
            await ShowMessage("미리보기를 열 수 없음", $"WebView2 마크다운 미리보기를 초기화하지 못했습니다.\n\n{exception.Message}");
        }
    }

    private void UpdateMarkdownPreview()
    {
        if (!_previewReady) return;
        MarkdownPreview.NavigateToString(MarkdownPreviewRenderer.Render(Editor.Text, _workspace.RootPath, ResolveNoteBody, CurrentFoldStates()));
    }

    private Dictionary<string, bool> CurrentFoldStates()
    {
        if (_selected is null) return [];
        if (!_noteFoldStates.TryGetValue(_selected.Path, out var states))
        {
            states = new Dictionary<string, bool>(StringComparer.Ordinal);
            _noteFoldStates[_selected.Path] = states;
        }
        return states;
    }

    private void MarkdownPreview_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            if (type.GetString() == "fold-state"
                && root.TryGetProperty("key", out var keyElement)
                && root.TryGetProperty("open", out var openElement)
                && keyElement.GetString() is { Length: > 0 } key
                && openElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                CurrentFoldStates()[key] = openElement.GetBoolean();
            }
            else if (type.GetString() == "focus-editor")
            {
                DispatcherQueue.TryEnqueue(() => Editor.Focus(FocusState.Programmatic));
            }
        }
        catch { }
    }

    private string? ResolveNoteBody(string title) => _notes.FirstOrDefault(note => note.Title.Equals(title, StringComparison.OrdinalIgnoreCase))?.Body;

    private void MarkdownPreview_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme.Equals("node-note", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            var title = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
            var note = _notes.FirstOrDefault(item => item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (note is not null) Select(note);
        }
        else if (uri.Scheme is "http" or "https")
        {
            args.Cancel = true;
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
    }
    private void Search_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) ApplySearch(); }
    private void NewNote_Click(object sender, RoutedEventArgs e) => NewNote();
    private void DailyNote_Click(object sender, RoutedEventArgs e) => OpenDailyNote();
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = _selected?.Path;
        SaveCurrent();
        RefreshNotes();
        var selected = _notes.FirstOrDefault(note => note.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) Select(selected);
        else if (_notes.Count > 0) Select(_notes[0]);
    }
    private void NoteList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_loading && NoteList.SelectedItem is VaultItem { Note: NoteInfo note } && note.Path != _selected?.Path) { SaveCurrent(); Select(note); } }
    private void BacklinkList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (BacklinkList.SelectedItem is NoteInfo note && note.Path != _selected?.Path) { SaveCurrent(); Select(note); } }
    private void UpdateBacklinks()
    {
        if (_selected is null) { BacklinkTitle.Text = "이 노트를 언급한 노트"; BacklinkList.ItemsSource = Array.Empty<NoteInfo>(); return; }
        var backlinks = _notes.Where(note => _noteLinks.TryGetValue(note.Title, out var targets) && targets.Contains(_selected.Title, StringComparer.OrdinalIgnoreCase)).ToList();
        BacklinkTitle.Text = $"이 노트를 언급한 노트 ({backlinks.Count})";
        BacklinkList.ItemsSource = backlinks;
    }

    private async Task ShowMessage(string title, string content) => await new ContentDialog { Title = title, Content = content, CloseButtonText = "확인", XamlRoot = Root.XamlRoot }.ShowAsync();
}
