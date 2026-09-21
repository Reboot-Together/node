using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private bool _pdfMode;
    private bool _pdfReaderReady;
    private bool _pdfReaderInitializing;
    private string? _currentPdfPath;

    private async void PdfMode_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfMode && _currentPdfPath is not null) return;
        await PickAndOpenPdfAsync();
    }

    private async void ChangePdf_Click(object sender, RoutedEventArgs e) => await PickAndOpenPdfAsync();

    private async Task PickAndOpenPdfAsync()
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".pdf");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null) await ShowPdfModeAsync(file.Path);
    }

    private async Task ShowPdfModeAsync(string path)
    {
        if (!PdfReaderRules.IsPdf(path) || !File.Exists(path))
        {
            await ShowMessage("PDF를 열 수 없음", "선택한 PDF 파일을 찾을 수 없습니다.");
            return;
        }

        if (_constellationMode) ShowDocumentMode();
        SaveCurrent();
        if (!SaveSideDocuments())
        {
            await ShowMessage("PDF를 열 수 없음", "저장하지 못한 옆 문서가 있습니다. 문서의 저장 오류를 확인해 주세요.");
            return;
        }

        _pdfMode = true;
        _currentPdfPath = Path.GetFullPath(path);
        PdfTitleText.Text = Path.GetFileName(path);
        PdfStatusText.Text = "PDF 불러오는 중…";
        DocumentGroupsHost.Visibility = Visibility.Collapsed;
        ConstellationPanel.Visibility = Visibility.Collapsed;
        PdfPanel.Visibility = Visibility.Visible;
        DocumentModeIndicator.Visibility = Visibility.Collapsed;
        ConstellationModeIndicator.Visibility = Visibility.Collapsed;
        PdfModeIndicator.Visibility = Visibility.Visible;

        try
        {
            await EnsurePdfReaderAsync();
            PdfReader.Source = PdfReaderRules.FileUri(_currentPdfPath);
        }
        catch (Exception exception)
        {
            PdfStatusText.Text = $"PDF를 표시하지 못했습니다: {exception.Message}";
        }
    }

    private async Task EnsurePdfReaderAsync()
    {
        if (_pdfReaderReady) return;
        if (_pdfReaderInitializing)
        {
            while (_pdfReaderInitializing) await Task.Delay(20);
            if (_pdfReaderReady) return;
        }

        _pdfReaderInitializing = true;
        try
        {
            await PdfReader.EnsureCoreWebView2Async();
            _pdfReaderReady = true;
        }
        finally
        {
            _pdfReaderInitializing = false;
        }
    }

    private void PdfReader_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!_pdfMode || _currentPdfPath is null) return;
        PdfStatusText.Text = args.IsSuccess
            ? "페이지 이동·확대·검색·인쇄는 PDF 도구 모음에서 사용할 수 있습니다."
            : $"PDF를 표시하지 못했습니다: {args.WebErrorStatus}";
    }

    private void OpenPdfLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPdfPath is null || !File.Exists(_currentPdfPath))
        {
            PdfStatusText.Text = "PDF 파일을 찾을 수 없습니다.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_currentPdfPath}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            PdfStatusText.Text = $"파일 위치를 열지 못했습니다: {exception.Message}";
        }
    }

    private void ClosePdf_Click(object sender, RoutedEventArgs e) => ShowDocumentMode();

    private void HidePdfMode()
    {
        if (!_pdfMode && PdfPanel.Visibility != Visibility.Visible) return;
        _pdfMode = false;
        PdfPanel.Visibility = Visibility.Collapsed;
        PdfModeIndicator.Visibility = Visibility.Collapsed;
        _currentPdfPath = null;
        PdfTitleText.Text = "PDF";
        PdfStatusText.Text = "";
        if (_pdfReaderReady) PdfReader.Source = new Uri("about:blank");
    }
}
