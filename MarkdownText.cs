using System.Text.RegularExpressions;

namespace NodeApp;

public static class MarkdownText
{
    public static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string NormalizeTitle(string title)
    {
        title = Regex.Replace(title ?? "", "^#{1,6}\\s+", "").Trim();
        return string.IsNullOrWhiteSpace(title) ? "제목 없는 노트" : title;
    }
}
