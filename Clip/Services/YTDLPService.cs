using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clip.Models;

namespace Clip.Services;

public sealed partial class YTDLPService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ProcessRunner _processRunner;
    private readonly RedditResolver _redditResolver;

    public YTDLPService(ProcessRunner processRunner, RedditResolver redditResolver)
    {
        _processRunner = processRunner;
        _redditResolver = redditResolver;
    }

    public IReadOnlyList<string> GetMissingBinaries(bool includeFfmpeg)
    {
        var paths = new List<string>();
        if (!File.Exists(ClipConstants.YtDlpPath))
        {
            paths.Add(ClipConstants.YtDlpPath);
        }

        if (includeFfmpeg)
        {
            if (!File.Exists(ClipConstants.FFmpegPath))
            {
                paths.Add(ClipConstants.FFmpegPath);
            }

            if (!File.Exists(ClipConstants.FFprobePath))
            {
                paths.Add(ClipConstants.FFprobePath);
            }
        }

        return paths;
    }

    public async Task<VideoMetadata> AnalyzeAsync(string url, CancellationToken cancellationToken)
    {
        var missing = GetMissingBinaries(includeFfmpeg: false);
        if (missing.Count > 0)
        {
            throw new MissingBinaryException(missing);
        }

        var platform = URLDetector.DetectPlatform(url);
        var resolvedUrl = platform == Platform.Reddit
            ? await _redditResolver.ResolveAsync(url, cancellationToken)
            : url;

        var args = new List<string>
        {
            "--dump-single-json",
            "--no-playlist",
            "--no-warnings"
        };

        AddCookieArguments(args, platform);
        args.Add(resolvedUrl);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var result = await _processRunner.RunAsync(
            ClipConstants.YtDlpPath,
            args,
            standardOutput: line => stdout.AppendLine(line),
            standardError: line => stderr.AppendLine(line),
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(CleanError(stderr.ToString(), "yt-dlp could not analyze the URL."));
        }

        var metadata = JsonSerializer.Deserialize<VideoMetadata>(stdout.ToString(), JsonOptions)
            ?? throw new InvalidOperationException("yt-dlp returned empty metadata.");

        metadata.WebpageUrl ??= url;
        return metadata;
    }

    public async Task<string> DownloadAsync(
        DownloadItem item,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var missing = GetMissingBinaries(includeFfmpeg: true);
        if (missing.Count > 0)
        {
            throw new MissingBinaryException(missing);
        }

        Directory.CreateDirectory(item.SaveDirectory);
        var startedAt = DateTimeOffset.Now;
        var platform = URLDetector.DetectPlatform(item.Url);
        var url = platform == Platform.Reddit
            ? await _redditResolver.ResolveAsync(item.Url, cancellationToken)
            : item.Url;

        var args = new List<string>
        {
            "--newline",
            "--no-playlist",
            "--windows-filenames",
            "--paths",
            item.SaveDirectory,
            "--output",
            "%(title).200B [%(id)s].%(ext)s",
            "--print",
            "after_move:filepath",
            "--progress-template",
            "download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s"
        };

        AddCookieArguments(args, platform);
        AddFormatArguments(args, item);
        args.Add(url);

        var finalPath = "";
        var stderr = new StringBuilder();
        var result = await _processRunner.RunAsync(
            ClipConstants.YtDlpPath,
            args,
            workingDirectory: item.SaveDirectory,
            standardOutput: line =>
            {
                if (TryReadOutputPath(line, out var path))
                {
                    finalPath = path;
                }

                ReportProgress(line, progress);
            },
            standardError: line =>
            {
                stderr.AppendLine(line);
                ReportProgress(line, progress);
            },
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(CleanError(stderr.ToString(), "yt-dlp could not complete the download."));
        }

        if (string.IsNullOrWhiteSpace(finalPath) || !File.Exists(finalPath))
        {
            finalPath = FindNewestMediaFile(item.SaveDirectory, startedAt)
                ?? throw new FileNotFoundException("The download finished, but Clip could not locate the output file.");
        }

        progress?.Report(new DownloadProgress(100, "Download complete"));
        return finalPath;
    }

    private static void AddFormatArguments(List<string> args, DownloadItem item)
    {
        var format = item.Format.ToUpperInvariant();
        if (format == "MP3")
        {
            args.AddRange(["-x", "--audio-format", "mp3", "--audio-quality", "0"]);
            return;
        }

        var height = ParseResolutionHeight(item.Resolution);
        var heightFilter = height > 0 ? $"[height<={height}]" : "";
        var mergeFormat = format.ToLowerInvariant();

        var selector = format switch
        {
            "WEBM" => $"bv*{heightFilter}[ext=webm]+ba[ext=webm]/b{heightFilter}/best",
            "MOV" => $"bv*{heightFilter}[ext=mp4]+ba[ext=m4a]/b{heightFilter}[ext=mp4]/best",
            _ => $"bv*{heightFilter}[ext=mp4]+ba[ext=m4a]/b{heightFilter}[ext=mp4]/best"
        };

        args.AddRange(["-f", selector, "--merge-output-format", mergeFormat]);
    }

    private static void AddCookieArguments(List<string> args, Platform platform)
    {
        if (platform != Platform.Instagram)
        {
            return;
        }

        var browser = DetectBrowserCookieSource();
        if (!string.IsNullOrWhiteSpace(browser))
        {
            args.AddRange(["--cookies-from-browser", browser]);
        }
    }

    private static string? DetectBrowserCookieSource()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new Dictionary<string, string>
        {
            ["chrome"] = Path.Combine(local, "Google", "Chrome", "User Data"),
            ["edge"] = Path.Combine(local, "Microsoft", "Edge", "User Data"),
            ["firefox"] = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"),
            ["brave"] = Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")
        };

        return candidates.FirstOrDefault(candidate => Directory.Exists(candidate.Value)).Key;
    }

    private static int ParseResolutionHeight(string resolution)
    {
        if (resolution.Equals("4K", StringComparison.OrdinalIgnoreCase))
        {
            return 2160;
        }

        var digits = new string(resolution.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? height
            : 0;
    }

    private static bool TryReadOutputPath(string line, out string path)
    {
        path = line.Trim().Trim('"');
        return Path.IsPathFullyQualified(path) && File.Exists(path);
    }

    private static void ReportProgress(string line, IProgress<DownloadProgress>? progress)
    {
        if (progress is null)
        {
            return;
        }

        var match = PercentRegex().Match(line);
        if (match.Success &&
            double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            progress.Report(new DownloadProgress(percent, line.Trim()));
        }
    }

    private static string? FindNewestMediaFile(string directory, DateTimeOffset startedAt)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".webm", ".mp3", ".m4a", ".mkv"
        };

        return Directory.EnumerateFiles(directory)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc >= startedAt.UtcDateTime.AddMinutes(-2))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string CleanError(string text, string fallback)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(line => !line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    [GeneratedRegex(@"(?<percent>\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled)]
    private static partial Regex PercentRegex();
}
