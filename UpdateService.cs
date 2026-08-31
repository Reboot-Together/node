using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NodeApp;

public sealed record NodeRelease(
    Version Version,
    string Tag,
    string Title,
    string Notes,
    Uri AssetUri,
    string Sha256,
    Uri ReleaseUri,
    DateTimeOffset PublishedAt)
{
    public string DisplayName => $"v{Version.ToString(3)} · {Title}";
}

public sealed class UpdateService
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/Reboot-Together/node/releases?per_page=20";
    private const string AllowedAssetPrefix =
        "https://github.com/Reboot-Together/node/releases/download/";

    private readonly HttpClient _client;

    public UpdateService()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Node-Desktop-Updater/1.0");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _client.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
    }

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null
                ? new Version(0, 0, 0)
                : new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public async Task<IReadOnlyList<NodeRelease>> GetStableReleasesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(
            $"{ReleasesApiUrl}&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub 릴리스 목록 형식이 올바르지 않습니다.");

        var releases = new List<NodeRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) continue;
            if (!release.TryGetProperty("tag_name", out var tagElement)) continue;

            var tag = tagElement.GetString() ?? "";
            if (!TryParseVersion(tag, out var version)
                || !release.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array)
                continue;

            JsonElement? installerAsset = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                if (name?.EndsWith("-Setup-win-x64.exe", StringComparison.OrdinalIgnoreCase) == true)
                {
                    installerAsset = asset;
                    break;
                }
            }
            if (installerAsset is null) continue;

            var assetUrl = installerAsset.Value.TryGetProperty(
                "browser_download_url", out var assetUrlElement)
                ? assetUrlElement.GetString() ?? ""
                : "";
            var digest = installerAsset.Value.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString() ?? ""
                : "";
            var sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest[7..]
                : "";
            var releaseUrl = release.TryGetProperty("html_url", out var releaseUrlElement)
                ? releaseUrlElement.GetString() ?? ""
                : "";

            if (!IsSha256(sha256)
                || !assetUrl.StartsWith(AllowedAssetPrefix, StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri)
                || assetUri.Scheme != Uri.UriSchemeHttps
                || !Uri.TryCreate(releaseUrl, UriKind.Absolute, out var releaseUri)
                || releaseUri.Scheme != Uri.UriSchemeHttps)
                continue;

            var title = release.TryGetProperty("name", out var titleElement)
                ? titleElement.GetString()
                : null;
            var notes = release.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString()
                : null;
            var publishedAt = release.TryGetProperty("published_at", out var dateElement)
                && dateElement.TryGetDateTimeOffset(out var date)
                    ? date
                    : DateTimeOffset.MinValue;

            releases.Add(new NodeRelease(
                version,
                tag,
                string.IsNullOrWhiteSpace(title) ? $"Node {tag}" : title,
                string.IsNullOrWhiteSpace(notes) ? "등록된 변경 사항이 없습니다." : notes.Trim(),
                assetUri,
                sha256,
                releaseUri,
                publishedAt));
        }

        return releases.OrderByDescending(release => release.Version).ToArray();
    }

    public async Task PrepareInstallationAsync(
        NodeRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!release.AssetUri.AbsoluteUri.StartsWith(AllowedAssetPrefix, StringComparison.OrdinalIgnoreCase)
            || release.AssetUri.Scheme != Uri.UriSchemeHttps
            || !IsSha256(release.Sha256))
            throw new InvalidDataException("허용되지 않은 업데이트 파일입니다.");

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)
            || !Path.GetFileName(executablePath).Equals("Node.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("실행 중인 Node.exe를 확인하지 못했습니다.");

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Node",
            "Updates",
            release.Version.ToString(3));
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, "Node-Setup.exe");
        var temporaryPath = installerPath + ".download";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

        try
        {
            using var response = await _client.GetAsync(
                release.AssetUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    if (totalBytes is > 0)
                        progress?.Report((int)(downloaded * 100 / totalBytes.Value));
                }
            }

            var actualHash = await ComputeSha256Async(temporaryPath, cancellationToken);
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("업데이트 파일의 SHA-256 검증에 실패했습니다.");

            File.Move(temporaryPath, installerPath, true);
            progress?.Report(100);

            var scriptPath = Path.Combine(updateDirectory, "install-update.ps1");
            var logPath = Path.Combine(updateDirectory, "install-error.log");
            var installedExecutablePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Node",
                "Node.exe");
            await File.WriteAllTextAsync(
                scriptPath,
                BuildInstallerScript(Environment.ProcessId, installerPath, installedExecutablePath, logPath),
                Encoding.Unicode,
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = updateDirectory
            };
            foreach (var argument in new[]
            {
                "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-WindowStyle", "Hidden", "-File", scriptPath
            })
                startInfo.ArgumentList.Add(argument);

            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("업데이트 설치 프로세스를 시작하지 못했습니다.");
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        if (Version.TryParse(normalized, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        version = new Version();
        return false;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(await sha256.ComputeHashAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64) return false;
        try
        {
            return Convert.FromHexString(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BuildInstallerScript(
        int processId,
        string installerPath,
        string installedExecutablePath,
        string logPath) => $$"""
        $ErrorActionPreference = 'Stop'
        $installerPath = '{{PowerShellQuote(installerPath)}}'
        $installedExecutablePath = '{{PowerShellQuote(installedExecutablePath)}}'
        $logPath = '{{PowerShellQuote(logPath)}}'

        try {
            Wait-Process -Id {{processId}} -Timeout 60 -ErrorAction SilentlyContinue
            $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS', ('/LOG="' + $logPath + '"'))
            $installer = Start-Process -FilePath $installerPath -ArgumentList $arguments -Wait -PassThru
            if ($installer.ExitCode -ne 0) {
                throw "설치 프로그램이 종료 코드 $($installer.ExitCode)를 반환했습니다."
            }
            if (-not (Test-Path -LiteralPath $installedExecutablePath)) {
                throw '설치 후 Node.exe를 찾지 못했습니다.'
            }
            Start-Process -FilePath $installedExecutablePath -WorkingDirectory (Split-Path $installedExecutablePath)
        }
        catch {
            [System.IO.File]::WriteAllText($logPath, ($_ | Out-String))
        }
        """;

    private static string PowerShellQuote(string value) => value.Replace("'", "''");
}
