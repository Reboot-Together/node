using System.Text.Json;

namespace AsterismApp;

public sealed record UiLayoutSettings(
    double PreviewRatio,
    bool ExplorerCollapsed,
    double InspectorWidth = 348,
    double FontScale = 1,
    string AccentTheme = "gold",
    string SurfaceTheme = "dark")
{
    public static UiLayoutSettings Default { get; } = new(.68, false, 348, 1, "gold", "dark");
}

public sealed class UiLayoutSettingsService
{
    private readonly string _settingsPath;

    public UiLayoutSettingsService(string? settingsPath = null) =>
        _settingsPath = settingsPath ?? ApplicationDataPaths.UiLayoutSettingsFile;

    public UiLayoutSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return UiLayoutSettings.Default;
            var settings = JsonSerializer.Deserialize<UiLayoutSettings>(File.ReadAllText(_settingsPath));
            return settings is null ? UiLayoutSettings.Default : Normalize(settings);
        }
        catch
        {
            return UiLayoutSettings.Default;
        }
    }

    public void Save(UiLayoutSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Normalize(settings)));
    }

    private static UiLayoutSettings Normalize(UiLayoutSettings settings) =>
        settings with
        {
            PreviewRatio = Math.Clamp(settings.PreviewRatio, .3, .85),
            InspectorWidth = Math.Clamp(settings.InspectorWidth <= 0 ? 348 : settings.InspectorWidth, 240, 720),
            FontScale = Math.Clamp(settings.FontScale <= 0 ? 1 : settings.FontScale, .8, 1.4),
            AccentTheme = new[] { "gold", "blue", "teal", "purple" }.Contains(
                settings.AccentTheme,
                StringComparer.OrdinalIgnoreCase)
                    ? settings.AccentTheme.ToLowerInvariant()
                    : "gold",
            SurfaceTheme = new[] { "dark", "light", "midnight" }.Contains(
                settings.SurfaceTheme,
                StringComparer.OrdinalIgnoreCase)
                    ? settings.SurfaceTheme.ToLowerInvariant()
                    : "dark"
        };
}
