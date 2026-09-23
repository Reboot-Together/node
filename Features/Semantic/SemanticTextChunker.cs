using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AsterismApp;

public sealed record SemanticChunk(string Key, string Text, string ContentHash);

public static class SemanticTextChunker
{
    private const int MaximumCharacters = 1400;
    private static readonly Regex Heading = new("^#{1,6}\\s+(.+?)\\s*$", RegexOptions.Compiled);
    private static readonly Regex WikiLink = new("!?\\[\\[([^\\]|#]+)(?:#[^\\]|]+)?(?:\\|([^\\]]+))?\\]\\]", RegexOptions.Compiled);

    public static IReadOnlyList<SemanticChunk> Split(NoteInfo note)
    {
        var sections = new List<(string Heading, string Body)>();
        var sectionHeading = note.Title;
        var sectionBody = new StringBuilder();

        foreach (var line in MarkdownText.NormalizeNewlines(note.Body).Split('\n'))
        {
            var heading = Heading.Match(line);
            if (heading.Success)
            {
                AddSection(sections, note.Title, sectionHeading, sectionBody);
                sectionHeading = heading.Groups[1].Value.Trim();
                sectionBody.Clear();
            }
            else
            {
                sectionBody.AppendLine(line);
            }
        }
        AddSection(sections, note.Title, sectionHeading, sectionBody);
        if (sections.Count == 0) sections.Add((note.Title, ""));

        var packed = new List<string>();
        var current = new StringBuilder();
        foreach (var section in sections)
        {
            var sectionText = section.Heading.Equals(note.Title, StringComparison.Ordinal)
                ? section.Body
                : $"{section.Heading}\n{section.Body}".Trim();
            foreach (var part in SplitLongSection(sectionText).DefaultIfEmpty(section.Heading))
            {
                if (current.Length > 0 && current.Length + part.Length + 2 > MaximumCharacters)
                {
                    packed.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.AppendLine().AppendLine();
                current.Append(part);
            }
        }
        if (current.Length > 0) packed.Add(current.ToString());

        var chunks = new List<SemanticChunk>(packed.Count);
        for (var index = 0; index < packed.Count; index++)
        {
            var text = Clean($"{note.Title}\n{packed[index]}");
            chunks.Add(new SemanticChunk(
                index.ToString(),
                text,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()));
        }
        return chunks;
    }

    private static void AddSection(
        List<(string Heading, string Body)> sections,
        string noteTitle,
        string heading,
        StringBuilder body)
    {
        var text = body.ToString().Trim();
        if (text.Length > 0 || !heading.Equals(noteTitle, StringComparison.Ordinal))
            sections.Add((heading, text));
    }

    private static List<string> SplitLongSection(string body)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var paragraph in Regex.Split(body.Trim(), "\\n\\s*\\n").Where(value => value.Length > 0))
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > MaximumCharacters)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            if (paragraph.Length <= MaximumCharacters)
            {
                if (current.Length > 0) current.AppendLine().AppendLine();
                current.Append(paragraph);
                continue;
            }
            for (var offset = 0; offset < paragraph.Length; offset += MaximumCharacters)
                parts.Add(paragraph.Substring(offset, Math.Min(MaximumCharacters, paragraph.Length - offset)));
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private static string Clean(string text)
    {
        text = WikiLink.Replace(text, match => match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value);
        text = Regex.Replace(text, "```[a-zA-Z0-9_-]*", " ");
        text = Regex.Replace(text, "[`*_~=]+", " ");
        text = Regex.Replace(text, "\\s+", " ");
        return text.Trim();
    }
}
