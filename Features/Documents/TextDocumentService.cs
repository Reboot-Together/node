using System.Text;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using RecycleOption = Microsoft.VisualBasic.FileIO.RecycleOption;
using UIOption = Microsoft.VisualBasic.FileIO.UIOption;

namespace AsterismApp;

public sealed class TextDocumentService
{
    public TextDocumentService(string rootPath) => SetRootPath(rootPath);

    public string RootPath { get; private set; } = "";

    public void SetRootPath(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(RootPath);
    }

    public List<NoteInfo> Load()
    {
        try
        {
            return Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories)
                .Where(IsTextFile)
                .Where(path => !IsFallbackTrashPath(path))
                .Select(TryRead)
                .OfType<NoteInfo>()
                .OrderByDescending(text => text.LastWriteTime)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public NoteInfo Create(string directory, string requestedTitle)
    {
        var destination = UniqueDestination(ValidateDirectoryPath(directory), ValidateFileName(requestedTitle) + ".txt");
        File.WriteAllText(destination, "", new UTF8Encoding(false));
        return Read(destination);
    }

    public NoteInfo Save(string path, string body)
    {
        var source = ValidateTextPath(path);
        File.WriteAllText(source, body, new UTF8Encoding(false));
        return Read(source);
    }

    public NoteInfo Rename(string path, string requestedName)
    {
        var source = ValidateTextPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("TXT 파일을 찾을 수 없습니다.", source);
        var destination = Path.Combine(Path.GetDirectoryName(source)!, ValidateFileName(requestedName) + ".txt");
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)) return Read(source);
        if (File.Exists(destination)) throw new IOException("같은 이름의 TXT 파일이 이미 있습니다.");
        File.Move(source, destination);
        return Read(destination);
    }

    public NoteInfo Move(string path, string destinationDirectory)
    {
        var source = ValidateTextPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("TXT 파일을 찾을 수 없습니다.", source);
        var destinationFolder = ValidateDirectoryPath(destinationDirectory);
        if (Path.GetDirectoryName(source)?.Equals(destinationFolder, StringComparison.OrdinalIgnoreCase) == true)
            return Read(source);

        var destination = UniqueDestination(destinationFolder, Path.GetFileName(source));
        File.Move(source, destination);
        return Read(destination);
    }

    public void MoveToTrash(string path)
    {
        var source = ValidateTextPath(path);
        if (!File.Exists(source)) return;
        try
        {
            FileSystem.DeleteFile(source, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch
        {
            var trashDirectory = Path.Combine(RootPath, ".trash");
            Directory.CreateDirectory(trashDirectory);
            var destination = UniqueDestination(
                trashDirectory,
                $"{Path.GetFileNameWithoutExtension(source)} {DateTime.Now:yyyyMMdd-HHmmssfff}.txt");
            File.Move(source, destination);
        }
    }

    public static bool IsTextFile(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

    private NoteInfo Read(string path) => new(
        Path.GetFileNameWithoutExtension(path),
        Path.GetFullPath(path),
        File.ReadAllText(path),
        File.GetLastWriteTime(path),
        new NoteMetadata("Text", File.GetCreationTime(path), "File", "Text"),
        IsPlainText: true);

    private NoteInfo? TryRead(string path)
    {
        try { return Read(path); }
        catch { return null; }
    }

    private string ValidateTextPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsTextFile(fullPath) || !IsInsideRoot(fullPath))
            throw new InvalidOperationException("현재 저장소 안의 TXT 파일만 변경할 수 있습니다.");
        return fullPath;
    }

    private string ValidateDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!IsInsideRoot(fullPath)) throw new InvalidOperationException("현재 저장소 밖에 TXT 파일을 만들 수 없습니다.");
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("대상 폴더를 찾을 수 없습니다.");
        return fullPath;
    }

    private bool IsInsideRoot(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return fullPath.Equals(RootPath, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateFileName(string requestedName)
    {
        var name = Path.GetFileNameWithoutExtension(requestedName.Trim());
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("TXT 이름에 사용할 수 없는 문자가 있습니다.");
        return name.TrimEnd('.');
    }

    private static string UniqueDestination(string directory, string fileName)
    {
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination)) return destination;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            destination = Path.Combine(directory, $"{stem} {index}{extension}");
            if (!File.Exists(destination)) return destination;
        }
    }

    private bool IsFallbackTrashPath(string path)
    {
        var relative = Path.GetRelativePath(RootPath, path);
        return relative.Equals(".trash", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith($".trash{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
