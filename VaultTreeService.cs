namespace NodeApp;

public sealed class VaultTreeService
{
    public IReadOnlyList<VaultItem> Build(
        string rootPath,
        IReadOnlyList<NoteInfo> notes,
        IReadOnlySet<string> expandedFolders,
        string? query = null)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedQuery = query?.Trim() ?? "";
        var notesByDirectory = notes
            .Where(note => Matches(note, normalizedQuery))
            .GroupBy(note => Path.GetDirectoryName(note.Path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(note => note.Title).ToList(), StringComparer.OrdinalIgnoreCase);
        var result = new List<VaultItem>();
        AddFolder(result, root, root, notesByDirectory, expandedFolders, 0, true, normalizedQuery.Length > 0);
        return result;
    }

    private static void AddFolder(
        ICollection<VaultItem> result,
        string root,
        string folder,
        IReadOnlyDictionary<string, List<NoteInfo>> notesByDirectory,
        IReadOnlySet<string> expandedFolders,
        int depth,
        bool isRoot,
        bool expandAll)
    {
        var expanded = isRoot || expandAll || expandedFolders.Contains(folder);
        var name = isRoot ? new DirectoryInfo(root).Name : Path.GetFileName(folder);
        result.Add(new VaultItem(name, folder, true, isRoot, expanded, depth, null));
        if (!expanded) return;

        foreach (var child in ChildDirectories(folder))
            AddFolder(result, root, child, notesByDirectory, expandedFolders, depth + 1, false, expandAll);

        if (!notesByDirectory.TryGetValue(folder, out var notes)) return;
        foreach (var note in notes)
            result.Add(new VaultItem(note.Title, note.Path, false, false, false, depth + 1, note));
    }

    private static IEnumerable<string> ChildDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(path => !Path.GetFileName(path).Equals(".trash", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool Matches(NoteInfo note, string query) =>
        query.Length == 0
        || $"{note.Title}\n{note.Body}\n{note.Metadata.Category}\n{note.Metadata.Source}\n{note.Metadata.Type}"
            .Contains(query, StringComparison.OrdinalIgnoreCase);
}
