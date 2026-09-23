using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using RecycleOption = Microsoft.VisualBasic.FileIO.RecycleOption;
using UIOption = Microsoft.VisualBasic.FileIO.UIOption;

namespace AsterismApp;

public sealed class NoteRepository
{
    private static readonly Regex Heading = new("^#\\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

    public NoteRepository(string rootPath) => SetRootPath(rootPath);

    public string RootPath { get; private set; } = "";

    public void SetRootPath(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
    }

    public List<NoteInfo> Load() => Directory.EnumerateFiles(RootPath, "*.md", SearchOption.AllDirectories)
        .Where(path => !IsFallbackTrashPath(path))
        .Select(Read)
        .OrderByDescending(note => note.LastWriteTime)
        .ToList();

    public NoteInfo Create(string? suggestedTitle = null, NoteMetadata? metadata = null)
        => CreateInFolder(RootPath, suggestedTitle, metadata);

    public NoteInfo CreateInFolder(string directory, string? suggestedTitle = null, NoteMetadata? metadata = null)
    {
        var parent = ValidateDirectoryPath(directory);
        var title = suggestedTitle ?? $"새 노트 {DateTime.Now:yyyy-MM-dd HHmm}";
        return Save(Path.Combine(parent, SafeFileName(title) + ".md"), title, "", metadata ?? NoteMetadata.Manual);
    }

    public NoteInfo Save(string? originalPath, string title, string body, NoteMetadata metadata, string? previousTitle = null)
    {
        title = MarkdownText.NormalizeTitle(title);
        if (previousTitle is null || !title.Equals(MarkdownText.NormalizeTitle(previousTitle), StringComparison.OrdinalIgnoreCase))
            title = UniqueTitle(title, originalPath);
        body = MarkdownText.NormalizeNewlines(body).Trim();
        var path = originalPath ?? Path.Combine(RootPath, SafeFileName(title) + ".md");
        metadata = metadata with { Created = metadata.Created == default ? DateTime.Today : metadata.Created };
        File.WriteAllText(path, Serialize(title, body, metadata), new UTF8Encoding(false));
        return new NoteInfo(title, path, body, File.GetLastWriteTime(path), metadata);
    }

    public void MoveToTrash(string path)
    {
        var fullPath = ValidateNotePath(path);
        if (!File.Exists(fullPath)) return;
        try
        {
            FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch
        {
            var trashDirectory = Path.Combine(RootPath, ".trash");
            Directory.CreateDirectory(trashDirectory);
            var name = Path.GetFileNameWithoutExtension(fullPath);
            var destination = Path.Combine(trashDirectory, $"{name} {DateTime.Now:yyyyMMdd-HHmmssfff}.md");
            File.Move(fullPath, destination);
        }
    }

    public NoteInfo Rename(string path, string requestedTitle)
    {
        var fullPath = ValidateNotePath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("노트 파일을 찾을 수 없습니다.", fullPath);

        var note = Read(fullPath);
        var title = UniqueTitle(MarkdownText.NormalizeTitle(requestedTitle), fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var destination = Path.Combine(directory, SafeFileName(title) + ".md");
        if (!destination.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(destination)) throw new IOException("같은 이름의 노트 파일이 이미 있습니다.");
            File.Move(fullPath, destination);
        }

        return Save(destination, title, note.Body, note.Metadata);
    }

    public string CreateFolder(string parentDirectory, string requestedName)
    {
        var parent = ValidateDirectoryPath(parentDirectory);
        var name = ValidateFolderName(requestedName);

        var destination = Path.Combine(parent, name);
        if (Directory.Exists(destination))
        {
            for (var index = 2; ; index++)
            {
                destination = Path.Combine(parent, $"{name} {index}");
                if (!Directory.Exists(destination)) break;
            }
        }
        Directory.CreateDirectory(destination);
        return destination;
    }

    public string RenameFolder(string path, string requestedName)
    {
        var source = ValidateDirectoryPath(path);
        EnsureNotRoot(source);
        var name = ValidateFolderName(requestedName);
        var destination = Path.Combine(Path.GetDirectoryName(source)!, name);
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)) return source;
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("같은 이름의 항목이 이미 있습니다.");
        Directory.Move(source, destination);
        return destination;
    }

