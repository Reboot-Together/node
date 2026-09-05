using System.Net;
using System.Text.RegularExpressions;

namespace AsterismApp;

public static class TextDiagramRenderer
{
    private static readonly Regex Connector = new(@"^[ \t]*(?:[│┃┆┊]|[├└╰╠╚][─━═-])", RegexOptions.Compiled);
    private static readonly Regex Branch = new(@"^[ \t│┃┆┊]*[├└╰╠╚][─━═-]+", RegexOptions.Compiled);
    private static readonly Regex MarkdownBoundary = new(@"^(?:#{1,6}\s|>|\||[-+*]\s|\d+[.)]\s|`|~|<|\$|%|---|___|\*\*\*)", RegexOptions.Compiled);
    private static readonly Regex Decoration = new(@"[─━│┃┆┊├└┬┼╰╭╠╚═]+|[\u1100-\u115F\u2E80-\uA4CF\uAC00-\uD7A3\uF900-\uFAFF\uFF01-\uFF60]+", RegexOptions.Compiled);

    public static bool TryRead(IReadOnlyList<string> lines, int start, out int end)
    {
        end = start;
        if (start >= lines.Count || string.IsNullOrWhiteSpace(lines[start])) return false;
        if (!Connector.IsMatch(lines[start]))
        {
            // One root label immediately above the connecting lines belongs to the diagram.
            if (MarkdownBoundary.IsMatch(lines[start].TrimStart()) || start + 1 >= lines.Count
                || !Connector.IsMatch(lines[start + 1])) return false;
            end++;
        }
        var branches = 0;
        while (end < lines.Count)
        {
            var line = lines[end];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (end + 1 < lines.Count && Connector.IsMatch(lines[end + 1])) { end++; continue; }
                break;
            }
            var indentedContinuation = (line.StartsWith("  ") || line.StartsWith('\t'))
                && !MarkdownBoundary.IsMatch(line.TrimStart());
            if (!Connector.IsMatch(line) && !indentedContinuation) break;
            if (Branch.IsMatch(line)) branches++;
            end++;
        }
        return branches >= 2;
    }

    public static bool IsWholeDiagram(string text)
    {
        var lines = MarkdownText.NormalizeNewlines(text).Trim('\n').Split('\n');
        return TryRead(lines, 0, out var end) && end == lines.Length;
    }

    public static string Render(string text, int? sourceStart = null, int? sourceEnd = null)
    {
        var position = sourceStart is { } start
            ? $" data-source-offset=\"{start}\" data-source-end=\"{sourceEnd}\""
            : "";
        // Decorate encoded text only. Text nodes remain identical to the source for selection/copy.
        var encoded = WebUtility.HtmlEncode(text);
        var decorated = Decoration.Replace(encoded, match =>
        {
            var wide = match.Value[0] >= '\u1100' && match.Value[0] is not (>= '\u2500' and <= '\u257F');
            return wide
                ? $"<span class=\"diagram-wide\" style=\"width:{match.Length * 2}ch\">{match.Value}</span>"
                : $"<span class=\"diagram-connector\">{match.Value}</span>";
        });
        return $"<pre class=\"text-diagram\"{position} aria-label=\"텍스트 다이어그램\"><code>{decorated}</code></pre>";
    }
}
