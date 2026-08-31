using System.Text.Json;

namespace NodeApp;

public sealed class WorkspaceService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Node",
        "settings.json");

    public WorkspaceService()
    {
        RootPath = LoadSavedRoot() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Node");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; private set; }

    public void SetRootPath(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new WorkspaceSettings(RootPath)));
    }

    private static string? LoadSavedRoot()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var root = JsonSerializer.Deserialize<WorkspaceSettings>(File.ReadAllText(SettingsPath))?.RootPath;
            return string.IsNullOrWhiteSpace(root) ? null : root;
        }
        catch { return null; }
    }

    private sealed record WorkspaceSettings(string RootPath);
}
