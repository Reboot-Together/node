using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI.Core;

namespace AsterismApp;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceService _workspace = new();
    private readonly UiLayoutSettingsService _uiLayoutSettingsService = new();
    private readonly NoteRepository _repository;
    private readonly NoteLinkService _linkService = new();
    private readonly NoteImageService _imageService = new();
    private readonly VaultTreeService _vaultTreeService = new();
    private readonly FolderExpansionService _folderExpansionService = new();
    private readonly BuiltInGuideService _guideService = new();
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _previewTimer;
    private List<NoteInfo> _notes = [];
    private IReadOnlyList<string> _folders = [];
    private IReadOnlyList<VaultItem> _vaultItems = [];
    private IReadOnlyDictionary<string, List<string>> _noteLinks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private bool _folderExpansionInitialized;
    private readonly Dictionary<string, Dictionary<string, bool>> _noteFoldStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _notePreviewScrollPositions = new(StringComparer.OrdinalIgnoreCase);
    private NoteInfo? _selected;
    private bool _loading;
    private bool _previewReady;
    private UiLayoutSettings _uiLayoutSettings = UiLayoutSettings.Default;
    private ScrollViewer? _editorScrollViewer;
    private bool _previewHoverSelectionActive;
    private int _previewHoverOriginalStart;
    private int _previewHoverOriginalLength;
    private int _previewHoverSelectionRevision;

    public MainWindow()
    {
        _repository = new NoteRepository(_workspace.RootPath);
        InitializeComponent();
        GraphCanvas.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(GraphCanvas_PointerWheelChanged),
            handledEventsToo: true);
        ConfigureTitleBar();
        _uiLayoutSettings = _uiLayoutSettingsService.Load();
        ApplyAppearanceSettings(refreshContent: false);
        ApplyDocumentSplit(_uiLayoutSettings.PreviewRatio);
        ApplyExplorerState(_uiLayoutSettings.ExplorerCollapsed);
        ApplyInspectorWidth(_uiLayoutSettings.InspectorWidth);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Asterism.ico");
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
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                ResetGraphDirectionalCursor();
        };
        Closed += (_, _) =>
        {
            ResetGraphDirectionalCursor();
            SaveSideDocuments();
            SaveCurrent();
            _uiLayoutSettingsService.Save(_uiLayoutSettings);
            StopSemanticIndexing();
        };
    }

    private void RefreshNotes()
    {
        _notes = _repository.Load();
        _folders = _vaultTreeService.LoadFolders(_workspace.RootPath);
        if (!_folderExpansionInitialized)
        {
            _folderExpansionService.InitializeDefaults(_workspace.RootPath, _folders, _expandedFolders);
            _expandedFolders.Add(BuiltInGuideService.FolderPath);
            _folderExpansionInitialized = true;
        }
        RefreshLinkIndex();
        ApplySearch();
        QueueSemanticRefresh();
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
        var items = _vaultTreeService.Build(_workspace.RootPath, _notes, _folders, _expandedFolders, query).ToList();
        items.InsertRange(Math.Min(1, items.Count), _guideService.BuildItems(_expandedFolders, query));
        _vaultItems = items;
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
        if (parentFolder is not null) ExpandFolder(parentFolder);
        RefreshNotes();
        Select(note, focusEditor: true);
    }

    private void Select(NoteInfo note, bool focusEditor = false)
    {
        _previewHoverSelectionActive = false;
        _previewHoverSelectionRevision++;
        _loading = true;
        _selected = note;
        TitleBox.Text = note.Title;
        Editor.Text = note.Body;
        RevealNoteInTree(note);
        _loading = false;
        ShowEditorAndPreview();
        UpdateBacklinks();
        UpdateSemanticSuggestions();
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
        if (note.IsReadOnly)
            treeChanged |= _expandedFolders.Add(BuiltInGuideService.FolderPath);
        else
            foreach (var folder in _vaultTreeService.AncestorFolders(_workspace.RootPath, note.Path).Reverse())
            {
                var before = _expandedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
                _folderExpansionService.ExpandExclusive(_workspace.RootPath, _folders, _expandedFolders, folder);
                treeChanged |= !before.SetEquals(_expandedFolders);
            }

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

    private NoteMetadata MetadataFromEditor() => _selected?.Metadata ?? NoteMetadata.Manual;

    private void SaveCurrent()
    {
        if (_loading || _selected is null || _selected.IsReadOnly) return;
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
        var bodyChanged = previous.Body != _selected.Body;
        var metadataChanged = previous.Metadata != _selected.Metadata;
        if (titleChanged || metadataChanged) ApplySearch();
        if (titleChanged || linksChanged)
        {
            RefreshLinkIndex();
            UpdateBacklinks();
        }
        if (titleChanged || linksChanged || metadataChanged) DrawGraph();
        if (titleChanged || bodyChanged) QueueSemanticRefresh();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (_selected is { IsReadOnly: false })
        {
            _saveTimer.Stop();
            _saveTimer.Start();
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && _selected is { IsReadOnly: false })
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
        var altDown = e.KeyStatus.IsMenuKeyDown
            || IsKeyDown(Windows.System.VirtualKey.Menu)
            || IsKeyDown(Windows.System.VirtualKey.LeftMenu)
            || IsKeyDown(Windows.System.VirtualKey.RightMenu);
        var originalKeyCode = (int)e.OriginalKey;
        var keyCode = originalKeyCode is 188 or 190 ? originalKeyCode : (int)e.Key;

        var headingLevelDelta = keyCode switch
        {
            188 when altDown => -1,
            190 when altDown => 1,
            _ => 0
        };
        if (headingLevelDelta != 0)
        {
            e.Handled = true;
            var edit = MarkdownHeadingLevelService.Change(Editor.Text, Editor.SelectionStart, Editor.SelectionLength, headingLevelDelta);
            if (edit.Changed)
            {
                Editor.Text = edit.Text;
                Editor.SelectionStart = edit.SelectionStart;
                Editor.SelectionLength = edit.SelectionLength;
            }
            return;
        }

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
        var readOnly = _selected?.IsReadOnly == true;
        DocumentKindText.Text = readOnly ? "GUIDE" : "NOTE";
        ReadOnlyBadge.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
        TitleBox.IsReadOnly = readOnly;
        Editor.IsReadOnly = readOnly;
        EditorContainer.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
        EditorPreviewDivider.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
        MarkdownPreview.Visibility = Visibility.Visible;
        if (readOnly)
        {
            PreviewRow.Height = new GridLength(1, GridUnitType.Star);
            EditorDividerRow.Height = new GridLength(0);
            EditorRow.MinHeight = 0;
            EditorRow.Height = new GridLength(0);
        }
        else
        {
            EditorDividerRow.Height = new GridLength(8);
            EditorRow.MinHeight = 130;
            ApplyDocumentSplit(_uiLayoutSettings.PreviewRatio);
        }
        UpdateMarkdownPreview();
    }

    private void EditorPreviewDivider_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var availableHeight = DocumentWorkspace.ActualHeight - EditorPreviewDivider.ActualHeight;
        if (availableHeight <= 0) return;
        var minimumPreviewHeight = Math.Min(180, availableHeight * .5);
        var minimumEditorHeight = Math.Min(150, availableHeight * .4);
        var previewHeight = Math.Clamp(
            PreviewRow.ActualHeight + e.VerticalChange,
            minimumPreviewHeight,
            availableHeight - minimumEditorHeight);
        ApplyDocumentSplit(previewHeight / availableHeight);
    }

    private void EditorPreviewDivider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _uiLayoutSettingsService.Save(_uiLayoutSettings);
    }

    private void ApplyDocumentSplit(double previewRatio)
    {
        previewRatio = Math.Clamp(previewRatio, .3, .85);
        PreviewRow.Height = new GridLength(previewRatio, GridUnitType.Star);
        EditorRow.Height = new GridLength(1 - previewRatio, GridUnitType.Star);
        _uiLayoutSettings = _uiLayoutSettings with { PreviewRatio = previewRatio };
    }

    private void ExplorerCollapse_Click(object sender, RoutedEventArgs e) => SetExplorerCollapsed(true);

    private void ExplorerOpen_Click(object sender, RoutedEventArgs e) => SetExplorerCollapsed(false);

    private void SetExplorerCollapsed(bool collapsed)
    {
        ApplyExplorerState(collapsed);
        _uiLayoutSettings = _uiLayoutSettings with { ExplorerCollapsed = collapsed };
        _uiLayoutSettingsService.Save(_uiLayoutSettings);
    }

    private void ApplyExplorerState(bool collapsed)
    {
        ExplorerPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ExplorerColumn.Width = collapsed ? new GridLength(0) : new GridLength(232);
        ExplorerOpenButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        DocumentPanel.Padding = collapsed ? new Thickness(42, 18, 28, 12) : new Thickness(28, 18, 28, 12);
    }

    private void InspectorCollapse_Click(object sender, RoutedEventArgs e)
    {
        InspectorDivider.Visibility = Visibility.Collapsed;
        InspectorDividerColumn.Width = new GridLength(0);
        InspectorPanel.Visibility = Visibility.Collapsed;
        InspectorColumn.Width = new GridLength(0);
        InspectorOpenButton.Visibility = Visibility.Visible;
    }

    private void InspectorOpen_Click(object sender, RoutedEventArgs e)
    {
        InspectorDividerColumn.Width = new GridLength(8);
        InspectorDivider.Visibility = Visibility.Visible;
        ApplyInspectorWidth(_uiLayoutSettings.InspectorWidth);
        InspectorPanel.Visibility = Visibility.Visible;
        InspectorOpenButton.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(() => DrawGraph());
    }

    private void InspectorDivider_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var maximumWidth = Math.Clamp(
            Root.ActualWidth - ExplorerColumn.ActualWidth - InspectorDividerColumn.ActualWidth - 420,
            240,
            720);
        ApplyInspectorWidth(Math.Clamp(
            InspectorColumn.ActualWidth - e.HorizontalChange,
            240,
            maximumWidth));
    }

    private void InspectorDivider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _uiLayoutSettingsService.Save(_uiLayoutSettings);
        DispatcherQueue.TryEnqueue(() => DrawGraph());
    }

    private void ApplyInspectorWidth(double width)
    {
        width = Math.Clamp(width, 240, 720);
        InspectorColumn.Width = new GridLength(width);
        _uiLayoutSettings = _uiLayoutSettings with { InspectorWidth = width };
    }

    private async void MarkdownPreview_Loaded(object sender, RoutedEventArgs e)
    {
        if (_previewReady) return;
        try
        {
            await MarkdownPreview.EnsureCoreWebView2Async();
            var mathAssetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "KaTeX");
            if (Directory.Exists(mathAssetsPath))
            {
                MarkdownPreview.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "node-assets.local",
                    mathAssetsPath,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
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
        ClearPreviewHoverSelection();
        MarkdownPreview.NavigateToString(MarkdownPreviewRenderer.Render(
            Editor.Text,
            _workspace.RootPath,
            ResolveNoteBody,
            CurrentFoldStates(),
            CurrentPreviewScrollY(),
            _uiLayoutSettings.FontScale,
            CurrentAccent.CssColor));
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

    private double CurrentPreviewScrollY()
        => _selected is not null && _notePreviewScrollPositions.TryGetValue(_selected.Path, out var scrollY)
            ? scrollY
            : 0;

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
            else if (type.GetString() == "preview-scroll"
                && _selected is not null
                && root.TryGetProperty("y", out var yElement)
                && yElement.TryGetDouble(out var scrollY)
                && double.IsFinite(scrollY)
                && scrollY >= 0)
            {
                _notePreviewScrollPositions[_selected.Path] = scrollY;
                if (root.TryGetProperty("maxY", out var maxYElement)
                    && maxYElement.TryGetDouble(out var maximumScrollY)
                    && double.IsFinite(maximumScrollY)
                    && maximumScrollY > 0)
                {
                    SyncEditorToPreview(scrollY / maximumScrollY);
                }
            }
            else if (type.GetString() == "focus-editor")
            {
                if (_selected?.IsReadOnly == true) return;
                ClearPreviewHoverSelection(restoreOriginalSelection: false);
                var offset = root.TryGetProperty("offset", out var offsetElement) && offsetElement.TryGetInt32(out var requestedOffset)
                    ? MarkdownText.OriginalOffsetFromNormalized(Editor.Text, Math.Max(0, requestedOffset))
                    : Editor.SelectionStart;
                DispatcherQueue.TryEnqueue(() =>
                {
                    Editor.Focus(FocusState.Programmatic);
                    Editor.Select(offset, 0);
                    DispatcherQueue.TryEnqueue(() => CenterEditorOnCharacter(offset));
                });
            }
            else if (type.GetString() == "hover-editor"
                && root.TryGetProperty("offset", out var hoverOffsetElement)
                && hoverOffsetElement.TryGetInt32(out var hoverOffset))
            {
                var hoverEndOffset = root.TryGetProperty("endOffset", out var hoverEndElement)
                    && hoverEndElement.TryGetInt32(out var requestedEndOffset)
                        ? requestedEndOffset
                        : -1;
                ShowPreviewHoverSelection(hoverOffset, hoverEndOffset);
            }
            else if (type.GetString() == "hover-editor-clear")
            {
                ClearPreviewHoverSelection();
            }
        }
        catch { }
    }

    private void ShowPreviewHoverSelection(int normalizedStart, int normalizedEnd)
    {
        if (!_previewHoverSelectionActive)
        {
            _previewHoverOriginalStart = Editor.SelectionStart;
            _previewHoverOriginalLength = Editor.SelectionLength;
            _previewHoverSelectionActive = true;
        }

        var start = MarkdownText.OriginalOffsetFromNormalized(Editor.Text, Math.Max(0, normalizedStart));
        var end = normalizedEnd > normalizedStart
            ? MarkdownText.OriginalOffsetFromNormalized(Editor.Text, normalizedEnd)
            : Editor.Text.Length;
        start = Math.Clamp(start, 0, Editor.Text.Length);
        end = Math.Clamp(end, start, Editor.Text.Length);
        while (end > start && Editor.Text[end - 1] is '\r' or '\n') end--;
        SelectEditorRangeWithoutScrolling(start, Math.Max(0, end - start));
    }

    private void ClearPreviewHoverSelection(bool restoreOriginalSelection = true)
    {
        if (!_previewHoverSelectionActive) return;
        _previewHoverSelectionActive = false;
        if (restoreOriginalSelection)
        {
            var start = Math.Clamp(_previewHoverOriginalStart, 0, Editor.Text.Length);
            var length = Math.Clamp(_previewHoverOriginalLength, 0, Editor.Text.Length - start);
            SelectEditorRangeWithoutScrolling(start, length);
        }
        else
        {
            _previewHoverSelectionRevision++;
        }
    }

    private void SelectEditorRangeWithoutScrolling(int start, int length)
    {
        var scrollViewer = EditorScrollViewer();
        var verticalOffset = scrollViewer?.VerticalOffset;
        Editor.Select(start, length);
        if (scrollViewer is null || verticalOffset is null) return;

        var revision = ++_previewHoverSelectionRevision;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (revision == _previewHoverSelectionRevision)
                scrollViewer.ChangeView(null, verticalOffset.Value, null, true);
        });
    }

    private void CenterEditorOnCharacter(int offset)
    {
        var scrollViewer = EditorScrollViewer();
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0) return;

        var characterBounds = Editor.GetRectFromCharacterIndex(Math.Clamp(offset, 0, Editor.Text.Length), false);
        var centeredOffset = scrollViewer.VerticalOffset
            + characterBounds.Y
            + characterBounds.Height / 2
            - scrollViewer.ViewportHeight / 2;
        scrollViewer.ChangeView(
            null,
            Math.Clamp(centeredOffset, 0, scrollViewer.ScrollableHeight),
            null,
            true);
    }

    private void SyncEditorToPreview(double progress)
    {
        var scrollViewer = EditorScrollViewer();
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0) return;

        var targetOffset = scrollViewer.ScrollableHeight * Math.Clamp(progress, 0, 1);
        if (Math.Abs(scrollViewer.VerticalOffset - targetOffset) < 1) return;
        scrollViewer.ChangeView(null, targetOffset, null, true);
    }

    private ScrollViewer? EditorScrollViewer()
        => _editorScrollViewer ??= FindVisualDescendant<ScrollViewer>(Editor);

    private void Editor_Loaded(object sender, RoutedEventArgs e)
    {
        _editorScrollViewer = FindVisualDescendant<ScrollViewer>(Editor);
        foreach (var scrollBar in FindVisualDescendants<ScrollBar>(Editor))
        {
            if (scrollBar.Orientation != Orientation.Vertical) continue;
            scrollBar.Width = 8;
            scrollBar.MinWidth = 8;
        }
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualDescendant<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private NoteInfo? FindNoteByTitle(string title) =>
        _notes.FirstOrDefault(note => note.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
        ?? _guideService.FindByTitle(title);

    private string? ResolveNoteBody(string title) => FindNoteByTitle(title)?.Body;

    private void MarkdownPreview_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme.Equals("node-note", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            var title = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
            var note = FindNoteByTitle(title);
            if (note is not null) Select(note);
        }
        else if (uri.Scheme is "http" or "https")
        {
            args.Cancel = true;
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
    }
    private void Search_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) ApplySearch(); }
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
