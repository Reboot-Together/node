using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private bool _constellationMode;

    private void DocumentMode_Click(object sender, RoutedEventArgs e) => ShowDocumentMode();

    private void ConstellationMode_Click(object sender, RoutedEventArgs e) => ShowConstellationMode();

    private void WorkspaceGraph_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_constellationMode) ShowDocumentMode();
        else ShowConstellationMode();
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
        _constellationMode = false;
        ResetGraphDirectionalCursor();
        ConstellationPanel.Visibility = Visibility.Collapsed;
        DocumentGroupsHost.Visibility = Visibility.Visible;
        DocumentModeIndicator.Visibility = Visibility.Visible;
        ConstellationModeIndicator.Visibility = Visibility.Collapsed;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_selected?.IsReadOnly == true) MarkdownPreview.Focus(FocusState.Programmatic);
            else Editor.Focus(FocusState.Programmatic);
        });
    }

    private void ShowConstellationMode()
    {
        if (_constellationMode || _selected is null) return;

        SaveEditor();
        var selected = _selected!;
        _constellationMode = true;
        ConstellationTitleText.Text = selected.Title;
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

    private void HandleWorkspaceModeMessage(string? messageType)
    {
        if (messageType == "workspace-mode-toggle")
        {
            if (_constellationMode) ShowDocumentMode();
            else ShowConstellationMode();
        }
        else if (messageType == "workspace-mode-document" && _constellationMode)
        {
            ShowDocumentMode();
        }
    }
}