    public string MoveFolder(string path, string destinationDirectory)
    {
        var source = ValidateDirectoryPath(path);
        EnsureNotRoot(source);
        var parent = ValidateDirectoryPath(destinationDirectory);
        var sourcePrefix = source + Path.DirectorySeparatorChar;
        if (parent.Equals(source, StringComparison.OrdinalIgnoreCase)
            || parent.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("폴더를 자기 자신이나 하위 폴더로 이동할 수 없습니다.");

        var destination = Path.Combine(parent, Path.GetFileName(source));
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)) return source;
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("대상 폴더에 같은 이름의 항목이 이미 있습니다.");
        Directory.Move(source, destination);
        return destination;
    }

    public void MoveFolderToTrash(string path)
    {
        var source = ValidateDirectoryPath(path);
        EnsureNotRoot(source);
        try
        {
            FileSystem.DeleteDirectory(source, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch
        {
            var trashDirectory = Path.Combine(RootPath, ".trash");
            Directory.CreateDirectory(trashDirectory);
            var destination = Path.Combine(trashDirectory, $"{Path.GetFileName(source)} {DateTime.Now:yyyyMMdd-HHmmssfff}");
            Directory.Move(source, destination);
        }
    }

    public NoteInfo Move(string path, string destinationDirectory)
    {
        var fullPath = ValidateNotePath(path);
        var fullRoot = RootPrefix();
        var fullDestination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var destinationPrefix = fullDestination + Path.DirectorySeparatorChar;
        if (!(destinationPrefix.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || fullDestination.Equals(RootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("저장소 밖으로 이동할 수 없습니다.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("노트 파일을 찾을 수 없습니다.", fullPath);
        Directory.CreateDirectory(fullDestination);
        var destination = Path.Combine(fullDestination, Path.GetFileName(fullPath));
        if (destination.Equals(fullPath, StringComparison.OrdinalIgnoreCase)) return Read(fullPath);
        if (File.Exists(destination))
        {
            var stem = Path.GetFileNameWithoutExtension(fullPath);
            for (var index = 2; ; index++)
            {
                destination = Path.Combine(fullDestination, $"{stem} {index}.md");
                if (!File.Exists(destination)) break;
            }
        }
        File.Move(fullPath, destination);
        return Read(destination);
    }

    private NoteInfo Read(string path)
    {
        var raw = MarkdownText.NormalizeNewlines(File.ReadAllText(path));
        var (metadata, content) = Parse(raw, File.GetCreationTime(path));
        var title = MarkdownText.NormalizeTitle(metadata.Title ?? (Heading.Match(content) is { Success: true } heading ? heading.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(path)));
        content = RemoveSerializedTitleHeading(content, title);
        return new NoteInfo(title, path, content, File.GetLastWriteTime(path), metadata.ToMetadata());
    }

    private static string RemoveSerializedTitleHeading(string content, string title)
    {
        var match = Regex.Match(content, "\\A#\\s+([^\\n]+)(?:\\n{1,2}|\\z)");
        if (!match.Success || !MarkdownText.NormalizeTitle(match.Groups[1].Value).Equals(title, StringComparison.Ordinal))
            return content.Trim();

        return content[match.Length..].Trim();
    }

    private string UniqueTitle(string title, string? originalPath = null)
    {
        var existing = Load().Where(note => !string.Equals(note.Path, originalPath, StringComparison.OrdinalIgnoreCase)).Select(note => note.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(title)) return title;
        for (var index = 2; ; index++) if (!existing.Contains($"{title} {index}")) return $"{title} {index}";
    }

    private static string Serialize(string title, string body, NoteMetadata metadata) => $"---\ntitle: {title}\ncategory: {metadata.Category}\ncreated: {metadata.Created:yyyy-MM-dd}\nsource: {metadata.Source}\ntype: {metadata.Type}\n---\n\n# {title}\n\n{MarkdownText.NormalizeNewlines(body).Trim()}\n";

    private static (FrontMatter Metadata, string Content) Parse(string raw, DateTime created)
    {
        var metadata = new FrontMatter { Created = created };
        if (!raw.StartsWith("---", StringComparison.Ordinal)) return (metadata, raw);
        var close = raw.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (close < 0) return (metadata, raw);
        foreach (var line in raw[3..close].Split('\n'))
        {
            var split = line.IndexOf(':');
            if (split < 0) continue;
            var value = line[(split + 1)..].Trim();
            switch (line[..split].Trim().ToLowerInvariant())
            {
                case "title": metadata.Title = value; break;
                case "category": metadata.Category = value; break;
                case "created" when DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date): metadata.Created = date; break;
                case "source": metadata.Source = value; break;
                case "type": metadata.Type = value; break;
            }
        }
        return (metadata, raw[(close + 4)..].TrimStart('\r', '\n'));
    }

    private string ValidateNotePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(RootPrefix(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("저장소 밖의 파일은 변경할 수 없습니다.");
        return fullPath;
    }

    private string ValidateDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = RootPath.TrimEnd(Path.DirectorySeparatorChar);
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(RootPrefix(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("저장소 밖에는 폴더를 만들 수 없습니다.");
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("대상 폴더를 찾을 수 없습니다.");
        return fullPath;
    }

    private void EnsureNotRoot(string path)
    {
        if (path.Equals(RootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("저장소 루트 폴더는 변경할 수 없습니다.");
    }

    private static string ValidateFolderName(string requestedName)
    {
        var name = requestedName.Trim();
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("폴더 이름에 사용할 수 없는 문자가 있습니다.");
        return name;
    }

    private string RootPrefix() => Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private static string SafeFileName(string title) => string.Concat(title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim().TrimEnd('.');
    private bool IsFallbackTrashPath(string path) => Path.GetRelativePath(RootPath, path) is var relative && (relative.Equals(".trash", StringComparison.OrdinalIgnoreCase) || relative.StartsWith($".trash{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private sealed class FrontMatter
    {
        public string? Title { get; set; }
        public string Category { get; set; } = "Inbox";
        public DateTime Created { get; set; }
        public string Source { get; set; } = "Manual";
        public string Type { get; set; } = "Note";
        public NoteMetadata ToMetadata() => new(Category, Created == default ? DateTime.Today : Created, Source, Type);
    }
}
