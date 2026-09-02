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

    public void EnforceExclusive(
        string rootPath,
        IReadOnlyList<string> folders,
        ISet<string> expandedFolders,
        IEnumerable<string>? preferredFolders = null)
    {
        var root = Normalize(rootPath);
        var available = folders
            .Select(Normalize)
            .Append(root)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preferred = (preferredFolders ?? [])
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedExpanded = expandedFolders
            .Select(Normalize)
            .Where(available.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        expandedFolders.Clear();
        expandedFolders.Add(root);
        foreach (var group in normalizedExpanded
            .Where(folder => !folder.Equals(root, StringComparison.OrdinalIgnoreCase))
            .GroupBy(folder => ParentOf(root, folder), StringComparer.OrdinalIgnoreCase))
        {
            var selected = group.FirstOrDefault(preferred.Contains)
                ?? group.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).First();
            expandedFolders.Add(selected);
        }
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
