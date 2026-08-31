using System.Net;
using System.Text.RegularExpressions;

namespace NodeApp;

public sealed class ChatGptImportService
{
    private static readonly IReadOnlyDictionary<string, string> Categories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = "Database", ["sql"] = "Database", ["database"] = "Database",
            ["node.js"] = "Development", ["javascript"] = "Development", ["c#"] = "Development",
            ["python"] = "Development", ["react"] = "Development", ["http"] = "Development"
        };

    public bool TryParseShareUrl(string text, out Uri uri) =>
        Uri.TryCreate(text, UriKind.Absolute, out uri!) &&
        uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/share/", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath[7..].Trim('/').Length >= 12;

    public async Task<string> ReadConversationAsync(Uri uri, Func<Uri, Task<string?>> preferredReader)
    {
        try
        {
            var rendered = await preferredReader(uri);
            if (IsUseful(rendered)) return rendered!.Trim();
        }
        catch { }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 NodeNotes/1.0");
        var text = HtmlToText(await client.GetStringAsync(uri));
        if (!IsUseful(text)) throw new InvalidOperationException("공유 페이지에서 대화 본문을 찾지 못했습니다.");
        return text.Trim();
    }

    public string CreateTitle(string transcript) => FirstUsefulLine(transcript) ?? $"ChatGPT 대화 {DateTime.Now:yyyy-MM-dd}";

    public NoteMetadata InferMetadata(string text)
    {
        var category = Categories.FirstOrDefault(item => text.Contains(item.Key, StringComparison.OrdinalIgnoreCase)).Value ?? "Inbox";
        return new NoteMetadata(category, DateTime.Today, "ChatGPT", "Study");
    }

    public string BuildNoteBody(string transcript, string url, IEnumerable<string> relatedTitles)
    {
        var related = relatedTitles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var relatedBlock = related.Count == 0 ? "" : "\n## 관련 노트\n\n" + string.Join("\n", related.Select(title => $"- [[{title}]]")) + "\n";
        return $"> ChatGPT 공유 대화\n> 원본: {url}\n\n---\n\n{transcript}\n{relatedBlock}";
    }

    public bool IsUseful(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length >= 180 && !text.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);

    private static string HtmlToText(string html)
    {
        html = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<(br|/p|/div|/li|/h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
        return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), "[ \\t]+", " ")
            .Replace("\r", "").Replace("\n\n\n", "\n\n").Trim();
    }

    private static string? FirstUsefulLine(string text) => text.Split('\n').Select(line => line.Trim().TrimStart('#', '>', ' ')).FirstOrDefault(line => line.Length is > 3 and < 80);
}
