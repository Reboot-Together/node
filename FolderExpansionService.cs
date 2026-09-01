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
            .GroupBy(folder => Path.GetDirectoryName(folder)!, StringComparer.OrdinalIgnoreCase))
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

        var parent = Path.GetDirectoryName(folder);
        foreach (var sibling in folders
            .Select(Normalize)
            .Where(candidate => !candidate.Equals(folder, StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(candidate)?.Equals(parent, StringComparison.OrdinalIgnoreCase) == true))
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

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
