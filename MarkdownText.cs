using System.Text.RegularExpressions;

namespace AsterismApp;

public static class MarkdownText
{
    public static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    public static int OriginalOffsetFromNormalized(string text, int normalizedOffset)
    {
        var originalOffset = 0;
        var currentNormalizedOffset = 0;
        while (originalOffset < text.Length && currentNormalizedOffset < normalizedOffset)
        {
            if (text[originalOffset] == '\r' && originalOffset + 1 < text.Length && text[originalOffset + 1] == '\n')
                originalOffset += 2;
            else
                originalOffset++;
            currentNormalizedOffset++;
        }
        return originalOffset;
    }

    public static string NormalizeTitle(string title)
    {
        title = Regex.Replace(title ?? "", "^#{1,6}\\s+", "").Trim();
        return string.IsNullOrWhiteSpace(title) ? "제목 없는 노트" : title;
    }
}
