using System.Text.RegularExpressions;

namespace NodeApp;

public static class MarkdownSectionService
{
    public static IReadOnlyList<string> ExtractBodies(string markdown)
    {
        var lines = MarkdownText.NormalizeNewlines(markdown).Split('\n');
        var headings = HeadingLines(lines);
        var sections = new List<string>(headings.Count);
        for (var index = 0; index < headings.Count; index++)
        {
            var start = headings[index].Line + 1;
            var end = SectionEnd(headings, index, lines.Length);
            sections.Add(string.Join("\n", lines[start..end]).Trim('\n'));
        }
        return sections;
    }

    public static string ReplaceBody(string markdown, int sectionIndex, string replacement)
    {
        var lines = MarkdownText.NormalizeNewlines(markdown).Split('\n');
        var headings = HeadingLines(lines);
        if (sectionIndex < 0 || sectionIndex >= headings.Count) return MarkdownText.NormalizeNewlines(markdown);
        var start = headings[sectionIndex].Line + 1;
        var end = SectionEnd(headings, sectionIndex, lines.Length);
        var replacementLines = MarkdownText.NormalizeNewlines(replacement).Trim('\n').Split('\n');
        if (replacementLines is [""]) replacementLines = [];
        return string.Join("\n", lines[..start].Concat(replacementLines).Concat(lines[end..])).TrimEnd();
    }

    private static int SectionEnd(IReadOnlyList<Heading> headings, int sectionIndex, int documentEnd)
    {
        var level = headings[sectionIndex].Level;
        for (var index = sectionIndex + 1; index < headings.Count; index++)
        {
            if (headings[index].Level <= level) return headings[index].Line;
        }
        return documentEnd;
    }

    private static List<Heading> HeadingLines(IReadOnlyList<string> lines)
    {
        var headings = new List<Heading>();
        var fenced = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { fenced = !fenced; continue; }
            if (fenced) continue;

            var match = Regex.Match(lines[index], "^ {0,3}(#{1,6})[ \\t]+\\S");
            if (match.Success) headings.Add(new Heading(index, match.Groups[1].Length));
        }
        return headings;
    }

    private readonly record struct Heading(int Line, int Level);
}
