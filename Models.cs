namespace AsterismApp;

public sealed record NoteMetadata(string Category, DateTime Created, string Source, string Type)
{
    public static NoteMetadata Manual => new("Inbox", DateTime.Today, "Manual", "Note");
}

public sealed record NoteInfo(string Title, string Path, string Body, DateTime LastWriteTime, NoteMetadata Metadata, bool IsReadOnly = false);

public sealed record SemanticSuggestion(NoteInfo Note, double Score)
{
    public string ScoreText => $"{Score:P0}";
}

public sealed record VaultItem(
    string Name,
    string Path,
    bool IsFolder,
    bool IsRoot,
    bool IsExpanded,
    int Depth,
    NoteInfo? Note,
    string OrderNumber = "")
{
    public string DisplayName => OrderNumber.Length == 0 ? Name : $"{OrderNumber}  {Name}";
    public double Indent => Depth * 14;
    public double CollapsedChevronOpacity => IsFolder && !IsRoot && !IsExpanded ? 1 : 0;
    public double ExpandedChevronOpacity => IsFolder && !IsRoot && IsExpanded ? 1 : 0;
    public double NoteDotOpacity => IsFolder ? 0 : 1;
    public double FolderIconOpacity => IsFolder ? 1 : 0;
    public bool IsVirtual => Path.StartsWith("asterism-guide://", StringComparison.OrdinalIgnoreCase);
}
