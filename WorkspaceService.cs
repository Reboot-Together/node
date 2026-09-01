using System.Text.Json;

namespace AsterismApp;

public sealed class WorkspaceService
{
    public WorkspaceService()
    {
        RootPath = LoadSavedRoot() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Asterism");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; private set; }

    public void SetRootPath(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ApplicationDataPaths.SettingsFile)!);
        File.WriteAllText(ApplicationDataPaths.SettingsFile, JsonSerializer.Serialize(new WorkspaceSettings(RootPath)));
    }

    private static string? LoadSavedRoot()
    {
        try
        {
            foreach (var settingsPath in ApplicationDataPaths.SettingsCandidates())
            {
                if (!File.Exists(settingsPath)) continue;
                var root = JsonSerializer.Deserialize<WorkspaceSettings>(File.ReadAllText(settingsPath))?.RootPath;
                if (!string.IsNullOrWhiteSpace(root)) return root;
            }
            return null;
        }
        catch { return null; }
    }

    private sealed record WorkspaceSettings(string RootPath);
}
