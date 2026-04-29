using System.Globalization;
using Clip.Core.App;

namespace Clip.Core.Ffmpeg;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildFastTrim(FfmpegTrimOptions options) =>
    [
        "-y",
        "-ss",
        FormatTime(options.StartSeconds),
        "-to",
        FormatTime(options.EndSeconds),
        "-i",
        options.InputPath,
        "-map",
        "0",
        "-c",
        "copy",
        "-avoid_negative_ts",
        "make_zero",
        options.OutputPath
    ];

    public static IReadOnlyList<string> BuildExactTrim(FfmpegTrimOptions options)
    {
        var args = new List<string>
        {
            "-y",
            "-ss",
            FormatTime(options.StartSeconds),
            "-to",
            FormatTime(options.EndSeconds),
            "-i",
            options.InputPath,
            "-map",
            "0"
        };

        AddVideoEncoder(args, options.VideoEncoder, options.CompressionMode);
        args.AddRange(["-c:a", "aac", options.OutputPath]);
        return args;
    }

    public static IReadOnlyList<string> BuildCompression(FfmpegCompressionOptions options)
    {
        var args = new List<string>
        {
            "-y",
            "-i",
            options.InputPath,
            "-map",
            "0"
        };

        AddVideoEncoder(args, options.VideoEncoder, options.CompressionMode);

        if (options.TargetMegabytes is > 0 && options.DurationSeconds is > 0)
        {
            var bitrates = CalculateTargetBitrates(options.TargetMegabytes.Value, options.DurationSeconds.Value, options.AudioKbps);
            args.AddRange(
            [
                "-b:v",
                $"{bitrates.VideoKbps}k",
                "-maxrate",
                $"{Math.Ceiling(bitrates.VideoKbps * 1.2)}k",
                "-bufsize",
                $"{bitrates.VideoKbps * 2}k"
            ]);
        }

        args.AddRange(["-c:a", "aac", "-b:a", $"{options.AudioKbps}k", options.OutputPath]);
        return args;
    }

    public static IReadOnlyList<string> BuildMux(string videoPath, string audioPath, string outputPath) =>
    [
        "-y",
        "-i",
        videoPath,
        "-i",
        audioPath,
        "-c",
        "copy",
        outputPath
    ];

    public static TargetBitrate CalculateTargetBitrates(double targetMegabytes, double durationSeconds, int audioKbps = 128)
    {
        if (targetMegabytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetMegabytes), "Target size must be greater than zero.");
        }

        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be known.");
        }

        var targetKilobits = targetMegabytes * 1024 * 1024 * 8 / 1000;
        var totalKbps = targetKilobits / durationSeconds;
        if (totalKbps < 384)
        {
            throw new InvalidOperationException("The target size is too small for a usable video bitrate.");
        }

        var videoKbps = Math.Max(256, (int)Math.Floor(totalKbps - audioKbps));
        return new TargetBitrate(videoKbps, audioKbps);
    }

    private static void AddVideoEncoder(List<string> args, VideoEncoderChoice encoder, CompressionMode compressionMode)
    {
        var codec = ToFfmpegCodec(encoder);
        args.AddRange(["-c:v", codec]);

        if (IsSoftwareEncoder(codec))
        {
            args.AddRange(["-preset", ToPreset(compressionMode)]);
        }
    }

    public static string ToFfmpegCodec(VideoEncoderChoice encoder) => encoder switch
    {
        VideoEncoderChoice.SoftwareX265 => "libx265",
        VideoEncoderChoice.NvidiaH264 => "h264_nvenc",
        VideoEncoderChoice.NvidiaHevc => "hevc_nvenc",
        VideoEncoderChoice.IntelH264 => "h264_qsv",
        VideoEncoderChoice.IntelHevc => "hevc_qsv",
        VideoEncoderChoice.AmdH264 => "h264_amf",
        VideoEncoderChoice.AmdHevc => "hevc_amf",
        VideoEncoderChoice.AppleH264 => "h264_videotoolbox",
        VideoEncoderChoice.AppleHevc => "hevc_videotoolbox",
        _ => "libx264"
    };

    public static string ToPreset(CompressionMode mode) => mode switch
    {
        CompressionMode.Fast => "veryfast",
        CompressionMode.Quality => "slow",
        _ => "medium"
    };

    private static bool IsSoftwareEncoder(string codec) =>
        codec.Equals("libx264", StringComparison.OrdinalIgnoreCase) ||
        codec.Equals("libx265", StringComparison.OrdinalIgnoreCase);

    public static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class FfmpegTrimOptions
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public CompressionMode CompressionMode { get; init; } = CompressionMode.Balance;
    public VideoEncoderChoice VideoEncoder { get; init; } = VideoEncoderChoice.SoftwareX264;
}

public sealed class FfmpegCompressionOptions
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public double? TargetMegabytes { get; init; }
    public double? DurationSeconds { get; init; }
    public int AudioKbps { get; init; } = 128;
    public CompressionMode CompressionMode { get; init; } = CompressionMode.Balance;
    public VideoEncoderChoice VideoEncoder { get; init; } = VideoEncoderChoice.SoftwareX264;
}

public sealed record TargetBitrate(int VideoKbps, int AudioKbps);
