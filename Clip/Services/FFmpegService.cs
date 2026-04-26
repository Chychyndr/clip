using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Clip.Models;

namespace Clip.Services;

public sealed partial class FFmpegService
{
    private readonly ProcessRunner _processRunner;

    public FFmpegService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<string> ClipAsync(
        string inputPath,
        ClipRange range,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ClipConstants.FFmpegPath))
        {
            throw new MissingBinaryException([ClipConstants.FFmpegPath]);
        }

        var outputPath = BuildDerivativePath(inputPath, "-clip", Path.GetExtension(inputPath));
        var duration = Math.Max(1, range.LengthSeconds);
        var args = new[]
        {
            "-y",
            "-ss", FormatFFmpegTime(range.StartSeconds),
            "-i", inputPath,
            "-t", duration.ToString("0.###", CultureInfo.InvariantCulture),
            "-map", "0",
            "-c", "copy",
            "-avoid_negative_ts", "make_zero",
            outputPath
        };

        await RunFFmpegAsync(args, duration, progress, cancellationToken);
        return outputPath;
    }

    public async Task<string> ConvertAsync(
        string inputPath,
        string extension,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ClipConstants.FFmpegPath))
        {
            throw new MissingBinaryException([ClipConstants.FFmpegPath]);
        }

        var cleanExtension = extension.StartsWith('.') ? extension : "." + extension;
        var duration = await ProbeDurationAsync(inputPath, cancellationToken);
        var outputPath = BuildDerivativePath(inputPath, "-converted", cleanExtension);
        var args = new[]
        {
            "-y",
            "-i", inputPath,
            "-map", "0",
            "-c:v", "libx264",
            "-preset", "medium",
            "-c:a", "aac",
            outputPath
        };

        await RunFFmpegAsync(args, duration, progress, cancellationToken);
        return outputPath;
    }

    public async Task<string> CompressToTargetSizeAsync(
        string inputPath,
        double targetMegabytes,
        double? knownDurationSeconds,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ClipConstants.FFmpegPath))
        {
            throw new MissingBinaryException([ClipConstants.FFmpegPath]);
        }

        var duration = knownDurationSeconds is > 0
            ? knownDurationSeconds.Value
            : await ProbeDurationAsync(inputPath, cancellationToken);

        var targetKilobits = targetMegabytes * 1024 * 1024 * 8 / 1000;
        var totalKbps = Math.Max(384, targetKilobits / Math.Max(1, duration));
        const int audioKbps = 128;
        var videoKbps = Math.Max(256, totalKbps - audioKbps);
        var outputPath = BuildDerivativePath(inputPath, $"-{targetMegabytes:0}mb", Path.GetExtension(inputPath));

        var args = new[]
        {
            "-y",
            "-i", inputPath,
            "-map", "0",
            "-c:v", "libx264",
            "-preset", "slow",
            "-b:v", $"{videoKbps:0}k",
            "-maxrate", $"{videoKbps * 1.2:0}k",
            "-bufsize", $"{videoKbps * 2:0}k",
            "-c:a", "aac",
            "-b:a", $"{audioKbps}k",
            outputPath
        };

        await RunFFmpegAsync(args, duration, progress, cancellationToken);
        return outputPath;
    }

    public async Task<double> ProbeDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(ClipConstants.FFprobePath))
        {
            throw new MissingBinaryException([ClipConstants.FFprobePath]);
        }

        var result = await _processRunner.RunAsync(
            ClipConstants.FFprobePath,
            [
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                inputPath
            ],
            cancellationToken: cancellationToken);

        if (!result.IsSuccess ||
            !double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            throw new InvalidOperationException("ffprobe could not read the media duration.");
        }

        return duration;
    }

    private async Task RunFFmpegAsync(
        IEnumerable<string> args,
        double durationSeconds,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stderr = "";
        var result = await _processRunner.RunAsync(
            ClipConstants.FFmpegPath,
            args,
            standardError: line =>
            {
                stderr = line;
                ReportFFmpegProgress(line, durationSeconds, progress);
            },
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "ffmpeg failed." : stderr);
        }
    }

    private static void ReportFFmpegProgress(
        string line,
        double durationSeconds,
        IProgress<DownloadProgress>? progress)
    {
        if (progress is null)
        {
            return;
        }

        var match = FFmpegTimeRegex().Match(line);
        if (!match.Success)
        {
            return;
        }

        var hours = double.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
        var minutes = double.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
        var current = TimeSpan.FromHours(hours).TotalSeconds + TimeSpan.FromMinutes(minutes).TotalSeconds + seconds;
        var percent = Math.Clamp(current / Math.Max(1, durationSeconds) * 100, 0, 100);
        progress.Report(new DownloadProgress(percent, line.Trim()));
    }

    private static string BuildDerivativePath(string inputPath, string suffix, string extension)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? ClipConstants.DefaultDownloadDirectory;
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(directory, $"{fileName}{suffix}{extension}");
        var index = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileName}{suffix}-{index}{extension}");
            index++;
        }

        return candidate;
    }

    private static string FormatFFmpegTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"time=(?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2}(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex FFmpegTimeRegex();
}
