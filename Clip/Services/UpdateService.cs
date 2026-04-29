using System.Net.Http.Headers;
using System.Text.Json;
using Clip.Core.Cache;
using Clip.Core.Tools;

namespace Clip.Services;

public sealed class UpdateService
{
    private readonly ProcessRunner _processRunner;
    private readonly ToolResolver _toolResolver;
    private readonly MetadataCacheService _metadataCache;
    private readonly HttpClient _httpClient;

    public UpdateService(
        ProcessRunner processRunner,
        ToolResolver toolResolver,
        MetadataCacheService metadataCache,
        HttpClient? httpClient = null)
    {
        _processRunner = processRunner;
        _toolResolver = toolResolver;
        _metadataCache = metadataCache;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Clip", "1.0"));
    }

    public async Task<string?> CheckForYtDlpUpdateAsync(CancellationToken cancellationToken)
    {
        var resolved = _toolResolver.Resolve(ExternalTool.YtDlp);
        if (!resolved.IsFound || resolved.Path is null)
        {
            return "yt-dlp is missing.";
        }

        var local = await ReadLocalVersionAsync(resolved.Path, cancellationToken);
        var latest = await ReadLatestVersionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(latest))
        {
            return null;
        }

        return string.Equals(local, latest, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"yt-dlp {latest} is available. Bundled version: {local}.";
    }

    public async Task<YtDlpUpdateResult> UpdateYtDlpAsync(IProgress<string>? status, CancellationToken cancellationToken)
    {
        try
        {
            status?.Report("Checking version");
            var release = await ReadLatestReleaseAsync(cancellationToken);
            if (release is null)
            {
                return new YtDlpUpdateResult(false, "Could not read the latest yt-dlp release.");
            }

            var assetUrl = SelectAssetUrl(release);
            if (string.IsNullOrWhiteSpace(assetUrl))
            {
                return new YtDlpUpdateResult(false, "No compatible yt-dlp binary was found in the latest release.");
            }

            var existing = _toolResolver.Resolve(ExternalTool.YtDlp);
            var targetPath = existing is { IsFound: true, IsFromPath: false, Path: not null }
                ? existing.Path
                : _toolResolver.GetPreferredBundledPath(ExternalTool.YtDlp);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ClipConstants.BinDirectory);
            var tempPath = targetPath + ".download";
            var backupPath = targetPath + ".bak";

            status?.Report("Downloading update");
            await using (var stream = await _httpClient.GetStreamAsync(assetUrl, cancellationToken))
            await using (var file = File.Create(tempPath))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }

            if (_toolResolver.Platform.IsMacOS && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tempPath, File.GetUnixFileMode(tempPath) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            status?.Report("Verifying file");
            var verification = await _processRunner.RunAsync(tempPath, ["--version"], cancellationToken: cancellationToken);
            if (!verification.IsSuccess)
            {
                File.Delete(tempPath);
                return new YtDlpUpdateResult(false, "Downloaded yt-dlp did not start correctly.");
            }

            if (File.Exists(targetPath))
            {
                File.Copy(targetPath, backupPath, overwrite: true);
            }

            try
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, targetPath, overwrite: true);
                }

                throw;
            }

            _metadataCache.Clear();
            status?.Report("Done");
            return new YtDlpUpdateResult(true, $"yt-dlp updated to {verification.StandardOutput.Trim()}.");
        }
        catch (Exception ex)
        {
            status?.Report("Update failed");
            return new YtDlpUpdateResult(false, ex.Message);
        }
    }

    private async Task<string?> ReadLocalVersionAsync(string ytDlpPath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                ytDlpPath,
                ["--version"],
                cancellationToken: cancellationToken);
            return result.IsSuccess ? result.StandardOutput.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ReadLatestVersionAsync(CancellationToken cancellationToken)
    {
        var release = await ReadLatestReleaseAsync(cancellationToken);
        return release?.TagName;
    }

    private async Task<YtDlpRelease?> ReadLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagProperty)
                ? tagProperty.GetString() ?? ""
                : "";

            var assets = new List<YtDlpAsset>();
            if (document.RootElement.TryGetProperty("assets", out var assetsProperty))
            {
                foreach (var asset in assetsProperty.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? "" : "";
                    var url = asset.TryGetProperty("browser_download_url", out var urlProperty) ? urlProperty.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    {
                        assets.Add(new YtDlpAsset(name, url));
                    }
                }
            }

            return new YtDlpRelease(tag, assets);
        }
        catch
        {
            return null;
        }
    }

    private string? SelectAssetUrl(YtDlpRelease release)
    {
        string[] preferredNames;
        if (_toolResolver.Platform.IsWindows)
        {
            preferredNames = ["yt-dlp.exe"];
        }
        else if (_toolResolver.Platform.IsMacOS)
        {
            preferredNames = ["yt-dlp_macos", "yt-dlp"];
        }
        else
        {
            preferredNames = ["yt-dlp"];
        }

        foreach (var preferred in preferredNames)
        {
            var asset = release.Assets.FirstOrDefault(item => item.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (asset is not null)
            {
                return asset.DownloadUrl;
            }
        }

        return null;
    }

    private sealed record YtDlpRelease(string TagName, IReadOnlyList<YtDlpAsset> Assets);
    private sealed record YtDlpAsset(string Name, string DownloadUrl);
}

public sealed record YtDlpUpdateResult(bool Success, string Message);
