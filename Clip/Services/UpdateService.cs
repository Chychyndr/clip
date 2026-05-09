using System.Net.Http.Headers;
using System.Text.Json;
using Clip.Core.Cache;
using Clip.Core.Tools;

namespace Clip.Services;

public sealed class UpdateService
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);
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
        YtDlpReleaseSecurity.ConfigureHttpClient(_httpClient);
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Clip", "1.0"));
    }

    public async Task<string?> CheckForYtDlpUpdateAsync(
        CancellationToken cancellationToken,
        bool includeUpToDateStatus = false)
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

        var location = resolved.IsFromPath
            ? $"External PATH executable: {resolved.Path}"
            : $"Bundled executable: {resolved.Path}";

        if (string.Equals(local, latest, StringComparison.OrdinalIgnoreCase))
        {
            return includeUpToDateStatus ? $"yt-dlp is present and up to date. {location}" : null;
        }

        return $"yt-dlp {latest} is available. Current version: {local}. {location}";
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

            var assetUrl = YtDlpReleaseSecurity.SelectWindowsBinaryUrl(release.Assets);
            if (string.IsNullOrWhiteSpace(assetUrl))
            {
                return new YtDlpUpdateResult(false, "No compatible yt-dlp binary was found in the latest release.");
            }

            var checksumUrl = YtDlpReleaseSecurity.SelectChecksumUrl(release.Assets);
            if (string.IsNullOrWhiteSpace(checksumUrl))
            {
                return new YtDlpUpdateResult(false, "No yt-dlp checksum file was found in the latest release.");
            }

            var existing = _toolResolver.Resolve(ExternalTool.YtDlp);
            var targetPath = existing is { IsFound: true, IsFromPath: false, Path: not null }
                ? existing.Path
                : _toolResolver.GetPreferredBundledPath(ExternalTool.YtDlp);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ClipConstants.BinDirectory);
            var tempPath = targetPath + ".download";
            var backupPath = targetPath + ".bak";

            status?.Report("Downloading update");
            try
            {
                var bytes = await YtDlpReleaseSecurity.DownloadVerifiedAssetAsync(
                    _httpClient,
                    "yt-dlp.exe",
                    assetUrl,
                    checksumUrl,
                    cancellationToken);
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

                status?.Report("Verifying file");
                using var verificationTimeout = new CancellationTokenSource(YtDlpReleaseSecurity.VerificationTimeout);
                using var verificationToken = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    verificationTimeout.Token);
                var verification = await _processRunner.RunAsync(
                    tempPath,
                    ["--version"],
                    cancellationToken: verificationToken.Token,
                    timeout: YtDlpReleaseSecurity.VerificationTimeout);
                if (!verification.IsSuccess)
                {
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
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
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
                cancellationToken: cancellationToken,
                timeout: VersionTimeout);
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
            using var response = await _httpClient.GetAsync(YtDlpReleaseSecurity.LatestReleaseApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagProperty)
                ? tagProperty.GetString() ?? ""
                : "";

            var assets = new List<YtDlpReleaseAsset>();
            if (document.RootElement.TryGetProperty("assets", out var assetsProperty))
            {
                foreach (var asset in assetsProperty.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? "" : "";
                    var url = asset.TryGetProperty("browser_download_url", out var urlProperty) ? urlProperty.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    {
                        assets.Add(new YtDlpReleaseAsset(name, url));
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

    private sealed record YtDlpRelease(string TagName, IReadOnlyList<YtDlpReleaseAsset> Assets);
}

public sealed record YtDlpUpdateResult(bool Success, string Message);
