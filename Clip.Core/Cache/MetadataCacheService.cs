using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clip.Core.Cache;

public sealed class MetadataCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _cacheDirectory;
    private readonly TimeProvider _timeProvider;

    public MetadataCacheService(string cacheDirectory, TimeProvider? timeProvider = null)
    {
        _cacheDirectory = cacheDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SaveAsync(
        string url,
        string ytDlpVersion,
        string analysisOptionsKey,
        string metadataJson,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var entry = new MetadataCacheEntry(
            NormalizeUrl(url),
            ytDlpVersion,
            analysisOptionsKey,
            _timeProvider.GetUtcNow(),
            metadataJson);

        var path = GetCachePath(url, ytDlpVersion, analysisOptionsKey);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<MetadataCacheReadResult> TryReadAsync(
        string url,
        string ytDlpVersion,
        string analysisOptionsKey,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var path = GetCachePath(url, ytDlpVersion, analysisOptionsKey);
        if (!File.Exists(path))
        {
            return MetadataCacheReadResult.Miss;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var entry = JsonSerializer.Deserialize<MetadataCacheEntry>(json, JsonOptions);
            if (entry is null ||
                !string.Equals(entry.YtDlpVersion, ytDlpVersion, StringComparison.Ordinal) ||
                !string.Equals(entry.NormalizedUrl, NormalizeUrl(url), StringComparison.Ordinal) ||
                !string.Equals(entry.AnalysisOptionsKey, analysisOptionsKey, StringComparison.Ordinal))
            {
                return MetadataCacheReadResult.Miss;
            }

            if (_timeProvider.GetUtcNow() - entry.CreatedAtUtc > ttl)
            {
                return MetadataCacheReadResult.Expired;
            }

            return new MetadataCacheReadResult(true, false, entry.MetadataJson);
        }
        catch
        {
            return MetadataCacheReadResult.Miss;
        }
    }

    public void Clear()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }

        Directory.CreateDirectory(_cacheDirectory);
    }

    public string GetCachePath(string url, string ytDlpVersion, string analysisOptionsKey)
    {
        var key = $"{NormalizeUrl(url)}|{ytDlpVersion}|{analysisOptionsKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_cacheDirectory, hash + ".json");
    }

    public static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant()
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private sealed record MetadataCacheEntry(
        string NormalizedUrl,
        string YtDlpVersion,
        string AnalysisOptionsKey,
        DateTimeOffset CreatedAtUtc,
        string MetadataJson);
}

public sealed record MetadataCacheReadResult(bool Hit, bool IsExpired, string? MetadataJson)
{
    public static MetadataCacheReadResult Miss { get; } = new(false, false, null);
    public static MetadataCacheReadResult Expired { get; } = new(false, true, null);
}
