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

        var firstAttempt = await RunAnalyzeAttemptAsync(args, resolvedUrl, platform, browser: null, cancellationToken);
        if (firstAttempt.Result.IsSuccess)
        {
            return DeserializeMetadata(firstAttempt.StandardOutput, url);
        }

        var lastError = firstAttempt.ErrorText;
        if (ShouldRetryWithBrowserCookies(platform, lastError))
        {
            foreach (var browser in DetectBrowserCookieSources())
            {
                var retry = await RunAnalyzeAttemptAsync(args, resolvedUrl, platform, browser, cancellationToken);
                if (retry.Result.IsSuccess)
                {
                    return DeserializeMetadata(retry.StandardOutput, url);
                }

                lastError = retry.ErrorText;
            }
        }

        throw new InvalidOperationException(CleanError(lastError, "yt-dlp could not analyze the URL."));
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

        var firstAttempt = await RunDownloadAttemptAsync(item, url, platform, browser: null, progress, cancellationToken);
        if (firstAttempt.Result.IsSuccess)
        {
            return ResolveDownloadedFile(firstAttempt.OutputCandidates, firstAttempt.StandardOutput, item.SaveDirectory, startedAt);
        }

        var lastError = firstAttempt.ErrorText;
        if (ShouldRetryWithBrowserCookies(platform, lastError))
        {
            foreach (var browser in DetectBrowserCookieSources())
            {
                progress?.Report(new DownloadProgress(0, $"Retrying with {browser} cookies"));
                var retry = await RunDownloadAttemptAsync(item, url, platform, browser, progress, cancellationToken);
                if (retry.Result.IsSuccess)
                {
                    return ResolveDownloadedFile(retry.OutputCandidates, retry.StandardOutput, item.SaveDirectory, startedAt);
                }

                lastError = retry.ErrorText;
            }
        }

        throw new InvalidOperationException(CleanError(lastError, "yt-dlp could not complete the download."));
    }

    private async Task<YtDlpAttemptResult> RunAnalyzeAttemptAsync(
        IReadOnlyList<string> baseArgs,
        string url,
        Platform platform,
        string? browser,
        CancellationToken cancellationToken)
    {
        var args = baseArgs.ToList();
        AddCookieArguments(args, platform, browser);
        args.Add(url);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var result = await _processRunner.RunAsync(
            ClipConstants.YtDlpPath,
            args,
            standardOutput: line => stdout.AppendLine(line),
            standardError: line => stderr.AppendLine(line),
            cancellationToken: cancellationToken);

        return new YtDlpAttemptResult(result, stdout.ToString(), stderr.ToString(), []);
    }

    private async Task<YtDlpAttemptResult> RunDownloadAttemptAsync(
        DownloadItem item,
        string url,
        Platform platform,
        string? browser,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
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

        AddCookieArguments(args, platform, browser);
        AddFormatArguments(args, item);
        args.Add(url);

        var outputCandidates = new List<string>();
        var outputGate = new object();
        var stderr = new StringBuilder();
        var result = await _processRunner.RunAsync(
            ClipConstants.YtDlpPath,
            args,
            workingDirectory: item.SaveDirectory,
            standardOutput: line =>
            {
                if (TryReadOutputPath(line, item.SaveDirectory, out var path))
                {
                    lock (outputGate)
                    {
                        outputCandidates.Add(path);
                    }
                }

                ReportProgress(line, progress);
            },
            standardError: line =>
            {
                stderr.AppendLine(line);
                ReportProgress(line, progress);
            },
            cancellationToken: cancellationToken);

        lock (outputGate)
        {
            outputCandidates.AddRange(ReadOutputPaths(result.StandardOutput, item.SaveDirectory));
        }

        return new YtDlpAttemptResult(result, result.StandardOutput, stderr.ToString(), outputCandidates);
    }

    private static VideoMetadata DeserializeMetadata(string text, string fallbackUrl)
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>(text, JsonOptions)
            ?? throw new InvalidOperationException("yt-dlp returned empty metadata.");

        metadata.WebpageUrl ??= fallbackUrl;
        return metadata;
    }

    private static string ResolveDownloadedFile(
        IReadOnlyList<string> outputCandidates,
        string standardOutput,
        string saveDirectory,
        DateTimeOffset startedAt)
    {
        var candidates = outputCandidates
            .Concat(ReadOutputPaths(standardOutput, saveDirectory))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var finalPath = candidates
            .Reverse()
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(finalPath))
        {
            return finalPath;
        }

        return FindNewestMediaFile(saveDirectory, startedAt)
            ?? throw new FileNotFoundException($"The download finished, but Clip could not locate the output file in {saveDirectory}.");
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

    private static void AddCookieArguments(List<string> args, Platform platform, string? browser)
    {
        if (string.IsNullOrWhiteSpace(browser) ||
            platform is not (Platform.Instagram or Platform.YouTube))
        {
            return;
        }

        args.AddRange(["--cookies-from-browser", browser]);
    }

    private static IReadOnlyList<string> DetectBrowserCookieSources()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        (string Browser, string Path)[] candidates =
        [
            ("edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("firefox", Path.Combine(roaming, "Mozilla", "Firefox", "Profiles")),
            ("brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("chrome", Path.Combine(local, "Google", "Chrome", "User Data"))
        ];

        return candidates
            .Where(candidate => Directory.Exists(candidate.Path))
            .Select(candidate => candidate.Browser)
            .ToList();
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

        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc >= startedAt.UtcDateTime.AddMinutes(-10))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string CleanError(string text, string fallback)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (IsCookieDatabaseError(text))
        {
            return "Clip could not read browser cookies. Close Chrome, Edge, Firefox, and Brave, then try again. Public links are downloaded without cookies.";
        }

        if (lines.Any(line => line.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) ||
                              line.Contains("not a bot", StringComparison.OrdinalIgnoreCase)))
        {
            var browsers = DetectBrowserCookieSources();
            return browsers.Count == 0
                ? "YouTube asked for authentication. Sign in to YouTube in Chrome, Edge, Firefox, or Brave, then try again."
                : $"YouTube asked for authentication. Clip tried browser cookies from {string.Join(", ", browsers)}. Make sure you are signed in to YouTube and close the browser if cookies are locked.";
        }

        return lines.LastOrDefault(line => !line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static bool ShouldRetryWithBrowserCookies(Platform platform, string error)
    {
        if (platform is not (Platform.YouTube or Platform.Instagram))
        {
            return false;
        }

        return error.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("not a bot", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("cookies", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCookieDatabaseError(string text) =>
        text.Contains("Could not copy", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("cookie", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<percent>\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled)]
    private static partial Regex PercentRegex();

    private sealed record YtDlpAttemptResult(
        ProcessResult Result,
        string StandardOutput,
        string ErrorText,
        IReadOnlyList<string> OutputCandidates);
}
