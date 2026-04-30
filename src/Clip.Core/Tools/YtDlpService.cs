using System.Text.Json;
using Clip.Core.App;
using Clip.Core.Cache;
using Clip.Core.Models;
using Clip.Core.Processes;
using Clip.Core.YtDlp;

namespace Clip.Core.Tools;

public sealed class YtDlpService
{
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
        var result = await _processRunner.RunAsync(ytDlp.Path!, ["--version"], cancellationToken: cancellationToken);
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
        var ttl = TimeSpan.FromHours(_settingsProvider.Current.MetadataCacheTtlHours);
        var cached = await _metadataCache.TryReadAsync(url, version, cacheKey, ttl, cancellationToken);
        if (cached.Hit && cached.MetadataJson is not null)
        {
            return ParseMetadata(cached.MetadataJson, fromCache: true);
        }

        var args = YtDlpCommandBuilder.BuildAnalyze(new YtDlpAnalyzeOptions
        {
            Url = url,
            BrowserCookieSource = browserCookieSource
        });

        var result = await _processRunner.RunAsync(ytDlp.Path!, args, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "yt-dlp metadata analysis failed."));
        }

        var metadataJson = result.StandardOutput.Trim();
        await _metadataCache.SaveAsync(url, version, cacheKey, metadataJson, cancellationToken);
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
        var finalPath = "";

        var result = await _processRunner.RunAsync(
            ytDlp.Path!,
            args,
            standardOutput: line =>
            {
                if (_progressParser.TryParse(line, out var parsed))
                {
                    progress?.Invoke(parsed);
                    return;
                }

                if (File.Exists(line))
                {
                    finalPath = line;
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

        if (!string.IsNullOrWhiteSpace(resolved.Message))
        {
            throw new InvalidOperationException(resolved.Message);
        }

        return resolved;
    }

    private static YtDlpAnalyzeResult ParseMetadata(string metadataJson, bool fromCache)
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>(metadataJson) ?? new VideoMetadata();
        metadata.IsFromCache = fromCache;
        return new YtDlpAnalyzeResult(metadata, metadataJson, fromCache);
    }

    private static string FirstErrorLine(string text, string fallback) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? fallback;
}

public sealed record YtDlpAnalyzeResult(VideoMetadata Metadata, string MetadataJson, bool IsFromCache);

public sealed record YtDlpDownloadResult(string? OutputPath, string StandardOutput, string StandardError);
