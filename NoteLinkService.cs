using System.Text.RegularExpressions;

namespace NodeApp;

public sealed class NoteLinkService
{
    private static readonly Regex WikiLink = new("\\[\\[([^\\]|#]+)(?:#[^\\]|]+)?(?:\\|[^\\]]+)?\\]\\]", RegexOptions.Compiled);

    public IReadOnlySet<string> ExtractTargets(string body) => WikiLink.Matches(body)
        .Select(match => match.Groups[1].Value.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<string>> Build(IEnumerable<NoteInfo> notes)
    {
        var distinctNotes = notes.GroupBy(note => note.Title, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        var titles = distinctNotes.Select(note => note.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return distinctNotes.ToDictionary(
            note => note.Title,
            note => ExtractTargets(note.Body).Where(titles.Contains).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }
}
