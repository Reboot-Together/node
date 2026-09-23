using System.Text.Json;

namespace AsterismApp;

public sealed class WorkspaceService
{
    public WorkspaceService()
    {
        var savedRoot = LoadSavedRoot();
        if (savedRoot is not null && TryPrepareRoot(savedRoot, out var availableRoot))
        {
            RootPath = availableRoot;
            return;
        }

        UnavailableRootPath = savedRoot;
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Asterism");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; private set; }
    public string? UnavailableRootPath { get; private set; }

    public void SetRootPath(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        UnavailableRootPath = null;
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

    private static bool TryPrepareRoot(string rootPath, out string availableRoot)
    {
        availableRoot = "";
        try
        {
            var fullPath = Path.GetFullPath(rootPath);
            var driveRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(driveRoot) || !Directory.Exists(driveRoot)) return false;
            Directory.CreateDirectory(fullPath);
            availableRoot = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record WorkspaceSettings(string RootPath);
}
