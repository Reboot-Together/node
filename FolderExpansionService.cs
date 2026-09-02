namespace AsterismApp;

public sealed class FolderExpansionService
{
    public void InitializeDefaults(
        string rootPath,
        IReadOnlyList<string> folders,
        ISet<string> expandedFolders)
    {
        var root = Normalize(rootPath);
        expandedFolders.Add(root);

        foreach (var group in folders
            .Select(Normalize)
            .Where(folder => !folder.Equals(root, StringComparison.OrdinalIgnoreCase))
            .GroupBy(folder => ParentOf(root, folder), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).First();
            expandedFolders.Add(first);
        }
    }

    public void ExpandExclusive(
        string rootPath,
        IReadOnlyList<string> folders,
        ISet<string> expandedFolders,
        string folder)
    {
        var root = Normalize(rootPath);
        folder = Normalize(folder);
        if (folder.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            expandedFolders.Add(root);
            return;
        }

        var parent = ParentOf(root, folder);
        foreach (var sibling in folders
            .Select(Normalize)
            .Where(candidate => !candidate.Equals(folder, StringComparison.OrdinalIgnoreCase)
                && ParentOf(root, candidate).Equals(parent, StringComparison.OrdinalIgnoreCase)))
            expandedFolders.Remove(sibling);

        expandedFolders.Add(folder);
    }

    public void ToggleExclusive(
        string rootPath,
        IReadOnlyList<string> folders,
        ISet<string> expandedFolders,
        string folder)
    {
        folder = Normalize(folder);
        if (expandedFolders.Contains(folder)) expandedFolders.Remove(folder);
        else ExpandExclusive(rootPath, folders, expandedFolders, folder);
    }

    private static string Normalize(string path) => IsVirtual(path)
        ? path.TrimEnd('/')
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string ParentOf(string root, string folder) => IsVirtual(folder)
        ? root
        : Path.GetDirectoryName(folder)!;

    private static bool IsVirtual(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile;
}
