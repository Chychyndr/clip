using System.Text.Json;
using System.Text;
using Clip.Core.App;
using Clip.Core.Cache;
using Clip.Core.Models;
using Clip.Core.Processes;
using Clip.Core.YtDlp;

namespace Clip.Core.Tools;

public sealed class YtDlpService
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromMinutes(2);

    private readonly ToolResolver _toolResolver;
    private readonly IExternalProcessRunner _processRunner;
    private readonly MetadataCacheService _metadataCache;
    private readonly IAppSettingsProvider _settingsProvider;
    private readonly YtDlpProgressParser _progressParser = new();

    public YtDlpService(
        ToolResolver toolResolver,
        IExternalProcessRunner processRunner,
        MetadataCacheService metadataCache,
        IAppSettingsProvider settingsProvider)
    {
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _metadataCache = metadataCache;
        _settingsProvider = settingsProvider;
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var ytDlp = ResolveRequired(ExternalTool.YtDlp);
        var result = await _processRunner.RunAsync(
            ytDlp.Path!,
            ["--version"],
            cancellationToken: cancellationToken,
            timeout: VersionTimeout);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Unable to read yt-dlp version.");
        }

        return result.StandardOutput.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
    }

    public async Task<YtDlpAnalyzeResult> AnalyzeAsync(
        string url,
        string? browserCookieSource = null,
        CancellationToken cancellationToken = default)
    {
        var ytDlp = ResolveRequired(ExternalTool.YtDlp);
        var version = await GetVersionAsync(cancellationToken);
        var cacheKey = $"browser={browserCookieSource ?? "none"}";
        var settings = _settingsProvider.Current;
        var ttl = TimeSpan.FromHours(settings.MetadataCacheTtlHours);
        if (settings.EnableMetadataCache)
        {
            var cached = await _metadataCache.TryReadAsync(url, version, cacheKey, ttl, cancellationToken);
            if (cached.Hit && cached.MetadataJson is not null)
            {
                return ParseMetadata(cached.MetadataJson, fromCache: true);
            }
        }

        var args = YtDlpCommandBuilder.BuildAnalyze(new YtDlpAnalyzeOptions
        {
            Url = url,
            BrowserCookieSource = browserCookieSource
        });

        var result = await _processRunner.RunAsync(
            ytDlp.Path!,
            args,
            cancellationToken: cancellationToken,
            timeout: MetadataTimeout);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "yt-dlp metadata analysis failed."));
        }

        var metadataJson = result.StandardOutput.Trim();
        await SaveMetadataCacheAsync(url, version, cacheKey, metadataJson, cancellationToken);
        return ParseMetadata(metadataJson, fromCache: false);
    }

    public async Task<YtDlpDownloadResult> DownloadAsync(
        YtDlpDownloadOptions options,
        Action<YtDlpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ytDlp = ResolveRequired(ExternalTool.YtDlp);
        var effectiveOptions = ResolveDownloadOptions(options);
        var args = YtDlpCommandBuilder.BuildDownload(effectiveOptions);
        var startedAt = DateTimeOffset.UtcNow;
        var outputCandidates = new List<string>();
        var outputGate = new object();

        var result = await _processRunner.RunAsync(
            ytDlp.Path!,
            args,
            workingDirectory: effectiveOptions.SaveDirectory,
            standardOutput: line =>
            {
                if (_progressParser.TryParse(line, out var parsed))
                {
                    progress?.Invoke(parsed);
                    return;
                }

                if (TryReadOutputPath(line, effectiveOptions.SaveDirectory, out var path))
                {
                    lock (outputGate)
                    {
                        outputCandidates.Add(path);
                    }
                }
            },
            standardError: line =>
            {
                if (_progressParser.TryParse(line, out var parsed))
                {
                    progress?.Invoke(parsed);
                }
            },
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "yt-dlp download failed."));
        }

        lock (outputGate)
        {
            outputCandidates.AddRange(ReadOutputPaths(result.StandardOutput, effectiveOptions.SaveDirectory));
        }

        var finalPath = ResolveDownloadedFile(outputCandidates, effectiveOptions.SaveDirectory, startedAt);
        return new YtDlpDownloadResult(finalPath, result.StandardOutput, result.StandardError);
    }

    public async Task<YtDlpDownloadResult> DownloadBatchAsync(
        YtDlpBatchDownloadOptions options,
        Action<YtDlpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ytDlp = ResolveRequired(ExternalTool.YtDlp);
        var effectiveOptions = ResolveBatchOptions(options);
        var args = YtDlpCommandBuilder.BuildBatchDownload(effectiveOptions);

        var result = await _processRunner.RunAsync(
            ytDlp.Path!,
            args,
            standardOutput: line =>
            {
                if (_progressParser.TryParse(line, out var parsed))
                {
                    progress?.Invoke(parsed);
                }
            },
            standardError: line =>
            {
                if (_progressParser.TryParse(line, out var parsed))
                {
                    progress?.Invoke(parsed);
                }
            },
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "yt-dlp batch download failed."));
        }

        return new YtDlpDownloadResult("", result.StandardOutput, result.StandardError);
    }

    private YtDlpDownloadOptions ResolveDownloadOptions(YtDlpDownloadOptions options)
    {
        var settings = _settingsProvider.Current;
        var aria2cPath = ResolveAria2cPath(settings.UseAria2c);
        return new YtDlpDownloadOptions
        {
            Url = options.Url,
            SaveDirectory = options.SaveDirectory,
            MediaMode = options.MediaMode,
            Format = options.Format,
            Resolution = options.Resolution,
            ConcurrentFragments = settings.YtDlpConcurrentFragments,
            UseAria2c = settings.UseAria2c && aria2cPath is not null,
            Aria2cPath = aria2cPath,
            BrowserCookieSource = options.BrowserCookieSource,
            OutputTemplate = options.OutputTemplate
        };
    }

    private YtDlpBatchDownloadOptions ResolveBatchOptions(YtDlpBatchDownloadOptions options)
    {
        var settings = _settingsProvider.Current;
        var aria2cPath = ResolveAria2cPath(settings.UseAria2c);
        return new YtDlpBatchDownloadOptions
        {
            BatchFilePath = options.BatchFilePath,
            SaveDirectory = options.SaveDirectory,
            MediaMode = options.MediaMode,
            Format = options.Format,
            Resolution = options.Resolution,
            ConcurrentFragments = settings.YtDlpConcurrentFragments,
            UseAria2c = settings.UseAria2c && aria2cPath is not null,
            Aria2cPath = aria2cPath,
            BrowserCookieSource = options.BrowserCookieSource,
            OutputTemplate = options.OutputTemplate
        };
    }

    private string? ResolveAria2cPath(bool requested)
    {
        if (!requested)
        {
            return null;
        }

        var aria2c = _toolResolver.Resolve(ExternalTool.Aria2c);
        return aria2c.IsFound ? aria2c.Path : null;
    }

    private ExternalToolResolution ResolveRequired(ExternalTool tool)
    {
        var resolved = _toolResolver.Resolve(tool);
        if (!resolved.IsFound || resolved.Path is null)
        {
            throw new FileNotFoundException($"{resolved.DisplayName} was not found.", resolved.DisplayName);
        }

        return resolved;
    }

    private static YtDlpAnalyzeResult ParseMetadata(string metadataJson, bool fromCache)
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>(metadataJson) ?? new VideoMetadata();
        metadata.IsFromCache = fromCache;
        return new YtDlpAnalyzeResult(metadata, metadataJson, fromCache);
    }

    private async Task SaveMetadataCacheAsync(
        string url,
        string version,
        string cacheKey,
        string metadataJson,
        CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.Current;
        if (!settings.EnableMetadataCache)
        {
            return;
        }

        var maxBytes = settings.MaxMetadataCacheFileKilobytes * 1024L;
        if (Encoding.UTF8.GetByteCount(metadataJson) > maxBytes)
        {
            return;
        }

        await _metadataCache.PruneAsync(TimeSpan.FromHours(settings.MetadataCacheTtlHours), maxBytes, cancellationToken);
        await _metadataCache.SaveAsync(url, version, cacheKey, metadataJson, cancellationToken);
    }

    private static string FirstErrorLine(string text, string fallback) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? fallback;

    private static string ResolveDownloadedFile(
        IEnumerable<string> outputCandidates,
        string saveDirectory,
        DateTimeOffset startedAt)
    {
        var finalPath = outputCandidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Reverse()
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(finalPath))
        {
            return finalPath;
        }

        return FindNewestMediaFile(saveDirectory, startedAt)
            ?? throw new FileNotFoundException($"The download finished, but Clip could not locate the output file in {saveDirectory}.");
    }

    private static IEnumerable<string> ReadOutputPaths(string text, string saveDirectory)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryReadOutputPath(line, saveDirectory, out var path))
            {
                yield return path;
            }
        }
    }

    private static bool TryReadOutputPath(string line, string saveDirectory, out string path)
    {
        path = line.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("[", StringComparison.Ordinal) ||
            path.StartsWith("download:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            path.Contains('|'))
        {
            path = "";
            return false;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            path = Path.Combine(saveDirectory, path);
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindNewestMediaFile(string directory, DateTimeOffset startedAt)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".webm", ".mp3", ".m4a", ".mkv"
        };

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc >= startedAt.UtcDateTime.AddMinutes(-10))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }
}

public sealed record YtDlpAnalyzeResult(VideoMetadata Metadata, string MetadataJson, bool IsFromCache);

public sealed record YtDlpDownloadResult(string? OutputPath, string StandardOutput, string StandardError);
