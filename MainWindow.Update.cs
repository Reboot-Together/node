using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NodeApp;

public sealed partial class MainWindow
{
    private readonly UpdateService _updateService = new();
    private bool _checkingUpdates;

    private async void UpdateVersions_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdates) return;

        _checkingUpdates = true;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "확인 중…";
        try
        {
            var releases = await _updateService.GetStableReleasesAsync();
            if (releases.Count == 0)
            {
                await ShowMessage("업데이트", "설치 가능한 안정화 버전이 아직 없습니다.");
                return;
            }

            var current = UpdateService.CurrentVersion;
            var selector = new ComboBox
            {
                Header = $"설치된 버전: v{current.ToString(3)}",
                ItemsSource = releases,
                DisplayMemberPath = nameof(NodeRelease.DisplayName),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var releaseNotes = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 65))
            };
            var notesScroll = new ScrollViewer
            {
                Content = releaseNotes,
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var content = new StackPanel { Spacing = 12, MinWidth = 430 };
            content.Children.Add(selector);
            content.Children.Add(notesScroll);

            var dialog = new ContentDialog
            {
                Title = "Node 버전 선택",
                Content = content,
                PrimaryButtonText = "선택 버전 설치",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Root.XamlRoot
            };

            void RefreshSelection()
            {
                if (selector.SelectedItem is not NodeRelease selected) return;
                var relation = selected.Version > current
                    ? "업데이트"
                    : selected.Version < current
                        ? "이전 버전 설치"
                        : "현재 설치된 버전";
                releaseNotes.Text = $"{relation}\n\n{selected.Notes}";
                dialog.IsPrimaryButtonEnabled = selected.Version != current;
            }

            selector.SelectionChanged += (_, _) => RefreshSelection();
            RefreshSelection();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary
                || selector.SelectedItem is not NodeRelease release)
                return;

            SaveCurrent();
            UpdateButton.Content = "다운로드 0%";
            var progress = new Progress<int>(percent =>
                UpdateButton.Content = $"다운로드 {percent}%");
            await _updateService.PrepareInstallationAsync(release, progress);
            UpdateButton.Content = "설치 준비됨";
            Close();
        }
        catch (HttpRequestException exception)
        {
            await ShowMessage("업데이트 확인 실패", $"GitHub 릴리스에 연결하지 못했습니다.\n\n{exception.Message}");
        }
        catch (Exception exception)
        {
            await ShowMessage("업데이트 실패", exception.Message);
        }
        finally
        {
            _checkingUpdates = false;
            if (UpdateButton is not null)
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "업데이트";
            }
        }
    }
}
