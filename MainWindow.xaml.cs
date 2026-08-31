using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace NodeApp;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceService _workspace = new();
    private readonly NoteRepository _repository;
    private readonly NoteLinkService _linkService = new();
    private readonly VaultTreeService _vaultTreeService = new();
    private readonly DispatcherQueueTimer _saveTimer;
    private List<NoteInfo> _notes = [];
    private IReadOnlyList<VaultItem> _vaultItems = [];
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private NoteInfo? _selected;
    private bool _loading;
    private bool _previewing = true;
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
        MarkdownPreview.Loaded += MarkdownPreview_Loaded;
        RefreshNotes();
        if (_notes.Count == 0) NewNote(); else Select(_notes[0]);
        Closed += (_, _) => SaveCurrent();
    }

    private void RefreshNotes()
    {
        _notes = _repository.Load();
        StoragePathText.Text = $"저장 위치: {_workspace.RootPath}";
        ApplySearch();
        DrawGraph();
        UpdateBacklinks();
    }

    private void ApplySearch()
    {
        var query = SearchBox?.Text.Trim() ?? "";
        _vaultItems = _vaultTreeService.Build(_workspace.RootPath, _notes, _expandedFolders, query);
        NoteList.ItemsSource = _vaultItems;
        if (_selected is not null)
            NoteList.SelectedItem = _vaultItems.FirstOrDefault(item => item.Note?.Path == _selected.Path);
    }

    private void NewNote()
    {
        SaveCurrent();
        var note = _repository.Create();
        RefreshNotes();
        Select(note, openEditor: true);
        DispatcherQueue.TryEnqueue(() => Editor.Focus(FocusState.Programmatic));
    }

    private void Select(NoteInfo note, bool openEditor = false)
    {
        _loading = true;
        _selected = note;
        TitleBox.Text = note.Title;
        CategoryBox.Text = note.Metadata.Category;
        SourceBox.Text = note.Metadata.Source;
        TypeBox.Text = note.Metadata.Type;
        Editor.Text = note.Body;
        NoteList.SelectedItem = _vaultItems.FirstOrDefault(item => item.Note?.Path == note.Path);
        _loading = false;
        SetPreviewMode(!openEditor);
        UpdateBacklinks();
        DrawGraph();
    }

    private NoteMetadata MetadataFromEditor() => new(
        string.IsNullOrWhiteSpace(CategoryBox.Text) ? "Inbox" : CategoryBox.Text.Trim(),
        _selected?.Metadata.Created ?? DateTime.Today,
        string.IsNullOrWhiteSpace(SourceBox.Text) ? "Manual" : SourceBox.Text.Trim(),
        string.IsNullOrWhiteSpace(TypeBox.Text) ? "Note" : TypeBox.Text.Trim());

    private void SaveCurrent()
    {
        if (_loading || _selected is null) return;
        var metadata = MetadataFromEditor();
        _selected = _repository.Save(_selected.Path, TitleBox.Text, Editor.Text, metadata);
        if (TitleBox.Text != _selected.Title)
        {
            _loading = true;
            TitleBox.Text = _selected.Title;
            _loading = false;
        }
        RefreshNotes();
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
        if (_previewing) UpdateMarkdownPreview();
        if (_selected is not null) _saveTimer.Start();
    }

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewMode(!_previewing);
    }

    private void SetPreviewMode(bool preview)
    {
        _previewing = preview;
        Editor.Visibility = _previewing ? Visibility.Collapsed : Visibility.Visible;
        MarkdownPreview.Visibility = _previewing ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.Content = _previewing ? "편집" : "미리보기";
        if (_previewing) UpdateMarkdownPreview();
    }

    private async void MarkdownPreview_Loaded(object sender, RoutedEventArgs e)
    {
        if (_previewReady) return;
        try
        {
            await MarkdownPreview.EnsureCoreWebView2Async();
            MarkdownPreview.CoreWebView2.WebMessageReceived += MarkdownPreview_WebMessageReceived;
            _previewReady = true;
            if (_previewing) UpdateMarkdownPreview();
        }
        catch (Exception exception)
        {
            SetPreviewMode(false);
            await ShowMessage("미리보기를 열 수 없음", $"WebView2 마크다운 미리보기를 초기화하지 못했습니다.\n\n{exception.Message}");
        }
    }

    private void UpdateMarkdownPreview()
    {
        if (!_previewReady) return;
        MarkdownPreview.NavigateToString(MarkdownPreviewRenderer.Render(Editor.Text, _workspace.RootPath, ResolveNoteBody));
    }

    private void MarkdownPreview_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "update-section") return;
            if (!root.TryGetProperty("index", out var indexValue) || !root.TryGetProperty("markdown", out var markdownValue)) return;
            var updated = MarkdownSectionService.ReplaceBody(Editor.Text, indexValue.GetInt32(), markdownValue.GetString() ?? "");
            if (updated == Editor.Text) { UpdateMarkdownPreview(); return; }
            Editor.Text = updated;
            SaveCurrent();
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
    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();
    private void NewNote_Click(object sender, RoutedEventArgs e) => NewNote();
    private void DailyNote_Click(object sender, RoutedEventArgs e) => OpenDailyNote();
    private void Refresh_Click(object sender, RoutedEventArgs e) { SaveCurrent(); RefreshNotes(); }
    private void NoteList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_loading && NoteList.SelectedItem is VaultItem { Note: NoteInfo note } && note.Path != _selected?.Path) { SaveCurrent(); Select(note); } }
    private void BacklinkList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (BacklinkList.SelectedItem is NoteInfo note && note.Path != _selected?.Path) { SaveCurrent(); Select(note); } }
    private void UpdateBacklinks()
    {
        if (_selected is null) { BacklinkTitle.Text = "이 노트를 언급한 노트"; BacklinkList.ItemsSource = Array.Empty<NoteInfo>(); return; }
        var links = _linkService.Build(_notes);
        var backlinks = _notes.Where(note => links.TryGetValue(note.Title, out var targets) && targets.Contains(_selected.Title, StringComparer.OrdinalIgnoreCase)).ToList();
        BacklinkTitle.Text = $"이 노트를 언급한 노트 ({backlinks.Count})";
        BacklinkList.ItemsSource = backlinks;
    }

    private async Task ShowMessage(string title, string content) => await new ContentDialog { Title = title, Content = content, CloseButtonText = "확인", XamlRoot = Root.XamlRoot }.ShowAsync();
}
