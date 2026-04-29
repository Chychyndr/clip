using Clip.Core.Tools;

namespace Clip.Core.YtDlp;

public static class YtDlpCommandBuilder
{
    public const string ProgressTemplate = "download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(progress.status)s";

    public static IReadOnlyList<string> BuildAnalyze(YtDlpAnalyzeOptions options)
    {
        var args = new List<string>
        {
            "--dump-single-json",
            "--no-playlist",
            "--no-warnings"
        };

        AddCookieArguments(args, options.BrowserCookieSource);
        args.Add(options.Url);
        return args;
    }

    public static IReadOnlyList<string> BuildDownload(YtDlpDownloadOptions options)
    {
        var args = new List<string>
        {
            "--newline",
            "--no-playlist",
            "--continue",
            "--no-overwrites",
            "--windows-filenames",
            "--paths",
            options.SaveDirectory,
            "--output",
            options.OutputTemplate,
            "--print",
            "after_move:filepath",
            "--progress-template",
            ProgressTemplate,
            "--concurrent-fragments",
            options.ConcurrentFragments.ToString()
        };

        if (options.UseAria2c && !string.IsNullOrWhiteSpace(options.Aria2cPath))
        {
            args.AddRange(
            [
                "--downloader",
                "aria2c",
                "--downloader-args",
                "aria2c:-x 8 -s 8 -k 1M"
            ]);
        }

        AddCookieArguments(args, options.BrowserCookieSource);
        AddFormatArguments(args, options);
        args.Add(options.Url);
        return args;
    }

    public static IReadOnlyList<string> BuildBatchDownload(YtDlpBatchDownloadOptions options)
    {
        var downloadOptions = new YtDlpDownloadOptions
        {
            Url = "",
            SaveDirectory = options.SaveDirectory,
            MediaMode = options.MediaMode,
            Format = options.Format,
            Resolution = options.Resolution,
            ConcurrentFragments = options.ConcurrentFragments,
            UseAria2c = options.UseAria2c,
            Aria2cPath = options.Aria2cPath,
            OutputTemplate = options.OutputTemplate
        };

        var args = BuildDownload(downloadOptions).ToList();
        if (args.Count > 0 && string.IsNullOrWhiteSpace(args[^1]))
        {
            args.RemoveAt(args.Count - 1);
        }

        args.AddRange(["-a", options.BatchFilePath]);
        return args;
    }

    private static void AddCookieArguments(List<string> args, string? browser)
    {
        if (!string.IsNullOrWhiteSpace(browser))
        {
            args.AddRange(["--cookies-from-browser", browser]);
        }
    }

    private static void AddFormatArguments(List<string> args, YtDlpDownloadOptions options)
    {
        var format = options.Format.Trim().ToUpperInvariant();
        var mediaMode = options.MediaMode.Trim();
        var isAudioOnly = mediaMode.Equals("Only audio", StringComparison.OrdinalIgnoreCase) || format == "MP3";
        var isVideoOnly = mediaMode.Equals("Only video", StringComparison.OrdinalIgnoreCase);

        if (isAudioOnly)
        {
            args.AddRange(["-f", "ba/bestaudio/best", "-x", "--audio-format", "mp3", "--audio-quality", "0"]);
            return;
        }

        if (format is "ORIGINAL" or "BEST")
        {
            args.Add("-f");
            args.Add(isVideoOnly ? "bv*/bestvideo/best" : "bv*+ba/b");
            return;
        }

        var heightFilter = BuildHeightFilter(options.Resolution);
        var mergeFormat = format.Equals("WEBM", StringComparison.OrdinalIgnoreCase)
            ? "webm"
            : format.Equals("MOV", StringComparison.OrdinalIgnoreCase)
                ? "mov"
                : "mp4";

        var selector = isVideoOnly
            ? BuildVideoOnlySelector(format, heightFilter)
            : BuildVideoAudioSelector(format, heightFilter);

        args.AddRange(["-f", selector, "--merge-output-format", mergeFormat]);
    }

    private static string BuildVideoAudioSelector(string format, string heightFilter) => format switch
    {
        "WEBM" => $"bv*[ext=webm]{heightFilter}+ba[ext=webm]/b[ext=webm]{heightFilter}/b{heightFilter}/best",
        "MOV" => $"bv*[ext=mp4]{heightFilter}+ba[ext=m4a]/b[ext=mp4]{heightFilter}/b{heightFilter}/best",
        _ => $"bv*[ext=mp4]{heightFilter}+ba[ext=m4a]/b[ext=mp4]{heightFilter}/b{heightFilter}/best"
    };

    private static string BuildVideoOnlySelector(string format, string heightFilter) => format switch
    {
        "WEBM" => $"bv*[ext=webm]{heightFilter}/bv*{heightFilter}/bestvideo{heightFilter}/best",
        "MOV" => $"bv*[ext=mp4]{heightFilter}/bv*{heightFilter}/bestvideo{heightFilter}/best",
        _ => $"bv*[ext=mp4]{heightFilter}/bv*{heightFilter}/bestvideo{heightFilter}/best"
    };

    private static string BuildHeightFilter(string resolution)
    {
        if (resolution.Equals("Original", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (resolution.Equals("4K", StringComparison.OrdinalIgnoreCase))
        {
            return "[height<=2160]";
        }

        var digits = new string(resolution.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var height) && height > 0
            ? $"[height<={height}]"
            : "";
    }
}

public sealed class YtDlpAnalyzeOptions
{
    public required string Url { get; init; }
    public string? BrowserCookieSource { get; init; }
}

public sealed class YtDlpDownloadOptions
{
    public required string Url { get; init; }
    public required string SaveDirectory { get; init; }
    public string MediaMode { get; init; } = "Video + audio";
    public string Format { get; init; } = "MP4";
    public string Resolution { get; init; } = "1080p";
    public int ConcurrentFragments { get; init; } = 4;
    public bool UseAria2c { get; init; }
    public string? Aria2cPath { get; init; }
    public string? BrowserCookieSource { get; init; }
    public string OutputTemplate { get; init; } = "%(title).200B [%(id)s].%(ext)s";
}

public sealed class YtDlpBatchDownloadOptions
{
    public required string BatchFilePath { get; init; }
    public required string SaveDirectory { get; init; }
    public string MediaMode { get; init; } = "Video + audio";
    public string Format { get; init; } = "MP4";
    public string Resolution { get; init; } = "1080p";
    public int ConcurrentFragments { get; init; } = 4;
    public bool UseAria2c { get; init; }
    public string? Aria2cPath { get; init; }
    public string OutputTemplate { get; init; } = "%(title).200B [%(id)s].%(ext)s";
}
