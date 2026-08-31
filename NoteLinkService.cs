using System.Text.RegularExpressions;

namespace NodeApp;

public sealed class NoteLinkService
{
    private static readonly Regex WikiLink = new("\\[\\[([^\\]|#]+)(?:#[^\\]|]+)?(?:\\|[^\\]]+)?\\]\\]", RegexOptions.Compiled);

    public IReadOnlyDictionary<string, List<string>> Build(IEnumerable<NoteInfo> notes)
    {
        var distinctNotes = notes.GroupBy(note => note.Title, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        var titles = distinctNotes.Select(note => note.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return distinctNotes.ToDictionary(
            note => note.Title,
            note => WikiLink.Matches(note.Body).Select(match => match.Groups[1].Value.Trim()).Where(titles.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }
}
