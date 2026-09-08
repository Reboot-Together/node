using System.Runtime.InteropServices;

namespace AsterismApp;

public sealed class VaultTreeService
{
    private static readonly IComparer<string> NameComparer = Comparer<string>.Create(CompareNames);
    private readonly VaultOrderService _orderService;

    public VaultTreeService(string? orderStorageDirectory = null)
    {
        _orderService = new VaultOrderService(orderStorageDirectory);
    }

    public void Reorder(
        string rootPath,
        string parentPath,
        IEnumerable<string> siblingPaths,
        string sourcePath,
        string targetPath,
        bool after) =>
        _orderService.Reorder(rootPath, parentPath, siblingPaths, sourcePath, targetPath, after);

    public void RemapOrderPath(string rootPath, string sourcePath, string destinationPath) =>
        _orderService.RemapPath(rootPath, sourcePath, destinationPath);

    public void RemoveOrderPath(string rootPath, string path) =>
        _orderService.RemovePath(rootPath, path);

    public IReadOnlyList<string> OrderedChildren(
        string rootPath,
        string parentPath,
        IReadOnlyList<NoteInfo> notes,
        IReadOnlyList<string> folders)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar);
        var defaultPaths = folders
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .Where(path => !path.Equals(Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(path)?.Equals(parent, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(path => Path.GetFileName(path) ?? "", NameComparer)
            .Concat(notes
                .Where(note => Path.GetDirectoryName(note.Path)?.Equals(parent, StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(note => note.Title, NameComparer)
                .Select(note => note.Path));
        return _orderService.Order(rootPath, parent, defaultPaths);
    }

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
            .GroupBy(note => Path.GetDirectoryName(note.Path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(note => note.Title, NameComparer).ToList(), StringComparer.OrdinalIgnoreCase);
        var visibleNotes = notes
            .Where(note => Matches(note, normalizedQuery))
            .Select(note => note.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var childFolders = folders
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .Where(path => !path.Equals(root, StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(path => Path.GetFileName(path), NameComparer).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var notesByPath = notes.ToDictionary(note => note.Path, StringComparer.OrdinalIgnoreCase);
        var folderPaths = folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<VaultItem>();
        AddChildren(root, 0, "", normalizedQuery.Length > 0);
        return result;

        void AddChildren(string parent, int depth, string parentNumber, bool expandAll)
        {
            var defaultPaths = new List<string>();
            if (childFolders.TryGetValue(parent, out var childFolderPaths)) defaultPaths.AddRange(childFolderPaths);
            if (notesByDirectory.TryGetValue(parent, out var childNotes)) defaultPaths.AddRange(childNotes.Select(note => note.Path));
            var ordered = _orderService.Order(root, parent, defaultPaths);
            for (var index = 0; index < ordered.Count; index++)
            {
                var path = ordered[index];
                var number = parentNumber.Length == 0 ? $"{index + 1}" : $"{parentNumber}.{index + 1}";
                if (folderPaths.Contains(path))
                {
                    var expanded = expandAll || expandedFolders.Contains(path);
                    result.Add(new VaultItem(Path.GetFileName(path), path, true, false, expanded, depth, null, number));
                    if (expanded) AddChildren(path, depth + 1, number, expandAll);
                }
                else if (notesByPath.TryGetValue(path, out var note) && visibleNotes.Contains(path))
                {
                    result.Add(new VaultItem(note.Title, note.Path, false, false, false, depth, note, number));
                }
            }
        }
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
