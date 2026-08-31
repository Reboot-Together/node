using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace NodeApp;

public sealed partial class MainWindow
{
    private readonly ChatGptImportService _chatImport = new();
    private bool _importing;

    private async void ImportChat_Click(object sender, RoutedEventArgs e) => await ImportChatAsync();

    private async void ShareUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        var valid = UpdateImportButtonState();
        if (valid)
            await ImportChatAsync();
    }

    private bool UpdateImportButtonState()
    {
        var valid = _chatImport.TryParseShareUrl(ShareUrlBox.Text.Trim(), out _);
        if (ImportButton is not null) ImportButton.IsEnabled = valid && !_importing;
        return valid;
    }

    private async Task ImportChatAsync()
    {
        if (_importing) return;

        var url = ShareUrlBox.Text.Trim();
        if (!_chatImport.TryParseShareUrl(url, out var uri))
        {
            if (!string.IsNullOrWhiteSpace(url))
                await ShowMessage("올바른 공유 링크가 아님", "chatgpt.com/share/로 시작하는 공개 공유 링크를 붙여 넣으세요.");
            return;
        }

        string transcript;
        _importing = true;
        UpdateImportButtonState();
        try
        {
            transcript = await _chatImport.ReadConversationAsync(uri, ReadSharedConversationInBrowser);
        }
        catch (Exception exception)
        {
            await ShowMessage(
                "가져오기 실패",
                $"공유 링크에서 대화 내용을 읽지 못했습니다. 공개 공유 링크인지 확인해 주세요.\n\n{exception.Message}");
            return;
        }
        finally
        {
            _importing = false;
            UpdateImportButtonState();
        }

        SaveCurrent();
        var title = _chatImport.CreateTitle(transcript);
        var metadata = _chatImport.InferMetadata(transcript);
        var note = _repository.Create(title, metadata);
        var related = _notes
            .Where(item => !item.Title.Equals(note.Title, StringComparison.OrdinalIgnoreCase)
                && transcript.Contains(item.Title, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        note = _repository.Save(
            note.Path,
            note.Title,
            _chatImport.BuildNoteBody(transcript, url, related),
            metadata);
        ShareUrlBox.Text = "";
        RefreshNotes();
        Select(note);
    }

    private async Task<string?> ReadSharedConversationInBrowser(Uri uri)
    {
        try
        {
            await ShareReader.EnsureCoreWebView2Async();
            var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Completed(WebView2 _, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.IsSuccess)
                    navigation.TrySetResult();
                else
                    navigation.TrySetException(new InvalidOperationException("공유 페이지를 열 수 없습니다."));
            }

            ShareReader.NavigationCompleted += Completed;
            try
            {
                ShareReader.Source = uri;
                await navigation.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Task.Delay(1200);
                var rendered = JsonSerializer.Deserialize<string>(
                    await ShareReader.ExecuteScriptAsync("document.body.innerText")) ?? "";
                if (_chatImport.IsUseful(rendered)) return rendered.Trim();
            }
            finally
            {
                ShareReader.NavigationCompleted -= Completed;
            }
        }
        catch
        {
            // The import service falls back to HTTP extraction when browser reading fails.
        }

        return null;
    }
}
