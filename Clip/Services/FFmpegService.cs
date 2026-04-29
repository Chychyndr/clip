using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Clip.Core.App;
using Clip.Core.Ffmpeg;
using Clip.Core.Tools;
using Clip.Models;

namespace Clip.Services;

public sealed partial class FFmpegService
{
    private readonly ProcessRunner _processRunner;
    private readonly ToolResolver _toolResolver;
    private readonly IAppSettingsProvider _settingsProvider;
    private readonly FfmpegEncoderDetector _encoderDetector;
    private FfmpegEncoderDetectionResult? _encoderDetection;

    public FFmpegService(
        ProcessRunner processRunner,
        ToolResolver toolResolver,
        IAppSettingsProvider settingsProvider,
        FfmpegEncoderDetector encoderDetector)
    {
        _processRunner = processRunner;
        _toolResolver = toolResolver;
        _settingsProvider = settingsProvider;
        _encoderDetector = encoderDetector;
    }

    public async Task<string> ClipAsync(
        string inputPath,
        ClipRange range,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveRequiredTool(ExternalTool.Ffmpeg);

        var outputPath = BuildDerivativePath(inputPath, "-clip", Path.GetExtension(inputPath));
        var duration = Math.Max(1, range.LengthSeconds);
        IReadOnlyList<string> args;
        if (_settingsProvider.Current.TrimMode == TrimMode.Fast)
        {
            args = FfmpegCommandBuilder.BuildFastTrim(new FfmpegTrimOptions
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                StartSeconds = range.StartSeconds,
                EndSeconds = range.EndSeconds
            });
        }
        else
        {
            args = FfmpegCommandBuilder.BuildExactTrim(new FfmpegTrimOptions
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                StartSeconds = range.StartSeconds,
                EndSeconds = range.EndSeconds,
                CompressionMode = _settingsProvider.Current.CompressionMode,
                VideoEncoder = await ResolveVideoEncoderAsync(cancellationToken)
            });
        }

        await RunFFmpegAsync(ffmpegPath, args, duration, progress, cancellationToken);
        return outputPath;
    }

    public async Task<string> ConvertAsync(
        string inputPath,
        string extension,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveRequiredTool(ExternalTool.Ffmpeg);

        var cleanExtension = extension.StartsWith('.') ? extension : "." + extension;
        var duration = await ProbeDurationAsync(inputPath, cancellationToken);
        var outputPath = BuildDerivativePath(inputPath, "-converted", cleanExtension);
        var args = new List<string>
        {
            "-y",
            "-i",
            inputPath,
            "-map",
            "0"
        };
        var encoder = await ResolveVideoEncoderAsync(cancellationToken);
        args.AddRange(["-c:v", FfmpegCommandBuilder.ToFfmpegCodec(encoder)]);
        if (encoder is VideoEncoderChoice.SoftwareX264 or VideoEncoderChoice.SoftwareX265)
        {
            args.AddRange(["-preset", FfmpegCommandBuilder.ToPreset(_settingsProvider.Current.CompressionMode)]);
        }

        args.AddRange(["-c:a", "aac", outputPath]);

        await RunFFmpegAsync(ffmpegPath, args, duration, progress, cancellationToken);
        return outputPath;
    }

    public async Task<string> CompressToTargetSizeAsync(
        string inputPath,
        double targetMegabytes,
        double? knownDurationSeconds,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveRequiredTool(ExternalTool.Ffmpeg);

        var duration = knownDurationSeconds is > 0
            ? knownDurationSeconds.Value
            : await ProbeDurationAsync(inputPath, cancellationToken);

        var outputPath = BuildDerivativePath(inputPath, $"-{targetMegabytes:0}mb", Path.GetExtension(inputPath));
        var args = FfmpegCommandBuilder.BuildCompression(new FfmpegCompressionOptions
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            TargetMegabytes = targetMegabytes,
            DurationSeconds = duration,
            CompressionMode = _settingsProvider.Current.CompressionMode,
            VideoEncoder = await ResolveVideoEncoderAsync(cancellationToken)
        });

        await RunFFmpegAsync(ffmpegPath, args, duration, progress, cancellationToken);
        return outputPath;
    }

    public async Task<double> ProbeDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        var ffprobePath = ResolveRequiredTool(ExternalTool.Ffprobe);

        var result = await _processRunner.RunAsync(
            ffprobePath,
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

    public async Task<FfmpegEncoderDetectionResult> DetectEncodersAsync(CancellationToken cancellationToken) =>
        await GetEncoderDetectionAsync(cancellationToken);

    private string ResolveRequiredTool(ExternalTool tool)
    {
        var resolved = _toolResolver.Resolve(tool);
        if (resolved.IsFound && resolved.Path is not null)
        {
            if (!string.IsNullOrWhiteSpace(resolved.Message))
            {
                CrashLog.Info(resolved.Message);
            }

            return resolved.Path;
        }

        throw new MissingBinaryException([resolved.Message ?? $"{ToolResolver.GetDisplayName(tool)} was not found."]);
    }

    private async Task<VideoEncoderChoice> ResolveVideoEncoderAsync(CancellationToken cancellationToken)
    {
        var selected = _settingsProvider.Current.VideoEncoder;
        if (selected == VideoEncoderChoice.Auto)
        {
            return (await GetEncoderDetectionAsync(cancellationToken)).RecommendedEncoder;
        }

        var detection = await GetEncoderDetectionAsync(cancellationToken);
        if (!FfmpegEncoderDetector.IsEncoderAvailable(selected, detection.AvailableEncoders))
        {
            CrashLog.Info($"{selected} is not available, falling back to Software x264.");
            return VideoEncoderChoice.SoftwareX264;
        }

        return selected;
    }

    private async Task<FfmpegEncoderDetectionResult> GetEncoderDetectionAsync(CancellationToken cancellationToken)
    {
        if (_encoderDetection is not null)
        {
            return _encoderDetection;
        }

        _encoderDetection = await _encoderDetector.DetectAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(_encoderDetection.Warning))
        {
            CrashLog.Info(_encoderDetection.Warning);
        }

        return _encoderDetection;
    }

    private async Task RunFFmpegAsync(
        string ffmpegPath,
        IEnumerable<string> args,
        double durationSeconds,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stderr = "";
        var result = await _processRunner.RunAsync(
            ffmpegPath,
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
