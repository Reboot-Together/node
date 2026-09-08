using System.Text;
using System.Text.Json;

namespace AsterismApp;

public sealed class VaultOrderService
{
    private const string OrderFileName = ".asterism-order.json";
    private string? _loadedRoot;
    private Dictionary<string, List<string>> _groups = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Order(string rootPath, string parentPath, IEnumerable<string> defaultPaths)
    {
        EnsureLoaded(rootPath);
        var paths = defaultPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pathsById = paths.ToDictionary(path => RelativeId(rootPath, path), StringComparer.OrdinalIgnoreCase);
        var parentId = RelativeId(rootPath, parentPath);
        var result = new List<string>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_groups.TryGetValue(parentId, out var saved))
            foreach (var id in saved)
                if (pathsById.TryGetValue(id, out var path) && included.Add(id))
                    result.Add(path);

        foreach (var path in paths)
            if (included.Add(RelativeId(rootPath, path)))
                result.Add(path);
        return result;
    }

    public void Reorder(
        string rootPath,
        string parentPath,
        IEnumerable<string> siblingPaths,
        string sourcePath,
        string targetPath,
        bool after)
    {
        var ordered = Order(rootPath, parentPath, siblingPaths).ToList();
        var source = ordered.FirstOrDefault(path => path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
        var target = ordered.FirstOrDefault(path => path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null || source.Equals(target, StringComparison.OrdinalIgnoreCase)) return;

        ordered.Remove(source);
        var targetIndex = ordered.FindIndex(path => path.Equals(target, StringComparison.OrdinalIgnoreCase));
        ordered.Insert(targetIndex + (after ? 1 : 0), source);
        _groups[RelativeId(rootPath, parentPath)] = ordered
            .Select(path => RelativeId(rootPath, path))
            .ToList();
        Save(rootPath);
    }

    public void RemapPath(string rootPath, string sourcePath, string destinationPath)
    {
        EnsureLoaded(rootPath);
        if (_groups.Count == 0) return;
        var sourceId = RelativeId(rootPath, sourcePath);
        var destinationId = RelativeId(rootPath, destinationPath);
        var remapped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (group, entries) in _groups)
        {
            var mappedGroup = ReplacePrefix(group, sourceId, destinationId);
            if (!remapped.TryGetValue(mappedGroup, out var mappedEntries))
                remapped[mappedGroup] = mappedEntries = [];
            foreach (var entry in entries)
            {
                var mappedEntry = ReplacePrefix(entry, sourceId, destinationId);
                if (!mappedEntries.Contains(mappedEntry, StringComparer.OrdinalIgnoreCase))
                    mappedEntries.Add(mappedEntry);
            }
        }

        _groups = NormalizeGroups(remapped);
        Save(rootPath);
    }

    public void RemovePath(string rootPath, string path)
    {
        EnsureLoaded(rootPath);
        if (_groups.Count == 0) return;
        var id = RelativeId(rootPath, path);
        _groups = _groups
            .Where(pair => !IsSameOrDescendant(pair.Key, id))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Where(entry => !IsSameOrDescendant(entry, id)).ToList(),
                StringComparer.OrdinalIgnoreCase);
        Save(rootPath);
    }

    private void EnsureLoaded(string rootPath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        if (root.Equals(_loadedRoot, StringComparison.OrdinalIgnoreCase)) return;
        _loadedRoot = root;
        _groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(root, OrderFileName);
            if (!File.Exists(path)) return;
            var state = JsonSerializer.Deserialize<OrderState>(File.ReadAllText(path));
            if (state?.Groups is null) return;
            _groups = state.Groups.ToDictionary(
                pair => NormalizeId(pair.Key),
                pair => pair.Value.Select(NormalizeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(string rootPath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.Combine(root, OrderFileName);
        var temporaryPath = Path.Combine(root, $"{OrderFileName}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(new OrderState(1, _groups), new JsonSerializerOptions { WriteIndented = true });
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                var writableAttributes = attributes & ~(FileAttributes.Hidden | FileAttributes.ReadOnly);
                if (writableAttributes != attributes) File.SetAttributes(path, writableAttributes);
            }
            File.Move(temporaryPath, path, true);
            try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
            catch { }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch { }
        }
    }

    private static Dictionary<string, List<string>> NormalizeGroups(Dictionary<string, List<string>> groups)
    {
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entries in groups.Values)
            foreach (var entry in entries)
            {
                var parent = ParentId(entry);
                if (!normalized.TryGetValue(parent, out var siblings)) normalized[parent] = siblings = [];
                if (!siblings.Contains(entry, StringComparer.OrdinalIgnoreCase)) siblings.Add(entry);
            }
        foreach (var (group, entries) in groups)
            if (entries.Count == 0 && !normalized.ContainsKey(group))
                normalized[group] = [];
        return normalized;
    }

    private static string RelativeId(string rootPath, string path)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("저장소 밖의 항목 순서는 변경할 수 없습니다.");
        var relative = Path.GetRelativePath(root, fullPath);
        return relative == "." ? "" : NormalizeId(relative);
    }

    private static string ReplacePrefix(string value, string source, string destination)
    {
        if (value.Equals(source, StringComparison.OrdinalIgnoreCase)) return destination;
        return value.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase)
            ? destination + value[source.Length..]
            : value;
    }

    private static bool IsSameOrDescendant(string value, string parent) =>
        value.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);

    private static string ParentId(string value)
    {
        var separator = value.LastIndexOf('/');
        return separator < 0 ? "" : value[..separator];
    }

    private static string NormalizeId(string value) => value.Replace('\\', '/').Trim('/');

    private sealed record OrderState(int Version, Dictionary<string, List<string>> Groups);
}
