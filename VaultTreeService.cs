using System.Runtime.InteropServices;

namespace AsterismApp;

public sealed class VaultTreeService
{
    private static readonly IComparer<string> NameComparer = Comparer<string>.Create(CompareNames);

    public IReadOnlyList<string> LoadFolders(string rootPath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            return [root, .. Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                    .Contains(".trash", StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)];
        }
        catch
        {
            return [root];
        }
    }

    public IReadOnlyList<string> AncestorFolders(string rootPath, string notePath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var current = Path.GetDirectoryName(Path.GetFullPath(notePath));
        var folders = new List<string>();

        while (current is not null
            && (current.Equals(root, StringComparison.OrdinalIgnoreCase)
                || current.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            folders.Add(current);
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }

        return folders;
    }

    public IReadOnlyList<VaultItem> Build(
        string rootPath,
        IReadOnlyList<NoteInfo> notes,
        IReadOnlyList<string> folders,
        IReadOnlySet<string> expandedFolders,
        string? query = null)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedQuery = query?.Trim() ?? "";
        var notesByDirectory = notes
            .Where(note => Matches(note, normalizedQuery))
            .GroupBy(note => Path.GetDirectoryName(note.Path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(note => note.Title, NameComparer).ToList(), StringComparer.OrdinalIgnoreCase);
        var childFolders = folders
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .Where(path => !path.Equals(root, StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(path => Path.GetFileName(path), NameComparer).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var result = new List<VaultItem>();
        AddFolder(result, root, root, notesByDirectory, childFolders, expandedFolders, 0, true, normalizedQuery.Length > 0);
        return result;
    }

    private static void AddFolder(
        ICollection<VaultItem> result,
        string root,
        string folder,
        IReadOnlyDictionary<string, List<NoteInfo>> notesByDirectory,
        IReadOnlyDictionary<string, List<string>> childFolders,
        IReadOnlySet<string> expandedFolders,
        int depth,
        bool isRoot,
        bool expandAll)
    {
        var expanded = isRoot || expandAll || expandedFolders.Contains(folder);
        var name = isRoot ? new DirectoryInfo(root).Name : Path.GetFileName(folder);
        result.Add(new VaultItem(name, folder, true, isRoot, expanded, depth, null));
        if (!expanded) return;

        if (childFolders.TryGetValue(folder, out var children))
            foreach (var child in children)
                AddFolder(result, root, child, notesByDirectory, childFolders, expandedFolders, depth + 1, false, expandAll);

        if (!notesByDirectory.TryGetValue(folder, out var notes)) return;
        foreach (var note in notes)
            result.Add(new VaultItem(note.Title, note.Path, false, false, false, depth + 1, note));
    }

    private static bool Matches(NoteInfo note, string query) =>
        query.Length == 0
        || $"{note.Title}\n{note.Body}\n{note.Metadata.Category}\n{note.Metadata.Source}\n{note.Metadata.Type}"
            .Contains(query, StringComparison.OrdinalIgnoreCase);

    private static int CompareNames(string? left, string? right)
    {
        var logical = StrCmpLogicalW(left ?? "", right ?? "");
        return logical != 0 ? logical : StringComparer.Ordinal.Compare(left, right);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string left, string right);
}
