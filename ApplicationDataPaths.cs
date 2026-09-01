namespace AsterismApp;

public static class ApplicationDataPaths
{
    public static string CurrentDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Asterism");

    public static string LegacyDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Node");

    public static string SettingsFile => Path.Combine(CurrentDirectory, "settings.json");

    public static string UiLayoutSettingsFile => Path.Combine(CurrentDirectory, "ui-layout.json");

    public static IEnumerable<string> SettingsCandidates()
    {
        yield return SettingsFile;
        yield return Path.Combine(LegacyDirectory, "settings.json");
    }

    public static string SemanticIndexFile => ExistingFileOrCurrent("semantic-index.db");

    public static string ModelDirectory
    {
        get
        {
            var current = Path.Combine(CurrentDirectory, "models");
            var legacy = Path.Combine(LegacyDirectory, "models");
            return File.Exists(Path.Combine(current, "multilingual-e5-small-qint8.onnx"))
                ? current
                : File.Exists(Path.Combine(legacy, "multilingual-e5-small-qint8.onnx"))
                    ? legacy
                    : current;
        }
    }

    public static string UpdatesDirectory => Path.Combine(CurrentDirectory, "Updates");

    private static string ExistingFileOrCurrent(string fileName)
    {
        var current = Path.Combine(CurrentDirectory, fileName);
        var legacy = Path.Combine(LegacyDirectory, fileName);
        return File.Exists(current) ? current : File.Exists(legacy) ? legacy : current;
    }
}
