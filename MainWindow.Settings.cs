using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private TypographySettings Typography =>
        (TypographySettings)Application.Current.Resources["Typography"];

    private AccentPalette CurrentAccent => AppearanceThemes.Get(_uiLayoutSettings.AccentTheme);

    private void ApplyAppearanceSettings(bool refreshContent = true)
    {
        Typography.Apply(_uiLayoutSettings.FontScale);
        var palette = CurrentAccent;
        if (Application.Current.Resources["Positive"] is SolidColorBrush positive)
            positive.Color = palette.Accent;
        if (Application.Current.Resources["Accent"] is SolidColorBrush accent)
            accent.Color = palette.Surface;
        if (Editor?.Resources["TextControlSelectionHighlightColor"] is SolidColorBrush selection)
            selection.Color = palette.Surface;

        if (!refreshContent) return;
        RefreshSideDocumentAppearance();
        if (_previewReady) UpdateMarkdownPreview();
        if (GraphCanvas is not null) DrawGraph(centerCurrentNode: false);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var fontValue = new TextBlock
        {
            Text = $"{_uiLayoutSettings.FontScale:P0}",
            Foreground = (Brush)Application.Current.Resources["MutedText"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var fontSlider = new Slider
        {
            Minimum = .8,
            Maximum = 1.4,
            StepFrequency = .05,
            Value = _uiLayoutSettings.FontScale,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var fontGrid = new Grid { ColumnSpacing = 10 };
        fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fontGrid.Children.Add(fontSlider);
        Grid.SetColumn(fontValue, 1);
        fontGrid.Children.Add(fontValue);

        var themeSelector = new ComboBox
        {
            ItemsSource = AppearanceThemes.All,
            DisplayMemberPath = nameof(AccentPalette.DisplayName),
            SelectedItem = CurrentAccent,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var updateButton = new Button
        {
            Content = "업데이트 확인",
            Style = (Style)Application.Current.Resources["QuietButton"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var storagePathText = new TextBlock
        {
            Text = _workspace.UnavailableRootPath is null
                ? _workspace.RootPath
                : $"현재 임시 저장소: {_workspace.RootPath}\n연결되지 않은 저장소: {_workspace.UnavailableRootPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["MutedText"]
        };
        var changeFolderButton = new Button
        {
            Content = "폴더 변경",
            Style = (Style)Application.Current.Resources["QuietButton"]
        };
        var openFolderButton = new Button
        {
            Content = "폴더 열기",
            Style = (Style)Application.Current.Resources["QuietButton"]
        };
        var storageButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        storageButtons.Children.Add(changeFolderButton);
        storageButtons.Children.Add(openFolderButton);
        var versionText = new TextBlock
        {
            Text = $"Asterism v{UpdateService.CurrentVersionText}\n로컬 저장 · 0.7초 후 자동 저장",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["MutedText"]
        };

        var content = new StackPanel { Spacing = 10, MinWidth = 390 };
        content.Children.Add(SettingsHeading("글자 크기"));
        content.Children.Add(fontGrid);
        content.Children.Add(SettingsHeading("강조색"));
        content.Children.Add(themeSelector);
        content.Children.Add(SettingsHeading("저장소"));
        content.Children.Add(storagePathText);
        content.Children.Add(storageButtons);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["CardBorder"],
            Margin = new Thickness(0, 8, 0, 4)
        });
        content.Children.Add(SettingsHeading("앱 정보"));
        content.Children.Add(versionText);
        content.Children.Add(updateButton);

        var dialog = new ContentDialog
        {
            Title = "설정",
            Content = content,
            CloseButtonText = "닫기",
            XamlRoot = Root.XamlRoot
        };

        fontSlider.ValueChanged += (_, args) =>
        {
            var scale = Math.Round(args.NewValue * 20) / 20;
            _uiLayoutSettings = _uiLayoutSettings with { FontScale = scale };
            fontValue.Text = $"{scale:P0}";
            ApplyAppearanceSettings();
        };
        themeSelector.SelectionChanged += (_, _) =>
        {
            if (themeSelector.SelectedItem is not AccentPalette palette) return;
            _uiLayoutSettings = _uiLayoutSettings with { AccentTheme = palette.Key };
            ApplyAppearanceSettings();
        };
        updateButton.Click += (_, _) =>
        {
            dialog.Hide();
            DispatcherQueue.TryEnqueue(() => UpdateVersions_Click(updateButton, new RoutedEventArgs()));
        };
        openFolderButton.Click += (_, _) => OpenFolder_Click(openFolderButton, new RoutedEventArgs());
        changeFolderButton.Click += async (_, _) =>
        {
            if (await ChangeWorkspaceFolderAsync()) storagePathText.Text = _workspace.RootPath;
        };

        await dialog.ShowAsync();
        _uiLayoutSettingsService.Save(_uiLayoutSettings);
    }

    private static TextBlock SettingsHeading(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 5, 0, 0)
    };
}
